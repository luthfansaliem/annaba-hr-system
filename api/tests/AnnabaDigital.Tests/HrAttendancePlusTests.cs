using System.Net;
using System.Net.Http.Json;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Stage 13: breaks, hourly leave, roster, on-call, timesheets, the kiosk and field visits.</summary>
[Collection("db")]
public sealed class HrAttendancePlusTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private DateOnly Today => DateOnly.FromDateTime(fx.Clock.UtcNow.UtcDateTime.AddHours(7));
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");
    private static DateOnly Monday(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    private async Task<TestBrowser> Login(string email, bool reauth = false)
    {
        var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return b;
    }
    private sealed record Person(TestBrowser B, Guid EmployeeId, string No);
    private async Task<Person> Worker(TestBrowser hr, string role = "user", Guid? manager = null)
    {
        var u = await _seed.UserAsync(role: role); var no = "K" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var r = await hr.Post("/api/hr/employees", new { employeeNo = no, fullName = "Shift " + no, hireDate = D(Today.AddYears(-2)), jobTitle = "Operator", managerId = manager, userId = u.Id });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return new Person(await Login(u.Email), (await r.Body()).GetProperty("id").GetGuid(), no);
    }

    [Fact]
    public async Task Breaks_are_left_out_of_worked_hours()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true); var w = await Worker(hr);
        Assert.Equal(HttpStatusCode.OK, (await w.B.Post("/api/hr/attendance/clock-in")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.B.Post("/api/hr/attendance/break-start")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await w.B.Post("/api/hr/attendance/break-start")).StatusCode);
        async Task Pass(int minutes) { for (var i = 0; i < minutes / 5; i++) { fx.Clock.Advance(TimeSpan.FromMinutes(5)); await w.B.Get("/api/hr/attendance/me"); } }   // keep the session active while time passes
        await Pass(45);
        var back = await (await w.B.Post("/api/hr/attendance/break-end")).Body(); Assert.Equal(45, back.GetProperty("breakMinutes").GetInt32());
        await Pass(75);
        var done = await (await w.B.Post("/api/hr/attendance/clock-out")).Body();
        Assert.Equal(1.25m, done.GetProperty("hours").GetDecimal());                                  // 2 h on the clock, 45 min of it on a break
    }

    [Fact]
    public async Task Hourly_leave_is_decided_by_the_manager_and_unpaid_hours_are_deducted_in_the_pay_run()
    {
        // an isolated database: the pay run needs every earlier month of the year locked, which other tests sharing a database would not guarantee
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock); var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));
        async Task<TestBrowser> As(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); await b.Post("/api/auth/reauth", new { password = Seed.Password }); return b; }
        var hr = await As("hr_admin"); var officer = await As("payroll_officer"); var approver = await As("payroll_approver");
        async Task<(TestBrowser B, Guid Id)> Emp(string role, Guid? manager)
        {
            var u = await seed.UserAsync(role: role); var r = await hr.Post("/api/hr/employees", new { fullName = "Hour " + Guid.NewGuid().ToString("N")[..5], hireDate = D(today.AddYears(-2)), jobTitle = "Operator", managerId = manager, userId = u.Id, bankName = "BCA", bankAccount = "123" });
            var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (b, (await r.Body()).GetProperty("id").GetGuid());
        }
        var boss = await Emp("manager", null); var w = await Emp("user", boss.Id); var other = await Emp("manager", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await w.B.Post("/api/hr/attendance/hourly-leave", new { date = D(today), from = "15:00", to = "14:00", reason = "x" })).StatusCode);
        var h = await (await w.B.Post("/api/hr/attendance/hourly-leave", new { date = D(today), from = "14:00", to = "16:00", paid = false, reason = "Bank errand" })).Body();
        Assert.Equal(2m, h.GetProperty("hours").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict, (await w.B.Post("/api/hr/attendance/hourly-leave", new { date = D(today), from = "15:00", to = "17:00", reason = "Overlap" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.B.Post($"/api/hr/attendance/hourly-leave/{h.GetProperty("id").GetGuid()}/decide", new { approve = true })).StatusCode);
        Assert.Equal("approved", (await (await boss.B.Post($"/api/hr/attendance/hourly-leave/{h.GetProperty("id").GetGuid()}/decide", new { approve = true })).Body()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await hr.Post("/api/hr/attendance/on-call", new { employeeId = w.Id, dates = new[] { D(today) }, allowance = 150_000 })).StatusCode);   // HR plans on-call days
        Assert.Equal(HttpStatusCode.OK, (await officer.SendAsync(HttpMethod.Put, $"/api/hr/payroll/compensation/{w.Id}", new { effectiveFrom = D(today.AddYears(-1)), basic = 10_000_000, ptkpStatus = "TK0" })).StatusCode);
        for (var m = 1; m < today.Month; m++)
        {
            var e = (await (await officer.Post("/api/hr/payroll/runs", new { year = today.Year, month = m })).Body()).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await officer.Post($"/api/hr/payroll/runs/{e}/calculate")).StatusCode);
            await approver.Post($"/api/hr/payroll/runs/{e}/approve"); await approver.Post($"/api/hr/payroll/runs/{e}/lock");
        }
        var run = (await (await officer.Post("/api/hr/payroll/runs", new { year = today.Year, month = today.Month })).Body()).GetProperty("id").GetGuid();
        var body = await (await officer.Post($"/api/hr/payroll/runs/{run}/calculate")).Body();
        // 2 h away at 10,000,000 / 173 an hour = 115,606.94 → 115,607
        var item = body.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("employeeId").GetGuid() == w.Id);
        Assert.Equal(115_607m, item.GetProperty("unpaidDeduction").GetDecimal());
        Assert.Contains(body.GetProperty("lines").EnumerateArray(), l => l.GetProperty("employeeId").GetGuid() == w.Id && l.GetProperty("label").GetString()!.StartsWith("On-call allowance (1 day") && l.GetProperty("amount").GetDecimal() == 150_000m);
    }

    [Fact]
    public async Task A_planned_roster_day_overrides_the_standing_shift_and_a_pattern_fills_a_period()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true); var w = await Worker(hr);
        var mgr = await Login((await _seed.UserAsync(role: "super_admin")).Email, reauth: true);
        var night = (await (await mgr.Post("/api/hr/shifts", new { code = "N" + Guid.NewGuid().ToString("N")[..4], name = "Night", startTime = "22:00", endTime = "06:00", graceMinutes = 10, workDays = 127 })).Body()).GetProperty("id").GetGuid();
        var mon = Monday(Today.AddDays(14));
        var r = await mgr.Post("/api/hr/attendance/roster/pattern", new { employeeIds = new[] { w.EmployeeId }, from = D(mon), to = D(mon.AddDays(5)), pattern = new Guid?[] { night, night, null } });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); Assert.Equal(6, (await r.Body()).GetProperty("changed").GetInt32());
        var roster = await (await w.B.Get($"/api/hr/attendance/roster/mine?from={D(mon)}&to={D(mon.AddDays(6))}")).Body();
        var days = roster[0].GetProperty("days").EnumerateArray().ToList();
        Assert.True(days[0].GetProperty("works").GetBoolean()); Assert.Equal(night, days[0].GetProperty("shiftId").GetGuid());
        Assert.False(days[2].GetProperty("works").GetBoolean()); Assert.True(days[2].GetProperty("planned").GetBoolean());   // a planned day off on a Wednesday
        Assert.False(days[6].GetProperty("planned").GetBoolean());                                                     // Sunday follows the standing shift again
        Assert.Equal(HttpStatusCode.Forbidden, (await w.B.Post("/api/hr/attendance/roster/pattern", new { employeeIds = new[] { w.EmployeeId }, from = D(mon), to = D(mon), pattern = new Guid?[] { null } })).StatusCode);
    }

    [Fact]
    public async Task A_timesheet_week_is_handed_in_frozen_and_approved_by_the_manager()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true); var boss = await Worker(hr, "manager"); var w = await Worker(hr, manager: boss.EmployeeId);
        var mgr = await Login((await _seed.UserAsync(role: "super_admin")).Email, reauth: true);
        var proj = await (await mgr.Post("/api/hr/projects", new { code = "P" + Guid.NewGuid().ToString("N")[..5], name = "Website", client = "Acme" })).Body(); var pid = proj.GetProperty("id").GetGuid();
        var mon = Monday(Today);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await w.B.SendAsync(HttpMethod.Put, $"/api/hr/timesheets/week?weekStart={D(mon)}", new { lines = new[] { new { projectId = pid, date = D(mon.AddDays(8)), hours = 2 } } })).StatusCode);   // outside the week
        var saved = await (await w.B.SendAsync(HttpMethod.Put, $"/api/hr/timesheets/week?weekStart={D(mon)}&submit=true", new { lines = new[] { new { projectId = pid, date = D(mon), hours = 6.5m }, new { projectId = pid, date = D(mon.AddDays(1)), hours = 8m } } })).Body();
        Assert.Equal("submitted", saved.GetProperty("status").GetString()); Assert.Equal(14.5m, saved.GetProperty("total").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict, (await w.B.SendAsync(HttpMethod.Put, $"/api/hr/timesheets/week?weekStart={D(mon)}", new { lines = Array.Empty<object>() })).StatusCode);   // handed in = frozen
        var pending = await (await boss.B.Get("/api/hr/timesheets/pending")).Body(); Assert.Contains(pending.EnumerateArray(), x => x.GetProperty("employeeId").GetGuid() == w.EmployeeId);
        var ok = await (await boss.B.Post("/api/hr/timesheets/decide", new { employeeId = w.EmployeeId, weekStart = D(mon), approve = true })).Body();
        Assert.Equal("approved", ok.GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_kiosk_needs_a_registered_device_and_the_right_PIN_and_locks_after_wrong_tries()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true); var w = await Worker(hr);
        var mgr = await Login((await _seed.UserAsync(role: "super_admin")).Email, reauth: true);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await w.B.SendAsync(HttpMethod.Put, "/api/hr/attendance/kiosk-pin", new { pin = "1234" })).StatusCode);   // a straight run is refused
        Assert.Equal(HttpStatusCode.NoContent, (await w.B.SendAsync(HttpMethod.Put, "/api/hr/attendance/kiosk-pin", new { pin = "4815" })).StatusCode);
        var dev = await (await mgr.Post("/api/hr/attendance/kiosks", new { name = "Lobby tablet" })).Body(); var token = dev.GetProperty("token").GetString()!;
        var kiosk = TestBrowser.Create(fx.Factory);
        async Task<HttpResponseMessage> Clock(string? tok, string pin, string action = "in")
        {
            // the kiosk page talks to the API through the web application like every other page, without a session
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/kiosk/clock") { Content = JsonContent.Create(new { employeeNo = w.No, pin, action }) };
            req.Headers.Add("X-Internal-Proxy-Key", ApiFactory.ProxyKey); req.Headers.Add("X-Forwarded-For", kiosk.Ip); req.Headers.Add("Origin", ApiFactory.Origin);
            if (tok is not null) req.Headers.Add("X-Kiosk-Token", tok);
            return await kiosk.Http.SendAsync(req);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await Clock("not-a-device", "4815")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Clock(token, "0000")).StatusCode);
        var inOk = await Clock(token, "4815"); Assert.Equal(HttpStatusCode.OK, inOk.StatusCode);
        Assert.Equal("Shift " + w.No, (await inOk.Body()).GetProperty("employeeName").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Clock(token, "4815")).StatusCode);                  // already in
        Assert.Equal(HttpStatusCode.OK, (await Clock(token, "4815", "out")).StatusCode);
        for (var i = 0; i < 5; i++) await Clock(token, "1111");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Clock(token, "4815")).StatusCode);         // five wrong PINs lock the number for a while
    }

    [Fact]
    public async Task Field_visits_record_the_position_and_only_the_team_or_HR_may_see_them()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true); var boss = await Worker(hr, "manager"); var w = await Worker(hr, manager: boss.EmployeeId); var stranger = await Worker(hr);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await w.B.Post("/api/hr/attendance/visits", new { place = "PT Maju" })).StatusCode);   // no position
        Assert.Equal(HttpStatusCode.OK, (await w.B.Post("/api/hr/attendance/visits", new { kind = "arrive", place = "PT Maju", latitude = -7.2575123, longitude = 112.7521456, accuracy = 12.34 })).StatusCode);
        var mine = await (await w.B.Get($"/api/hr/attendance/visits?date={D(Today)}")).Body(); Assert.Equal(-7.25751, mine[0].GetProperty("latitude").GetDouble());
        Assert.Equal(1, (await (await boss.B.Get($"/api/hr/attendance/visits?date={D(Today)}&scope=all")).Body()).EnumerateArray().Count(v => v.GetProperty("employeeId").GetGuid() == w.EmployeeId));
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.B.Get($"/api/hr/attendance/visits?date={D(Today)}&scope=all")).StatusCode);
    }
}
