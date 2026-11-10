using System.Net;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class HrDashboardAnnouncementTests(PostgresFixture fx)
{
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");
    private static async Task<TestBrowser> As(ApiFactory f, Seed seed, string role, bool reauth = false)
    {
        var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return b;
    }

    [Fact]
    public async Task The_dashboard_counts_headcount_movement_departments_and_queues_and_is_for_people_who_may_read_employees()
    {
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock); var hr = await As(f, seed, "hr_admin", reauth: true); var t = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));
        async Task<Guid> Unit(string code, string name) => (await (await hr.Post("/api/hr/org-units", new { code, name })).Body()).GetProperty("id").GetGuid();
        var a = await Unit("AAA", "Alpha"); var b = await Unit("BBB", "Beta");
        async Task<Guid> Emp(string name, string hire, Guid unit, string type = "permanent", string? gender = null, string? end = null)
            => (await (await hr.Post("/api/hr/employees", new { fullName = name, hireDate = hire, jobTitle = "Analyst", orgUnitId = unit, type, contractEnd = end, gender })).Body()).GetProperty("id").GetGuid();
        await Emp("One", D(t.AddYears(-4)), a, gender: "female"); await Emp("Two", D(t.AddDays(-10)), a, gender: "male"); await Emp("Three", D(t.AddMonths(-8)), b, "contract", end: D(t.AddDays(30)));
        var gone = await Emp("Four", D(t.AddYears(-2)), b); Assert.Equal(HttpStatusCode.OK, (await hr.Post($"/api/hr/employees/{gone}/terminate", new { reason = "Resigned" })).StatusCode);

        var d = await (await (await As(f, seed, "hr_head")).Get("/api/hr/dashboard")).Body();
        Assert.Equal(3, d.GetProperty("headcount").GetInt32()); Assert.Equal(1, d.GetProperty("joiners30").GetInt32()); Assert.Equal(1, d.GetProperty("leavers12").GetInt32());
        Assert.True(d.GetProperty("turnoverPct").GetDecimal() > 0); Assert.Equal(1, d.GetProperty("contractsEnding60").GetInt32()); Assert.Equal(3, d.GetProperty("noCompensation").GetInt32()); Assert.Equal(3, d.GetProperty("noLinkedAccount").GetInt32());
        var dept = d.GetProperty("byDepartment").EnumerateArray().ToDictionary(x => x.GetProperty("key").GetString()!, x => x.GetProperty("count").GetInt32());
        Assert.Equal(2, dept["Alpha"]); Assert.Equal(1, dept["Beta"]);
        Assert.Equal(12, d.GetProperty("trend").GetArrayLength());
        var last = d.GetProperty("trend")[11]; Assert.Equal(3, last.GetProperty("headcount").GetInt32()); Assert.Equal(1, last.GetProperty("leavers").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As(f, seed, "user")).Get("/api/hr/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Announcements_reach_their_audience_policies_need_an_acknowledgement_and_hr_can_see_who_has_not_acknowledged()
    {
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock); var hr = await As(f, seed, "hr_admin"); var t = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));
        var alpha = (await (await hr.Post("/api/hr/org-units", new { code = "ALP", name = "Alpha" })).Body()).GetProperty("id").GetGuid(); var beta = (await (await hr.Post("/api/hr/org-units", new { code = "BET", name = "Beta" })).Body()).GetProperty("id").GetGuid();
        async Task<(TestBrowser B, Guid E)> Worker(Guid unit)
        {
            var u = await seed.UserAsync(role: "user"); var r = await hr.Post("/api/hr/employees", new { fullName = "W " + Guid.NewGuid().ToString("N")[..5], hireDate = D(t.AddYears(-2)), jobTitle = "Analyst", orgUnitId = unit, userId = u.Id });
            var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (b, (await r.Body()).GetProperty("id").GetGuid());
        }
        var (wa, _) = await Worker(alpha); var (wb, _) = await Worker(beta);

        Assert.Equal(HttpStatusCode.Forbidden, (await wa.Post("/api/hr/announcements", new { title = "Hack", body = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/announcements", new { title = "", body = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/announcements", new { title = "Old", body = "x", expiresOn = D(t.AddDays(-1)) })).StatusCode);
        var policy = (await (await hr.Post("/api/hr/announcements", new { title = "Code of conduct", body = "Please read.", requiresAck = true })).Body()).GetProperty("id").GetGuid();
        var memo = (await (await hr.Post("/api/hr/announcements", new { title = "Alpha offsite", body = "Friday.", orgUnitId = alpha })).Body()).GetProperty("id").GetGuid();

        var forA = (await (await wa.Get("/api/hr/announcements")).Body()).EnumerateArray().Select(x => x.GetProperty("title").GetString()).ToList();
        var forB = (await (await wb.Get("/api/hr/announcements")).Body()).EnumerateArray().Select(x => x.GetProperty("title").GetString()).ToList();
        Assert.Contains("Alpha offsite", forA); Assert.DoesNotContain("Alpha offsite", forB); Assert.Contains("Code of conduct", forB);
        Assert.Equal(HttpStatusCode.NotFound, (await wb.Post($"/api/hr/announcements/{memo}/read")).StatusCode);                // another department's memo is not theirs to open

        Assert.Equal(HttpStatusCode.NoContent, (await wa.Post($"/api/hr/announcements/{memo}/read")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await wa.Post($"/api/hr/announcements/{memo}/ack")).StatusCode);                  // nothing to acknowledge
        Assert.Equal(HttpStatusCode.NoContent, (await wa.Post($"/api/hr/announcements/{policy}/ack")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await wa.Post($"/api/hr/announcements/{policy}/ack")).StatusCode);               // idempotent
        Assert.True((await (await wa.Get("/api/hr/announcements")).Body()).EnumerateArray().First(x => x.GetProperty("title").GetString() == "Code of conduct").GetProperty("acked").GetBoolean());

        var report = await (await hr.Get($"/api/hr/announcements/{policy}/report")).Body();
        Assert.Equal(2, report.GetProperty("audience").GetInt32()); Assert.Equal(1, report.GetProperty("acked").GetInt32());
        Assert.Equal(1, (await (await hr.Get($"/api/hr/announcements/{memo}/report")).Body()).GetProperty("audience").GetInt32());   // only Alpha people
        Assert.Equal(HttpStatusCode.Forbidden, (await wa.Get($"/api/hr/announcements/{policy}/report")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await hr.SendAsync(HttpMethod.Patch, $"/api/hr/announcements/{policy}", new { archived = true })).StatusCode);
        Assert.DoesNotContain("Code of conduct", (await (await wb.Get("/api/hr/announcements")).Body()).EnumerateArray().Select(x => x.GetProperty("title").GetString()));
        Assert.True(await seed.Db(db => db.AuditEvents.AnyAsync(x => x.Action == "hr.announcement.ack")));
    }
}
