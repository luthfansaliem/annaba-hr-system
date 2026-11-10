using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task The_manager_is_told_about_a_request_and_the_employee_about_the_decision_and_nobody_else_is_mailed()
    {
        var hr = await Hr(); var boss = await Worker(hr, role: "manager"); var staff = await Worker(hr, manager: boss.EmployeeId); var other = await Worker(hr); var mon = Monday();
        var id = (await (await Request(staff.B, "ANNUAL", mon, mon.AddDays(1), reason: "Family event")).Body()).GetProperty("id").GetGuid();
        var toBoss = await _seed.Mail.WaitFor(boss.Email, "Leave request");
        Assert.Contains("/hr/leave-approvals", toBoss.Text); Assert.DoesNotContain("Family event", toBoss.Text); // the reason stays in the app
        await boss.B.Post($"/api/hr/leave/requests/{id}/reject", new { note = "Busy period" });
        var toStaff = await _seed.Mail.WaitFor(staff.Email, "not approved");
        Assert.Contains("Busy period", toStaff.Text); Assert.Contains("/hr/my-leave", toStaff.Text);
        await _seed.Mail.AssertNone(other.Email);
        // an employee with no manager on file simply produces no e-mail and no error
        Assert.Equal(HttpStatusCode.Created, (await Request(other.B, "SICK", mon, mon)).StatusCode);
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

    [Fact]
    public async Task Long_leave_of_a_type_that_needs_HR_goes_manager_then_HR_and_short_leave_stays_with_the_manager()
    {
        var hr = await Hr(); var code = "LONG" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/leave/types", new { code, name = "Long leave", unlimited = true, hrApprovalFromDays = -1 })).StatusCode);
        var made = await hr.Post("/api/hr/leave/types", new { code, name = "Long leave " + code, unlimited = true, hrApprovalFromDays = 3 });
        Assert.Equal(HttpStatusCode.Created, made.StatusCode); var madeBody = await made.Body(); Assert.Equal(3m, madeBody.GetProperty("hrApprovalFromDays").GetDecimal());
        var boss = await Worker(hr, role: "manager"); var staff = await Worker(hr, manager: boss.EmployeeId); var mon = Monday();

        var shortId = (await (await Request(staff.B, code, mon, mon.AddDays(1))).Body()).GetProperty("id").GetGuid();            // 2 days: below the threshold
        Assert.Equal("approved", (await (await boss.B.Post($"/api/hr/leave/requests/{shortId}/approve")).Body()).GetProperty("status").GetString());

        var asked = await (await Request(staff.B, code, mon.AddDays(7), mon.AddDays(9))).Body();                                   // 3 days: two steps
        var id = asked.GetProperty("id").GetGuid(); Assert.True(asked.GetProperty("needsHrApproval").GetBoolean()); Assert.False(asked.GetProperty("awaitingHr").GetBoolean());
        var step1 = await (await boss.B.Post($"/api/hr/leave/requests/{id}/approve")).Body();
        Assert.Equal("pending", step1.GetProperty("status").GetString()); Assert.True(step1.GetProperty("awaitingHr").GetBoolean()); Assert.False(step1.GetProperty("canDecide").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await boss.B.Post($"/api/hr/leave/requests/{id}/approve")).StatusCode);            // the manager has had their say
        Assert.Equal(HttpStatusCode.Conflict, (await boss.B.Post($"/api/hr/leave/requests/{id}/reject", new { note = "Changed my mind" })).StatusCode);
        var mine = (await (await staff.B.Get("/api/hr/me/notifications")).Body()).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("kind").GetString()).ToList();
        Assert.Contains("leave.progress", mine); Assert.Single(mine, k => k == "leave.decided");                                 // only the short request has been decided so far
        Assert.Contains((await (await hr.Get("/api/hr/me/notifications")).Body()).GetProperty("items").EnumerateArray(), x => x.GetProperty("kind").GetString() == "leave.awaiting_hr");

        var final = await (await hr.Post($"/api/hr/leave/requests/{id}/approve")).Body();
        Assert.Equal("approved", final.GetProperty("status").GetString()); Assert.False(final.GetProperty("awaitingHr").GetBoolean());
        Assert.True(await _seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "hr.leave.manager_approve" && a.TargetId == id.ToString())));

        // HR can decide straight away (someone without a manager), and a manager's rejection at step one is final
        var direct = (await (await Request(staff.B, code, mon.AddDays(14), mon.AddDays(16))).Body()).GetProperty("id").GetGuid();
        Assert.Equal("approved", (await (await hr.Post($"/api/hr/leave/requests/{direct}/approve")).Body()).GetProperty("status").GetString());
        var no = (await (await Request(staff.B, code, mon.AddDays(21), mon.AddDays(23))).Body()).GetProperty("id").GetGuid();
        Assert.Equal("rejected", (await (await boss.B.Post($"/api/hr/leave/requests/{no}/reject", new { note = "Busy period" })).Body()).GetProperty("status").GetString());
        // switching the second step off again
        var typeId = madeBody.GetProperty("id").GetGuid();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await (await hr.SendAsync(HttpMethod.Patch, $"/api/hr/leave/types/{typeId}", new { noHrApproval = true })).Body()).GetProperty("hrApprovalFromDays").ValueKind);
    }

    [Fact]
    public async Task Carried_over_days_are_announced_before_they_expire_and_removed_once_after_the_deadline()
    {
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock);
        async Task<TestBrowser> In(string email, bool reauth = false) { var b = TestBrowser.Create(f); await b.LoginAsync(email, Seed.Password); if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password }); return b; }
        void GoTo(DateOnly day) => clock.Advance(day.ToDateTime(new TimeOnly(3, 0), DateTimeKind.Utc) - clock.UtcNow.UtcDateTime);   // 10:00 WIB that day
        var year = clock.UtcNow.Year + 1; GoTo(new DateOnly(year, 2, 20));
        var hrUser = await seed.UserAsync(role: "hr_admin"); var hr = await In(hrUser.Email, reauth: true); var u = await seed.UserAsync(role: "user");
        var made = await hr.Post("/api/hr/employees", new { fullName = "Carry Person", hireDate = $"{year - 3}-01-10", jobTitle = "Analyst", userId = u.Id }); Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        var empId = (await made.Body()).GetProperty("id").GetGuid(); var me = await In(u.Email);
        var annual = (await (await hr.Get("/api/hr/leave/types")).Body()).EnumerateArray().First(t => t.GetProperty("code").GetString() == "ANNUAL").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.SendAsync(HttpMethod.Patch, $"/api/hr/leave/types/{annual}", new { carryOverExpiryMonth = 13 })).StatusCode);
        Assert.Equal(3, (await (await hr.SendAsync(HttpMethod.Patch, $"/api/hr/leave/types/{annual}", new { carryOverMax = 5, carryOverExpiryMonth = 3 })).Body()).GetProperty("carryOverExpiryMonth").GetInt32());
        await seed.Db(async db =>                                                                                               // last year: 12 granted, 9 taken, so 3 are left to carry over
        {
            db.LeaveLedger.Add(new LeaveLedgerEntry { EmployeeId = empId, LeaveTypeId = annual, Year = year - 1, Days = 12, Kind = LedgerKind.Grant, CreatedAt = clock.UtcNow });
            db.LeaveLedger.Add(new LeaveLedgerEntry { EmployeeId = empId, LeaveTypeId = annual, Year = year - 1, Days = -9, Kind = LedgerKind.Used, CreatedAt = clock.UtcNow });
            return await db.SaveChangesAsync();
        });
        async Task<System.Text.Json.JsonElement> Annual(TestBrowser b) => (await (await b.Get("/api/hr/leave/balances")).Body()).EnumerateArray().First(x => x.GetProperty("code").GetString() == "ANNUAL");
        var bal = await Annual(me);
        Assert.Equal(15m, bal.GetProperty("granted").GetDecimal()); Assert.Equal(3m, bal.GetProperty("expiring").GetDecimal()); Assert.Equal($"{year}-03-31", bal.GetProperty("expiresOn").GetString());

        async Task<int> Remind() { using var scope = f.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Hr.LeaveExpiryReminderService>().RunAsync(); }
        Assert.Equal(0, await Remind());                                                                                        // 39 days ahead: too early
        GoTo(new DateOnly(year, 3, 5));
        Assert.Equal(1, await Remind()); Assert.Equal(0, await Remind());                                                       // told once for the 30-day mark
        var mail = await seed.Mail.WaitFor(u.Email, "expire"); Assert.Contains("3 day(s)", mail.Subject);
        me = await In(u.Email);
        Assert.Contains((await (await me.Get("/api/hr/me/notifications")).Body()).GetProperty("items").EnumerateArray(), n => n.GetProperty("kind").GetString() == "leave.expiring");

        await seed.Db(async db =>                                                                                               // one day taken in March uses a carried-over day first
        {
            var day = new DateOnly(year, 3, 10); var req = new LeaveRequest { EmployeeId = empId, LeaveTypeId = annual, StartDate = day, EndDate = day, Days = 1, Status = LeaveStatus.Approved, CreatedAt = clock.UtcNow };
            db.LeaveRequests.Add(req); db.LeaveLedger.Add(new LeaveLedgerEntry { EmployeeId = empId, LeaveTypeId = annual, Year = year, Days = -1, Kind = LedgerKind.Used, RequestId = req.Id, CreatedAt = clock.UtcNow });
            return await db.SaveChangesAsync();
        });
        Assert.Equal(2m, (await Annual(me)).GetProperty("expiring").GetDecimal());
        GoTo(new DateOnly(year, 3, 26)); Assert.Equal(1, await Remind());                                                       // the 7-day mark

        GoTo(new DateOnly(year, 4, 2)); me = await In(u.Email);
        var after = await Annual(me);                                                                                           // 15 granted, 1 used, 2 expired
        Assert.Equal(2m, after.GetProperty("expired").GetDecimal()); Assert.Equal(12m, after.GetProperty("available").GetDecimal()); Assert.Equal(0m, after.GetProperty("expiring").GetDecimal());
        await Annual(me);
        Assert.Equal(1, await seed.Db(db => db.LeaveLedger.CountAsync(l => l.EmployeeId == empId && l.Kind == LedgerKind.Expire)));   // written once
        Assert.Equal(0, await Remind());                                                                                        // nothing left to announce
    }
}
