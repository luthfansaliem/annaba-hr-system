using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Hr;

public sealed record CountBy(string Key, int Count);
public sealed record TrendPoint(string Month, int Headcount, int Joiners, int Leavers);
/// <summary>How far the organisation is set up, as counts (the home page turns them into a "get started" list while something is still missing).</summary>
public sealed record HrSetup(int Departments, int HolidaysThisYear, int PayRuns, int LockedPayRuns);
public sealed record HrDashboard(int Headcount, int Joiners30, int Joiners90, int Leavers12, decimal TurnoverPct, int ContractsEnding60, int OnLeaveToday,
    int PendingLeave, int PendingCorrections, int PendingOvertime, int PendingChangeRequests, int NoCompensation, int NoLinkedAccount,
    IReadOnlyList<CountBy> ByDepartment, IReadOnlyList<CountBy> ByType, IReadOnlyList<CountBy> ByGender, IReadOnlyList<CountBy> ByTenure, IReadOnlyList<TrendPoint> Trend, HrSetup Setup);

/// <summary>RPT-001. Headcount and movement computed from hire and leaving dates, plus the queues that need someone's attention. Counts only: no salaries or personal details.</summary>
public sealed class HrDashboardService(IAppDb db, IClock clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    public async Task<HrDashboard> GetAsync(CancellationToken ct = default)
    {
        var today = Today;
        var people = await db.Employees.AsNoTracking().Select(e => new
        {
            e.Id, e.HireDate, e.TerminationDate, Active = e.Status == EmployeeStatus.Active, e.Gender, e.UserId,
            Job = e.Employments.Where(m => m.EffectiveTo == null).Select(m => new { m.OrgUnitId, m.Type, m.ContractEnd }).FirstOrDefault(),
        }).ToListAsync(ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var active = people.Where(p => p.Active).ToList();

        int HeadcountAt(DateOnly d) => people.Count(p => p.HireDate <= d && (p.TerminationDate is null || p.TerminationDate > d));
        var trend = new List<TrendPoint>();
        for (var i = 11; i >= 0; i--)
        {
            var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-i); var last = first.AddMonths(1).AddDays(-1); if (last > today) last = today;
            trend.Add(new TrendPoint(first.ToString("yyyy-MM"), HeadcountAt(last), people.Count(p => p.HireDate >= first && p.HireDate <= last), people.Count(p => p.TerminationDate >= first && p.TerminationDate <= last)));
        }
        var yearAgo = today.AddYears(-1); var leavers = people.Count(p => p.TerminationDate > yearAgo && p.TerminationDate <= today);
        var avg = (HeadcountAt(yearAgo) + HeadcountAt(today)) / 2m;
        var withComp = await db.Compensations.AsNoTracking().Where(c => c.EffectiveTo == null).Select(c => c.EmployeeId).ToListAsync(ct);

        static IReadOnlyList<CountBy> Count(IEnumerable<string> keys) => [.. keys.GroupBy(k => k).Select(g => new CountBy(g.Key, g.Count())).OrderByDescending(c => c.Count).ThenBy(c => c.Key)];
        int Tenure(DateOnly hire) { var y = today.Year - hire.Year; if (today < hire.AddYears(y)) y--; return y; }
        return new HrDashboard(
            active.Count, people.Count(p => p.HireDate > today.AddDays(-30) && p.HireDate <= today), people.Count(p => p.HireDate > today.AddDays(-90) && p.HireDate <= today), leavers,
            avg > 0 ? Math.Round(leavers / avg * 100m, 1) : 0m,
            active.Count(p => p.Job is { } j && j.Type != EmploymentType.Permanent && j.ContractEnd != null && j.ContractEnd <= today.AddDays(60)),
            await db.LeaveRequests.AsNoTracking().CountAsync(l => l.Status == LeaveStatus.Approved && l.StartDate <= today && l.EndDate >= today, ct),
            await db.LeaveRequests.AsNoTracking().CountAsync(l => l.Status == LeaveStatus.Pending, ct), await db.AttendanceCorrections.AsNoTracking().CountAsync(c => c.Status == RequestStatus.Pending, ct),
            await db.OvertimeRequests.AsNoTracking().CountAsync(o => o.Status == RequestStatus.Pending, ct), await db.ChangeRequests.AsNoTracking().CountAsync(c => c.Status == RequestStatus.Pending, ct),
            active.Count(p => !withComp.Contains(p.Id)), active.Count(p => p.UserId == null),
            Count(active.Select(p => p.Job?.OrgUnitId is { } u ? units.GetValueOrDefault(u) ?? "—" : "—")), Count(active.Select(p => p.Job?.Type.ToString().ToLowerInvariant() ?? "—")),
            Count(active.Select(p => p.Gender?.ToString().ToLowerInvariant() ?? "unspecified")),
            Count(active.Select(p => Tenure(p.HireDate) switch { < 1 => "< 1 year", < 3 => "1–3 years", < 5 => "3–5 years", < 10 => "5–10 years", _ => "10+ years" })), trend,
            new HrSetup(await db.OrgUnits.AsNoTracking().CountAsync(o => o.ArchivedAt == null, ct), await db.Holidays.AsNoTracking().CountAsync(h => h.Date.Year == today.Year, ct), await db.PayRuns.AsNoTracking().CountAsync(ct),
                await db.PayRuns.AsNoTracking().CountAsync(r => r.Status == PayRunStatus.Locked || r.Status == PayRunStatus.Paid, ct)));
    }
}
