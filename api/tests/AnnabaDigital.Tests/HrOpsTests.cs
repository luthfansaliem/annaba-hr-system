using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Checklists, expiring contracts, documents, attendance corrections, overtime and month close.</summary>
[Collection("db")]
public sealed class HrOpsTests(PostgresFixture fx)
{
    private DateOnly Today(TestClock c) => DateOnly.FromDateTime(c.UtcNow.UtcDateTime.AddHours(7));
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    private sealed record Person(Guid EmployeeId, TestBrowser B, string Email);

    private static async Task<TestBrowser> As(ApiFactory f, Seed seed, string role, bool reauth = false)
    {
        var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return b;
    }

    private static async Task<Person> Worker(ApiFactory f, Seed seed, TestClock clock, TestBrowser hr, string role = "user", Guid? manager = null)
    {
        var u = await seed.UserAsync(role: role); var hire = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddYears(-3));
        var r = await hr.Post("/api/hr/employees", new { fullName = "Ops " + Guid.NewGuid().ToString("N")[..6], hireDate = D(hire), jobTitle = "Analyst", managerId = manager, userId = u.Id });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password);
        return new Person((await r.Body()).GetProperty("id").GetGuid(), b, u.Email);
    }

    // ------------------------------------------------------------ checklists and expiring contracts
    [Fact]
    public async Task New_employees_get_an_onboarding_checklist_that_can_be_ticked_extended_and_leavers_get_an_offboarding_one()
    {
        var seed = new Seed(fx.Factory, fx.Clock); var hr = await As(fx.Factory, seed, "hr_admin");
        var w = await Worker(fx.Factory, seed, fx.Clock, hr);
        var list = await (await hr.Get($"/api/hr/employees/{w.EmployeeId}/checklist")).Body();
        Assert.True(list.GetArrayLength() >= 6); Assert.All(list.EnumerateArray(), t => { Assert.Equal("onboarding", t.GetProperty("kind").GetString()); Assert.False(t.GetProperty("done").GetBoolean()); });
        var first = list[0].GetProperty("id").GetGuid();
        Assert.True((await (await hr.Post($"/api/hr/employees/checklist/{first}/done")).Body()).GetProperty("done").GetBoolean());
        Assert.False((await (await hr.Post($"/api/hr/employees/checklist/{first}/reopen")).Body()).GetProperty("done").GetBoolean());
        var add = await hr.Post($"/api/hr/employees/{w.EmployeeId}/checklist", new { kind = "onboarding", title = "Buy a welcome gift" });
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post($"/api/hr/employees/{w.EmployeeId}/checklist", new { kind = "x", title = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.B.Get($"/api/hr/employees/{w.EmployeeId}/checklist")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As(fx.Factory, seed, "hr_head")).Post($"/api/hr/employees/checklist/{first}/done")).StatusCode); // read-only role cannot tick

        Assert.Equal(HttpStatusCode.OK, (await hr.Post($"/api/hr/employees/{w.EmployeeId}/terminate", new { reason = "Resigned" })).StatusCode);
        var after = (await (await hr.Get($"/api/hr/employees/{w.EmployeeId}/checklist")).Body()).EnumerateArray().Select(t => t.GetProperty("kind").GetString()).ToList();
        Assert.Contains("offboarding", after); Assert.Contains("onboarding", after);
    }

    [Fact]
    public async Task The_expiring_list_shows_fixed_term_contracts_ending_soon_and_never_permanent_staff()
    {
        var seed = new Seed(fx.Factory, fx.Clock); var hr = await As(fx.Factory, seed, "hr_admin", reauth: true); var t = Today(fx.Clock); var tag = Guid.NewGuid().ToString("N")[..6];
        var soon = await hr.Post("/api/hr/employees", new { fullName = "Soon " + tag, hireDate = D(t.AddMonths(-11)), jobTitle = "Analyst", type = "contract", contractEnd = D(t.AddDays(20)) });
        var late = await hr.Post("/api/hr/employees", new { fullName = "Later " + tag, hireDate = D(t.AddMonths(-1)), jobTitle = "Analyst", type = "contract", contractEnd = D(t.AddDays(300)) });
        var perm = await hr.Post("/api/hr/employees", new { fullName = "Perm " + tag, hireDate = D(t.AddYears(-2)), jobTitle = "Analyst" });
        Assert.All(new[] { soon, late, perm }, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var items = (await (await hr.Get("/api/hr/employees/expiring?days=60")).Body()).EnumerateArray().Select(i => i.GetProperty("employeeName").GetString()).ToList();
        Assert.Contains("Soon " + tag, items); Assert.DoesNotContain("Later " + tag, items); Assert.DoesNotContain("Perm " + tag, items);
    }

    // ------------------------------------------------------------ documents
    private static string B64(byte[] b) => Convert.ToBase64String(b);
    private static readonly byte[] Pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF");

    [Fact]
    public async Task Documents_are_typed_by_their_bytes_cleaned_audited_and_need_permission_and_a_recent_password_check()
    {
        var seed = new Seed(fx.Factory, fx.Clock); var hr = await As(fx.Factory, seed, "hr_admin", reauth: true); var w = await Worker(fx.Factory, seed, fx.Clock, hr);
        var url = $"/api/hr/employees/{w.EmployeeId}/documents";

        var ok = await hr.Post(url, new { fileName = "../../Contract 2024.exe", category = "contract", contentBase64 = B64(Pdf), expiresOn = D(Today(fx.Clock).AddYears(1)) });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode); var doc = await ok.Body();
        Assert.Equal("Contract 2024.pdf", doc.GetProperty("fileName").GetString());   // path stripped, extension forced to the real type
        Assert.Equal("application/pdf", doc.GetProperty("contentType").GetString());

        var exe = await hr.Post(url, new { fileName = "invoice.pdf", category = "other", contentBase64 = B64([0x4D, 0x5A, 0x90, 0x00, 0x03]) }); // an executable renamed to .pdf
        Assert.Equal(HttpStatusCode.UnprocessableEntity, exe.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post(url, new { fileName = "x.pdf", category = "nonsense", contentBase64 = B64(Pdf) })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post(url, new { fileName = "big.pdf", category = "other", contentBase64 = new string('A', 5_800_000) })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await hr.Post(url, new { fileName = "pic", category = "id_card", contentBase64 = B64([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]) })).StatusCode);

        var id = doc.GetProperty("id").GetGuid();
        var dl = await hr.Get($"/api/hr/employees/documents/{id}/download");
        Assert.Equal(HttpStatusCode.OK, dl.StatusCode); Assert.Equal(Pdf, await dl.Content.ReadAsByteArrayAsync()); Assert.Equal("application/pdf", dl.Content.Headers.ContentType!.MediaType);
        Assert.Equal(2, (await (await hr.Get(url)).Body()).GetArrayLength());

        Assert.Equal(HttpStatusCode.Forbidden, (await (await As(fx.Factory, seed, "hr_head", reauth: true)).Get(url)).StatusCode);   // read-only HR role
        Assert.Equal(HttpStatusCode.Forbidden, (await w.B.Get(url)).StatusCode);                                                       // the employee themself, too
        var stale = await As(fx.Factory, seed, "hr_admin");
        Assert.Equal("reauth_required", await (await stale.Get(url)).ErrorCode());

        Assert.Equal(HttpStatusCode.NoContent, (await hr.SendAsync(HttpMethod.Delete, $"/api/hr/employees/documents/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await hr.Get($"/api/hr/employees/documents/{id}/download")).StatusCode);
        Assert.True(await seed.Db(db => db.EmployeeDocuments.AnyAsync(d => d.Id == id && d.DeletedAt != null)));  // hidden, not erased
        foreach (var a in new[] { "hr.document.upload", "hr.document.download", "hr.document.delete" }) Assert.True(await seed.Db(db => db.AuditEvents.AnyAsync(x => x.Action == a && x.TargetId == w.EmployeeId.ToString())), a);
        var expiring = (await (await hr.Get("/api/hr/employees/expiring?days=400&documents=true")).Body()).EnumerateArray().Count(i => i.GetProperty("kind").GetString() == "document");
        Assert.Equal(0, expiring); // the one with an expiry date was removed
    }

    // ------------------------------------------------------------ corrections
    [Fact]
    public async Task A_forgotten_clock_in_is_fixed_by_an_approved_correction_decided_by_the_manager_only()
    {
        var seed = new Seed(fx.Factory, fx.Clock); var hr = await As(fx.Factory, seed, "hr_admin");
        var boss = await Worker(fx.Factory, seed, fx.Clock, hr, "manager"); var staff = await Worker(fx.Factory, seed, fx.Clock, hr, manager: boss.EmployeeId); var other = await Worker(fx.Factory, seed, fx.Clock, hr, "manager");
        var yday = Today(fx.Clock).AddDays(-1);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await staff.B.Post("/api/hr/attendance/corrections", new { date = D(yday), clockIn = "17:00", clockOut = "09:00", reason = "x" })).StatusCode); // out before in
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await staff.B.Post("/api/hr/attendance/corrections", new { date = D(Today(fx.Clock).AddDays(1)), clockIn = "09:00", clockOut = "17:00", reason = "x" })).StatusCode); // future
        var r = await staff.B.Post("/api/hr/attendance/corrections", new { date = D(yday), clockIn = "08:55", clockOut = "17:30", reason = "Phone was dead" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode); var id = (await r.Body()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await staff.B.Post("/api/hr/attendance/corrections", new { date = D(yday), clockIn = "09:00", clockOut = "17:00", reason = "again" })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await other.B.Post($"/api/hr/attendance/corrections/{id}/approve")).StatusCode);   // a manager of someone else
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.B.Post($"/api/hr/attendance/corrections/{id}/approve")).StatusCode);   // no permission
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await boss.B.Post($"/api/hr/attendance/corrections/{id}/reject", new { })).StatusCode); // a rejection needs a reason
        Assert.Equal(HttpStatusCode.OK, (await boss.B.Post($"/api/hr/attendance/corrections/{id}/approve", new { note = "ok" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await boss.B.Post($"/api/hr/attendance/corrections/{id}/approve")).StatusCode);

        var log = await seed.Db(db => db.AttendanceLogs.SingleAsync(l => l.EmployeeId == staff.EmployeeId && l.Date == yday));
        Assert.Equal("correction", log.Source); Assert.Equal(new TimeOnly(8, 55), TimeOnly.FromDateTime(log.ClockIn.UtcDateTime.AddHours(7))); Assert.Equal(new TimeOnly(17, 30), TimeOnly.FromDateTime(log.ClockOut!.Value.UtcDateTime.AddHours(7)));
        Assert.Equal(HttpStatusCode.Conflict, (await staff.B.Post("/api/hr/attendance/corrections", new { date = D(yday), clockIn = "09:00", clockOut = "17:00", reason = "already fixed" })).StatusCode); // a finished day cannot be re-corrected by the employee

        var own = (await (await boss.B.Post("/api/hr/attendance/corrections", new { date = D(yday), clockIn = "09:00", clockOut = "17:00", reason = "mine" })).Body()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await boss.B.Post($"/api/hr/attendance/corrections/{own}/approve")).StatusCode); // never your own
        Assert.Equal(HttpStatusCode.OK, (await boss.B.Post($"/api/hr/attendance/corrections/{own}/cancel")).StatusCode);
    }

    // ------------------------------------------------------------ overtime
    [Fact]
    public async Task Overtime_is_limited_per_day_and_week_one_per_day_and_decided_by_the_manager()
    {
        var seed = new Seed(fx.Factory, fx.Clock); var hr = await As(fx.Factory, seed, "hr_admin");
        var boss = await Worker(fx.Factory, seed, fx.Clock, hr, "manager"); var staff = await Worker(fx.Factory, seed, fx.Clock, hr, manager: boss.EmployeeId);
        var d = Today(fx.Clock).AddDays(7); while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1);
        Task<HttpResponseMessage> Ask(DateOnly day, decimal h) => staff.B.Post("/api/hr/attendance/overtime", new { date = D(day), hours = h, reason = "Release night" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Ask(d, 4.5m)).StatusCode);   // over 4 h a day
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Ask(d, 1.25m)).StatusCode);  // not a half-hour step
        var ids = new List<Guid>();
        for (var i = 0; i < 4; i++) { var r = await Ask(d.AddDays(i), 4m); Assert.Equal(HttpStatusCode.Created, r.StatusCode); ids.Add((await r.Body()).GetProperty("id").GetGuid()); } // 16 h
        Assert.Equal(HttpStatusCode.Conflict, (await Ask(d.AddDays(4), 4m)).StatusCode);    // 20 h would pass the 18 h week limit
        Assert.Equal(HttpStatusCode.Created, (await Ask(d.AddDays(4), 2m)).StatusCode);     // 18 h exactly is fine
        Assert.Equal(HttpStatusCode.Conflict, (await Ask(d, 1m)).StatusCode);               // one request a day

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.B.Post($"/api/hr/attendance/overtime/{ids[0]}/approve")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await boss.B.Post($"/api/hr/attendance/overtime/{ids[0]}/approve")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.B.Post($"/api/hr/attendance/overtime/{ids[1]}/cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Ask(d.AddDays(1), 1m)).StatusCode);     // the cancelled day can be asked again
        var team = await (await boss.B.Get("/api/hr/attendance/overtime?scope=team&status=pending")).Body();
        Assert.All(team.GetProperty("data").EnumerateArray(), x => Assert.True(x.GetProperty("canDecide").GetBoolean()));
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.B.Get("/api/hr/attendance/overtime?scope=all")).StatusCode);
    }

    // ------------------------------------------------------------ closing a month
    [Fact]
    public async Task A_month_can_be_closed_only_when_clean_after_it_ends_and_a_closed_month_takes_no_new_requests_until_reopened()
    {
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock);
        var hr = await As(f, seed, "hr_admin", reauth: true); var boss = await Worker(f, seed, clock, hr, "manager"); var staff = await Worker(f, seed, clock, hr, manager: boss.EmployeeId);
        var t = Today(clock); var lastOfPrev = new DateOnly(t.Year, t.Month, 1).AddDays(-1); var month = lastOfPrev.ToString("yyyy-MM");
        var open = await staff.B.Post("/api/hr/attendance/corrections", new { date = D(lastOfPrev), clockIn = "09:00", clockOut = "17:00", reason = "Forgot" });
        var id = (await open.Body()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Conflict, (await hr.Post($"/api/hr/attendance/periods/{t:yyyy-MM}/close")).StatusCode);   // this month has not ended
        Assert.Equal(HttpStatusCode.Conflict, (await hr.Post($"/api/hr/attendance/periods/{month}/close")).StatusCode);       // a request is still pending
        await boss.B.Post($"/api/hr/attendance/corrections/{id}/approve");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.B.Post($"/api/hr/attendance/periods/{month}/close")).StatusCode);
        var stale = await As(f, seed, "hr_admin"); Assert.Equal("reauth_required", await (await stale.Post($"/api/hr/attendance/periods/{month}/close")).ErrorCode());
        Assert.Equal(HttpStatusCode.OK, (await hr.Post($"/api/hr/attendance/periods/{month}/close")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await hr.Post($"/api/hr/attendance/periods/{month}/close")).StatusCode);

        var worker2 = await Worker(f, seed, clock, hr);
        Assert.Equal(HttpStatusCode.Conflict, (await worker2.B.Post("/api/hr/attendance/corrections", new { date = D(lastOfPrev), clockIn = "09:00", clockOut = "17:00", reason = "late" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post($"/api/hr/attendance/periods/{month}/reopen", new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await hr.Post($"/api/hr/attendance/periods/{month}/reopen", new { reason = "Payroll found a missing day" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await worker2.B.Post("/api/hr/attendance/corrections", new { date = D(lastOfPrev), clockIn = "09:00", clockOut = "17:00", reason = "late" })).StatusCode);
        Assert.True(await seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "hr.attendance.period_reopen")));
    }

    // ------------------------------------------------------------ scheduled reminders
    [Fact]
    public async Task Contract_reminders_go_to_the_manager_and_record_keepers_once_per_threshold_and_never_for_permanent_staff()
    {
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock);
        var hrUser = await seed.UserAsync(role: "hr_admin"); var hr = TestBrowser.Create(f); await hr.LoginAsync(hrUser.Email, Seed.Password);
        var boss = await Worker(f, seed, clock, hr, "manager"); var t = Today(clock);
        await hr.Post("/api/hr/employees", new { fullName = "Fixed Term", hireDate = D(t.AddMonths(-11)), jobTitle = "Analyst", type = "contract", contractEnd = D(t.AddDays(25)), managerId = boss.EmployeeId });
        await hr.Post("/api/hr/employees", new { fullName = "Permanent One", hireDate = D(t.AddYears(-2)), jobTitle = "Analyst", managerId = boss.EmployeeId });
        async Task<int> Run() { using var scope = f.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Hr.ContractReminderService>().RunAllTenantsAsync(); }

        Assert.Equal(2, await Run());                                        // 25 days left: the 30-day threshold, to the manager and to HR
        var toBoss = await seed.Mail.WaitFor(boss.Email, "Contract ending"); Assert.Contains("Fixed Term", toBoss.Text); Assert.DoesNotContain("Permanent One", toBoss.Text);
        await seed.Mail.WaitFor(hrUser.Email, "Contract ending");
        Assert.Equal(0, await Run());                                        // the same day again: nothing new
        clock.Advance(TimeSpan.FromDays(12)); Assert.Equal(2, await Run());  // 13 days left: the 14-day threshold
        Assert.Equal(0, await Run());
        clock.Advance(TimeSpan.FromDays(8)); Assert.Equal(2, await Run());   // 5 days left: the 7-day threshold
        Assert.Equal(0, await seed.Db(db => db.HrReminders.CountAsync(r => r.ThresholdDays > 30)));
        Assert.Equal(3, await seed.Db(db => db.HrReminders.CountAsync()));
    }
}
