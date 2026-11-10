using System.Net;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class HrNotificationTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private DateOnly Today => DateOnly.FromDateTime(fx.Clock.UtcNow.UtcDateTime.AddHours(7));
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    private async Task<TestBrowser> Login(string email, bool reauth = false)
    {
        var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return b;
    }

    private sealed record Person(TestBrowser B, Guid EmployeeId, Guid UserId);

    private async Task<Person> Worker(TestBrowser hr, string role = "user", Guid? manager = null)
    {
        var u = await _seed.UserAsync(role: role);
        var r = await hr.Post("/api/hr/employees", new { fullName = "Notified " + Guid.NewGuid().ToString("N")[..6], hireDate = D(Today.AddYears(-2)), jobTitle = "Analyst", managerId = manager, userId = u.Id });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return new Person(await Login(u.Email), (await r.Body()).GetProperty("id").GetGuid(), u.Id);
    }

    private static async Task<System.Text.Json.JsonElement> Inbox(TestBrowser b, bool unreadOnly = false) => await (await b.Get($"/api/hr/me/notifications?unreadOnly={unreadOnly.ToString().ToLowerInvariant()}")).Body();

    [Fact]
    public async Task A_leave_request_tells_the_manager_and_the_decision_tells_the_employee_and_each_person_reads_only_their_own()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true);
        var boss = await Worker(hr, "manager"); var staff = await Worker(hr, manager: boss.EmployeeId); var other = await Worker(hr);

        var types = await (await staff.B.Get("/api/hr/leave/types")).Body(); var annual = types.EnumerateArray().First(t => t.GetProperty("code").GetString() == "SICK").GetProperty("id").GetGuid();
        var d = Today.AddDays(7); while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1);
        var made = await staff.B.Post("/api/hr/leave/requests", new { leaveTypeId = annual, startDate = D(d), endDate = D(d), halfDay = false });
        Assert.Equal(HttpStatusCode.Created, made.StatusCode); var id = (await made.Body()).GetProperty("id").GetGuid();

        var bossInbox = await Inbox(boss.B); Assert.Equal(1, bossInbox.GetProperty("unread").GetInt32());
        var first = bossInbox.GetProperty("items")[0]; Assert.Equal("leave.requested", first.GetProperty("kind").GetString()); Assert.Equal("/hr/leave-approvals", first.GetProperty("link").GetString()); Assert.False(first.GetProperty("read").GetBoolean());
        Assert.Equal(0, (await Inbox(staff.B)).GetProperty("unread").GetInt32());                                            // the person who asked is not told about their own request
        Assert.Equal(0, (await Inbox(other.B)).GetProperty("unread").GetInt32());                                            // nor is anyone else

        Assert.Equal(HttpStatusCode.OK, (await boss.B.Post($"/api/hr/leave/requests/{id}/approve", new { note = "Enjoy" })).StatusCode);
        var staffInbox = await Inbox(staff.B); Assert.Equal(1, staffInbox.GetProperty("unread").GetInt32());
        var decided = staffInbox.GetProperty("items")[0]; Assert.Equal("leave.decided", decided.GetProperty("kind").GetString()); Assert.Contains("approved", decided.GetProperty("title").GetString()); Assert.Contains("Enjoy", decided.GetProperty("body").GetString());

        var nid = decided.GetProperty("id").GetGuid();                                                                        // somebody else cannot mark it read
        Assert.Equal(HttpStatusCode.NoContent, (await other.B.Post($"/api/hr/me/notifications/{nid}/read")).StatusCode);
        Assert.Equal(1, (await Inbox(staff.B)).GetProperty("unread").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await staff.B.Post($"/api/hr/me/notifications/{nid}/read")).StatusCode);      // the owner can
        Assert.Equal(0, (await Inbox(staff.B)).GetProperty("unread").GetInt32());
        Assert.Equal(0, (await Inbox(staff.B, unreadOnly: true)).GetProperty("items").GetArrayLength());

        Assert.Equal(HttpStatusCode.OK, (await boss.B.Post("/api/hr/me/notifications/read-all")).StatusCode);
        Assert.Equal(0, (await Inbox(boss.B)).GetProperty("unread").GetInt32()); Assert.Equal(1, (await Inbox(boss.B)).GetProperty("items").GetArrayLength());     // read messages stay visible for a while
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get("/api/hr/me/notifications")).StatusCode);
    }

    [Fact]
    public async Task Old_read_messages_are_tidied_away_and_only_in_app_links_are_kept()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true); var w = await Worker(hr);
        await _seed.Db(async db =>
        {
            var now = fx.Clock.UtcNow;
            db.Notifications.Add(new UserNotification { UserId = w.UserId, Kind = "test", Title = "old and read", CreatedAt = now.AddDays(-200), ReadAt = now.AddDays(-120) });
            db.Notifications.Add(new UserNotification { UserId = w.UserId, Kind = "test", Title = "recent and read", CreatedAt = now.AddDays(-5), ReadAt = now.AddDays(-2) });
            return await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.OK, (await w.B.Post("/api/hr/me/notifications/read-all")).StatusCode);
        var titles = (await Inbox(w.B)).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("title").GetString()).ToList();
        Assert.Contains("recent and read", titles); Assert.DoesNotContain("old and read", titles);

        await _seed.Db(async db =>                                                                                              // a link that leaves the app is dropped, never stored
        {
            var svc = new AnnabaDigital.Application.Hr.NotificationService(db, fx.Clock, null!);
            svc.Notify(w.UserId, "test", "bad link", null, "https://evil.example/phish"); svc.Notify(w.UserId, "test", "protocol relative", null, "//evil.example");
            return await db.SaveChangesAsync();
        });
        var rows = (await Inbox(w.B)).GetProperty("items").EnumerateArray().Where(x => x.GetProperty("title").GetString() is "bad link" or "protocol relative").ToList();
        Assert.Equal(2, rows.Count); Assert.All(rows, r => Assert.Equal(System.Text.Json.JsonValueKind.Null, r.GetProperty("link").ValueKind));
    }
}
