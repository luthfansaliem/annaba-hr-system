using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Hr;

public sealed record ShiftView(Guid Id, string Code, string Name, string StartTime, string EndTime, int GraceMinutes, int WorkDays, bool Archived, int Assigned, bool Overnight = false);
public sealed record ShiftRequest(string? Code, string? Name, string? StartTime, string? EndTime, int? GraceMinutes, int? WorkDays, bool? Archived);
public sealed record AssignShiftRequest(Guid? ShiftId, DateOnly? EffectiveFrom);
public sealed record ShiftAssignmentRow(Guid EmployeeId, string EmployeeNo, string EmployeeName, Guid? ShiftId, string? ShiftName, DateOnly? Since);

/// <summary>What shift applies to whom, resolved once for a set of employees and a date range so that reports do not query per row.</summary>
/// A planned roster day (<see cref="RosterEntry"/>) overrides the standing shift: a planned shift makes it a working day on that shift, a planned day off makes it a rest day.
public sealed class ShiftLookup(IReadOnlyList<(Guid Emp, DateOnly From, DateOnly? To, Shift Shift)> rows, IReadOnlyDictionary<(Guid Emp, DateOnly Date), Shift?>? roster = null)
{
    public static readonly Shift Default = new() { Code = "DEFAULT", Name = "Standard", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0), GraceMinutes = 10, WorkDays = 31 };
    private Shift Standing(Guid emp, DateOnly date) => rows.Where(r => r.Emp == emp && r.From <= date && (r.To is null || r.To >= date)).OrderByDescending(r => r.From).Select(r => r.Shift).FirstOrDefault() ?? Default;
    public Shift For(Guid emp, DateOnly date) => roster is not null && roster.TryGetValue((emp, date), out var planned) && planned is not null ? planned : Standing(emp, date);
    public bool Works(Guid emp, DateOnly date) => roster is not null && roster.TryGetValue((emp, date), out var planned) ? planned is not null : ShiftService.WorksOn(Standing(emp, date).WorkDays, date.DayOfWeek);
    public TimeOnly LateAfter(Guid emp, DateOnly date) { var s = For(emp, date); return s.StartTime.AddMinutes(s.GraceMinutes); }
}

/// <summary>ATT-001. Shifts decide when someone counts as late and which days are working days (leave counting, unpaid-leave deductions and the overtime rest-day rule all follow them).</summary>
public sealed class ShiftService(IAppDb db, IClock clock, AuditWriter audit)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    public static bool WorksOn(int mask, DayOfWeek d) => (mask & (1 << (((int)d + 6) % 7))) != 0;

    public async Task<ShiftLookup> LookupAsync(IEnumerable<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var ids = employeeIds.Distinct().ToList();
        var rows = await db.ShiftAssignments.AsNoTracking().Where(a => ids.Contains(a.EmployeeId) && a.EffectiveFrom <= to && (a.EffectiveTo == null || a.EffectiveTo >= from))
            .Join(db.Shifts.AsNoTracking(), a => a.ShiftId, s => s.Id, (a, s) => new { a.EmployeeId, a.EffectiveFrom, a.EffectiveTo, Shift = s }).ToListAsync(ct);
        var planned = await db.RosterEntries.AsNoTracking().Where(x => ids.Contains(x.EmployeeId) && x.Date >= from && x.Date <= to).ToListAsync(ct);
        var shiftIds = planned.Where(p => p.ShiftId != null).Select(p => p.ShiftId!.Value).Distinct().ToList();
        var shiftsById = shiftIds.Count == 0 ? [] : await db.Shifts.AsNoTracking().Where(x => shiftIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        return new ShiftLookup([.. rows.Select(r => (r.EmployeeId, r.EffectiveFrom, r.EffectiveTo, r.Shift))], planned.ToDictionary(p => (p.EmployeeId, p.Date), p => p.ShiftId is { } sid ? shiftsById.GetValueOrDefault(sid) : null));
    }

    private static string Hm(TimeOnly t) => t.ToString("HH:mm");

    public async Task<IReadOnlyList<ShiftView>> ListAsync(bool includeArchived, CancellationToken ct = default)
    {
        var counts = await db.ShiftAssignments.AsNoTracking().Where(a => a.EffectiveTo == null).GroupBy(a => a.ShiftId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return [.. (await db.Shifts.AsNoTracking().Where(s => includeArchived || s.ArchivedAt == null).OrderBy(s => s.Code).ToListAsync(ct)).Select(s => new ShiftView(s.Id, s.Code, s.Name, Hm(s.StartTime), Hm(s.EndTime), s.GraceMinutes, s.WorkDays, s.ArchivedAt != null, counts.GetValueOrDefault(s.Id), s.EndTime < s.StartTime))];
    }

    public async Task<ShiftView> SaveAsync(Guid? id, ShiftRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var s = id is null ? new Shift { CreatedAt = clock.UtcNow } : await db.Shifts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Shift");
        var errors = new Dictionary<string, string>();
        var code = (r.Code ?? s.Code).Trim().ToUpperInvariant(); var name = (r.Name ?? s.Name).Trim();
        if (code.Length is < 2 or > 30 || !code.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')) errors["code"] = "Use 2–30 letters, digits, - or _.";
        if (name.Length is < 2 or > 100) errors["name"] = "Enter a name of 2–100 characters.";
        var start = s.StartTime; var end = s.EndTime;
        if (r.StartTime is not null && !TimeOnly.TryParseExact(r.StartTime, "HH:mm", out start)) errors["startTime"] = "Use HH:mm.";
        if (r.EndTime is not null && !TimeOnly.TryParseExact(r.EndTime, "HH:mm", out end)) errors["endTime"] = "Use HH:mm.";
        if (id is null && (r.StartTime is null || r.EndTime is null)) errors["startTime"] = "Enter the start and end time.";
        if (errors.Count == 0 && end == start) errors["endTime"] = "The start and end time cannot be the same.";
        var grace = r.GraceMinutes ?? s.GraceMinutes; if (grace is < 0 or > 240) errors["graceMinutes"] = "Enter 0–240 minutes.";
        var days = r.WorkDays ?? s.WorkDays; if (days is < 1 or > 127) errors["workDays"] = "Choose at least one working day.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the shift.", errors);
        if (await db.Shifts.AnyAsync(x => x.Code == code && x.Id != s.Id, ct)) throw DomainException.Conflict($"Shift code '{code}' is already used.");
        if (r.Archived == true && id is not null && await db.ShiftAssignments.AnyAsync(a => a.ShiftId == id && a.EffectiveTo == null, ct)) throw DomainException.Invariant("People are still assigned to this shift. Move them first.");
        s.Code = code; s.Name = name; s.StartTime = start; s.EndTime = end; s.GraceMinutes = grace; s.WorkDays = days;
        if (r.Archived is { } a) s.ArchivedAt = a ? s.ArchivedAt ?? clock.UtcNow : null;
        if (id is null) db.Shifts.Add(s);
        audit.Record(id is null ? "hr.shift.create" : "hr.shift.update", "shift", s.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["code"] = code });
        await db.SaveChangesAsync(ct);
        return (await ListAsync(true, ct)).First(x => x.Id == s.Id);
    }

    public async Task<IReadOnlyList<ShiftAssignmentRow>> AssignmentsAsync(CancellationToken ct = default)
    {
        var emps = await db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active).OrderBy(e => e.FullName).Select(e => new { e.Id, e.EmployeeNo, e.FullName }).ToListAsync(ct);
        var open = await db.ShiftAssignments.AsNoTracking().Where(a => a.EffectiveTo == null).Join(db.Shifts.AsNoTracking(), a => a.ShiftId, s => s.Id, (a, s) => new { a.EmployeeId, s.Id, s.Name, a.EffectiveFrom }).ToDictionaryAsync(x => x.EmployeeId, ct);
        return [.. emps.Select(e => open.TryGetValue(e.Id, out var o) ? new ShiftAssignmentRow(e.Id, e.EmployeeNo, e.FullName, o.Id, o.Name, o.EffectiveFrom) : new ShiftAssignmentRow(e.Id, e.EmployeeNo, e.FullName, null, null, null))];
    }

    /// <summary>Starts a new assignment (the previous one ends the day before). A null shift returns the person to the default.</summary>
    public async Task<ShiftAssignmentRow> AssignAsync(Guid employeeId, AssignShiftRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var emp = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId && e.Status == EmployeeStatus.Active, ct) ?? throw DomainException.NotFound("Employee");
        var from = r.EffectiveFrom ?? Today; Shift? shift = null;
        if (r.ShiftId is { } sid) shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == sid && s.ArchivedAt == null, ct) ?? throw DomainException.Validation("Please check the assignment.", new Dictionary<string, string> { ["shiftId"] = "Choose an existing shift." });
        if (from > Today.AddMonths(3)) throw DomainException.Validation("Please check the assignment.", new Dictionary<string, string> { ["effectiveFrom"] = "That date is too far ahead." });
        await db.InTransactionAsync(async () =>
        {
            var cur = await db.ShiftAssignments.FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.EffectiveTo == null, ct);
            if (cur is not null)
            {
                if (from <= cur.EffectiveFrom) throw DomainException.Validation("Please check the assignment.", new Dictionary<string, string> { ["effectiveFrom"] = $"Must be after the current assignment started ({cur.EffectiveFrom:yyyy-MM-dd})." });
                cur.EffectiveTo = from.AddDays(-1); await db.SaveChangesAsync(ct);
            }
            if (shift is not null) db.ShiftAssignments.Add(new ShiftAssignment { EmployeeId = employeeId, ShiftId = shift.Id, EffectiveFrom = from });
            audit.Record("hr.shift.assign", "employee", employeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["shift"] = shift?.Code ?? "DEFAULT", ["from"] = from.ToString("yyyy-MM-dd") });
            await db.SaveChangesAsync(ct); return 0;
        }, ct);
        return (await AssignmentsAsync(ct)).First(x => x.EmployeeId == employeeId);
    }
}
