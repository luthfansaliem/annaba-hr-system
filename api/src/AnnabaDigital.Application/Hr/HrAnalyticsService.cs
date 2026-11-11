using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Hr;

public sealed record AmountBy(string Key, decimal Amount, int Count);
public sealed record SeriesPoint(string Label, decimal Value);
public sealed record AttendanceDay(string Date, int Scheduled, int Present, int Late);
public sealed record AttendanceAnalytics(int ScheduledToday, int PresentToday, int LateToday, int OnLeaveToday, int NotInYetToday, decimal OnTimePct, decimal AvgHours, int OutsideZone, int OpenSessions,
    int PendingCorrections, int PendingOvertime, IReadOnlyList<AttendanceDay> Days, IReadOnlyList<CountBy> ByMode, IReadOnlyList<SeriesPoint> OvertimeHours, IReadOnlyList<CountBy> LateByDepartment);
public sealed record LeaveAnalytics(int OnLeaveToday, int Pending, decimal DaysThisYear, decimal ApprovalRatePct, decimal AvgDecisionHours, int UpcomingNext30,
    IReadOnlyList<SeriesPoint> DaysByMonth, IReadOnlyList<AmountBy> ByType, IReadOnlyList<AmountBy> ByDepartment, IReadOnlyList<CountBy> ByStatus);
public sealed record PayrollMonth(string Period, decimal Gross, decimal Net, decimal Tax, decimal EmployerCost, int Employees);
public sealed record PayrollAnalytics(string? LatestPeriod, string? LatestStatus, decimal LatestGross, decimal LatestNet, decimal LatestTax, decimal LatestEmployerCost, int LatestEmployees, decimal? ChangePct,
    int RunsInProgress, decimal LoansOutstanding, int ActiveLoans, IReadOnlyList<PayrollMonth> Months, IReadOnlyList<AmountBy> CostByDepartment, IReadOnlyList<AmountBy> Composition);
public sealed record ClaimsAnalytics(int Pending, decimal PendingAmount, decimal ApprovedUnpaidAmount, decimal PaidThisYear, decimal AvgDecisionHours, decimal ApprovalRatePct,
    IReadOnlyList<SeriesPoint> AmountByMonth, IReadOnlyList<AmountBy> ByCategory, IReadOnlyList<AmountBy> ByStatus, IReadOnlyList<AmountBy> ByDepartment);

/// <summary>
/// RPT-002. The figures behind each module's analytics screen: attendance, leave, payroll and expense claims (people analytics is <see cref="HrDashboardService"/>).
/// Totals and counts only, never a person's own figures; every method asks for the same permission as the module's organisation-wide list,
/// and payroll amounts additionally need the recent password check that the pay-run list itself needs.
/// </summary>
public sealed class HrAnalyticsService(IAppDb db, IClock clock, ShiftService shifts, PayrollService payroll)
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(7); // WIB
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime + Offset);
    private static string Ym(DateOnly d) => d.ToString("yyyy-MM");
    private static decimal Pct(decimal part, decimal whole) => whole > 0 ? Math.Round(part / whole * 100m, 1) : 0m;

    /// <summary>Department name of each employee's current job ("—" when none).</summary>
    private async Task<Dictionary<Guid, string>> DepartmentsAsync(CancellationToken ct)
    {
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var jobs = await db.Employments.AsNoTracking().Where(m => m.EffectiveTo == null).Select(m => new { m.EmployeeId, m.OrgUnitId }).ToListAsync(ct);
        return jobs.GroupBy(j => j.EmployeeId).ToDictionary(g => g.Key, g => g.First().OrgUnitId is { } u && units.TryGetValue(u, out var n) ? n : "—");
    }
    private static List<DateOnly> LastMonths(DateOnly today, int n) => [.. Enumerable.Range(0, n).Select(i => new DateOnly(today.Year, today.Month, 1).AddMonths(i - n + 1))];

    public async Task<AttendanceAnalytics> AttendanceAsync(CancellationToken ct = default)
    {
        var today = Today; var from = today.AddDays(-29);
        var people = await db.Employees.AsNoTracking().Where(e => e.HireDate <= today && (e.TerminationDate == null || e.TerminationDate >= from)).Select(e => new { e.Id, e.HireDate, e.TerminationDate }).ToListAsync(ct);
        var ids = people.Select(p => p.Id).ToList();
        var logs = await db.AttendanceLogs.AsNoTracking().Where(l => l.Date >= from && l.Date <= today).Select(l => new { l.Id, l.EmployeeId, l.Date, l.ClockIn, l.ClockOut, l.WorkMode, l.OutsideZone }).ToListAsync(ct);
        var lookup = await shifts.LookupAsync(ids, from, today, ct);
        var holidays = (await db.Holidays.AsNoTracking().Where(h => h.Date >= from && h.Date <= today).Select(h => h.Date).ToListAsync(ct)).ToHashSet();
        var dept = await DepartmentsAsync(ct);

        // the first clock-in of a person's day decides whether that day was late
        var firsts = logs.GroupBy(l => (l.EmployeeId, l.Date)).Select(g => g.OrderBy(x => x.ClockIn).First())
            .Select(l => new { l.EmployeeId, l.Date, Late = TimeOnly.FromDateTime(l.ClockIn.UtcDateTime + Offset) > lookup.LateAfter(l.EmployeeId, l.Date) }).ToList();
        int Scheduled(DateOnly d) => holidays.Contains(d) ? 0 : people.Count(p => p.HireDate <= d && (p.TerminationDate == null || p.TerminationDate > d) && lookup.Works(p.Id, d));
        var days = Enumerable.Range(0, 30).Select(i => from.AddDays(i)).Select(d => new AttendanceDay(d.ToString("yyyy-MM-dd"), Scheduled(d), firsts.Count(f => f.Date == d), firsts.Count(f => f.Date == d && f.Late))).ToList();

        var onLeave = await db.LeaveRequests.AsNoTracking().Where(l => l.Status == LeaveStatus.Approved && l.StartDate <= today && l.EndDate >= today).Select(l => l.EmployeeId).Distinct().ToListAsync(ct);
        var presentToday = firsts.Where(f => f.Date == today).Select(f => f.EmployeeId).ToHashSet();
        var scheduledToday = holidays.Contains(today) ? [] : people.Where(p => (p.TerminationDate == null || p.TerminationDate > today) && lookup.Works(p.Id, today)).Select(p => p.Id).ToList();
        var closed = logs.Where(l => l.ClockOut != null).ToList();
        var months = LastMonths(today, 6);
        var overtime = await db.OvertimeRequests.AsNoTracking().Where(o => o.Status == RequestStatus.Approved && o.Date >= months[0]).Select(o => new { o.Date, o.Hours }).ToListAsync(ct);

        return new AttendanceAnalytics(
            scheduledToday.Count, presentToday.Count, firsts.Count(f => f.Date == today && f.Late), onLeave.Count(scheduledToday.Contains),
            scheduledToday.Count(id => !presentToday.Contains(id) && !onLeave.Contains(id)),
            Pct(firsts.Count(f => !f.Late), firsts.Count), closed.Count > 0 ? Math.Round(closed.Average(l => (decimal)(l.ClockOut!.Value - l.ClockIn).TotalHours), 1) : 0m,
            logs.Count(l => l.OutsideZone), logs.Count(l => l.ClockOut == null),
            await db.AttendanceCorrections.AsNoTracking().CountAsync(c => c.Status == RequestStatus.Pending, ct), await db.OvertimeRequests.AsNoTracking().CountAsync(o => o.Status == RequestStatus.Pending, ct),
            days,
            [.. logs.GroupBy(l => (l.EmployeeId, l.Date)).Select(g => g.OrderBy(x => x.ClockIn).First().WorkMode.ToString().ToLowerInvariant()).GroupBy(m => m).Select(g => new CountBy(g.Key, g.Count())).OrderByDescending(c => c.Count)],
            [.. months.Select(m => new SeriesPoint(Ym(m), overtime.Where(o => o.Date.Year == m.Year && o.Date.Month == m.Month).Sum(o => o.Hours)))],
            [.. firsts.Where(f => f.Late).GroupBy(f => dept.GetValueOrDefault(f.EmployeeId, "—")).Select(g => new CountBy(g.Key, g.Count())).OrderByDescending(c => c.Count).ThenBy(c => c.Key).Take(8)]);
    }

    public async Task<LeaveAnalytics> LeaveAsync(CancellationToken ct = default)
    {
        var today = Today; var months = LastMonths(today, 12); var yearStart = new DateOnly(today.Year, 1, 1);
        var since = months[0] < yearStart ? months[0] : yearStart;
        var rows = await db.LeaveRequests.AsNoTracking().Where(l => l.EndDate >= since || l.Status == LeaveStatus.Pending)
            .Select(l => new { l.EmployeeId, l.LeaveTypeId, l.StartDate, l.EndDate, l.Days, l.Status, l.CreatedAt, l.DecidedAt }).ToListAsync(ct);
        var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var dept = await DepartmentsAsync(ct);
        var approved = rows.Where(r => r.Status == LeaveStatus.Approved).ToList();
        var thisYear = approved.Where(r => r.StartDate.Year == today.Year).ToList();
        var decided = rows.Where(r => r.DecidedAt != null && r.Status is LeaveStatus.Approved or LeaveStatus.Rejected && r.CreatedAt >= clock.UtcNow.AddMonths(-12)).ToList();
        var requestsThisYear = rows.Where(r => r.StartDate.Year == today.Year).ToList();
        static IReadOnlyList<AmountBy> Sum<T>(IEnumerable<T> src, Func<T, string> key, Func<T, decimal> days) => [.. src.GroupBy(key).Select(g => new AmountBy(g.Key, g.Sum(days), g.Count())).OrderByDescending(a => a.Amount).ThenBy(a => a.Key)];
        return new LeaveAnalytics(
            approved.Where(r => r.StartDate <= today && r.EndDate >= today).Select(r => r.EmployeeId).Distinct().Count(),
            rows.Count(r => r.Status == LeaveStatus.Pending), thisYear.Sum(r => r.Days),
            Pct(decided.Count(r => r.Status == LeaveStatus.Approved), decided.Count),
            decided.Count > 0 ? Math.Round((decimal)decided.Average(r => (r.DecidedAt!.Value - r.CreatedAt).TotalHours), 1) : 0m,
            approved.Where(r => r.StartDate > today && r.StartDate <= today.AddDays(30)).Select(r => r.EmployeeId).Distinct().Count(),
            // a request counts in the month it starts in
            [.. months.Select(m => new SeriesPoint(Ym(m), approved.Where(r => r.StartDate.Year == m.Year && r.StartDate.Month == m.Month).Sum(r => r.Days)))],
            Sum(thisYear, r => types.GetValueOrDefault(r.LeaveTypeId, "—"), r => r.Days), Sum(thisYear, r => dept.GetValueOrDefault(r.EmployeeId, "—"), r => r.Days).Take(8).ToList(),
            [.. requestsThisYear.GroupBy(r => r.Status.ToString().ToLowerInvariant()).Select(g => new CountBy(g.Key, g.Count())).OrderByDescending(c => c.Count)]);
    }

    public async Task<PayrollAnalytics> PayrollAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var today = Today; var months = LastMonths(today, 12);
        var runs = await db.PayRuns.AsNoTracking().Select(r => new { r.Id, r.Year, r.Month, r.Status, r.EmployeeCount, r.TotalGross, r.TotalNet, r.TotalTax, r.TotalEmployerCost }).ToListAsync(ct);
        // a month is "paid for" once its run is locked; draft figures still move, so they stay out of the trend
        var final = runs.Where(r => r.Status is PayRunStatus.Locked or PayRunStatus.Paid).ToList();
        var series = months.Select(m =>
        {
            var rs = final.Where(r => r.Year == m.Year && r.Month == m.Month).ToList();
            return new PayrollMonth(Ym(m), rs.Sum(r => r.TotalGross), rs.Sum(r => r.TotalNet), rs.Sum(r => r.TotalTax), rs.Sum(r => r.TotalEmployerCost), rs.Sum(r => r.EmployeeCount));
        }).ToList();
        var latest = final.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).FirstOrDefault();
        var latestRuns = latest is null ? [] : final.Where(r => r.Year == latest.Year && r.Month == latest.Month).ToList();
        var prev = latest is null ? [] : final.Where(r => new DateOnly(r.Year, r.Month, 1) == new DateOnly(latest.Year, latest.Month, 1).AddMonths(-1)).ToList();
        var latestIds = latestRuns.Select(r => r.Id).ToList();
        var items = await db.PayRunItems.AsNoTracking().Where(i => latestIds.Contains(i.PayRunId))
            .Select(i => new { i.EmployeeId, i.Basic, i.Allowance, i.OvertimePay, i.TaxableExtras, i.NonTaxableExtras, i.Gross, i.Tax, i.Net, i.EmployerCost, Bpjs = i.KesEmployer + i.JhtEmployer + i.JpEmployer + i.JkkEmployer + i.JkmEmployer }).ToListAsync(ct);
        var dept = await DepartmentsAsync(ct);
        var loans = await db.Loans.AsNoTracking().Where(l => l.Status == LoanStatus.Active).Select(l => l.Remaining).ToListAsync(ct);
        decimal gross = latestRuns.Sum(r => r.TotalGross), prevGross = prev.Sum(r => r.TotalGross);
        return new PayrollAnalytics(
            latest is null ? null : $"{latest.Year:0000}-{latest.Month:00}", latest?.Status.ToString().ToLowerInvariant(), gross, latestRuns.Sum(r => r.TotalNet), latestRuns.Sum(r => r.TotalTax), latestRuns.Sum(r => r.TotalEmployerCost),
            latestRuns.Sum(r => r.EmployeeCount), prevGross > 0 ? Math.Round((gross - prevGross) / prevGross * 100m, 1) : null,
            runs.Count(r => r.Status is PayRunStatus.Draft or PayRunStatus.Calculated or PayRunStatus.Approved), loans.Sum(), loans.Count, series,
            [.. items.GroupBy(i => dept.GetValueOrDefault(i.EmployeeId, "—")).Select(g => new AmountBy(g.Key, g.Sum(i => i.EmployerCost), g.Count())).OrderByDescending(a => a.Amount).ThenBy(a => a.Key).Take(8)],
            [new AmountBy("basic", items.Sum(i => i.Basic), items.Count), new AmountBy("allowance", items.Sum(i => i.Allowance), items.Count), new AmountBy("overtime", items.Sum(i => i.OvertimePay), items.Count(i => i.OvertimePay > 0)),
             new AmountBy("extras", items.Sum(i => i.TaxableExtras + i.NonTaxableExtras), items.Count(i => i.TaxableExtras + i.NonTaxableExtras > 0)), new AmountBy("employer_bpjs", items.Sum(i => i.Bpjs), items.Count)]);
    }

    public async Task<ClaimsAnalytics> ClaimsAsync(CancellationToken ct = default)
    {
        var today = Today; var months = LastMonths(today, 12); var yearStart = new DateOnly(today.Year, 1, 1);
        var since = months[0] < yearStart ? months[0] : yearStart;
        var rows = await db.ExpenseClaims.AsNoTracking().Where(c => c.ExpenseDate >= since || c.Status == ClaimStatus.Pending || c.Status == ClaimStatus.Approved)
            .Select(c => new { c.EmployeeId, c.Category, c.ExpenseDate, c.Amount, c.Status, c.CreatedAt, c.DecidedAt }).ToListAsync(ct);
        var dept = await DepartmentsAsync(ct);
        var accepted = rows.Where(r => r.Status is ClaimStatus.Approved or ClaimStatus.Paid).ToList();
        var thisYear = accepted.Where(r => r.ExpenseDate.Year == today.Year).ToList();
        var decided = rows.Where(r => r.DecidedAt != null && r.Status is ClaimStatus.Approved or ClaimStatus.Paid or ClaimStatus.Rejected).ToList();
        static IReadOnlyList<AmountBy> Sum<T>(IEnumerable<T> src, Func<T, string> key, Func<T, decimal> amount) => [.. src.GroupBy(key).Select(g => new AmountBy(g.Key, g.Sum(amount), g.Count())).OrderByDescending(a => a.Amount).ThenBy(a => a.Key)];
        return new ClaimsAnalytics(
            rows.Count(r => r.Status == ClaimStatus.Pending), rows.Where(r => r.Status == ClaimStatus.Pending).Sum(r => r.Amount), rows.Where(r => r.Status == ClaimStatus.Approved).Sum(r => r.Amount),
            rows.Where(r => r.Status == ClaimStatus.Paid && r.ExpenseDate.Year == today.Year).Sum(r => r.Amount),
            decided.Count > 0 ? Math.Round((decimal)decided.Average(r => (r.DecidedAt!.Value - r.CreatedAt).TotalHours), 1) : 0m, Pct(decided.Count(r => r.Status != ClaimStatus.Rejected), decided.Count),
            [.. months.Select(m => new SeriesPoint(Ym(m), accepted.Where(r => r.ExpenseDate.Year == m.Year && r.ExpenseDate.Month == m.Month).Sum(r => r.Amount)))],
            Sum(thisYear, r => r.Category, r => r.Amount), Sum(rows.Where(r => r.ExpenseDate.Year == today.Year), r => r.Status.ToString().ToLowerInvariant(), r => r.Amount),
            Sum(thisYear, r => dept.GetValueOrDefault(r.EmployeeId, "—"), r => r.Amount).Take(8).ToList());
    }
}
