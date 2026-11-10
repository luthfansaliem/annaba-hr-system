using System.Net;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class HrWorkspaceTests(PostgresFixture fx)
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
    private sealed record Person(TestBrowser B, Guid EmployeeId);
    private async Task<Person> Worker(TestBrowser hr, string role = "user", Guid? manager = null, object? extra = null)
    {
        var u = await _seed.UserAsync(role: role);
        var body = new Dictionary<string, object?> { ["fullName"] = "Team " + Guid.NewGuid().ToString("N")[..6], ["hireDate"] = D(Today.AddYears(-2)), ["jobTitle"] = "Analyst", ["managerId"] = manager, ["userId"] = u.Id };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        var r = await hr.Post("/api/hr/employees", body); Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return new Person(await Login(u.Email), (await r.Body()).GetProperty("id").GetGuid());
    }
    private DateOnly Monday() { var d = Today.AddDays(7); while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1); while (d.AddDays(28).Year != d.Year) d = d.AddDays(-7); return d; }
    private static async Task<Guid> AskLeave(TestBrowser b, DateOnly day)
    {
        var sick = (await (await b.Get("/api/hr/leave/types")).Body()).EnumerateArray().First(t => t.GetProperty("code").GetString() == "SICK").GetProperty("id").GetGuid();
        var r = await b.Post("/api/hr/leave/requests", new { leaveTypeId = sick, startDate = D(day), endDate = D(day), halfDay = false }); Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Body()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task The_inbox_shows_only_what_the_caller_can_decide_and_bulk_decisions_report_each_result()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true);
        var boss = await Worker(hr, "manager"); var other = await Worker(hr, "manager"); var a = await Worker(hr, manager: boss.EmployeeId); var b = await Worker(hr, manager: boss.EmployeeId); var mon = Monday();
        var l1 = await AskLeave(a.B, mon); var l2 = await AskLeave(b.B, mon);
        var ot = await a.B.Post("/api/hr/attendance/overtime", new { date = D(mon.AddDays(1)), hours = 2, reason = "Release night" }); Assert.Equal(HttpStatusCode.Created, ot.StatusCode); var o1 = (await ot.Body()).GetProperty("id").GetGuid();

        var inbox = await (await boss.B.Get("/api/hr/approvals")).Body();
        Assert.Equal(3, inbox.GetProperty("total").GetInt32()); Assert.Equal(2, inbox.GetProperty("leave").GetArrayLength()); Assert.Equal(1, inbox.GetProperty("overtime").GetArrayLength());
        Assert.Equal(0, (await (await other.B.Get("/api/hr/approvals")).Body()).GetProperty("total").GetInt32());              // another manager's inbox stays empty
        Assert.Equal(0, (await (await a.B.Get("/api/hr/approvals")).Body()).GetProperty("total").GetInt32());                  // and so does an employee's
        Assert.Contains((await (await hr.Get("/api/hr/approvals")).Body()).GetProperty("leave").EnumerateArray(), x => x.GetProperty("id").GetGuid() == l1);   // HR sees everything pending

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await boss.B.Post("/api/hr/approvals/bulk", new { approve = false, items = new[] { new { kind = "leave", id = l1 } } })).StatusCode);   // rejecting needs a reason
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await boss.B.Post("/api/hr/approvals/bulk", new { approve = true, items = Enumerable.Range(0, 51).Select(_ => new { kind = "leave", id = Guid.NewGuid() }) })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await boss.B.Post("/api/hr/approvals/bulk", new { approve = true, items = new[] { new { kind = "salary", id = l1 } } })).StatusCode);

        var foreign = await (await other.B.Post("/api/hr/approvals/bulk", new { approve = true, items = new[] { new { kind = "leave", id = l2 } } })).Body();   // not their report: refused, nothing changes
        Assert.False(foreign[0].GetProperty("ok").GetBoolean());
        Assert.Equal(LeaveStatus.Pending, await _seed.Db(db => db.LeaveRequests.Where(x => x.Id == l2).Select(x => x.Status).SingleAsync()));

        var ghost = Guid.NewGuid();
        var res = (await (await boss.B.Post("/api/hr/approvals/bulk", new { approve = true, items = new object[] { new { kind = "leave", id = l1 }, new { kind = "overtime", id = o1 }, new { kind = "leave", id = ghost } } })).Body()).EnumerateArray().ToList();
        Assert.Equal([true, true, false], res.Select(x => x.GetProperty("ok").GetBoolean()).ToList()); Assert.False(string.IsNullOrEmpty(res[2].GetProperty("error").GetString()));
        Assert.Equal(LeaveStatus.Approved, await _seed.Db(db => db.LeaveRequests.Where(x => x.Id == l1).Select(x => x.Status).SingleAsync()));
        Assert.Equal(RequestStatus.Approved, await _seed.Db(db => db.OvertimeRequests.Where(x => x.Id == o1).Select(x => x.Status).SingleAsync()));

        var no = await (await boss.B.Post("/api/hr/approvals/bulk", new { approve = false, note = "Team offsite that week", items = new[] { new { kind = "leave", id = l2 } } })).Body();
        Assert.True(no[0].GetProperty("ok").GetBoolean());
        Assert.Equal("Team offsite that week", await _seed.Db(db => db.LeaveRequests.Where(x => x.Id == l2).Select(x => x.DecisionNote).SingleAsync()));
        Assert.Equal(0, (await (await boss.B.Get("/api/hr/approvals")).Body()).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get("/api/hr/approvals")).StatusCode);
    }

    [Fact]
    public async Task A_manager_sees_the_team_today_and_an_employee_sees_their_own_start_page()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true);
        var boss = await Worker(hr, "manager"); var here = await Worker(hr, manager: boss.EmployeeId); var away = await Worker(hr, manager: boss.EmployeeId);
        var temp = await Worker(hr, manager: boss.EmployeeId, extra: new { type = "contract", contractEnd = D(Today.AddDays(30)) });
        Assert.Equal(HttpStatusCode.OK, (await here.B.Post("/api/hr/attendance/clock-in")).StatusCode);
        var pending = await AskLeave(here.B, Monday());
        await _seed.Db(async db =>
        {
            var sick = await db.LeaveTypes.FirstAsync(t => t.Code == "SICK");
            db.LeaveRequests.Add(new LeaveRequest { EmployeeId = away.EmployeeId, LeaveTypeId = sick.Id, StartDate = Today, EndDate = Today.AddDays(1), Days = 2, Status = LeaveStatus.Approved, CreatedAt = fx.Clock.UtcNow });
            return await db.SaveChangesAsync();
        });

        var team = await (await boss.B.Get("/api/hr/team")).Body();
        Assert.Equal(3, team.GetProperty("size").GetInt32()); Assert.Equal(1, team.GetProperty("present").GetInt32()); Assert.Equal(1, team.GetProperty("onLeave").GetInt32()); Assert.Equal(1, team.GetProperty("pendingApprovals").GetInt32());
        string Status(Guid id) => team.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("employeeId").GetGuid() == id).GetProperty("today").GetString()!;
        Assert.Equal("present", Status(here.EmployeeId)); Assert.Equal("leave", Status(away.EmployeeId)); Assert.Contains(Status(temp.EmployeeId), new[] { "absent", "off" });
        Assert.Equal(temp.EmployeeId, team.GetProperty("contractsEnding")[0].GetProperty("employeeId").GetGuid());
        Assert.Contains(team.GetProperty("upcomingLeave").EnumerateArray(), l => l.GetProperty("employeeId").GetGuid() == away.EmployeeId && l.GetProperty("status").GetString() == "approved");
        Assert.Equal(0, (await (await here.B.Get("/api/hr/team")).Body()).GetProperty("size").GetInt32());                          // someone without reports has no team to look at

        var home = await (await here.B.Get("/api/hr/me/home")).Body();
        Assert.True(home.GetProperty("linked").GetBoolean()); Assert.Equal(1, home.GetProperty("pendingLeave").GetInt32());
        Assert.Equal(D(Today), home.GetProperty("today").GetProperty("date").GetString());                                            // clocked in today
        Assert.Contains(home.GetProperty("balances").EnumerateArray(), x => x.GetProperty("code").GetString() == "ANNUAL" && x.GetProperty("available").GetDecimal() == 12m);
        Assert.True(home.GetProperty("tasks").GetArrayLength() > 0);                                                                 // the onboarding checklist
        Assert.Equal(pending, await _seed.Db(db => db.LeaveRequests.Where(x => x.EmployeeId == here.EmployeeId).Select(x => x.Id).SingleAsync()));
        Assert.Equal(1, home.GetProperty("nextLeave").GetArrayLength());
        var loose = await Login((await _seed.UserAsync(role: "user")).Email);
        Assert.False((await (await loose.Get("/api/hr/me/home")).Body()).GetProperty("linked").GetBoolean());                        // an account without an employee record gets an empty page, not an error
    }
}
