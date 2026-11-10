using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record ApprovalInbox(IReadOnlyList<LeaveRequestView> Leave, IReadOnlyList<TimeRequestView> Corrections, IReadOnlyList<TimeRequestView> Overtime, IReadOnlyList<ChangeRequestView> ChangeRequests, int Total);
public sealed record BulkItem(string? Kind, Guid? Id);
public sealed record BulkDecisionRequest(List<BulkItem>? Items, bool? Approve, string? Note);
public sealed record BulkResult(string Kind, Guid Id, bool Ok, string? Error);

public sealed record TeamMember(Guid EmployeeId, string EmployeeNo, string FullName, string? JobTitle, string Today, DateTimeOffset? ClockIn, bool Late, DateOnly? ContractEnd);
public sealed record TeamLeave(Guid EmployeeId, string FullName, string LeaveType, DateOnly StartDate, DateOnly EndDate, decimal Days, string Status);
public sealed record TeamDashboard(int Size, int Present, int OnLeave, int NotClockedIn, int PendingApprovals, IReadOnlyList<TeamMember> Members, IReadOnlyList<TeamLeave> UpcomingLeave, IReadOnlyList<TeamMember> ContractsEnding);

public sealed record HomeTask(Guid Id, string Title, DateOnly? DueDate, bool Overdue);
public sealed record HomeNotice(Guid Id, string Title, bool RequiresAck);
public sealed record MyHome(bool Linked, string? FullName, string? EmployeeNo, string? JobTitle, IReadOnlyList<BalanceView> Balances, AttendanceLogView? Today, bool WorkingDay,
    int PendingLeave, int PendingTime, int PendingChanges, IReadOnlyList<TeamLeave> NextLeave, IReadOnlyList<HomeTask> Tasks, int UnreadAnnouncements, IReadOnlyList<HomeNotice> Announcements,
    string? LatestPayslip, string? NextHoliday);

/// <summary>
/// One place for what waits on a person (MSS-001), a manager's view of the team today (MSS-002) and an employee's own start page (ESS-001).
/// It only gathers what the other services already allow the caller to see and decides nothing itself: every decision still goes through the service that owns the request, with its own checks.
/// </summary>
public sealed class HrWorkspaceService(IAppDb db, IClock clock, AuthorizationService authz, LeaveService leave, TimeRequestService time, SelfServiceHrService self, AttendanceService attendance,
    AnnouncementService announcements, ManagerScope managers, ShiftService shifts)
{
    public const int MaxBulk = 50;
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    // ============================================================ approvals inbox
    public async Task<ApprovalInbox> InboxAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        IReadOnlyList<LeaveRequestView> lv = []; IReadOnlyList<TimeRequestView> corr = [], over = []; IReadOnlyList<ChangeRequestView> changes = [];
        if (perms.Contains(Permissions.LeaveManage) || perms.Contains(Permissions.LeaveApprove))
            lv = [.. (await leave.ListAsync(new LeaveQuery(perms.Contains(Permissions.LeaveManage) ? "all" : "team", "pending", null, null, 1, 200), actor, ct)).Data.Where(x => x.CanDecide).OrderBy(x => x.StartDate)];
        if (perms.Contains(Permissions.AttendanceManage) || perms.Contains(Permissions.AttendanceApprove))
        {
            var q = new TimeQuery(perms.Contains(Permissions.AttendanceManage) ? "all" : "team", "pending", 1, 100);
            corr = [.. (await time.ListAsync("corrections", q, actor, ct)).Data.Where(x => x.CanDecide)];
            over = [.. (await time.ListAsync("overtime", q, actor, ct)).Data.Where(x => x.CanDecide)];
        }
        if (perms.Contains(Permissions.EmployeesUpdate)) changes = [.. (await self.ListAsync(false, "pending", actor, ct)).Where(x => x.CanDecide)];
        return new ApprovalInbox(lv, corr, over, changes, lv.Count + corr.Count + over.Count + changes.Count);
    }

    /// <summary>Decides up to <see cref="MaxBulk"/> requests in one go. Each is decided on its own (one failing does not undo the others) and reported back with its reason.</summary>
    public async Task<IReadOnlyList<BulkResult>> BulkDecideAsync(BulkDecisionRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        if (r.Approve is null) errors["approve"] = "Say whether to approve or reject.";
        if (r.Items is null || r.Items.Count == 0) errors["items"] = "Choose at least one request."; else if (r.Items.Count > MaxBulk) errors["items"] = $"Decide at most {MaxBulk} requests at a time.";
        else if (r.Items.Any(i => i.Id is null || i.Kind is not ("leave" or "correction" or "overtime" or "change"))) errors["items"] = "Each item needs an id and a kind (leave, correction, overtime or change).";
        if (r.Approve == false && string.IsNullOrWhiteSpace(r.Note)) errors["note"] = "Tell the employees why.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the request.", errors);
        var results = new List<BulkResult>();
        foreach (var i in r.Items!.DistinctBy(x => (x.Kind, x.Id)))
        {
            try
            {
                switch (i.Kind)
                {
                    case "leave": await leave.DecideAsync(i.Id!.Value, r.Approve!.Value, r.Note, actor, client, ct); break;
                    case "correction": await time.DecideAsync("corrections", i.Id!.Value, r.Approve!.Value, r.Note, actor, client, ct); break;
                    case "overtime": await time.DecideAsync("overtime", i.Id!.Value, r.Approve!.Value, r.Note, actor, client, ct); break;
                    default: await self.DecideAsync(i.Id!.Value, r.Approve!.Value, r.Note, actor, client, ct); break;
                }
                results.Add(new BulkResult(i.Kind!, i.Id.Value, true, null));
            }
            catch (DomainException e) { db.ResetTracking(); results.Add(new BulkResult(i.Kind!, i.Id!.Value, false, e.Message)); }
        }
        return results;
    }

    // ============================================================ my team today
    public async Task<TeamDashboard> TeamAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var acting = await managers.ActingIdsAsync(actor.UserId, ct); var today = Today;
        var team = acting.Count == 0 ? [] : await (from m in db.Employments.AsNoTracking() join e in db.Employees.AsNoTracking() on m.EmployeeId equals e.Id
                          where m.EffectiveTo == null && m.ManagerId != null && acting.Contains(m.ManagerId.Value) && e.Status == EmployeeStatus.Active && e.UserId != actor.UserId
                          orderby e.FullName select new { e.Id, e.EmployeeNo, e.FullName, m.JobTitle, m.ContractEnd }).ToListAsync(ct);
        var ids = team.Select(t => t.Id).ToList();
        var logs = await db.AttendanceLogs.AsNoTracking().Where(l => l.Date == today && ids.Contains(l.EmployeeId)).ToListAsync(ct);
        var leaves = await db.LeaveRequests.AsNoTracking().Where(l => ids.Contains(l.EmployeeId) && l.EndDate >= today && l.StartDate <= today.AddDays(30) && (l.Status == LeaveStatus.Approved || l.Status == LeaveStatus.Pending)).OrderBy(l => l.StartDate).ToListAsync(ct);
        var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var lookup = await shifts.LookupAsync(ids, today, today, ct); var holiday = await db.Holidays.AsNoTracking().AnyAsync(h => h.Date == today, ct);
        var members = team.Select(t =>
        {
            var log = logs.Where(l => l.EmployeeId == t.Id).OrderBy(l => l.ClockIn).FirstOrDefault();
            var away = leaves.Any(l => l.EmployeeId == t.Id && l.Status == LeaveStatus.Approved && l.StartDate <= today && l.EndDate >= today);
            var status = away ? "leave" : log is not null ? "present" : holiday || !lookup.Works(t.Id, today) ? "off" : "absent";
            return new TeamMember(t.Id, t.EmployeeNo, t.FullName, t.JobTitle, status, log?.ClockIn, log is not null && TimeOnly.FromDateTime(log.ClockIn.UtcDateTime.AddHours(7)) > lookup.LateAfter(t.Id, today), t.ContractEnd);
        }).ToList();
        var names = team.ToDictionary(t => t.Id, t => t.FullName);
        var inbox = await InboxAsync(actor, ct);
        return new TeamDashboard(members.Count, members.Count(m => m.Today == "present"), members.Count(m => m.Today == "leave"), members.Count(m => m.Today == "absent"), inbox.Total, members,
            [.. leaves.Select(l => new TeamLeave(l.EmployeeId, names[l.EmployeeId], types.GetValueOrDefault(l.LeaveTypeId) ?? "", l.StartDate, l.EndDate, l.Days, l.Status.ToString().ToLowerInvariant()))],
            [.. members.Where(m => m.ContractEnd is { } end && end >= today && end <= today.AddDays(60)).OrderBy(m => m.ContractEnd)]);
    }

    // ============================================================ my start page
    public async Task<MyHome> HomeAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var me = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == actor.UserId && e.Status == EmployeeStatus.Active, ct);
        if (me is null) return new MyHome(false, null, null, null, [], null, false, 0, 0, 0, [], [], 0, [], null, null);
        var today = Today; var title = await db.Employments.AsNoTracking().Where(m => m.EmployeeId == me.Id && m.EffectiveTo == null).Select(m => m.JobTitle).FirstOrDefaultAsync(ct);
        var balances = (await leave.BalancesAsync(null, actor, ct)).Where(b => b.Granted + b.Adjusted > 0 || b.Used > 0 || b.Pending > 0).ToList();
        var mine = await attendance.MineAsync(null, actor, ct); var todayLog = mine.Open ?? mine.Logs.FirstOrDefault(l => l.Date == today);
        var lookup = await shifts.LookupAsync([me.Id], today, today, ct); var working = lookup.Works(me.Id, today) && !await db.Holidays.AsNoTracking().AnyAsync(h => h.Date == today, ct);
        var pendingLeave = await db.LeaveRequests.CountAsync(l => l.EmployeeId == me.Id && l.Status == LeaveStatus.Pending, ct);
        var pendingTime = await db.AttendanceCorrections.CountAsync(c => c.EmployeeId == me.Id && c.Status == RequestStatus.Pending, ct) + await db.OvertimeRequests.CountAsync(o => o.EmployeeId == me.Id && o.Status == RequestStatus.Pending, ct);
        var pendingChanges = await db.ChangeRequests.CountAsync(c => c.EmployeeId == me.Id && c.Status == RequestStatus.Pending, ct);
        var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var next = await db.LeaveRequests.AsNoTracking().Where(l => l.EmployeeId == me.Id && l.EndDate >= today && (l.Status == LeaveStatus.Approved || l.Status == LeaveStatus.Pending)).OrderBy(l => l.StartDate).Take(3).ToListAsync(ct);
        var tasks = await db.EmployeeTasks.AsNoTracking().Where(t => t.EmployeeId == me.Id && t.DoneAt == null).OrderBy(t => t.DueDate).ThenBy(t => t.SortOrder).Take(10).ToListAsync(ct);
        var notices = (await announcements.ForMeAsync(actor, ct)).Where(a => !a.Read || a.RequiresAck && !a.Acked).ToList();
        var slip = await (from i in db.PayRunItems.AsNoTracking() join p in db.PayRuns.AsNoTracking() on i.PayRunId equals p.Id where i.EmployeeId == me.Id && (p.Status == PayRunStatus.Locked || p.Status == PayRunStatus.Paid)
                          orderby p.Year descending, p.Month descending select new { p.Year, p.Month }).FirstOrDefaultAsync(ct);
        var holidayNext = await db.Holidays.AsNoTracking().Where(h => h.Date >= today).OrderBy(h => h.Date).Select(h => new { h.Date, h.Name }).FirstOrDefaultAsync(ct);
        return new MyHome(true, me.FullName, me.EmployeeNo, title, balances, todayLog, working, pendingLeave, pendingTime, pendingChanges,
            [.. next.Select(l => new TeamLeave(me.Id, me.FullName, types.GetValueOrDefault(l.LeaveTypeId) ?? "", l.StartDate, l.EndDate, l.Days, l.Status.ToString().ToLowerInvariant()))],
            [.. tasks.Select(t => new HomeTask(t.Id, t.Title, t.DueDate, t.DueDate is { } d && d < today))], notices.Count, [.. notices.Take(3).Select(a => new HomeNotice(a.Id, a.Title, a.RequiresAck && !a.Acked))],
            slip is null ? null : $"{slip.Year}-{slip.Month:D2}", holidayNext is null ? null : $"{holidayNext.Date:yyyy-MM-dd} {holidayNext.Name}");
    }
}
