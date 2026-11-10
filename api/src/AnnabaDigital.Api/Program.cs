using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AnnabaDigital.Api;
using AnnabaDigital.Api.Endpoints;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Infrastructure;
using AnnabaDigital.Infrastructure.Persistence;
using AnnabaDigital.Infrastructure.Security;

// Container health probe (the runtime image has no curl): `dotnet AnnabaDigital.Api.dll --healthcheck` exits 0 when /health answers.
if (args.Contains("--healthcheck"))
{
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var port = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")?.Split(':').Last().TrimEnd('/') is { Length: > 0 } p && int.TryParse(p, out _) ? p : "8080";
        return (await http.GetAsync($"http://localhost:{port}/health")).IsSuccessStatusCode ? 0 : 1;
    }
    catch { return 1; }
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection("Api"));
builder.Services.AddAnnabaDigitalInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<AnnabaDigital.Application.Abstractions.IAppLinks, AppLinks>();

builder.Services.AddAuthentication(SessionDefaults.Scheme).AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionDefaults.Scheme, _ => { });
builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()); // deny by default
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, EnvelopeAuthorizationResultHandler>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddSingleton<HttpMetrics>();
// ADM-004: the API describes itself (paths, parameters, request bodies; the permission each route needs is in its description).
builder.Services.AddOpenApi("v1", o =>
{
    o.AddDocumentTransformer((doc, _, _) =>
    {
        doc.Info.Title = "Annaba HR API"; doc.Info.Version = "v1";
        doc.Info.Description = "Identity, HR, leave, attendance and payroll. Sign in through the web application (session cookie); state-changing requests also need the X-CSRF-Token header. Errors use one envelope: { error: { code, message, fields?, requestId } }. Webhook payloads are described in docs/29-hr-webhooks.md.";
        return Task.CompletedTask;
    });
    o.AddOperationTransformer((op, ctx, _) =>
    {
        var perm = ctx.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Select(a => a.Policy).FirstOrDefault(p => p is not null && p.StartsWith(PermissionPolicy.Prefix, StringComparison.Ordinal));
        var anonymous = ctx.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any();
        var note = perm is not null ? $"Requires permission: {perm[PermissionPolicy.Prefix.Length..]}." : anonymous ? "No sign-in needed." : "Any signed-in user (the service limits the result to what the caller may see).";
        op.Description = string.IsNullOrEmpty(op.Description) ? note : op.Description + " " + note;
        return Task.CompletedTask;
    });
});

var app = builder.Build();

StartupChecks.Validate(app.Services, app.Environment);

if (app.Services.GetRequiredService<IOptions<ApiOptions>>().Value.AutoMigrate)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AnnabaDigitalDbContext>();
    await db.Database.MigrateAsync();
    await Seeder.SeedReferenceDataAsync(db, DateTimeOffset.UtcNow);
    if (app.Services.GetRequiredService<IOptions<ApiOptions>>().Value.SeedDevUsers && app.Environment.IsDevelopment())
        await DevData.SeedAsync(scope.ServiceProvider);
    await Bootstrap.RunAsync(scope.ServiceProvider, app.Services.GetRequiredService<IOptions<ApiOptions>>().Value.BootstrapAdminEmail);
}

// Order matters: gate → error envelope → headers → authN → session state → CSRF → authZ
app.UseMiddleware<MetricsMiddleware>();
app.UseMiddleware<ProxyGateMiddleware>();
app.UseMiddleware<ExceptionEnvelopeMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseAuthentication();
app.UseMiddleware<SessionStateMiddleware>();
app.UseMiddleware<CsrfMiddleware>();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapMetrics();
if (app.Services.GetRequiredService<IOptions<ApiOptions>>().Value.OpenApi) app.MapOpenApi("/api/openapi/{documentName}.json").RequireAuthorization();
// Readiness: can we reach the database, and does it have every migration this build expects? A deploy that skipped the migration step reports "not ready" instead of failing on the first user's request.
app.MapGet("/health/ready", async (AnnabaDigitalDbContext db, CancellationToken ct) =>
{
    try
    {
        if (!await db.Database.CanConnectAsync(ct)) return Results.Json(new { status = "not_ready", reason = "database unreachable" }, statusCode: 503);
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).Count();
        return pending == 0 ? Results.Ok(new { status = "ready" }) : Results.Json(new { status = "not_ready", reason = $"{pending} migration(s) pending" }, statusCode: 503);
    }
    catch { return Results.Json(new { status = "not_ready", reason = "database check failed" }, statusCode: 503); }
}).AllowAnonymous();
app.MapAuth();
app.MapSso();
app.MapAccount();
app.MapMfa();
app.MapUsers();
app.MapAccessRequests();
app.MapAccessReviews();
app.MapTenants();
app.MapAccountClosures();
app.MapRoles();
app.MapSelfService();
app.MapHr();
app.MapLeave();
app.MapAttendance();
app.MapPayroll();
app.MapHrOps();
app.MapReadiness();
app.MapScim();
foreach (var module in app.Services.GetServices<IEndpointModule>()) module.Map(app);

app.Run();
return 0;

public partial class Program;
