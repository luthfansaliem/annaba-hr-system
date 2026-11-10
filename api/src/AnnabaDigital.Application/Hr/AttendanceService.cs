using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Hr;

public sealed record AttendanceLogView(Guid Id, DateOnly Date, DateTimeOffset ClockIn, DateTimeOffset? ClockOut, decimal? Hours, bool Late);
public sealed record MyAttendance(AttendanceLogView? Open, IReadOnlyList<AttendanceLogView> Logs, int DaysPresent, int LateDays, decimal Hours);
public sealed record AttendanceReportRow(Guid EmployeeId, string EmployeeNo, string EmployeeName, int DaysPresent, int LateDays, decimal Hours, int OpenSessions);

/// <summary>
/// ATT-002/006 (web clock). Times come only from the server clock (the browser cannot choose them), a person has at most one open session
/// (database-enforced), and "late" means the first clock-in of a day after 09:10 local time (default policy; becomes a setting with shifts).
/// </summary>
public sealed class AttendanceService(IAppDb db, IClock clock, AuditWriter audit, ShiftService shifts)
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(7); // WIB
    private DateOnly Local(DateTimeOffset t) => DateOnly.FromDateTime(t.UtcDateTime + Offset);

    private async Task<Employee> MeAsync(SessionPrincipal actor, CancellationToken ct)
        => await db.Employees.FirstOrDefaultAsync(e => e.UserId == actor.UserId && e.Status == EmployeeStatus.Active, ct)
           ?? throw DomainException.Invariant("Your account is not linked to an active employee record. Ask HR to link it.");

    private static bool IsLate(AttendanceLog l, bool firstOfDay, ShiftLookup shifts) => firstOfDay && TimeOnly.FromDateTime(l.ClockIn.UtcDateTime + Offset) > shifts.LateAfter(l.EmployeeId, l.Date);
    private static decimal HoursOf(AttendanceLog l) => l.ClockOut is { } o ? Math.Round((decimal)(o - l.ClockIn).TotalHours, 2) : 0;

    private static List<AttendanceLogView> Views(IEnumerable<AttendanceLog> logs, Func<AttendanceLog, bool, bool> late)
    {
        var firsts = logs.GroupBy(l => l.Date).ToDictionary(g => g.Key, g => g.OrderBy(x => x.ClockIn).First().Id);
        return [.. logs.OrderByDescending(l => l.ClockIn).Select(l => new AttendanceLogView(l.Id, l.Date, l.ClockIn, l.ClockOut, l.ClockOut is null ? null : HoursOf(l), late(l, firsts[l.Date] == l.Id)))];
    }

    public async Task<AttendanceLogView> ClockInAsync(SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var emp = await MeAsync(actor, ct); var now = clock.UtcNow;
        var open = await db.AttendanceLogs.AsNoTracking().FirstOrDefaultAsync(l => l.EmployeeId == emp.Id && l.ClockOut == null, ct);
        if (open is not null) throw DomainException.Conflict($"You are already clocked in since {(open.ClockIn + Offset):HH:mm}.");
        var log = new AttendanceLog { EmployeeId = emp.Id, Date = Local(now), ClockIn = now, Ip = client.Ip };
        db.AttendanceLogs.Add(log);
        audit.Record("hr.attendance.clock_in", "employee", emp.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.ResetTracking(); throw DomainException.Conflict("You are already clocked in."); } // lost a race with a second tab
        var first = !await db.AttendanceLogs.AnyAsync(l => l.EmployeeId == emp.Id && l.Date == log.Date && l.ClockIn < log.ClockIn, ct);
        return new AttendanceLogView(log.Id, log.Date, log.ClockIn, null, null, IsLate(log, first, await shifts.LookupAsync([emp.Id], log.Date, log.Date, ct)));
    }

    public async Task<AttendanceLogView> ClockOutAsync(SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var emp = await MeAsync(actor, ct);
        var log = await db.AttendanceLogs.FirstOrDefaultAsync(l => l.EmployeeId == emp.Id && l.ClockOut == null, ct) ?? throw DomainException.Conflict("You are not clocked in.");
        log.ClockOut = clock.UtcNow;
        audit.Record("hr.attendance.clock_out", "employee", emp.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return new AttendanceLogView(log.Id, log.Date, log.ClockIn, log.ClockOut, HoursOf(log), false);
    }

    public async Task<MyAttendance> MineAsync(string? month, SessionPrincipal actor, CancellationToken ct = default)
    {
        var emp = await MeAsync(actor, ct);
        var (from, to) = Month(month);
        var logs = await db.AttendanceLogs.AsNoTracking().Where(l => l.EmployeeId == emp.Id && l.Date >= from && l.Date <= to).ToListAsync(ct);
        var lookup = await shifts.LookupAsync([emp.Id], from, to, ct);
        var views = Views(logs, (l, first) => IsLate(l, first, lookup));
        var open = await db.AttendanceLogs.AsNoTracking().Where(l => l.EmployeeId == emp.Id && l.ClockOut == null).FirstOrDefaultAsync(ct);
        return new MyAttendance(open is null ? null : new AttendanceLogView(open.Id, open.Date, open.ClockIn, null, null, false), views,
            logs.Select(l => l.Date).Distinct().Count(), views.Count(v => v.Late), views.Sum(v => v.Hours ?? 0));
    }

    public async Task<IReadOnlyList<AttendanceReportRow>> ReportAsync(string? month, Guid? orgUnitId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var (from, to) = Month(month);
        var emps = db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active || e.TerminationDate >= from);
        if (orgUnitId is { } ou) emps = emps.Where(e => e.Employments.Any(m => m.EffectiveTo == null && m.OrgUnitId == ou));
        var people = await emps.OrderBy(e => e.FullName).Select(e => new { e.Id, e.EmployeeNo, e.FullName }).ToListAsync(ct);
        var ids = people.Select(p => p.Id).ToList();
        var logs = (await db.AttendanceLogs.AsNoTracking().Where(l => ids.Contains(l.EmployeeId) && l.Date >= from && l.Date <= to).ToListAsync(ct)).GroupBy(l => l.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var lookup = await shifts.LookupAsync(ids, from, to, ct);
        audit.Record("hr.attendance.report", "attendance", null, Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["from"] = from.ToString("yyyy-MM-dd") });
        await db.SaveChangesAsync(ct);
        return [.. people.Select(p =>
        {
            var l = logs.GetValueOrDefault(p.Id) ?? [];
            var v = Views(l, (x, first) => IsLate(x, first, lookup));
            return new AttendanceReportRow(p.Id, p.EmployeeNo, p.FullName, l.Select(x => x.Date).Distinct().Count(), v.Count(x => x.Late), v.Sum(x => x.Hours ?? 0), l.Count(x => x.ClockOut == null));
        })];
    }

    private (DateOnly From, DateOnly To) Month(string? month)
    {
        DateOnly first;
        if (string.IsNullOrWhiteSpace(month)) first = new DateOnly(Local(clock.UtcNow).Year, Local(clock.UtcNow).Month, 1);
        else if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out first)) throw DomainException.Validation("Unknown month.", new Dictionary<string, string> { ["month"] = "Use the form 2026-11." });
        return (first, first.AddMonths(1).AddDays(-1));
    }
}
