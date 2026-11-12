using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record HourlyLeaveView(Guid Id, Guid EmployeeId, string EmployeeNo, string EmployeeName, DateOnly Date, string From, string To, decimal Hours, bool Paid, string Reason, string Status, string? DecisionNote, DateTimeOffset CreatedAt, bool CanDecide, bool CanCancel);
public sealed record HourlyLeaveRequest(DateOnly? Date, string? From, string? To, bool? Paid, string? Reason);
public sealed record RosterCell(Guid EmployeeId, DateOnly Date, Guid? ShiftId, string? ShiftCode, bool Planned, bool Works);
public sealed record RosterRow(Guid EmployeeId, string EmployeeNo, string EmployeeName, IReadOnlyList<RosterCell> Days);
public sealed record RosterSetRequest(IReadOnlyList<RosterSetItem>? Items);
public sealed record RosterSetItem(Guid EmployeeId, DateOnly Date, Guid? ShiftId, bool? Clear);
public sealed record RosterPatternRequest(IReadOnlyList<Guid>? EmployeeIds, DateOnly? From, DateOnly? To, IReadOnlyList<Guid?>? Pattern);
public sealed record OnCallView(Guid Id, Guid EmployeeId, string EmployeeName, DateOnly Date, decimal Allowance, string? Note);
public sealed record OnCallRequest(Guid? EmployeeId, IReadOnlyList<DateOnly>? Dates, decimal? Allowance, string? Note);
public sealed record ProjectView(Guid Id, string Code, string Name, string? Client, bool Archived, decimal HoursThisMonth);
public sealed record ProjectRequest(string? Code, string? Name, string? Client, bool? Archived);
public sealed record TimesheetEntryView(Guid Id, Guid ProjectId, string Project, DateOnly Date, decimal Hours, string? Note, string Status, string? DecisionNote);
public sealed record TimesheetWeek(DateOnly WeekStart, Guid EmployeeId, string EmployeeName, IReadOnlyList<TimesheetEntryView> Entries, decimal Total, string Status, bool CanEdit, bool CanDecide);
public sealed record TimesheetSaveRequest(IReadOnlyList<TimesheetLine>? Lines);
public sealed record TimesheetLine(Guid? ProjectId, DateOnly? Date, decimal? Hours, string? Note);
public sealed record TimesheetDecision(Guid? EmployeeId, DateOnly? WeekStart, bool? Approve, string? Note);
public sealed record KioskDeviceView(Guid Id, string Name, bool Active, DateTimeOffset? LastSeenAt, DateTimeOffset CreatedAt, string? Token = null);
public sealed record KioskDeviceRequest(string? Name);
public sealed record KioskPinRequest(string? Pin);
public sealed record KioskClockRequest(string? EmployeeNo, string? Pin, string? Action);
public sealed record KioskClockResult(string EmployeeName, string Action, DateTimeOffset At);
public sealed record FieldVisitView(Guid Id, Guid EmployeeId, string EmployeeName, DateTimeOffset At, string Kind, string Place, string? Note, double Latitude, double Longitude, double? Accuracy);
public sealed record FieldVisitRequest(string? Kind, string? Place, string? Note, double? Latitude, double? Longitude, double? Accuracy);
public sealed record BlockLeaveStatus(Guid EmployeeId, string EmployeeNo, string EmployeeName, string LeaveType, int Required, int LongestBlock, bool Met);

/// <summary>
/// Stage 13, advanced time and attendance (Talenta: hourly leave, shift planner, on-call, task &amp; timesheet, Tab Portal kiosk, live tracking, block leave).
/// Employees ask for hourly leave, book time, check in at visits and set their kiosk PIN; managers decide for their reports; HR (attendance.manage) plans rosters, on-call duties,
/// projects and kiosk devices. Unpaid hourly leave and on-call allowances flow into the pay run.
/// </summary>
public sealed class AttendancePlusService(IAppDb db, IClock clock, AuthorizationService authz, AuditWriter audit, ManagerScope managers, NotificationService notes, IPasswordHasher hasher, ITokenService tokens, IRateLimiter limiter, ITenantContext tenant)
{
    private static readonly TimeSpan Wib = TimeSpan.FromHours(7);
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime + Wib);
    private async Task<Employee> MeAsync(SessionPrincipal actor, CancellationToken ct)
        => await db.Employees.FirstOrDefaultAsync(e => e.UserId == actor.UserId && e.Status == EmployeeStatus.Active, ct) ?? throw DomainException.Invariant("Your account is not linked to an active employee record. Ask HR to link it.");
    private async Task<IReadOnlySet<string>> PermsAsync(SessionPrincipal actor, CancellationToken ct) => await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
    private async Task<HashSet<Guid>> ReportsAsync(SessionPrincipal actor, CancellationToken ct)
    {
        var acting = await managers.ActingIdsAsync(actor.UserId, ct); if (acting.Count == 0) return [];
        return [.. await db.Employments.AsNoTracking().Where(m => m.EffectiveTo == null && m.ManagerId != null && acting.Contains(m.ManagerId.Value)).Select(m => m.EmployeeId).ToListAsync(ct)];
    }
    private Task<bool> ClosedAsync(DateOnly d, CancellationToken ct) => db.AttendancePeriods.AnyAsync(p => p.Year == d.Year && p.Month == d.Month, ct);

    public async Task<Guid> MyEmployeeIdAsync(SessionPrincipal actor, CancellationToken ct = default) => (await MeAsync(actor, ct)).Id;

    // ============================================================ hourly leave
    private async Task<IReadOnlyList<HourlyLeaveView>> HourlyViewsAsync(IReadOnlyList<HourlyLeave> rows, SessionPrincipal actor, CancellationToken ct)
    {
        var ids = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var emps = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.EmployeeNo, e.FullName, e.UserId }).ToDictionaryAsync(e => e.Id, ct);
        var perms = await PermsAsync(actor, ct); var reports = perms.Contains(Permissions.AttendanceApprove) ? await ReportsAsync(actor, ct) : [];
        return [.. rows.Select(h =>
        {
            var e = emps[h.EmployeeId]; var own = e.UserId == actor.UserId;
            return new HourlyLeaveView(h.Id, h.EmployeeId, e.EmployeeNo, e.FullName, h.Date, h.From.ToString("HH:mm"), h.To.ToString("HH:mm"), Math.Round(h.Hours, 2), h.Paid, h.Reason, h.Status.ToString().ToLowerInvariant(), h.DecisionNote, h.CreatedAt,
                h.Status == RequestStatus.Pending && !own && (perms.Contains(Permissions.AttendanceManage) || reports.Contains(h.EmployeeId)), own && h.Status == RequestStatus.Pending);
        })];
    }

    public async Task<IReadOnlyList<HourlyLeaveView>> HourlyAsync(string? scope, SessionPrincipal actor, CancellationToken ct = default)
    {
        var q = db.HourlyLeaves.AsNoTracking().AsQueryable(); var perms = await PermsAsync(actor, ct);
        switch (scope ?? "mine")
        {
            case "mine": { var me = await MeAsync(actor, ct); q = q.Where(h => h.EmployeeId == me.Id); break; }
            case "team": { if (!perms.Contains(Permissions.AttendanceApprove) && !perms.Contains(Permissions.AttendanceManage)) throw DomainException.Denied(Permissions.AttendanceApprove); var r = await ReportsAsync(actor, ct); q = q.Where(h => r.Contains(h.EmployeeId)); break; }
            case "all": if (!perms.Contains(Permissions.AttendanceReadAll) && !perms.Contains(Permissions.AttendanceManage)) throw DomainException.Denied(Permissions.AttendanceReadAll); break;
            default: throw DomainException.Validation("Unknown scope.", new Dictionary<string, string> { ["scope"] = "Use mine, team or all." });
        }
        return await HourlyViewsAsync(await q.OrderByDescending(h => h.Status == RequestStatus.Pending).ThenByDescending(h => h.Date).Take(300).ToListAsync(ct), actor, ct);
    }

    public async Task<HourlyLeaveView> AskHourlyAsync(HourlyLeaveRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct); var errors = new Dictionary<string, string>();
        if (r.Date is null) errors["date"] = "Choose the day."; else if (r.Date < Today.AddDays(-7) || r.Date > Today.AddDays(60)) errors["date"] = "Choose a day from a week ago to 60 days ahead.";
        TimeOnly from = default, to = default;
        if (!TimeOnly.TryParseExact(r.From ?? "", "HH:mm", out from)) errors["from"] = "Enter the time as HH:mm.";
        if (!TimeOnly.TryParseExact(r.To ?? "", "HH:mm", out to)) errors["to"] = "Enter the time as HH:mm.";
        if (!errors.ContainsKey("from") && !errors.ContainsKey("to") && (to <= from || (to - from).TotalHours > 8)) errors["to"] = "End after the start, at most 8 hours later.";
        var reason = r.Reason?.Trim(); if (string.IsNullOrEmpty(reason) || reason.Length > 300) errors["reason"] = "Say why (up to 300 characters).";
        if (errors.Count > 0) throw DomainException.Validation("Please check the request.", errors);
        if (await ClosedAsync(r.Date!.Value, ct)) throw DomainException.Invariant("That month is closed for payroll. Ask HR to reopen it.");
        if (await db.HourlyLeaves.AnyAsync(h => h.EmployeeId == me.Id && h.Date == r.Date && h.Status != RequestStatus.Rejected && h.Status != RequestStatus.Cancelled && h.From < to && from < h.To, ct)) throw DomainException.Conflict("You already asked for hours that overlap these.");
        var h = new HourlyLeave { EmployeeId = me.Id, Date = r.Date!.Value, From = from, To = to, Paid = r.Paid ?? true, Reason = reason!, CreatedAt = clock.UtcNow };
        db.HourlyLeaves.Add(h);
        audit.Record("hr.hourly_leave.request", "employee", me.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["date"] = h.Date.ToString("yyyy-MM-dd"), ["paid"] = h.Paid.ToString() });
        foreach (var uid in await notes.ApproverUsersAsync(me.Id, ct)) notes.Notify(uid, "time.requested", $"{me.FullName} asked for leave by the hour", h.Date.ToString("d MMM yyyy", CultureInfo.InvariantCulture), "/hr/time-approvals");
        await db.SaveChangesAsync(ct);
        return (await HourlyViewsAsync([h], actor, ct))[0];
    }

    public async Task<HourlyLeaveView> DecideHourlyAsync(Guid id, bool? approve, string? note, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var h = await db.HourlyLeaves.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Request");
        var view = (await HourlyViewsAsync([h], actor, ct))[0];
        if (await db.Employees.AnyAsync(e => e.Id == h.EmployeeId && e.UserId == actor.UserId, ct)) throw DomainException.Invariant("You cannot decide your own request.");
        if (h.Status != RequestStatus.Pending) throw DomainException.Conflict("This request is already decided.");
        if (!view.CanDecide) throw DomainException.DeniedBecause("Only the employee's manager or HR can decide this request.");
        var reason = note?.Trim(); if (approve != true && string.IsNullOrEmpty(reason)) throw DomainException.Validation("Please give a reason.", new Dictionary<string, string> { ["note"] = "Tell the employee why." });
        if (approve == true && await ClosedAsync(h.Date, ct)) throw DomainException.Invariant("That month is closed for payroll. Reopen it first.");
        h.Status = approve == true ? RequestStatus.Approved : RequestStatus.Rejected; h.DecidedBy = actor.UserId; h.DecidedAt = clock.UtcNow; h.DecisionNote = reason?[..Math.Min(reason.Length, 300)]; h.Version++;
        var uid = await db.Employees.AsNoTracking().Where(e => e.Id == h.EmployeeId).Select(e => e.UserId).FirstOrDefaultAsync(ct);
        notes.Notify(uid, "time.decided", approve == true ? "Your leave by the hour was approved" : "Your leave by the hour was rejected", h.Date.ToString("d MMM yyyy", CultureInfo.InvariantCulture), "/hr/my-attendance");
        audit.Record(approve == true ? "hr.hourly_leave.approve" : "hr.hourly_leave.reject", "employee", h.EmployeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return (await HourlyViewsAsync([h], actor, ct))[0];
    }

    public async Task<HourlyLeaveView> CancelHourlyAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct); var h = await db.HourlyLeaves.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == me.Id, ct) ?? throw DomainException.NotFound("Request");
        if (h.Status != RequestStatus.Pending) throw DomainException.Conflict("Only a waiting request can be cancelled.");
        h.Status = RequestStatus.Cancelled; h.Version++; audit.Record("hr.hourly_leave.cancel", "employee", me.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct); return (await HourlyViewsAsync([h], actor, ct))[0];
    }

    // ============================================================ roster (shift planner)
    public async Task<IReadOnlyList<RosterRow>> RosterAsync(DateOnly from, DateOnly to, Guid? orgUnitId, Guid? employeeId, ShiftService shifts, CancellationToken ct = default)
    {
        if (to < from || to.DayNumber - from.DayNumber > 62) throw DomainException.Validation("Please check the dates.", new Dictionary<string, string> { ["to"] = "Show at most two months at a time." });
        var q = db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active);
        if (orgUnitId is { } u) q = q.Where(e => e.Employments.Any(m => m.EffectiveTo == null && m.OrgUnitId == u));
        if (employeeId is { } eid) q = q.Where(e => e.Id == eid);
        var emps = await q.OrderBy(e => e.FullName).Select(e => new { e.Id, e.EmployeeNo, e.FullName }).Take(300).ToListAsync(ct);
        var lookup = await shifts.LookupAsync(emps.Select(e => e.Id), from, to, ct);
        var planned = (await db.RosterEntries.AsNoTracking().Where(x => x.Date >= from && x.Date <= to).Select(x => new { x.EmployeeId, x.Date }).ToListAsync(ct)).Select(x => (x.EmployeeId, x.Date)).ToHashSet();
        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i)).ToList();
        return [.. emps.Select(e => new RosterRow(e.Id, e.EmployeeNo, e.FullName, [.. days.Select(d => { var works = lookup.Works(e.Id, d); var s = lookup.For(e.Id, d); return new RosterCell(e.Id, d, works && s.Id != ShiftLookup.Default.Id ? s.Id : null, works ? s.Code : null, planned.Contains((e.Id, d)), works); })]))];
    }

    public async Task<int> SetRosterAsync(RosterSetRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var items = r.Items ?? []; if (items.Count is 0 or > 2000) throw DomainException.Validation("Please check the roster.", new Dictionary<string, string> { ["items"] = "Send between 1 and 2,000 days." });
        var shiftIds = items.Where(i => i.ShiftId != null).Select(i => i.ShiftId!.Value).Distinct().ToList();
        if (await db.Shifts.CountAsync(s => shiftIds.Contains(s.Id) && s.ArchivedAt == null, ct) != shiftIds.Count) throw DomainException.Validation("Please check the roster.", new Dictionary<string, string> { ["shiftId"] = "Choose active shifts." });
        foreach (var month in items.Select(i => new DateOnly(i.Date.Year, i.Date.Month, 1)).Distinct()) if (await ClosedAsync(month, ct)) throw DomainException.Invariant($"{month:MMMM yyyy} is closed for payroll. Reopen it first.");
        var emps = items.Select(i => i.EmployeeId).Distinct().ToList(); var min = items.Min(i => i.Date); var max = items.Max(i => i.Date);
        var existing = await db.RosterEntries.Where(x => emps.Contains(x.EmployeeId) && x.Date >= min && x.Date <= max).ToListAsync(ct);
        var changed = 0;
        foreach (var it in items)
        {
            var row = existing.FirstOrDefault(x => x.EmployeeId == it.EmployeeId && x.Date == it.Date);
            if (it.Clear == true) { if (row is not null) { db.RosterEntries.Remove(row); existing.Remove(row); changed++; } continue; }
            if (row is null) { row = new RosterEntry { EmployeeId = it.EmployeeId, Date = it.Date, CreatedBy = actor.UserId, CreatedAt = clock.UtcNow }; db.RosterEntries.Add(row); existing.Add(row); }
            row.ShiftId = it.ShiftId; changed++;
        }
        audit.Record("hr.roster.set", "roster", null, Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["days"] = changed.ToString(), ["from"] = min.ToString("yyyy-MM-dd"), ["to"] = max.ToString("yyyy-MM-dd") });
        await db.SaveChangesAsync(ct);
        return changed;
    }

    /// <summary>Fills a period by repeating a pattern of shifts and days off (e.g. Morning, Morning, Night, Night, off, off) for each chosen person, starting on the first day.</summary>
    public async Task<int> ApplyPatternAsync(RosterPatternRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        if (r.EmployeeIds is not { Count: > 0 and <= 300 }) errors["employeeIds"] = "Choose up to 300 people.";
        if (r.Pattern is not { Count: > 0 and <= 28 }) errors["pattern"] = "Give a pattern of 1 to 28 days.";
        if (r.From is null || r.To is null || r.To < r.From || r.To.Value.DayNumber - r.From.Value.DayNumber > 92) errors["to"] = "Choose a period of up to three months.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the pattern.", errors);
        var items = new List<RosterSetItem>();
        foreach (var e in r.EmployeeIds!) for (var d = r.From!.Value; d <= r.To!.Value; d = d.AddDays(1)) items.Add(new RosterSetItem(e, d, r.Pattern![(d.DayNumber - r.From.Value.DayNumber) % r.Pattern.Count], null));
        return await SetRosterAsync(new RosterSetRequest(items), actor, client, ct);
    }

    // ============================================================ on-call duty
    public async Task<IReadOnlyList<OnCallView>> OnCallAsync(int year, int month, CancellationToken ct = default)
    {
        var first = new DateOnly(year, month, 1); var last = first.AddMonths(1).AddDays(-1);
        var rows = await db.OnCallDuties.AsNoTracking().Where(o => o.Date >= first && o.Date <= last).OrderBy(o => o.Date).ToListAsync(ct); var ids = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        return [.. rows.Select(o => new OnCallView(o.Id, o.EmployeeId, names.GetValueOrDefault(o.EmployeeId) ?? "", o.Date, o.Allowance, o.Note))];
    }

    public async Task<int> AddOnCallAsync(OnCallRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        if (r.EmployeeId is null || !await db.Employees.AnyAsync(e => e.Id == r.EmployeeId && e.Status == EmployeeStatus.Active, ct)) errors["employeeId"] = "Choose an active employee.";
        if (r.Dates is not { Count: > 0 and <= 62 }) errors["dates"] = "Choose up to 62 days.";
        if (r.Allowance is null or < 0 or > 100_000_000m) errors["allowance"] = "Enter the allowance per day in rupiah (0 if none).";
        if (errors.Count > 0) throw DomainException.Validation("Please check the on-call duty.", errors);
        foreach (var month in r.Dates!.Select(d => new DateOnly(d.Year, d.Month, 1)).Distinct()) if (await ClosedAsync(month, ct)) throw DomainException.Invariant($"{month:MMMM yyyy} is closed for payroll.");
        var have = (await db.OnCallDuties.AsNoTracking().Where(o => o.EmployeeId == r.EmployeeId && r.Dates.Contains(o.Date)).Select(o => o.Date).ToListAsync(ct)).ToHashSet(); var added = 0;
        foreach (var d in r.Dates.Distinct().Where(d => !have.Contains(d))) { db.OnCallDuties.Add(new OnCallDuty { EmployeeId = r.EmployeeId!.Value, Date = d, Allowance = PayrollRules.Rupiah(r.Allowance!.Value), Note = r.Note?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 200)] : null, CreatedBy = actor.UserId, CreatedAt = clock.UtcNow }); added++; }
        audit.Record("hr.on_call.add", "employee", r.EmployeeId!.Value.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["days"] = added.ToString() });
        await db.SaveChangesAsync(ct);
        return added;
    }

    public async Task DeleteOnCallAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var o = await db.OnCallDuties.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("On-call duty");
        if (await ClosedAsync(o.Date, ct)) throw DomainException.Invariant("That month is closed for payroll.");
        db.OnCallDuties.Remove(o); audit.Record("hr.on_call.delete", "employee", o.EmployeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email); await db.SaveChangesAsync(ct);
    }

    // ============================================================ projects and timesheets
    private static DateOnly Monday(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    public async Task<IReadOnlyList<ProjectView>> ProjectsAsync(bool includeArchived, CancellationToken ct = default)
    {
        var first = new DateOnly(Today.Year, Today.Month, 1);
        var hours = await db.TimesheetEntries.AsNoTracking().Where(t => t.Date >= first && t.Status != TimesheetStatus.Rejected).GroupBy(t => t.ProjectId).Select(g => new { g.Key, H = g.Sum(x => x.Hours) }).ToDictionaryAsync(x => x.Key, x => x.H, ct);
        return [.. (await db.Projects.AsNoTracking().Where(p => includeArchived || !p.Archived).OrderBy(p => p.Archived).ThenBy(p => p.Code).ToListAsync(ct)).Select(p => new ProjectView(p.Id, p.Code, p.Name, p.Client, p.Archived, hours.GetValueOrDefault(p.Id)))];
    }

    public async Task<ProjectView> SaveProjectAsync(Guid? id, ProjectRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var p = id is { } pid ? await db.Projects.FirstOrDefaultAsync(x => x.Id == pid, ct) ?? throw DomainException.NotFound("Project") : new Project { CreatedAt = clock.UtcNow };
        var errors = new Dictionary<string, string>();
        var code = (r.Code ?? p.Code).Trim().ToUpperInvariant(); if (code.Length is < 1 or > 20) errors["code"] = "Enter a short code (up to 20 characters).";
        var name = (r.Name ?? p.Name).Trim(); if (name.Length is < 1 or > 120) errors["name"] = "Enter a name (up to 120 characters).";
        if (errors.Count == 0 && await db.Projects.AnyAsync(x => x.Code == code && x.Id != p.Id, ct)) errors["code"] = "Another project already uses this code.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the project.", errors);
        p.Code = code; p.Name = name; p.Client = r.Client is null ? p.Client : r.Client.Trim() is { Length: > 0 } c ? c[..Math.Min(c.Length, 120)] : null; p.Archived = r.Archived ?? p.Archived;
        if (id is null) db.Projects.Add(p);
        audit.Record(id is null ? "hr.project.create" : "hr.project.update", "project", p.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["code"] = code });
        await db.SaveChangesAsync(ct);
        return (await ProjectsAsync(true, ct)).First(x => x.Id == p.Id);
    }

    private static string WeekStatus(IReadOnlyCollection<TimesheetEntry> rows) => rows.Count == 0 ? "empty" : rows.All(r => r.Status == TimesheetStatus.Approved) ? "approved" : rows.Any(r => r.Status == TimesheetStatus.Submitted) ? "submitted" : rows.Any(r => r.Status == TimesheetStatus.Rejected) ? "rejected" : "draft";

    public async Task<TimesheetWeek> WeekAsync(Guid? employeeId, DateOnly weekStart, SessionPrincipal actor, CancellationToken ct = default)
    {
        var start = Monday(weekStart); var end = start.AddDays(6); var perms = await PermsAsync(actor, ct);
        Employee emp;
        if (employeeId is null) emp = await MeAsync(actor, ct);
        else
        {
            emp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct) ?? throw DomainException.NotFound("Employee");
            if (emp.UserId != actor.UserId && !perms.Contains(Permissions.AttendanceManage) && !perms.Contains(Permissions.AttendanceReadAll) && !(await ReportsAsync(actor, ct)).Contains(emp.Id)) throw DomainException.NotFound("Employee");
        }
        var rows = await db.TimesheetEntries.AsNoTracking().Where(t => t.EmployeeId == emp.Id && t.Date >= start && t.Date <= end).OrderBy(t => t.Date).ToListAsync(ct);
        var projects = await db.Projects.AsNoTracking().ToDictionaryAsync(p => p.Id, p => $"{p.Code} · {p.Name}", ct);
        var status = WeekStatus(rows); var own = emp.UserId == actor.UserId;
        var canDecide = !own && status == "submitted" && (perms.Contains(Permissions.AttendanceManage) || perms.Contains(Permissions.AttendanceApprove) && (await ReportsAsync(actor, ct)).Contains(emp.Id));
        return new TimesheetWeek(start, emp.Id, emp.FullName, [.. rows.Select(t => new TimesheetEntryView(t.Id, t.ProjectId, projects.GetValueOrDefault(t.ProjectId) ?? "", t.Date, t.Hours, t.Note, t.Status.ToString().ToLowerInvariant(), t.DecisionNote))],
            rows.Sum(t => t.Hours), status, own && status is "empty" or "draft" or "rejected", canDecide);
    }

    /// <summary>Replaces the person's draft (or rejected) entries of one week with the given lines; <paramref name="submit"/> hands the week to the manager.</summary>
    public async Task<TimesheetWeek> SaveWeekAsync(DateOnly weekStart, TimesheetSaveRequest r, bool submit, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct); var start = Monday(weekStart); var end = start.AddDays(6);
        var current = await db.TimesheetEntries.Where(t => t.EmployeeId == me.Id && t.Date >= start && t.Date <= end).ToListAsync(ct);
        if (current.Any(t => t.Status is TimesheetStatus.Submitted or TimesheetStatus.Approved)) throw DomainException.Conflict("This week is already handed in.");
        var lines = r.Lines ?? []; var errors = new Dictionary<string, string>();
        var projects = (await db.Projects.AsNoTracking().Where(p => !p.Archived).Select(p => p.Id).ToListAsync(ct)).ToHashSet();
        foreach (var (l, i) in lines.Select((l, i) => (l, i)))
        {
            if (l.ProjectId is null || !projects.Contains(l.ProjectId.Value)) errors[$"lines.{i}"] = "Choose an active project.";
            else if (l.Date is null || l.Date < start || l.Date > end) errors[$"lines.{i}"] = "The day must be in this week.";
            else if (l.Hours is null or <= 0 or > 24 || l.Hours % 0.25m != 0) errors[$"lines.{i}"] = "Enter hours in quarters, up to 24.";
        }
        if (lines.GroupBy(l => l.Date).Any(g => g.Sum(x => x.Hours ?? 0) > 24)) errors["lines"] = "No day can have more than 24 hours.";
        if (submit && lines.Count == 0) errors["lines"] = "Add at least one line before handing in.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the timesheet.", errors);
        db.TimesheetEntries.RemoveRange(current);
        foreach (var l in lines) db.TimesheetEntries.Add(new TimesheetEntry { EmployeeId = me.Id, ProjectId = l.ProjectId!.Value, Date = l.Date!.Value, Hours = l.Hours!.Value, Note = l.Note?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 300)] : null, Status = submit ? TimesheetStatus.Submitted : TimesheetStatus.Draft, CreatedAt = clock.UtcNow });
        audit.Record(submit ? "hr.timesheet.submit" : "hr.timesheet.save", "employee", me.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["week"] = start.ToString("yyyy-MM-dd"), ["hours"] = lines.Sum(l => l.Hours ?? 0).ToString(CultureInfo.InvariantCulture) });
        if (submit) foreach (var uid in await notes.ApproverUsersAsync(me.Id, ct)) notes.Notify(uid, "time.requested", $"{me.FullName} handed in a timesheet", $"Week of {start:d MMM}", "/hr/timesheets");
        await db.SaveChangesAsync(ct);
        return await WeekAsync(null, start, actor, ct);
    }

    public async Task<IReadOnlyList<TimesheetWeek>> PendingWeeksAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var perms = await PermsAsync(actor, ct); var all = perms.Contains(Permissions.AttendanceManage); var reports = await ReportsAsync(actor, ct);
        if (!all && !perms.Contains(Permissions.AttendanceApprove)) throw DomainException.Denied(Permissions.AttendanceApprove);
        var weeks = (await db.TimesheetEntries.AsNoTracking().Where(t => t.Status == TimesheetStatus.Submitted).Select(t => new { t.EmployeeId, t.Date }).ToListAsync(ct))
            .Where(x => all || reports.Contains(x.EmployeeId)).Select(x => (x.EmployeeId, Week: Monday(x.Date))).Distinct().OrderBy(x => x.Week).Take(100).ToList();
        var list = new List<TimesheetWeek>(); foreach (var (e, w) in weeks) list.Add(await WeekAsync(e, w, actor, ct));
        return list;
    }

    public async Task<TimesheetWeek> DecideWeekAsync(TimesheetDecision r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        if (r.EmployeeId is null || r.WeekStart is null) throw DomainException.Validation("Please check the decision.", new Dictionary<string, string> { ["weekStart"] = "Choose the person and the week." });
        var week = await WeekAsync(r.EmployeeId, r.WeekStart.Value, actor, ct);
        if (!week.CanDecide) throw DomainException.DeniedBecause("Only the employee's manager or HR can decide a handed-in timesheet.");
        var note = r.Note?.Trim(); if (r.Approve != true && string.IsNullOrEmpty(note)) throw DomainException.Validation("Please give a reason.", new Dictionary<string, string> { ["note"] = "Tell the employee what to fix." });
        var start = week.WeekStart; var end = start.AddDays(6);
        foreach (var t in await db.TimesheetEntries.Where(t => t.EmployeeId == r.EmployeeId && t.Date >= start && t.Date <= end && t.Status == TimesheetStatus.Submitted).ToListAsync(ct))
        { t.Status = r.Approve == true ? TimesheetStatus.Approved : TimesheetStatus.Rejected; t.DecidedBy = actor.UserId; t.DecidedAt = clock.UtcNow; t.DecisionNote = note?[..Math.Min(note.Length, 300)]; }
        var uid = await db.Employees.AsNoTracking().Where(e => e.Id == r.EmployeeId).Select(e => e.UserId).FirstOrDefaultAsync(ct);
        notes.Notify(uid, "time.decided", r.Approve == true ? "Your timesheet was approved" : "Your timesheet was sent back", $"Week of {start:d MMM}", "/hr/timesheets");
        audit.Record(r.Approve == true ? "hr.timesheet.approve" : "hr.timesheet.reject", "employee", r.EmployeeId.Value.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["week"] = start.ToString("yyyy-MM-dd") });
        await db.SaveChangesAsync(ct);
        return await WeekAsync(r.EmployeeId, start, actor, ct);
    }

    /// <summary>Approved hours per project and person for one month.</summary>
    public async Task<ExportFile> TimesheetReportAsync(int year, int month, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var first = new DateOnly(year, month, 1); var last = first.AddMonths(1).AddDays(-1);
        var rows = await (from t in db.TimesheetEntries.AsNoTracking() join p in db.Projects.AsNoTracking() on t.ProjectId equals p.Id join e in db.Employees.AsNoTracking() on t.EmployeeId equals e.Id
                          where t.Date >= first && t.Date <= last && t.Status == TimesheetStatus.Approved select new { p.Code, p.Name, p.Client, e.EmployeeNo, e.FullName, t.Hours }).ToListAsync(ct);
        var sb = new StringBuilder("project_code,project,client,employee_no,name,hours\r\n");
        foreach (var g in rows.GroupBy(r => new { r.Code, r.Name, r.Client, r.EmployeeNo, r.FullName }).OrderBy(g => g.Key.Code).ThenBy(g => g.Key.FullName))
            sb.Append(string.Join(',', new[] { g.Key.Code, g.Key.Name, g.Key.Client ?? "", g.Key.EmployeeNo, g.Key.FullName, g.Sum(x => x.Hours).ToString("0.##", CultureInfo.InvariantCulture) }.Select(EmployeeService.Csv))).Append("\r\n");
        audit.Record("hr.timesheet.report", "attendance", null, Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["month"] = first.ToString("yyyy-MM") });
        await db.SaveChangesAsync(ct);
        return new ExportFile($"timesheets-{first:yyyy-MM}.csv", "text/csv; charset=utf-8", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())]);
    }

    // ============================================================ kiosk (shared attendance tablet)
    public async Task<IReadOnlyList<KioskDeviceView>> KiosksAsync(CancellationToken ct = default)
        => [.. (await db.KioskDevices.AsNoTracking().OrderByDescending(k => k.Active).ThenBy(k => k.Name).ToListAsync(ct)).Select(k => new KioskDeviceView(k.Id, k.Name, k.Active, k.LastSeenAt, k.CreatedAt))];

    /// <summary>Registers a tablet. The token is shown once (it goes into the kiosk address); only its hash is kept.</summary>
    public async Task<KioskDeviceView> CreateKioskAsync(KioskDeviceRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var name = r.Name?.Trim(); if (string.IsNullOrEmpty(name) || name.Length > 100) throw DomainException.Validation("Please check the device.", new Dictionary<string, string> { ["name"] = "Name the device, e.g. Lobby tablet." });
        var token = tokens.NewToken(); var k = new KioskDevice { Name = name, TokenHash = tokens.Hash(token), CreatedAt = clock.UtcNow };
        db.KioskDevices.Add(k); audit.Record("hr.kiosk.create", "kiosk", k.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name });
        await db.SaveChangesAsync(ct);
        return new KioskDeviceView(k.Id, k.Name, true, null, k.CreatedAt, token);
    }

    public async Task DeactivateKioskAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var k = await db.KioskDevices.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Device");
        k.Active = false; audit.Record("hr.kiosk.deactivate", "kiosk", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email); await db.SaveChangesAsync(ct);
    }

    public async Task SetMyPinAsync(KioskPinRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct); var pin = r.Pin?.Trim() ?? "";
        if (pin.Length is < 4 or > 8 || !pin.All(char.IsAsciiDigit) || pin.Distinct().Count() == 1 || "0123456789".Contains(pin) || "9876543210".Contains(pin))
            throw DomainException.Validation("Please choose another PIN.", new Dictionary<string, string> { ["pin"] = "Use 4–8 digits, not all the same and not a straight run like 1234." });
        me.KioskPinHash = hasher.Hash(pin);
        audit.Record("hr.kiosk.pin_set", "employee", me.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> HasPinAsync(SessionPrincipal actor, CancellationToken ct = default) => (await MeAsync(actor, ct)).KioskPinHash is not null;

    /// <summary>Clock in or out at a kiosk: the device proves itself with its token, the person with employee number and PIN. Wrong PINs are limited per person and per device.</summary>
    public async Task<KioskClockResult> KioskClockAsync(string? deviceToken, KioskClockRequest r, ClientInfo client, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceToken)) throw DomainException.Unauthenticated("This device is not registered.");
        var hash = tokens.Hash(deviceToken.Trim());
        var device = await db.KioskDevices.IgnoreQueryFilters().FirstOrDefaultAsync(k => k.TokenHash == hash && k.Active, ct) ?? throw DomainException.Unauthenticated("This device is not registered.");
        tenant.Use(device.TenantId, false);   // the kiosk request is anonymous: from here on it acts inside the device's organisation
        var hitDevice = await limiter.HitAsync($"kiosk:device:{device.Id}", 600, TimeSpan.FromMinutes(10), ct); if (!hitDevice.Allowed) throw DomainException.RateLimited(hitDevice.RetryAfter);
        var no = r.EmployeeNo?.Trim() ?? ""; var action = r.Action?.Trim().ToLowerInvariant();
        if (action is not ("in" or "out")) throw DomainException.Validation("Choose clock in or out.", new Dictionary<string, string> { ["action"] = "Use in or out." });
        var tries = await limiter.HitAsync($"kiosk:pin:{device.TenantId}:{no.ToUpperInvariant()}", 5, TimeSpan.FromMinutes(15), ct);
        if (!tries.Allowed) throw DomainException.RateLimited(tries.RetryAfter);
        var emp = await db.Employees.FirstOrDefaultAsync(e => e.EmployeeNo == no && e.Status == EmployeeStatus.Active, ct);
        if (emp?.KioskPinHash is null || !hasher.Verify(r.Pin ?? "", emp.KioskPinHash))
        {
            audit.Record("hr.kiosk.wrong_pin", "kiosk", device.Id.ToString(), Outcome.Failure, client, null, null, new Dictionary<string, string> { ["employeeNo"] = no.Length > 30 ? no[..30] : no });
            await db.SaveChangesAsync(ct);
            throw DomainException.Validation("Employee number or PIN is not right.", new Dictionary<string, string> { ["pin"] = "Check your employee number and PIN." });
        }
        await limiter.ResetAsync($"kiosk:pin:{device.TenantId}:{no.ToUpperInvariant()}", ct);
        var now = clock.UtcNow; device.LastSeenAt = now;
        var open = await db.AttendanceLogs.FirstOrDefaultAsync(l => l.EmployeeId == emp.Id && l.ClockOut == null, ct);
        if (action == "in")
        {
            if (open is not null) throw DomainException.Conflict($"{emp.FullName} is already clocked in since {(open.ClockIn + Wib):HH:mm}.");
            if (await ClosedAsync(DateOnly.FromDateTime(now.UtcDateTime + Wib), ct)) throw DomainException.Invariant("This month is closed for payroll.");
            db.AttendanceLogs.Add(new AttendanceLog { EmployeeId = emp.Id, Date = DateOnly.FromDateTime(now.UtcDateTime + Wib), ClockIn = now, Ip = client.Ip, Source = "kiosk", WorkMode = WorkMode.Office });
        }
        else
        {
            if (open is null) throw DomainException.Conflict($"{emp.FullName} is not clocked in.");
            open.ClockOut = now; if (open.BreakStartedAt is { } b) { open.BreakMinutes += (int)Math.Round((now - b).TotalMinutes); open.BreakStartedAt = null; }
        }
        audit.Record(action == "in" ? "hr.attendance.clock_in" : "hr.attendance.clock_out", "employee", emp.Id.ToString(), Outcome.Success, client, emp.UserId, null, new Dictionary<string, string> { ["source"] = "kiosk", ["device"] = device.Name });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ResetTracking(); throw DomainException.Conflict("Already clocked in."); }
        return new KioskClockResult(emp.FullName, action, now);
    }

    public async Task<string> KioskNameAsync(string? deviceToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceToken)) throw DomainException.Unauthenticated("This device is not registered.");
        var hash = tokens.Hash(deviceToken.Trim());
        return await db.KioskDevices.IgnoreQueryFilters().Where(k => k.TokenHash == hash && k.Active).Select(k => k.Name).FirstOrDefaultAsync(ct) ?? throw DomainException.Unauthenticated("This device is not registered.");
    }

    // ============================================================ field visits (live tracking)
    public async Task<FieldVisitView> VisitAsync(FieldVisitRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct); var errors = new Dictionary<string, string>();
        var kind = (r.Kind ?? "arrive").Trim().ToLowerInvariant(); if (kind is not ("arrive" or "leave")) errors["kind"] = "Use arrive or leave.";
        var place = r.Place?.Trim(); if (string.IsNullOrEmpty(place) || place.Length > 150) errors["place"] = "Name the place or client (up to 150 characters).";
        if (r.Latitude is null or < -90 or > 90 || r.Longitude is null or < -180 or > 180) errors["latitude"] = "Share your location to check in at a visit.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the visit.", errors);
        var now = clock.UtcNow;
        var v = new FieldVisit { EmployeeId = me.Id, At = now, Date = DateOnly.FromDateTime(now.UtcDateTime + Wib), Kind = kind, Place = place!, Note = r.Note?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 300)] : null,
            Latitude = Math.Round(r.Latitude!.Value, 5), Longitude = Math.Round(r.Longitude!.Value, 5), Accuracy = r.Accuracy is { } a ? Math.Round(a, 1) : null };
        db.FieldVisits.Add(v); audit.Record("hr.visit.check_in", "employee", me.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["kind"] = kind });
        await db.SaveChangesAsync(ct);
        return new FieldVisitView(v.Id, me.Id, me.FullName, v.At, v.Kind, v.Place, v.Note, v.Latitude, v.Longitude, v.Accuracy);
    }

    public async Task<IReadOnlyList<FieldVisitView>> VisitsAsync(DateOnly date, Guid? employeeId, bool mine, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var q = db.FieldVisits.AsNoTracking().Where(v => v.Date == date);
        if (mine) { var me = await MeAsync(actor, ct); q = q.Where(v => v.EmployeeId == me.Id); }
        else
        {
            var perms = await PermsAsync(actor, ct);
            if (!perms.Contains(Permissions.AttendanceReadAll) && !perms.Contains(Permissions.AttendanceManage)) { var reports = await ReportsAsync(actor, ct); if (reports.Count == 0) throw DomainException.Denied(Permissions.AttendanceReadAll); q = q.Where(v => reports.Contains(v.EmployeeId)); }
            if (employeeId is { } e) q = q.Where(v => v.EmployeeId == e);
            audit.Record("hr.visit.viewed", "attendance", date.ToString("yyyy-MM-dd"), Outcome.Success, client, actor.UserId, actor.Email); await db.SaveChangesAsync(ct);   // looking at people's positions is recorded
        }
        var rows = await q.OrderBy(v => v.At).Take(1000).ToListAsync(ct); var ids = rows.Select(v => v.EmployeeId).Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        return [.. rows.Select(v => new FieldVisitView(v.Id, v.EmployeeId, names.GetValueOrDefault(v.EmployeeId) ?? "", v.At, v.Kind, v.Place, v.Note, v.Latitude, v.Longitude, v.Accuracy))];
    }

    // ============================================================ block leave
    /// <summary>For each leave type with a block-leave rule: who has, and who has not yet, taken that many consecutive working days of it this year.</summary>
    public async Task<IReadOnlyList<BlockLeaveStatus>> BlockLeaveAsync(int year, ShiftService shifts, CancellationToken ct = default)
    {
        var types = await db.LeaveTypes.AsNoTracking().Where(t => t.BlockDays != null && t.BlockDays > 0).ToListAsync(ct); if (types.Count == 0) return [];
        DateOnly jan1 = new(year, 1, 1), dec31 = new(year, 12, 31);
        var emps = await db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active && e.HireDate <= dec31).OrderBy(e => e.FullName).Select(e => new { e.Id, e.EmployeeNo, e.FullName }).ToListAsync(ct);
        var typeIds = types.Select(t => t.Id).ToList();
        var leave = await db.LeaveRequests.AsNoTracking().Where(l => l.Status == LeaveStatus.Approved && typeIds.Contains(l.LeaveTypeId) && l.StartDate <= dec31 && l.EndDate >= jan1).ToListAsync(ct);
        var lookup = await shifts.LookupAsync(emps.Select(e => e.Id), jan1, dec31, ct);
        var holidays = (await db.Holidays.AsNoTracking().Where(h => h.Date >= jan1 && h.Date <= dec31).Select(h => h.Date).ToListAsync(ct)).ToHashSet();
        var list = new List<BlockLeaveStatus>();
        foreach (var t in types)
            foreach (var e in emps)
            {
                // working days covered by approved leave of this type, then the longest run of consecutive working days (rest days and holidays in between do not break a run)
                var covered = new HashSet<DateOnly>();
                foreach (var l in leave.Where(l => l.EmployeeId == e.Id && l.LeaveTypeId == t.Id && !l.HalfDay)) for (var d = l.StartDate < jan1 ? jan1 : l.StartDate; d <= (l.EndDate > dec31 ? dec31 : l.EndDate); d = d.AddDays(1)) covered.Add(d);
                int best = 0, run = 0;
                for (var d = jan1; d <= dec31; d = d.AddDays(1))
                {
                    var working = lookup.Works(e.Id, d) && !holidays.Contains(d);
                    if (!working) continue;
                    run = covered.Contains(d) ? run + 1 : 0; best = Math.Max(best, run);
                }
                list.Add(new BlockLeaveStatus(e.Id, e.EmployeeNo, e.FullName, t.Name, t.BlockDays!.Value, best, best >= t.BlockDays));
            }
        return list;
    }
}
