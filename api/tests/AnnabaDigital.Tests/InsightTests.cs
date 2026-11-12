using System.Net;
using System.Text.Json;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Stage 19: turnover and overtime figures, the rule-based at-risk list, and the report builder (filters, grouping, pivot, permissions, CSV, saved reports).</summary>
[Collection("db")]
public sealed class InsightTests(PostgresFixture fx)
{
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    [Fact]
    public async Task Turnover_overtime_at_risk_and_the_report_builder_on_a_small_company()
    {
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));
        async Task<TestBrowser> Login(string email) { var b = TestBrowser.Create(f); await b.LoginAsync(email, Seed.Password); await b.Post("/api/auth/reauth", new { password = Seed.Password }); return b; }
        var hr = await Login((await seed.UserAsync(role: "hr_admin")).Email);
        async Task<Guid> Unit(string code, string name) => (await (await hr.Post("/api/hr/org-units", new { code, name })).Body()).GetProperty("id").GetGuid();
        async Task<Guid> Emp(string name, DateOnly hire, Guid unit, string type = "permanent", DateOnly? end = null, Guid? userId = null)
        {
            var r = await hr.Post("/api/hr/employees", new { fullName = name, hireDate = D(hire), jobTitle = "Engineer", orgUnitId = unit, type, contractEnd = end is { } e ? D(e) : null, userId });
            Assert.Equal(HttpStatusCode.Created, r.StatusCode); return (await r.Body()).GetProperty("id").GetGuid();
        }
        var eng = await Unit("ENG", "Engineering"); var ops = await Unit("OPS", "Operations");
        var a = await Emp("Ana", today.AddYears(-4), eng); var b = await Emp("Budi", today.AddYears(-2), eng); var c = await Emp("Citra", today.AddMonths(-3), ops);
        var contractor = await Emp("Dewi", today.AddMonths(-20), ops, "contract", today.AddDays(30));
        var gone1 = await Emp("Eko", today.AddMonths(-8), eng); var gone2 = await Emp("Fajar", today.AddYears(-3), ops);
        Assert.Equal(HttpStatusCode.OK, (await hr.Post($"/api/hr/employees/{gone1}/terminate", new { date = D(today.AddDays(-20)), reason = "Resigned" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hr.Post($"/api/hr/employees/{gone2}/terminate", new { date = D(today.AddDays(-40)), reason = "Resigned" })).StatusCode);

        var t = await (await hr.Get("/api/hr/analytics/turnover")).Body();
        Assert.Equal(2, t.GetProperty("leavers12").GetInt32()); Assert.Equal(50m, t.GetProperty("earlyLeaverPct").GetDecimal());   // Eko left within a year
        Assert.Equal(2, t.GetProperty("byReason").EnumerateArray().Single(x => x.GetProperty("key").GetString() == "Resigned").GetProperty("count").GetInt32());
        Assert.Equal(12, t.GetProperty("months").GetArrayLength());

        // overtime: a worker asks, HR approves
        var wu = await seed.UserAsync(role: "user"); var w = await Emp("Gita", today.AddYears(-1), eng, userId: wu.Id); var wb = await Login(wu.Email);
        var ot = await (await wb.Post("/api/hr/attendance/overtime", new { date = D(today.AddDays(-1)), hours = 3, reason = "Release" })).Body();
        Assert.Equal(HttpStatusCode.OK, (await hr.Post($"/api/hr/attendance/overtime/{ot.GetProperty("id").GetGuid()}/approve")).StatusCode);
        var o = await (await hr.Get("/api/hr/analytics/overtime")).Body();
        Assert.Equal(3m, o.GetProperty("hours12").GetDecimal()); Assert.Equal("Gita", o.GetProperty("top")[0].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, o.GetProperty("payThisYear").ValueKind);   // HR admin cannot see payroll amounts

        // at risk: the contractor whose contract ends next month and who is new-ish; the new joiner Citra only medium at most
        var risk = (await (await hr.Get("/api/hr/analytics/at-risk")).Body()).EnumerateArray().ToList();
        var dewi = risk.Single(x => x.GetProperty("employeeId").GetGuid() == contractor);
        Assert.Contains(dewi.GetProperty("signals").EnumerateArray(), s => s.GetProperty("code").GetString() == "contract_ending");
        Assert.DoesNotContain(risk, x => x.GetProperty("employeeId").GetGuid() == a);
        Assert.Equal(HttpStatusCode.Forbidden, (await wb.Get("/api/hr/analytics/at-risk")).StatusCode);

        // report builder: count active employees per department, pivot by type, filter, CSV, permissions
        var ds = (await (await hr.Get("/api/hr/reports/datasets")).Body()).EnumerateArray().Select(x => x.GetProperty("key").GetString()).ToList();
        Assert.Contains("employees", ds); Assert.Contains("claims", ds);
        Assert.Empty((await (await wb.Get("/api/hr/reports/datasets")).Body()).EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await wb.Post("/api/hr/reports/run", new { dataset = "employees" })).StatusCode);
        var grouped = await (await hr.Post("/api/hr/reports/run", new { dataset = "employees", filters = new[] { new { field = "status", op = "eq", value = "active" } }, groupBy = "department" })).Body();
        var rows = grouped.GetProperty("rows").EnumerateArray().ToDictionary(r => r[0].GetString()!, r => r[1].GetDecimal());
        Assert.Equal(3m, rows["Engineering"]); Assert.Equal(2m, rows["Operations"]);
        var pivot = await (await hr.Post("/api/hr/reports/run", new { dataset = "employees", groupBy = "department", pivotBy = "type" })).Body();
        Assert.Equal(new[] { "Department", "contract", "permanent", "Total" }, pivot.GetProperty("headers").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Equal(7m, pivot.GetProperty("rows").EnumerateArray().Last()[3].GetDecimal());   // grand total, everyone incl. leavers
        var avg = await (await hr.Post("/api/hr/reports/run", new { dataset = "employees", groupBy = "department", measure = "avg", measureField = "tenureYears", filters = new[] { new { field = "name", op = "contains", value = "an" } } })).Body();
        Assert.Contains("Average", avg.GetProperty("headers")[1].GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/reports/run", new { dataset = "employees", measure = "sum", measureField = "name", groupBy = "department" })).StatusCode);
        var list = await (await hr.Post("/api/hr/reports/run", new { dataset = "employees", columns = new[] { "name", "hireDate" }, sortBy = "hireDate", limit = 2 })).Body();
        Assert.True(list.GetProperty("truncated").GetBoolean()); Assert.Equal("Ana", list.GetProperty("rows")[0][0].GetString());
        var csv = await hr.Post("/api/hr/reports/export", new { dataset = "employees", columns = new[] { "name", "department" } });
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode); Assert.Contains("Name,Department", await csv.Content.ReadAsStringAsync());

        var saved = await (await hr.Post("/api/hr/reports/saved", new { name = "Headcount by department", shared = true, config = new { dataset = "employees", groupBy = "department" } })).Body();
        var other = await Login((await seed.UserAsync(role: "hr_admin")).Email);
        var seen = (await (await other.Get("/api/hr/reports/saved")).Body()).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == saved.GetProperty("id").GetGuid());
        Assert.False(seen.GetProperty("mine").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await other.SendAsync(HttpMethod.Delete, $"/api/hr/reports/saved/{saved.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Empty((await (await wb.Get("/api/hr/reports/saved")).Body()).EnumerateArray());   // shared, but not with people who cannot see the data
        _ = b; _ = c; _ = w;
    }
}
