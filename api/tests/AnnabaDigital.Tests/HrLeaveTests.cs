using System.Net;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class HrLeaveTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private DateOnly Today => DateOnly.FromDateTime(fx.Clock.UtcNow.UtcDateTime.AddHours(7));
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    /// <summary>
    /// The next Monday at least a week away whose four-week window stays in the current calendar year (a request cannot span two years and annual leave is granted per year).
    /// Overlaps only matter per employee and every test uses its own employees, so tests can share the date. In the last five weeks of a year it falls back to the nearest Monday
    /// that still fits, which keeps the tests valid but means the "cancel future leave" case runs against a past date then.
    /// </summary>
    private DateOnly Monday()
    {
        var d = Today.AddDays(7); while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1);
        while (d.AddDays(28).Year != d.Year) d = d.AddDays(-7);
        return d;
    }

    private async Task<TestBrowser> Login(string email, bool reauth = false)
    {
        var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return b;
    }

    private sealed record Person(TestBrowser B, Guid EmployeeId, string Email);

    private async Task<TestBrowser> Hr() => await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true);

    private async Task<Person> Worker(TestBrowser hr, string role = "user", Guid? manager = null, int hiredYearsAgo = 2, int hiredMonthsAgo = 0)
    {
        var u = await _seed.UserAsync(role: role);
        var hire = Today.AddYears(-hiredYearsAgo).AddMonths(-hiredMonthsAgo);
        var r = await hr.Post("/api/hr/employees", new { fullName = "Worker " + Guid.NewGuid().ToString("N")[..6], hireDate = D(hire), jobTitle = "Analyst", managerId = manager, userId = u.Id });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return new Person(await Login(u.Email), (await r.Body()).GetProperty("id").GetGuid(), u.Email);
    }

    private static async Task<System.Text.Json.JsonElement> Type(TestBrowser b, string code)
        => (await (await b.Get("/api/hr/leave/types")).Body()).EnumerateArray().First(t => t.GetProperty("code").GetString() == code);

    private static async Task<HttpResponseMessage> Request(TestBrowser b, string code, DateOnly s, DateOnly e, bool half = false, string? reason = null)
        => await b.Post("/api/hr/leave/requests", new { leaveTypeId = (await Type(b, code)).GetProperty("id").GetGuid(), startDate = D(s), endDate = D(e), halfDay = half, reason });

    private static async Task<decimal> Available(TestBrowser b)
        => (await (await b.Get("/api/hr/leave/balances")).Body()).EnumerateArray().First(x => x.GetProperty("code").GetString() == "ANNUAL").GetProperty("available").GetDecimal();

    [Fact]
    public async Task Annual_leave_is_granted_after_twelve_months_of_service_only()
    {
        var hr = await Hr();
        var veteran = await Worker(hr); var newcomer = await Worker(hr, hiredYearsAgo: 0, hiredMonthsAgo: 3);
        Assert.Equal(12m, await Available(veteran.B));
        Assert.Equal(0m, await Available(newcomer.B)); // not eligible yet: no grant row
        var r = await Request(newcomer.B, "ANNUAL", Monday(), Monday());
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Request(newcomer.B, "SICK", Monday(), Monday())).StatusCode); // sick leave has no balance
    }

    [Fact]
    public async Task Concurrent_balance_reads_grant_the_yearly_entitlement_only_once()
    {
        var hr = await Hr(); var w = await Worker(hr);
        var tabs = new[] { w.B, await Login(w.Email), await Login(w.Email), await Login(w.Email) };
        var all = await Task.WhenAll(tabs.Select(t => t.Get("/api/hr/leave/balances")));
        Assert.All(all, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(12m, await Available(w.B));
        Assert.Equal(1, await _seed.Db(db => db.LeaveLedger.CountAsync(l => l.EmployeeId == w.EmployeeId && l.Kind == LedgerKind.Grant)));
    }

    [Fact]
    public async Task Working_days_skip_weekends_and_holidays_and_pending_days_are_reserved()
    {
        var hr = await Hr(); var w = await Worker(hr); var mon = Monday();
        var holiday = await hr.Post("/api/hr/leave/holidays", new { date = D(mon.AddDays(2)), name = "Test holiday", collective = false });
        Assert.Equal(HttpStatusCode.Created, holiday.StatusCode);

        var r = await Request(w.B, "ANNUAL", mon, mon.AddDays(6)); // Mon..Sun: Mon Tue Thu Fri = 4 (Wed is a holiday)
        Assert.Equal(HttpStatusCode.Created, r.StatusCode); Assert.Equal(4m, (await r.Body()).GetProperty("days").GetDecimal());
        Assert.Equal(8m, await Available(w.B)); // reserved while pending

        Assert.Equal(HttpStatusCode.Conflict, (await Request(w.B, "ANNUAL", mon.AddDays(3), mon.AddDays(3))).StatusCode); // overlaps the pending request
        var weekend = await Request(w.B, "ANNUAL", mon.AddDays(12), mon.AddDays(13)); // a Saturday and Sunday
        Assert.Equal(HttpStatusCode.UnprocessableEntity, weekend.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await hr.Post("/api/hr/leave/holidays", new { date = D(mon.AddDays(2)), name = "Again" })).StatusCode);
    }

    [Fact]
    public async Task A_request_cannot_exceed_the_balance_span_years_or_misuse_half_days()
    {
        var hr = await Hr(); var w = await Worker(hr); var mon = Monday();
        var tooLong = await Request(w.B, "ANNUAL", mon, mon.AddDays(27)); // 4 weeks = 20 working days > 12
        Assert.Equal(HttpStatusCode.Conflict, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Request(w.B, "ANNUAL", mon, mon.AddDays(1), half: true)).StatusCode); // half day = one day only
        var half = await Request(w.B, "ANNUAL", mon, mon, half: true);
        Assert.Equal(0.5m, (await half.Body()).GetProperty("days").GetDecimal());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Request(w.B, "ANNUAL", new DateOnly(Today.Year, 12, 29), new DateOnly(Today.Year + 1, 1, 2))).StatusCode); // spans two years
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Request(w.B, "PATERNITY", mon.AddDays(14), mon.AddDays(18))).StatusCode); // type allows at most 2 days
    }

    [Fact]
    public async Task Only_the_direct_manager_or_hr_can_decide_never_for_oneself_and_each_request_is_decided_once()
    {
        var hr = await Hr();
        var boss = await Worker(hr, role: "manager"); var other = await Worker(hr, role: "manager");
        var staff = await Worker(hr, manager: boss.EmployeeId); var mon = Monday();
        var id = (await (await Request(staff.B, "ANNUAL", mon, mon.AddDays(1))).Body()).GetProperty("id").GetGuid();

        var team = await (await boss.B.Get("/api/hr/leave/requests?scope=team&status=pending")).Body();
        Assert.Contains(team.GetProperty("data").EnumerateArray(), r => r.GetProperty("id").GetGuid() == id && r.GetProperty("canDecide").GetBoolean());
        Assert.Empty((await (await other.B.Get("/api/hr/leave/requests?scope=team")).Body()).GetProperty("data").EnumerateArray().Where(r => r.GetProperty("id").GetGuid() == id));
        Assert.Equal(HttpStatusCode.Forbidden, (await other.B.Post($"/api/hr/leave/requests/{id}/approve")).StatusCode); // a manager of someone else
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.B.Post($"/api/hr/leave/requests/{id}/approve")).StatusCode); // no permission at all
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await boss.B.Post($"/api/hr/leave/requests/{id}/reject", new { })).StatusCode); // rejection needs a reason

        Assert.Equal(HttpStatusCode.OK, (await boss.B.Post($"/api/hr/leave/requests/{id}/approve", new { note = "Enjoy" })).StatusCode);
        Assert.Equal(10m, await Available(staff.B));
        Assert.Equal(HttpStatusCode.Conflict, (await boss.B.Post($"/api/hr/leave/requests/{id}/approve")).StatusCode);

        // the manager cannot approve their own request, even though the role could decide others'
        var own = (await (await Request(boss.B, "ANNUAL", mon.AddDays(14), mon.AddDays(14))).Body()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await boss.B.Post($"/api/hr/leave/requests/{own}/approve")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hr.Post($"/api/hr/leave/requests/{own}/approve")).StatusCode); // HR can
    }

    [Fact]
    public async Task Cancelling_approved_future_leave_restores_the_days_and_strangers_cannot_see_requests()
    {
        var hr = await Hr(); var boss = await Worker(hr, role: "manager"); var staff = await Worker(hr, manager: boss.EmployeeId); var stranger = await Worker(hr); var mon = Monday();
        var id = (await (await Request(staff.B, "ANNUAL", mon, mon.AddDays(2))).Body()).GetProperty("id").GetGuid();
        await boss.B.Post($"/api/hr/leave/requests/{id}/approve");
        Assert.Equal(9m, await Available(staff.B));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.B.Post($"/api/hr/leave/requests/{id}/cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.B.Post($"/api/hr/leave/requests/{id}/cancel")).StatusCode);
        Assert.Equal(12m, await Available(staff.B));
        Assert.Equal(HttpStatusCode.Conflict, (await staff.B.Post($"/api/hr/leave/requests/{id}/cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.B.Get("/api/hr/leave/requests?scope=all")).StatusCode);
    }

    [Fact]
    public async Task Two_simultaneous_requests_for_the_same_days_cannot_both_succeed()
    {
        var hr = await Hr(); var w = await Worker(hr); var mon = Monday();
        var second = await Login(w.Email); var type = (await Type(w.B, "ANNUAL")).GetProperty("id").GetGuid();
        var body = new { leaveTypeId = type, startDate = D(mon), endDate = D(mon.AddDays(1)) };
        var results = await Task.WhenAll(w.B.Post("/api/hr/leave/requests", body), second.Post("/api/hr/leave/requests", body));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(10m, await Available(w.B));
    }

    [Fact]
    public async Task Balance_adjustments_need_a_note_and_a_recent_password_check_and_are_audited()
    {
        var hr = await Hr(); var w = await Worker(hr); var type = (await Type(hr, "ANNUAL")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/leave/balances/adjust", new { employeeId = w.EmployeeId, leaveTypeId = type, days = 2 })).StatusCode);
        var ok = await hr.Post("/api/hr/leave/balances/adjust", new { employeeId = w.EmployeeId, leaveTypeId = type, days = 2, note = "Compensation for working on a holiday" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode); Assert.Equal(14m, await Available(w.B));
        Assert.Equal(HttpStatusCode.Forbidden, (await w.B.Post("/api/hr/leave/balances/adjust", new { employeeId = w.EmployeeId, leaveTypeId = type, days = 5, note = "me" })).StatusCode);
        Assert.True(await _seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "hr.leave.adjust" && a.TargetId == w.EmployeeId.ToString())));
        var staleHr = await Login((await _seed.UserAsync(role: "hr_admin")).Email); // no recent password check
        var denied = await staleHr.Post("/api/hr/leave/balances/adjust", new { employeeId = w.EmployeeId, leaveTypeId = type, days = 1, note = "x" });
        Assert.Equal("reauth_required", await denied.ErrorCode());
    }

    [Fact]
    public async Task Clocking_in_and_out_uses_the_server_clock_and_allows_one_open_session()
    {
        var hr = await Hr(); var w = await Worker(hr);
        Assert.Equal(HttpStatusCode.Conflict, (await w.B.Post("/api/hr/attendance/clock-out")).StatusCode); // not clocked in
        Assert.Equal(HttpStatusCode.OK, (await w.B.Post("/api/hr/attendance/clock-in", new { clockIn = "2000-01-01T00:00:00Z" })).StatusCode); // a client-supplied time is ignored
        Assert.Equal(HttpStatusCode.Conflict, (await w.B.Post("/api/hr/attendance/clock-in")).StatusCode);
        var open = await (await w.B.Get("/api/hr/attendance/me")).Body();
        Assert.NotEqual(JsonKindNull, open.GetProperty("open").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await w.B.Post("/api/hr/attendance/clock-out")).StatusCode);
        var after = await (await w.B.Get("/api/hr/attendance/me")).Body();
        Assert.Equal(JsonKindNull, after.GetProperty("open").ValueKind); Assert.Equal(1, after.GetProperty("daysPresent").GetInt32());
        Assert.DoesNotContain("2000-01-01", (await (await w.B.Get("/api/hr/attendance/me")).Content.ReadAsStringAsync()));

        Assert.Equal(HttpStatusCode.Forbidden, (await w.B.Get("/api/hr/attendance/report")).StatusCode);
        var rows = await (await hr.Get("/api/hr/attendance/report")).Body();
        Assert.Contains(rows.EnumerateArray(), r => r.GetProperty("employeeId").GetGuid() == w.EmployeeId && r.GetProperty("daysPresent").GetInt32() == 1);
        var loose = await Login((await _seed.UserAsync(role: "user")).Email); // an account with no employee record
        Assert.Equal(HttpStatusCode.Conflict, (await loose.Post("/api/hr/attendance/clock-in")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await loose.Get("/api/hr/leave/balances")).StatusCode);
    }

    private const System.Text.Json.JsonValueKind JsonKindNull = System.Text.Json.JsonValueKind.Null;
}
