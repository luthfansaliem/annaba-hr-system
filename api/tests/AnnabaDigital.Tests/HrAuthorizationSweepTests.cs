using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>
/// A sweep over every /api/hr route (found by reflection over the real routing table, not a hand-kept list):
/// each one must demand a permission, or be one of the few self-service routes named below; nobody signed-out gets in; and an ordinary user is refused (403) at every permission-gated route
/// before any handler runs. A new HR endpoint that forgets its permission fails here.
/// </summary>
[Collection("db")]
public sealed class HrAuthorizationSweepTests(PostgresFixture fx)
{
    private static readonly HashSet<string> SelfService =
    [
        "GET /api/hr/leave/types", "GET /api/hr/leave/holidays", "GET /api/hr/leave/balances", "GET /api/hr/leave/requests", "POST /api/hr/leave/requests", "POST /api/hr/leave/requests/{id}/cancel",
        "GET /api/hr/attendance/me", "POST /api/hr/attendance/clock-in", "POST /api/hr/attendance/clock-out",
        "GET /api/hr/attendance/corrections", "POST /api/hr/attendance/corrections", "POST /api/hr/attendance/corrections/{id}/cancel",
        "GET /api/hr/attendance/overtime", "POST /api/hr/attendance/overtime", "POST /api/hr/attendance/overtime/{id}/cancel",
        "GET /api/hr/payroll/payslips", "GET /api/hr/payroll/payslips/{itemId}", "GET /api/hr/payroll/payslips/{itemId}/pdf", "GET /api/hr/payroll/payouts/mine", "GET /api/hr/payroll/payslips/annual/{year}", "GET /api/hr/payroll/payslips/annual/{year}/pdf",
        "GET /api/hr/announcements/", "POST /api/hr/announcements/{id}/read", "POST /api/hr/announcements/{id}/ack",
        "GET /api/hr/me/", "PATCH /api/hr/me/", "GET /api/hr/me/change-requests", "POST /api/hr/me/change-requests", "POST /api/hr/me/change-requests/{id}/cancel", "GET /api/hr/me/export", "GET /api/hr/me/report-schedules", "POST /api/hr/me/report-schedules", "DELETE /api/hr/me/report-schedules/{id}", "GET /api/hr/employees/{id}/photo", "GET /api/hr/attendance/logs/{id}/photo", "GET /api/hr/me/letters", "POST /api/hr/me/letters", "POST /api/hr/me/letters/{id}/cancel", "GET /api/hr/me/letters/{id}/pdf",
        "GET /api/hr/payroll/payslips/lock", "PUT /api/hr/payroll/payslips/lock",
        "POST /api/hr/attendance/break-start", "POST /api/hr/attendance/break-end", "GET /api/hr/attendance/hourly-leave", "POST /api/hr/attendance/hourly-leave", "POST /api/hr/attendance/hourly-leave/{id}/cancel",
        "GET /api/hr/attendance/roster/mine", "GET /api/hr/attendance/kiosk-pin", "PUT /api/hr/attendance/kiosk-pin", "POST /api/hr/attendance/visits", "GET /api/hr/attendance/visits", "GET /api/hr/projects/", "GET /api/hr/timesheets/week", "PUT /api/hr/timesheets/week",
        "GET /api/hr/recruitment/requisitions", "POST /api/hr/recruitment/requisitions", "POST /api/hr/recruitment/requisitions/{id}/cancel", "GET /api/hr/recruitment/applications/{id}/cv", "GET /api/hr/recruitment/interviews/mine", "POST /api/hr/recruitment/interviews/{id}/feedback",
        "GET /api/hr/learning/mine", "GET /api/hr/learning/team", "GET /api/hr/learning/take/{id}", "POST /api/hr/learning/take/{id}/enroll", "POST /api/hr/learning/take/{id}/lessons/{lessonId}/done", "GET /api/hr/learning/take/{id}/lessons/{lessonId}/file", "POST /api/hr/learning/take/{id}/quiz", "GET /api/hr/learning/certificates/{id}",
        "GET /api/hr/goals/", "POST /api/hr/goals/", "PUT /api/hr/goals/{id}", "POST /api/hr/goals/{id}/check-in", "GET /api/hr/goals/{id}/check-ins", "GET /api/hr/competencies/", "POST /api/hr/review-cycles/{id}/peers",
        "GET /api/hr/reviews/mine", "GET /api/hr/reviews/results", "GET /api/hr/reviews/{id}", "PUT /api/hr/reviews/{id}", "GET /api/hr/talent/plans", "POST /api/hr/talent/plans", "PUT /api/hr/talent/plans/{id}",
        "GET /api/hr/me/custom-fields", "PUT /api/hr/me/custom-fields", "GET /api/hr/me/issued-letters", "GET /api/hr/issued-letters/{id}/pdf", "GET /api/hr/me/forms", "POST /api/hr/me/forms/{id}",
        "GET /api/hr/helpdesk", "POST /api/hr/helpdesk", "GET /api/hr/helpdesk/{id}", "POST /api/hr/helpdesk/{id}/messages", "POST /api/hr/helpdesk/{id}/status", "GET /api/hr/calendar", "GET /api/hr/directory",
        "GET /api/hr/claims/balances", "GET /api/hr/advances/", "POST /api/hr/advances/", "POST /api/hr/advances/{id}/cancel", "POST /api/hr/advances/{id}/settle", "GET /api/hr/advances/{id}/receipt",
        "GET /api/hr/payroll/wage-advances", "GET /api/hr/payroll/wage-advances/available", "POST /api/hr/payroll/wage-advances", "POST /api/hr/payroll/wage-advances/{id}/cancel",
        "GET /api/hr/claims/", "POST /api/hr/claims/", "POST /api/hr/claims/{id}/cancel", "GET /api/hr/claims/{id}/receipt", "GET /api/hr/me/notification-preferences", "PUT /api/hr/me/notification-preferences", "GET /api/hr/me/home", "GET /api/hr/approvals", "POST /api/hr/approvals/bulk", "GET /api/hr/team", "GET /api/hr/me/notifications", "POST /api/hr/me/notifications/{id}/read", "POST /api/hr/me/notifications/read-all",
    ];

    private sealed record Route(string Method, string Pattern, string[] Permissions)
    {
        public string Key => $"{Method} {Regex.Replace(Pattern, @"\{(\w+):\w+\}", "{$1}")}";
        public string Url => Regex.Replace(Pattern, @"\{\w+(:\w+)?\}", _ => Guid.NewGuid().ToString());
    }

    private IEnumerable<Route> Routes() =>
        fx.Factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/hr") == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => new Route(m, e.RoutePattern.RawText!,
                [.. e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).Where(p => p?.StartsWith(PermissionPolicy.Prefix) == true).Select(p => p![PermissionPolicy.Prefix.Length..])])));

    [Fact]
    public void Every_hr_route_requires_a_permission_unless_it_is_a_known_self_service_route()
    {
        var routes = Routes().ToList();
        Assert.True(routes.Count >= 40, $"expected the whole HR surface, found {routes.Count} routes");
        var unguarded = routes.Where(r => r.Permissions.Length == 0).Select(r => r.Key).Order().ToList();
        Assert.Empty(unguarded.Except(SelfService));            // a new unguarded route must be added to the list on purpose
        Assert.Empty(SelfService.Except(routes.Select(r => r.Key))); // and the list must not keep names of routes that no longer exist
    }

    [Fact]
    public async Task Signed_out_callers_get_401_everywhere_and_ordinary_users_get_403_at_every_permission_gated_route()
    {
        var anon = TestBrowser.Create(fx.Factory);
        var seed = new Seed(fx.Factory, fx.Clock); var u = await seed.UserAsync(role: "user"); var plain = TestBrowser.Create(fx.Factory); await plain.LoginAsync(u.Email, Seed.Password);
        await plain.Post("/api/auth/reauth", new { password = Seed.Password }); // a fresh password check must not open any door by itself
        var problems = new List<string>();
        foreach (var r in Routes())
        {
            var body = r.Method == "GET" ? null : new { };
            var a = await anon.SendAsync(new HttpMethod(r.Method), r.Url, body, csrf: false);
            if (a.StatusCode != HttpStatusCode.Unauthorized) problems.Add($"{r.Key}: signed-out got {(int)a.StatusCode}, expected 401");
            if (r.Permissions.Length == 0) continue;
            var p = await plain.SendAsync(new HttpMethod(r.Method), r.Url, body);
            if (p.StatusCode != HttpStatusCode.Forbidden) problems.Add($"{r.Key}: ordinary user got {(int)p.StatusCode}, expected 403");
        }
        Assert.Empty(problems);
    }

    [Fact]
    public async Task Other_peoples_data_is_not_reachable_by_guessing_ids()
    {
        var seed = new Seed(fx.Factory, fx.Clock); var hrUser = await seed.UserAsync(role: "hr_admin");
        var hr = TestBrowser.Create(fx.Factory); await hr.LoginAsync(hrUser.Email, Seed.Password); await hr.Post("/api/auth/reauth", new { password = Seed.Password });
        var w = await seed.UserAsync(role: "user"); var worker = TestBrowser.Create(fx.Factory); await worker.LoginAsync(w.Email, Seed.Password);
        var created = await hr.Post("/api/hr/employees", new { fullName = "Target Person", hireDate = "2023-01-01", jobTitle = "Analyst" });
        var target = (await created.Body()).GetProperty("id").GetGuid();
        // an employee reaching for someone else's record, leave, balances, compensation or payslip gets a refusal or "not found", never data
        foreach (var path in new[] { $"/api/hr/employees/{target}", $"/api/hr/payroll/compensation/{target}", $"/api/hr/leave/balances?employeeId={target}", $"/api/hr/payroll/payslips/{Guid.NewGuid()}", $"/api/hr/leave/requests?scope=all" })
        {
            var res = await worker.Get(path);
            Assert.Contains(res.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.Conflict });
        }
    }

    [Fact]
    public async Task Liveness_and_readiness_answer_without_signing_in_and_readiness_checks_the_database()
    {
        var b = TestBrowser.Create(fx.Factory); b.SendProxyKey = false; // probes come from the platform, not through the UI proxy
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/health")).StatusCode);
        var ready = await b.Get("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode); Assert.Equal("ready", (await ready.Body()).GetProperty("status").GetString());
    }
}
