using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>
/// Stage 20: API key management, bank file layouts and journal accounts, and the read-only public API under /api/public/v1.
/// The public routes are "anonymous" to the session system on purpose (like SCIM): the caller is another system with a bearer API key, checked on every call by the filter below.
/// They are GET only, so the browser-origin and CSRF checks do not apply.
/// </summary>
public static class IntegrationEndpoints
{
    public static void MapIntegrations(this IEndpointRouteBuilder app)
    {
        var k = app.MapGroup("/api/integrations/api-keys"); k.RequireAuthorization();
        k.MapGet("/", async (IntegrationService svc, HttpContext ctx) => TypedResults.Ok(new { keys = await svc.KeysAsync(ctx.RequestAborted), scopes = IntegrationService.Scopes })).RequirePermission(Permissions.SettingsManage);
        k.MapPost("/", async (CreateApiKeyRequest body, IntegrationService svc, HttpContext ctx) => TypedResults.Ok(await svc.CreateKeyAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.SettingsManage);
        k.MapDelete("/{id:guid}", async (Guid id, IntegrationService svc, HttpContext ctx) => { await svc.RevokeKeyAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); }).RequirePermission(Permissions.SettingsManage);

        var h = app.MapGroup("/api/hr/integrations"); h.RequireAuthorization();
        h.MapGet("/bank-layouts", async (IntegrationService svc, HttpContext ctx) => TypedResults.Ok(new { layouts = await svc.LayoutsAsync(ctx.RequestAborted), tokens = IntegrationService.BankTokens })).RequirePermission(Permissions.PayrollLock);
        h.MapPost("/bank-layouts", async (BankLayoutRequest body, IntegrationService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveLayoutAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollLock);
        h.MapPut("/bank-layouts/{id:guid}", async (Guid id, BankLayoutRequest body, IntegrationService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveLayoutAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollLock);
        h.MapDelete("/bank-layouts/{id:guid}", async (Guid id, IntegrationService svc, HttpContext ctx) => { await svc.DeleteLayoutAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); }).RequirePermission(Permissions.PayrollLock);
        h.MapGet("/journal-accounts", async (IntegrationService svc, HttpContext ctx) => TypedResults.Ok(await svc.JournalAccountsAsync(ctx.RequestAborted))).RequirePermission(Permissions.PayrollRead);
        h.MapPut("/journal-accounts", async (List<JournalAccountRequest> body, IntegrationService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveJournalAccountsAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollConfigure);

        var p = app.MapGroup("/api/public/v1").AllowAnonymous();
        p.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext; var svc = http.RequestServices.GetRequiredService<IntegrationService>();
            var caller = await svc.AuthenticateAsync(http.Request.Headers.Authorization.ToString(), http.Client(), http.RequestAborted);
            if (caller is null) { http.Response.Headers.WWWAuthenticate = "Bearer"; return Results.Json(new { error = new { code = ErrorCodes.Unauthenticated, message = "A valid API key is required (Authorization: Bearer ak_...)." } }, statusCode: 401); }
            http.Items["api-caller"] = caller;
            return await next(ctx);
        });
        static ApiCaller Caller(HttpContext ctx) => (ApiCaller)ctx.Items["api-caller"]!;
        p.MapGet("/employees", async (DateTimeOffset? updatedSince, int? page, int? pageSize, IntegrationService svc, HttpContext ctx) => TypedResults.Ok(await svc.EmployeesAsync(Caller(ctx), updatedSince, page, pageSize, ctx.Client(), ctx.RequestAborted)));
        p.MapGet("/org-units", async (IntegrationService svc, HttpContext ctx) => TypedResults.Ok(await svc.OrgUnitsAsync(Caller(ctx), ctx.Client(), ctx.RequestAborted)));
        p.MapGet("/leave-requests", async (DateOnly? from, DateOnly? to, int? page, int? pageSize, IntegrationService svc, HttpContext ctx) => TypedResults.Ok(await svc.LeaveAsync(Caller(ctx), from, to, page, pageSize, ctx.Client(), ctx.RequestAborted)));
        p.MapGet("/attendance", async (DateOnly? from, DateOnly? to, int? page, int? pageSize, IntegrationService svc, HttpContext ctx) => TypedResults.Ok(await svc.AttendanceAsync(Caller(ctx), from, to, page, pageSize, ctx.Client(), ctx.RequestAborted)));
    }
}
