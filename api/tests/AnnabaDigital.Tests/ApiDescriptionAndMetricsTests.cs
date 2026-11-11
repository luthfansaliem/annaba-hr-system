using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class ApiDescriptionAndMetricsTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);

    [Fact]
    public async Task The_openapi_document_needs_a_sign_in_covers_every_hr_route_and_names_the_permission_and_request_body()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get("/api/openapi/v1.json")).StatusCode);
        var b = TestBrowser.Create(fx.Factory); await b.LoginAsync((await _seed.UserAsync(role: "user")).Email, Seed.Password);
        var res = await b.Get("/api/openapi/v1.json"); Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadAsStringAsync(); var doc = JsonDocument.Parse(json).RootElement;
        if (Environment.GetEnvironmentVariable("UPDATE_API_DOC") == "1")                                                           // a copy for integrators who cannot run the application: docs/openapi-v1.json
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory); while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "README.md"))) dir = dir.Parent;
            var copy = System.Text.Json.Nodes.JsonNode.Parse(json)!; var all = copy["paths"]!.AsObject();
            foreach (var probe in all.Select(p => p.Key).Where(k => k.StartsWith("/api/_probe")).ToList()) all.Remove(probe);       // routes that exist only in this test host
            await File.WriteAllTextAsync(Path.Combine(dir!.FullName, "docs", "openapi-v1.json"), copy.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }
        Assert.StartsWith("3.", doc.GetProperty("openapi").GetString()); Assert.Equal("Annaba HR API", doc.GetProperty("info").GetProperty("title").GetString());
        var paths = doc.GetProperty("paths");

        // every HR route of the running application is described (path parameters lose their type constraint in OpenAPI)
        var routes = fx.Factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().Where(e => e.RoutePattern.RawText?.StartsWith("/api/hr") == true)
            .Select(e => Regex.Replace(e.RoutePattern.RawText!, @"\{(\w+):\w+\}", "{$1}").TrimEnd('/')).Distinct().ToList();
        var described = paths.EnumerateObject().Select(p => p.Name.TrimEnd('/')).ToHashSet();
        Assert.True(routes.Count > 100, $"only {routes.Count} HR routes found");
        Assert.Empty(routes.Where(r => !described.Contains(r)));

        var create = paths.GetProperty("/api/hr/payroll/runs").GetProperty("post");
        Assert.Contains("Requires permission: payroll.run.", create.GetProperty("description").GetString());
        var schemaRef = create.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString()!;
        var schema = doc.GetProperty("components").GetProperty("schemas").GetProperty(schemaRef.Split('/').Last());
        foreach (var field in new[] { "year", "month", "legalEntityId", "deductAbsence" }) Assert.True(schema.GetProperty("properties").TryGetProperty(field, out _), field);
        Assert.Contains("Any signed-in user", paths.GetProperty("/api/hr/me/notifications").GetProperty("get").GetProperty("description").GetString());
        Assert.False(paths.TryGetProperty("/health/metrics", out _));                                                             // operational endpoints are not part of the public description

        // answers are described too: what a successful call returns, and the one error envelope for the failures a route can give
        var schemas = doc.GetProperty("components").GetProperty("schemas");
        JsonElement Resolve(JsonElement s) => s.TryGetProperty("$ref", out var r) ? schemas.GetProperty(r.GetString()!.Split('/').Last()) : s;
        var one = paths.GetProperty("/api/hr/employees/{id}").GetProperty("get").GetProperty("responses");
        var detail = Resolve(one.GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        foreach (var field in new[] { "fullName", "employeeNo", "status", "version" }) Assert.True(detail.GetProperty("properties").TryGetProperty(field, out _), field);
        foreach (var status in new[] { "401", "403", "404" }) Assert.Equal("#/components/schemas/Error", one.GetProperty(status).GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.False(one.TryGetProperty("422", out _));                                                                           // reading has nothing to validate
        Assert.True(create.GetProperty("responses").TryGetProperty("422", out _)); Assert.True(create.GetProperty("responses").TryGetProperty("409", out _));
        Assert.True(schemas.GetProperty("Error").GetProperty("properties").GetProperty("error").GetProperty("properties").TryGetProperty("requestId", out _));
        Assert.True(paths.GetProperty("/api/auth/login").GetProperty("post").GetProperty("responses").TryGetProperty("429", out _));
        var ops = paths.EnumerateObject().Where(p => p.Name.StartsWith("/api/hr")).SelectMany(p => p.Value.EnumerateObject().Select(o => (Name: $"{o.Name.ToUpperInvariant()} {p.Name}", Responses: o.Value.GetProperty("responses")))).ToList();
        bool Described((string Name, JsonElement Responses) o) => o.Responses.EnumerateObject().Any(r => r.Name == "204" || (r.Name.StartsWith('2') && r.Value.TryGetProperty("content", out var c) && c.TryGetProperty("application/json", out var j) && j.TryGetProperty("schema", out _)));
        var bare = ops.Where(o => !Described(o)).Select(o => o.Name).ToList();
        Assert.True(bare.Count <= ops.Count / 5, $"{bare.Count} of {ops.Count} HR operations do not describe their answer:\n{string.Join("\n", bare)}");
    }

    [Fact]
    public async Task Metrics_answer_only_to_the_scrape_token_and_report_requests_by_route_template_and_queue_depths()
    {
        var http = fx.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/health/metrics")).StatusCode);                                // no token: the route "does not exist"
        var wrong = new HttpRequestMessage(HttpMethod.Get, "/health/metrics"); wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token-wrong-token-wrong-token");
        Assert.Equal(HttpStatusCode.NotFound, (await http.SendAsync(wrong)).StatusCode);

        var hr = TestBrowser.Create(fx.Factory); await hr.LoginAsync((await _seed.UserAsync(role: "hr_admin")).Email, Seed.Password);
        var id = Guid.NewGuid(); Assert.Equal(HttpStatusCode.NotFound, (await hr.Get($"/api/hr/employees/{id}")).StatusCode);       // a request whose id must not become a label

        var ok = new HttpRequestMessage(HttpMethod.Get, "/health/metrics"); ok.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.MetricsToken);
        var res = await http.SendAsync(ok); Assert.Equal(HttpStatusCode.OK, res.StatusCode); Assert.StartsWith("text/plain", res.Content.Headers.ContentType!.ToString());
        var text = await res.Content.ReadAsStringAsync();
        Assert.Contains("http_requests_total{method=\"GET\",route=\"/api/hr/employees/{id:guid}\",status=\"4xx\"}", text);
        Assert.DoesNotContain(id.ToString(), text);
        Assert.Contains("http_request_duration_seconds_bucket{method=\"GET\",route=\"/api/hr/employees/{id:guid}\",status=\"4xx\",le=\"+Inf\"}", text);
        foreach (var gauge in new[] { "database_up 1", "outbox_emails_pending ", "webhook_deliveries_dead ", "hr_pay_runs_awaiting_approval ", "process_uptime_seconds " }) Assert.Contains(gauge, text);
        Assert.DoesNotContain("route=\"/health", text);                                                                            // probes and scrapes do not count themselves
    }
}
