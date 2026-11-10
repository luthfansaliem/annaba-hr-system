using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class HrSchedulingAndPhotoTests(PostgresFixture fx)
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");
    private sealed record Env(ApiFactory F, TestClock Clock, Seed Seed)
    {
        public DateOnly Today => DateOnly.FromDateTime(Clock.UtcNow.UtcDateTime.AddHours(7));
        public async Task<TestBrowser> As(string role, bool reauth = true) { var u = await Seed.UserAsync(role: role); return await In(u.Email, reauth); }
        public async Task<TestBrowser> In(string email, bool reauth) { var b = TestBrowser.Create(F); await b.LoginAsync(email, Seed.Password); if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password }); return b; }
    }
    private async Task<Env> NewEnv() { var (f, clock, _) = await fx.NewIsolatedAsync(); return new Env(f, clock, new Seed(f, clock)); }

    [Fact]
    public async Task A_job_change_dated_ahead_waits_and_becomes_the_current_job_on_its_day()
    {
        var e = await NewEnv(); var hr = await e.As("hr_admin");
        var id = (await (await hr.Post("/api/hr/employees", new { fullName = "Promo Person", hireDate = D(e.Today.AddYears(-1)), jobTitle = "Analyst" })).Body()).GetProperty("id").GetGuid();
        var on = e.Today.AddDays(10);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post($"/api/hr/employees/{id}/employment", new { effectiveFrom = D(e.Today.AddYears(2)), jobTitle = "Director" })).StatusCode);   // too far ahead
        var scheduled = await hr.Post($"/api/hr/employees/{id}/employment", new { effectiveFrom = D(on), jobTitle = "Senior Analyst", reason = "Promotion" });
        Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode); var body = await scheduled.Body();
        Assert.Equal("Analyst", body.GetProperty("current").GetProperty("jobTitle").GetString());                                   // nothing changes today
        var waiting = body.GetProperty("scheduledChanges").EnumerateArray().Single(); Assert.Equal(D(on), waiting.GetProperty("effectiveFrom").GetString()); Assert.Contains("jobTitle", waiting.GetProperty("summary").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await hr.Post($"/api/hr/employees/{id}/employment", new { effectiveFrom = D(on), jobTitle = "Lead" })).StatusCode);   // one change per day
        var second = await (await hr.Post($"/api/hr/employees/{id}/employment", new { effectiveFrom = D(on.AddDays(5)), jobTitle = "Never Applied" })).Body();
        var cancelId = second.GetProperty("scheduledChanges").EnumerateArray().Single(x => x.GetProperty("effectiveFrom").GetString() == D(on.AddDays(5))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await hr.SendAsync(HttpMethod.Delete, $"/api/hr/employees/scheduled-changes/{cancelId}")).StatusCode);

        async Task<int> RunJobs() { using var scope = e.F.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Hr.EmployeeService>().ApplyScheduledAsync(); }
        Assert.Equal(0, await RunJobs());                                                                                           // not due yet
        e.Clock.Advance(TimeSpan.FromDays(11));
        Assert.Equal(1, await RunJobs()); Assert.Equal(0, await RunJobs());                                                         // applied once
        var jobs = await e.Seed.Db(db => db.Employments.Where(m => m.EmployeeId == id).OrderBy(m => m.EffectiveFrom).ToListAsync());
        Assert.Equal(2, jobs.Count); Assert.Equal(on.AddDays(-1), jobs[0].EffectiveTo); Assert.Equal(("Senior Analyst", on, (DateOnly?)null), (jobs[1].JobTitle, jobs[1].EffectiveFrom, jobs[1].EffectiveTo));   // dated on its own day, not on the day the job ran
        Assert.True(await e.Seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "hr.employee.employment_scheduled")));
        Assert.True(await e.Seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "hr.employee.employment_change" && a.TargetId == id.ToString())));
    }

    [Fact]
    public async Task Portraits_and_clock_in_pictures_are_typed_by_their_bytes_visible_to_the_right_people_and_clock_in_pictures_expire()
    {
        var e = await NewEnv(); var hr = await e.As("hr_admin"); var u = await e.Seed.UserAsync(role: "user"); var other = await e.As("user", reauth: false);
        var id = (await (await hr.Post("/api/hr/employees", new { fullName = "Photo Person", hireDate = D(e.Today.AddYears(-1)), jobTitle = "Analyst", userId = u.Id })).Body()).GetProperty("id").GetGuid();
        var me = await e.In(u.Email, reauth: false);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.SendAsync(HttpMethod.Put, $"/api/hr/employees/{id}/photo", new { contentBase64 = Convert.ToBase64String("%PDF-1.4 not a picture"u8.ToArray()) })).StatusCode);   // a PDF is not a portrait
        Assert.Equal(HttpStatusCode.Forbidden, (await me.SendAsync(HttpMethod.Put, $"/api/hr/employees/{id}/photo", new { contentBase64 = Convert.ToBase64String(Png) })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await hr.SendAsync(HttpMethod.Put, $"/api/hr/employees/{id}/photo", new { contentBase64 = Convert.ToBase64String(Png) })).StatusCode);
        var seen = await me.Get($"/api/hr/employees/{id}/photo"); Assert.Equal(HttpStatusCode.OK, seen.StatusCode); Assert.Equal("image/png", seen.Content.Headers.ContentType!.MediaType); Assert.Equal(Png, await seen.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await other.Get($"/api/hr/employees/{id}/photo")).StatusCode);                        // a colleague without HR rights

        var clocked = await (await me.Post("/api/hr/attendance/clock-in", new { mode = "field", photoBase64 = Convert.ToBase64String(Png) })).Body();
        Assert.True(clocked.GetProperty("hasPhoto").GetBoolean()); var logId = clocked.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await me.Get($"/api/hr/attendance/logs/{logId}/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Get($"/api/hr/attendance/logs/{logId}/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hr.Get($"/api/hr/attendance/logs/{logId}/photo")).StatusCode);
        Assert.True((await (await hr.Get($"/api/hr/attendance/logs?employeeId={id}")).Body())[0].GetProperty("hasPhoto").GetBoolean());
        Assert.True(await e.Seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "hr.attendance.photo_viewed")));                 // HR looking at it is recorded

        async Task<int> Purge() { using var scope = e.F.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Hr.PhotoService>().PurgeOldClockInPhotosAsync(); }
        Assert.Equal(0, await Purge()); e.Clock.Advance(TimeSpan.FromDays(91)); Assert.Equal(1, await Purge());
        Assert.Equal(1, await e.Seed.Db(db => db.HrPhotos.CountAsync()));                                                           // the portrait stays, the clock-in picture is gone
    }

    [Fact]
    public async Task A_scheduled_report_sends_a_link_only_on_its_day_once_and_only_while_the_person_may_open_the_report()
    {
        var e = await NewEnv(); var u = await e.Seed.UserAsync(role: "hr_admin"); var hr = await e.In(u.Email, reauth: false); var plain = await e.As("user", reauth: false);
        var overview = await (await plain.Get("/api/hr/me/report-schedules")).Body();
        Assert.All(overview.GetProperty("reports").EnumerateArray(), r => Assert.False(r.GetProperty("allowed").GetBoolean()));
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Post("/api/hr/me/report-schedules", new { report = "attendance", frequency = "weekly", day = 1 })).StatusCode);   // cannot schedule what you may not open
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/me/report-schedules", new { report = "attendance", frequency = "monthly", day = 31 })).StatusCode);
        var dow = ((int)e.Today.DayOfWeek + 6) % 7 + 1;
        var made = await hr.Post("/api/hr/me/report-schedules", new { report = "attendance", frequency = "weekly", day = dow }); Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await hr.Post("/api/hr/me/report-schedules", new { report = "leave", frequency = "weekly", day = dow % 7 + 1 })).StatusCode);   // tomorrow's

        async Task<int> Run() { using var scope = e.F.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Hr.ScheduledReportService>().RunDueAsync(); }
        Assert.Equal(1, await Run()); Assert.Equal(0, await Run());                                                                  // today's, once
        var mail = await e.Seed.Mail.WaitFor(u.Email, "scheduled report");
        Assert.Contains("/hr/attendance-report", mail.Text); Assert.DoesNotContain("attachment", mail.Text, StringComparison.OrdinalIgnoreCase);
        // the role is taken away: the next reminder is not sent and the schedule disappears
        await e.Seed.Db(db => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM user_roles WHERE user_id = {u.Id}"));
        e.Clock.Advance(TimeSpan.FromDays(7)); Assert.Equal(0, await Run());
        Assert.Equal(1, await e.Seed.Db(db => db.ReportSchedules.CountAsync()));                                                    // only tomorrow's is left (checked when its day comes)
    }
}
