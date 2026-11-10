using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record SubmitCorrectionRequest(DateOnly? Date, string? ClockIn, string? ClockOut, string? Reason);
public sealed record SubmitOvertimeRequest(DateOnly? Date, decimal? Hours, string? Reason);
public sealed record TimeQuery(string? Scope, string? Status, int Page = 1, int PageSize = 20);
public sealed record TimeRequestView(Guid Id, string Kind, Guid EmployeeId, string EmployeeNo, string EmployeeName, DateOnly Date, string? ClockIn, string? ClockOut, decimal? Hours, string Reason,
    string Status, string? DecisionNote, DateTimeOffset CreatedAt, bool CanDecide, bool CanCancel);
public sealed record PeriodView(int Year, int Month, DateTimeOffset ClosedAt);
public sealed record ReopenRequest(string? Reason);

/// <summary>
/// ATT-004/005/008. A person can ask to fix a forgotten clock-in/out (only for a day that has no finished record) and to work overtime (0.5–4 h a day, at most 18 h a week, asked beforehand
/// or within three days after). Direct managers decide for their reports, HR for anyone, nobody for themselves. A month closed for payroll accepts no new requests and no decisions until HR reopens it.
/// </summary>
public sealed class TimeRequestService(IAppDb db, IClock clock, AuthorizationService authz, SettingsProvider settings, AuditWriter audit, ManagerScope managers)
{
    public const decimal MaxHoursPerDay = 4m;
    public const decimal MaxHoursPerWeek = 18m;
    private static readonly TimeSpan Wib = TimeSpan.FromHours(7);
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime + Wib);

    private async Task<Employee> MeAsync(SessionPrincipal actor, CancellationToken ct)
        => await db.Employees.FirstOrDefaultAsync(e => e.UserId == actor.UserId && e.Status == EmployeeStatus.Active, ct)
           ?? throw DomainException.Invariant("Your account is not linked to an active employee record. Ask HR to link it.");

    private Task<bool> ClosedAsync(DateOnly d, CancellationToken ct) => db.AttendancePeriods.AnyAsync(p => p.Year == d.Year && p.Month == d.Month, ct);

    private async Task<bool> IsManagerOfAsync(SessionPrincipal actor, Guid employeeId, CancellationToken ct)
    {
        var acting = await managers.ActingIdsAsync(actor.UserId, ct);
        return acting.Count > 0 && await db.Employments.AnyAsync(m => m.EffectiveTo == null && m.EmployeeId == employeeId && m.ManagerId != null && acting.Contains(m.ManagerId.Value), ct);
    }

    private static string Hm(TimeOnly t) => t.ToString("HH:mm");

    // ============================================================ corrections
    public async Task<TimeRequestView> SubmitCorrectionAsync(SubmitCorrectionRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var emp = await MeAsync(actor, ct); var errors = new Dictionary<string, string>();
        if (r.Date is null) errors["date"] = "Choose the day."; else if (r.Date > Today) errors["date"] = "You can only correct a day that has passed."; else if (r.Date < Today.AddDays(-45)) errors["date"] = "Corrections go back at most 45 days."; else if (r.Date < emp.HireDate) errors["date"] = "That is before your hire date.";
        TimeOnly tin = default, tout = default;
        if (!TimeOnly.TryParseExact(r.ClockIn ?? "", "HH:mm", out tin)) errors["clockIn"] = "Enter the time as HH:mm.";
        if (!TimeOnly.TryParseExact(r.ClockOut ?? "", "HH:mm", out tout)) errors["clockOut"] = "Enter the time as HH:mm.";
        if (!errors.ContainsKey("clockIn") && !errors.ContainsKey("clockOut") && tout <= tin) errors["clockOut"] = "Clock-out must be after clock-in.";
        var reason = r.Reason?.Trim(); if (string.IsNullOrEmpty(reason) || reason.Length > 300) errors["reason"] = "Explain what happened (up to 300 characters).";
        if (errors.Count > 0) throw DomainException.Validation("Please check the correction.", errors);
        var date = r.Date!.Value;
        if (await ClosedAsync(date, ct)) throw DomainException.Invariant("That month is closed for payroll. Ask HR to reopen it.");
        if (await db.AttendanceLogs.AnyAsync(l => l.EmployeeId == emp.Id && l.Date == date && l.ClockOut != null, ct)) throw DomainException.Conflict("You already have attendance recorded for that day. Ask HR to change it.");
        if (await db.AttendanceCorrections.AnyAsync(c => c.EmployeeId == emp.Id && c.Date == date && c.Status == RequestStatus.Pending, ct)) throw DomainException.Conflict("There is already a pending correction for that day.");
        var c = new AttendanceCorrection { EmployeeId = emp.Id, Date = date, ClockIn = tin, ClockOut = tout, Reason = reason!, CreatedAt = clock.UtcNow };
        db.AttendanceCorrections.Add(c);
        audit.Record("hr.attendance.correction_request", "employee", emp.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["date"] = date.ToString("yyyy-MM-dd") });
        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([c], [], actor, ct))[0];
    }

    // ============================================================ overtime
    public async Task<TimeRequestView> SubmitOvertimeAsync(SubmitOvertimeRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var emp = await MeAsync(actor, ct); var errors = new Dictionary<string, string>();
        if (r.Date is null) errors["date"] = "Choose the day."; else if (r.Date < Today.AddDays(-3)) errors["date"] = "Overtime must be requested beforehand or within three days after."; else if (r.Date > Today.AddDays(30)) errors["date"] = "That is too far ahead."; else if (r.Date < emp.HireDate) errors["date"] = "That is before your hire date.";
        if (r.Hours is null || r.Hours <= 0 || r.Hours > MaxHoursPerDay || r.Hours % 0.5m != 0) errors["hours"] = $"Enter hours in steps of 0.5, up to {MaxHoursPerDay} a day.";
        var reason = r.Reason?.Trim(); if (string.IsNullOrEmpty(reason) || reason.Length > 300) errors["reason"] = "Explain the work (up to 300 characters).";
        if (errors.Count > 0) throw DomainException.Validation("Please check the overtime request.", errors);
        var date = r.Date!.Value; var hours = r.Hours!.Value;
        if (await ClosedAsync(date, ct)) throw DomainException.Invariant("That month is closed for payroll. Ask HR to reopen it.");
        return await db.InTransactionAsync(async () =>
        {
            await db.ExecuteSqlAsync($"SELECT id FROM hr_employees WHERE id = {emp.Id} FOR UPDATE", ct);
            var live = new[] { RequestStatus.Pending, RequestStatus.Approved };
            if (await db.OvertimeRequests.AnyAsync(o => o.EmployeeId == emp.Id && o.Date == date && live.Contains(o.Status), ct)) throw DomainException.Conflict("You already have overtime requested for that day.");
            var monday = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
            var week = await db.OvertimeRequests.Where(o => o.EmployeeId == emp.Id && o.Date >= monday && o.Date <= monday.AddDays(6) && live.Contains(o.Status)).SumAsync(o => (decimal?)o.Hours, ct) ?? 0;
            if (week + hours > MaxHoursPerWeek) throw DomainException.Conflict($"That would be {week + hours:0.##} overtime hours in the week; the limit is {MaxHoursPerWeek:0.##}.");
            var o = new OvertimeRequest { EmployeeId = emp.Id, Date = date, Hours = hours, Reason = reason!, CreatedAt = clock.UtcNow };
            db.OvertimeRequests.Add(o);
            audit.Record("hr.overtime.request", "employee", emp.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["date"] = date.ToString("yyyy-MM-dd"), ["hours"] = hours.ToString() });
            await db.SaveChangesAsync(ct);
            return (await ViewsAsync([], [o], actor, ct))[0];
        }, ct);
    }

    // ============================================================ lists
    public async Task<PagedResult<TimeRequestView>> ListAsync(string kind, TimeQuery q, SessionPrincipal actor, CancellationToken ct = default)
    {
        var scope = (q.Scope ?? "mine").ToLowerInvariant(); var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        var meId = await db.Employees.AsNoTracking().Where(e => e.UserId == actor.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        var acting = await managers.ActingIdsAsync(actor.UserId, ct);
        IQueryable<Guid> reports = db.Employments.Where(m => m.EffectiveTo == null && m.ManagerId != null && acting.Contains(m.ManagerId.Value)).Select(m => m.EmployeeId);
        switch (scope)
        {
            case "mine": if (meId is null) throw DomainException.Invariant("Your account is not linked to an active employee record. Ask HR to link it."); break;
            case "team": if (!perms.Contains(Permissions.AttendanceApprove) && !perms.Contains(Permissions.AttendanceManage)) throw DomainException.Denied(Permissions.AttendanceApprove); break;
            case "all": if (!perms.Contains(Permissions.AttendanceReadAll) && !perms.Contains(Permissions.AttendanceManage)) throw DomainException.Denied(Permissions.AttendanceReadAll); break;
            default: throw DomainException.Validation("Unknown scope.", new Dictionary<string, string> { ["scope"] = "Use mine, team or all." });
        }
        RequestStatus? st = null;
        if (!string.IsNullOrWhiteSpace(q.Status) && q.Status != "all") { if (!Enum.TryParse<RequestStatus>(q.Status, true, out var s)) throw DomainException.Validation("Unknown status.", new Dictionary<string, string> { ["status"] = "Use pending, approved, rejected or cancelled." }); st = s; }
        var page = Math.Max(1, q.Page); var size = Math.Clamp(q.PageSize, 1, 100);
        if (kind == "corrections")
        {
            var query = db.AttendanceCorrections.AsNoTracking().AsQueryable();
            query = scope == "mine" ? query.Where(x => x.EmployeeId == meId) : scope == "team" ? query.Where(x => reports.Contains(x.EmployeeId) && x.EmployeeId != meId) : query;
            if (st is { } s1) query = query.Where(x => x.Status == s1);
            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(x => x.Status == RequestStatus.Pending).ThenByDescending(x => x.Date).Skip((page - 1) * size).Take(size).ToListAsync(ct);
            return new PagedResult<TimeRequestView>(await ViewsAsync(rows, [], actor, ct), page, size, total);
        }
        else
        {
            var query = db.OvertimeRequests.AsNoTracking().AsQueryable();
            query = scope == "mine" ? query.Where(x => x.EmployeeId == meId) : scope == "team" ? query.Where(x => reports.Contains(x.EmployeeId) && x.EmployeeId != meId) : query;
            if (st is { } s2) query = query.Where(x => x.Status == s2);
            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(x => x.Status == RequestStatus.Pending).ThenByDescending(x => x.Date).Skip((page - 1) * size).Take(size).ToListAsync(ct);
            return new PagedResult<TimeRequestView>(await ViewsAsync([], rows, actor, ct), page, size, total);
        }
    }

    private async Task<IReadOnlyList<TimeRequestView>> ViewsAsync(IReadOnlyList<AttendanceCorrection> corr, IReadOnlyList<OvertimeRequest> ot, SessionPrincipal actor, CancellationToken ct)
    {
        var ids = corr.Select(c => c.EmployeeId).Concat(ot.Select(o => o.EmployeeId)).Distinct().ToList();
        var emps = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.EmployeeNo, e.FullName, e.UserId }).ToDictionaryAsync(e => e.Id, ct);
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        var meId = await db.Employees.AsNoTracking().Where(e => e.UserId == actor.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        var acting = await managers.ActingIdsAsync(actor.UserId, ct);
        var managed = (await db.Employments.AsNoTracking().Where(m => m.EffectiveTo == null && m.ManagerId != null && acting.Contains(m.ManagerId.Value) && ids.Contains(m.EmployeeId)).Select(m => m.EmployeeId).ToListAsync(ct)).ToHashSet();
        bool CanDecide(Guid emp, RequestStatus s) => s == RequestStatus.Pending && emps[emp].UserId != actor.UserId && (perms.Contains(Permissions.AttendanceManage) || perms.Contains(Permissions.AttendanceApprove) && managed.Contains(emp));
        bool CanCancel(Guid emp, RequestStatus s) => s == RequestStatus.Pending && emps[emp].UserId == actor.UserId;
        var list = new List<TimeRequestView>();
        list.AddRange(corr.Select(c => new TimeRequestView(c.Id, "correction", c.EmployeeId, emps[c.EmployeeId].EmployeeNo, emps[c.EmployeeId].FullName, c.Date, Hm(c.ClockIn), Hm(c.ClockOut), null, c.Reason,
            c.Status.ToString().ToLowerInvariant(), c.DecisionNote, c.CreatedAt, CanDecide(c.EmployeeId, c.Status), CanCancel(c.EmployeeId, c.Status))));
        list.AddRange(ot.Select(o => new TimeRequestView(o.Id, "overtime", o.EmployeeId, emps[o.EmployeeId].EmployeeNo, emps[o.EmployeeId].FullName, o.Date, null, null, o.Hours, o.Reason,
            o.Status.ToString().ToLowerInvariant(), o.DecisionNote, o.CreatedAt, CanDecide(o.EmployeeId, o.Status), CanCancel(o.EmployeeId, o.Status))));
        return list;
    }

    // ============================================================ decisions
    public async Task<TimeRequestView> DecideAsync(string kind, Guid id, bool approve, string? note, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var reason = note?.Trim(); if (reason is { Length: > 300 }) throw DomainException.Validation("Note is too long.", new Dictionary<string, string> { ["note"] = "Keep it under 300 characters." });
        if (!approve && string.IsNullOrEmpty(reason)) throw DomainException.Validation("Please give a reason.", new Dictionary<string, string> { ["note"] = "Tell the employee why." });
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        Guid empId = kind == "corrections"
            ? (await db.AttendanceCorrections.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.EmployeeId).FirstOrDefaultAsync(ct)) ?? throw DomainException.NotFound("Request")
            : (await db.OvertimeRequests.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.EmployeeId).FirstOrDefaultAsync(ct)) ?? throw DomainException.NotFound("Request");
        var emp = await db.Employees.FirstAsync(e => e.Id == empId, ct);
        if (emp.UserId == actor.UserId) throw DomainException.Invariant("You cannot decide your own request.");
        if (!perms.Contains(Permissions.AttendanceManage) && !await IsManagerOfAsync(actor, empId, ct)) throw DomainException.DeniedBecause("Only the employee's manager or HR can decide this request.");

        return await db.InTransactionAsync(async () =>
        {
            await db.ExecuteSqlAsync($"SELECT id FROM hr_employees WHERE id = {empId} FOR UPDATE", ct);
            var now = clock.UtcNow;
            if (kind == "corrections")
            {
                var c = await db.AttendanceCorrections.FirstAsync(x => x.Id == id, ct);
                if (c.Status != RequestStatus.Pending) throw DomainException.Conflict($"This request is already {c.Status.ToString().ToLowerInvariant()}.");
                if (approve)
                {
                    if (await ClosedAsync(c.Date, ct)) throw DomainException.Invariant("That month is closed for payroll. Reopen it first.");
                    var logs = await db.AttendanceLogs.Where(l => l.EmployeeId == empId && l.Date == c.Date).ToListAsync(ct);
                    if (logs.Any(l => l.ClockOut != null)) throw DomainException.Conflict("Attendance was recorded for that day after the request was made.");
                    var cin = new DateTimeOffset(c.Date.ToDateTime(c.ClockIn), Wib).ToUniversalTime(); var cout = new DateTimeOffset(c.Date.ToDateTime(c.ClockOut), Wib).ToUniversalTime();
                    if (logs.FirstOrDefault() is { } open) { open.ClockIn = cin; open.ClockOut = cout; open.Source = "correction"; }
                    else db.AttendanceLogs.Add(new AttendanceLog { EmployeeId = empId, Date = c.Date, ClockIn = cin, ClockOut = cout, Source = "correction" });
                }
                c.Status = approve ? RequestStatus.Approved : RequestStatus.Rejected; c.DecidedBy = actor.UserId; c.DecidedAt = now; c.DecisionNote = reason; c.Version++;
                audit.Record(approve ? "hr.attendance.correction_approve" : "hr.attendance.correction_reject", "employee", empId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["date"] = c.Date.ToString("yyyy-MM-dd") });
                await db.SaveChangesAsync(ct);
                return (await ViewsAsync([c], [], actor, ct))[0];
            }
            var o = await db.OvertimeRequests.FirstAsync(x => x.Id == id, ct);
            if (o.Status != RequestStatus.Pending) throw DomainException.Conflict($"This request is already {o.Status.ToString().ToLowerInvariant()}.");
            if (approve && await ClosedAsync(o.Date, ct)) throw DomainException.Invariant("That month is closed for payroll. Reopen it first.");
            o.Status = approve ? RequestStatus.Approved : RequestStatus.Rejected; o.DecidedBy = actor.UserId; o.DecidedAt = now; o.DecisionNote = reason; o.Version++;
            audit.Record(approve ? "hr.overtime.approve" : "hr.overtime.reject", "employee", empId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["date"] = o.Date.ToString("yyyy-MM-dd"), ["hours"] = o.Hours.ToString() });
            await db.SaveChangesAsync(ct);
            return (await ViewsAsync([], [o], actor, ct))[0];
        }, ct);
    }

    public async Task<TimeRequestView> CancelAsync(string kind, Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct);
        if (kind == "corrections")
        {
            var c = await db.AttendanceCorrections.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == me.Id, ct) ?? throw DomainException.NotFound("Request");
            if (c.Status != RequestStatus.Pending) throw DomainException.Conflict($"This request is already {c.Status.ToString().ToLowerInvariant()}.");
            c.Status = RequestStatus.Cancelled; c.Version++;
            audit.Record("hr.attendance.correction_cancel", "employee", me.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
            await db.SaveChangesAsync(ct); return (await ViewsAsync([c], [], actor, ct))[0];
        }
        var o = await db.OvertimeRequests.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == me.Id, ct) ?? throw DomainException.NotFound("Request");
        if (o.Status != RequestStatus.Pending) throw DomainException.Conflict($"This request is already {o.Status.ToString().ToLowerInvariant()}.");
        o.Status = RequestStatus.Cancelled; o.Version++;
        audit.Record("hr.overtime.cancel", "employee", me.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct); return (await ViewsAsync([], [o], actor, ct))[0];
    }

    // ============================================================ month close
    public async Task<IReadOnlyList<PeriodView>> PeriodsAsync(CancellationToken ct = default)
        => [.. (await db.AttendancePeriods.AsNoTracking().OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).ToListAsync(ct)).Select(p => new PeriodView(p.Year, p.Month, p.ClosedAt))];

    private static (int Y, int M) ParseMonth(string month)
        => DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out var d) ? (d.Year, d.Month) : throw DomainException.Validation("Unknown month.", new Dictionary<string, string> { ["month"] = "Use the form 2026-11." });

    public async Task<PeriodView> CloseAsync(string month, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);
        var (y, m) = ParseMonth(month); var first = new DateOnly(y, m, 1); var last = first.AddMonths(1).AddDays(-1);
        if (last >= Today) throw DomainException.Invariant("A month can be closed only after it has ended.");
        if (await db.AttendancePeriods.AnyAsync(p => p.Year == y && p.Month == m, ct)) throw DomainException.Conflict("That month is already closed.");
        var open = await db.AttendanceLogs.CountAsync(l => l.Date >= first && l.Date <= last && l.ClockOut == null, ct);
        var pending = await db.AttendanceCorrections.CountAsync(c => c.Date >= first && c.Date <= last && c.Status == RequestStatus.Pending, ct) + await db.OvertimeRequests.CountAsync(o => o.Date >= first && o.Date <= last && o.Status == RequestStatus.Pending, ct);
        if (open > 0 || pending > 0) throw DomainException.Invariant($"Resolve first: {open} open clock session(s) and {pending} pending correction/overtime request(s) in that month.");
        var p = new AttendancePeriod { Year = y, Month = m, ClosedAt = clock.UtcNow, ClosedBy = actor.UserId };
        db.AttendancePeriods.Add(p);
        audit.Record("hr.attendance.period_close", "attendance", month, Outcome.Success, client, actor.UserId, actor.Email);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ResetTracking(); throw DomainException.Conflict("That month is already closed."); }
        return new PeriodView(y, m, p.ClosedAt);
    }

    public async Task ReopenAsync(string month, string? reason, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);
        var (y, m) = ParseMonth(month);
        if (string.IsNullOrWhiteSpace(reason)) throw DomainException.Validation("Please give a reason.", new Dictionary<string, string> { ["reason"] = "Say why the month is reopened." });
        var p = await db.AttendancePeriods.FirstOrDefaultAsync(x => x.Year == y && x.Month == m, ct) ?? throw DomainException.NotFound("Closed month");
        if (await db.PayRuns.AnyAsync(r => r.Year == y && r.Month == m && (r.Status == PayRunStatus.Locked || r.Status == PayRunStatus.Paid), ct)) throw DomainException.Invariant("Payroll for that month is already locked.");
        db.AttendancePeriods.Remove(p);
        audit.Record("hr.attendance.period_reopen", "attendance", month, Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["reason"] = reason.Trim() });
        await db.SaveChangesAsync(ct);
    }
}
