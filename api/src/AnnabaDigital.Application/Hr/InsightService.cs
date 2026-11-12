using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record RateBy(string Key, int Count, decimal Pct);
public sealed record TurnoverMonth(string Month, int Leavers, int Joiners, int Headcount, decimal RatePct);
public sealed record TurnoverAnalytics(int Leavers12, int Joiners12, decimal AnnualRatePct, decimal EarlyLeaverPct, decimal AvgTenureYears, IReadOnlyList<TurnoverMonth> Months,
    IReadOnlyList<RateBy> ByDepartment, IReadOnlyList<CountBy> ByTenure, IReadOnlyList<CountBy> ByReason);
public sealed record PersonHours(Guid EmployeeId, string Name, string? Department, decimal Hours);
public sealed record OvertimeAnalytics(decimal Hours12, decimal HoursThisMonth, int PeopleThisMonth, decimal AvgPerPerson, int Pending, decimal? PayThisYear, IReadOnlyList<SeriesPoint> HoursByMonth, IReadOnlyList<AmountBy> ByDepartment, IReadOnlyList<PersonHours> Top);
public sealed record PerformanceAnalytics(string? Cycle, int Reviewed, decimal? AvgScore, IReadOnlyList<CountBy> Ratings, IReadOnlyList<AmountBy> AvgByDepartment, IReadOnlyList<CountBy> Boxes,
    int Goals, decimal AvgGoalProgress, int GoalsAtRisk, IReadOnlyList<AmountBy> GoalProgressByDepartment);
public sealed record RiskSignal(string Code, string Text, int Points);
public sealed record AtRiskRow(Guid EmployeeId, string EmployeeNo, string Name, string? JobTitle, string? Department, int Score, string Level, IReadOnlyList<RiskSignal> Signals);
public sealed record ReportColumn(string Key, string Label, string Type);
public sealed record ReportDataset(string Key, string Label, IReadOnlyList<ReportColumn> Columns);
public sealed record ReportFilter(string? Field, string? Op, string? Value);
public sealed record ReportConfig(string? Dataset, IReadOnlyList<string>? Columns, IReadOnlyList<ReportFilter>? Filters, string? GroupBy, string? PivotBy, string? Measure, string? MeasureField, string? SortBy, bool? SortDesc, int? Limit);
public sealed record ReportResult(IReadOnlyList<string> Headers, IReadOnlyList<string> Types, IReadOnlyList<IReadOnlyList<object?>> Rows, int TotalRows, bool Truncated);
public sealed record SavedReportView(Guid Id, string Name, string Dataset, ReportConfig Config, bool Shared, bool Mine, string Owner, DateTimeOffset UpdatedAt);
public sealed record SaveReportRequest(string? Name, ReportConfig? Config, bool? Shared);

/// <summary>
/// Stage 19, advanced analytics (Talenta Insight+, custom dashboards, pivot reports, employee at risk): turnover, overtime and performance dashboards, a rule-based list of people who may
/// be about to leave (every reason shown, no hidden model), and a report builder over whitelisted data sets with filters, grouping and pivot. Each data set needs the same permission as
/// its own list; nothing here reveals pay amounts or protected personal data.
/// </summary>
public sealed class InsightService(IAppDb db, IClock clock, AuthorizationService authz, AuditWriter audit)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));
    private static decimal Pct(decimal part, decimal whole) => whole > 0 ? Math.Round(part / whole * 100m, 1) : 0m;
    private static List<DateOnly> LastMonths(DateOnly today, int n) => [.. Enumerable.Range(0, n).Select(i => new DateOnly(today.Year, today.Month, 1).AddMonths(i - n + 1))];

    /// <summary>For every employee: the department of their current job, or of their last job if they have left.</summary>
    private async Task<Dictionary<Guid, (string Dept, string Job, Guid? Manager, EmploymentType Type, DateOnly? ContractEnd)>> JobsAsync(CancellationToken ct)
    {
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var jobs = await db.Employments.AsNoTracking().Select(m => new { m.EmployeeId, m.EffectiveFrom, m.EffectiveTo, m.OrgUnitId, m.JobTitle, m.ManagerId, m.Type, m.ContractEnd }).ToListAsync(ct);
        return jobs.GroupBy(j => j.EmployeeId).ToDictionary(g => g.Key, g =>
        {
            var j = g.OrderByDescending(x => x.EffectiveTo == null).ThenByDescending(x => x.EffectiveFrom).First();
            return (j.OrgUnitId is { } u && units.TryGetValue(u, out var n) ? n : "—", j.JobTitle, j.ManagerId, j.Type, j.ContractEnd);
        });
    }

    // ============================================================ turnover
    public async Task<TurnoverAnalytics> TurnoverAsync(CancellationToken ct = default)
    {
        var today = Today; var months = LastMonths(today, 12); var from = months[0];
        var emps = await db.Employees.AsNoTracking().Select(e => new { e.Id, e.HireDate, e.TerminationDate, e.TerminationReason, e.Status }).ToListAsync(ct);
        var jobs = await JobsAsync(ct);
        int HeadcountOn(DateOnly d) => emps.Count(e => e.HireDate <= d && (e.TerminationDate is null || e.TerminationDate > d));
        var series = months.Select(m =>
        {
            var end = m.AddMonths(1).AddDays(-1); if (end > today) end = today;
            var leavers = emps.Count(e => e.TerminationDate is { } t && t >= m && t <= end); var joiners = emps.Count(e => e.HireDate >= m && e.HireDate <= end);
            var avg = (HeadcountOn(m.AddDays(-1)) + HeadcountOn(end)) / 2m;
            return new TurnoverMonth(m.ToString("yyyy-MM"), leavers, joiners, HeadcountOn(end), Pct(leavers, avg));
        }).ToList();
        var left = emps.Where(e => e.TerminationDate is { } t && t >= from && t <= today).ToList();
        var avgHeadcount = (HeadcountOn(from.AddDays(-1)) + HeadcountOn(today)) / 2m;
        string Band(double years) => years < 1 ? "< 1 year" : years < 3 ? "1–3 years" : years < 5 ? "3–5 years" : "5+ years";
        double Tenure(DateOnly hire, DateOnly end) => (end.DayNumber - hire.DayNumber) / 365.25;
        var depts = jobs.Values.Select(j => j.Dept).Distinct().ToList();
        var byDept = depts.Select(d =>
        {
            var ids = jobs.Where(j => j.Value.Dept == d).Select(j => j.Key).ToHashSet();
            var l = left.Count(e => ids.Contains(e.Id)); var hc = emps.Count(e => ids.Contains(e.Id) && e.HireDate <= today && (e.TerminationDate is null || e.TerminationDate > from));
            return new RateBy(d, l, Pct(l, hc));
        }).Where(r => r.Count > 0).OrderByDescending(r => r.Pct).ToList();
        var reasons = left.GroupBy(e => string.IsNullOrWhiteSpace(e.TerminationReason) ? "Not recorded" : e.TerminationReason!.Trim().Length > 40 ? e.TerminationReason.Trim()[..40] : e.TerminationReason.Trim())
            .Select(g => new CountBy(g.Key, g.Count())).OrderByDescending(x => x.Count).Take(8).ToList();
        return new TurnoverAnalytics(left.Count, emps.Count(e => e.HireDate >= from && e.HireDate <= today), Pct(left.Count, avgHeadcount),
            Pct(left.Count(e => Tenure(e.HireDate, e.TerminationDate!.Value) < 1), left.Count), left.Count == 0 ? 0 : Math.Round((decimal)left.Average(e => Tenure(e.HireDate, e.TerminationDate!.Value)), 1),
            series, byDept, [.. new[] { "< 1 year", "1–3 years", "3–5 years", "5+ years" }.Select(b => new CountBy(b, left.Count(e => Band(Tenure(e.HireDate, e.TerminationDate!.Value)) == b)))], reasons);
    }

    // ============================================================ overtime
    public async Task<OvertimeAnalytics> OvertimeAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var today = Today; var months = LastMonths(today, 12); var from = months[0]; var monthStart = new DateOnly(today.Year, today.Month, 1);
        var rows = await db.OvertimeRequests.AsNoTracking().Where(o => o.Date >= from && o.Date <= today).Select(o => new { o.EmployeeId, o.Date, o.Hours, o.Status }).ToListAsync(ct);
        var ok = rows.Where(r => r.Status == RequestStatus.Approved).ToList();
        var jobs = await JobsAsync(ct);
        var names = await db.Employees.AsNoTracking().Where(e => ok.Select(r => r.EmployeeId).Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var thisMonth = ok.Where(r => r.Date >= monthStart).ToList();
        var byPerson = ok.GroupBy(r => r.EmployeeId).Select(g => new PersonHours(g.Key, names.GetValueOrDefault(g.Key) ?? "", jobs.TryGetValue(g.Key, out var j) ? j.Dept : null, g.Sum(x => x.Hours))).OrderByDescending(p => p.Hours).ToList();
        // the overtime pay itself is shown only to people who may see payroll amounts
        decimal? pay = null;
        if ((await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(Permissions.PayrollRead))
            pay = await (from i in db.PayRunItems.AsNoTracking() join r in db.PayRuns.AsNoTracking() on i.PayRunId equals r.Id where r.Year == today.Year && r.Status != PayRunStatus.Draft select i.OvertimePay).SumAsync(ct);
        return new OvertimeAnalytics(ok.Sum(r => r.Hours), thisMonth.Sum(r => r.Hours), thisMonth.Select(r => r.EmployeeId).Distinct().Count(),
            thisMonth.Count == 0 ? 0 : Math.Round(thisMonth.Sum(r => r.Hours) / thisMonth.Select(r => r.EmployeeId).Distinct().Count(), 1), await db.OvertimeRequests.CountAsync(o => o.Status == RequestStatus.Pending, ct), pay,
            [.. months.Select(m => new SeriesPoint(m.ToString("yyyy-MM"), ok.Where(r => r.Date.Year == m.Year && r.Date.Month == m.Month).Sum(r => r.Hours)))],
            [.. byPerson.GroupBy(p => p.Department ?? "—").Select(g => new AmountBy(g.Key, g.Sum(x => x.Hours), g.Count())).OrderByDescending(a => a.Amount)], [.. byPerson.Take(10)]);
    }

    // ============================================================ performance
    public async Task<PerformanceAnalytics> PerformanceAsync(CancellationToken ct = default)
    {
        var jobs = await JobsAsync(ct);
        var cycle = await db.ReviewCycles.AsNoTracking().Where(c => c.Status == ReviewCycleStatus.Closed).OrderByDescending(c => c.ClosedAt).FirstOrDefaultAsync(ct)
            ?? await db.ReviewCycles.AsNoTracking().Where(c => c.Status != ReviewCycleStatus.Draft).OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
        var results = cycle is null ? [] : await db.ReviewResults.AsNoTracking().Where(r => r.CycleId == cycle.Id).ToListAsync(ct);
        int? RatingOf(ReviewResult r) => r.CalibratedRating ?? (r.FinalScore is { } f ? (int)Math.Round(f, MidpointRounding.AwayFromZero) : null);
        var rated = results.Where(r => RatingOf(r) is not null).ToList();
        var year = Today.Year;
        var goals = await db.Goals.AsNoTracking().Where(g => g.Year == year && g.Status != GoalStatus.Cancelled && g.OwnerEmployeeId != null).ToListAsync(ct);
        string DeptOf(Guid id) => jobs.TryGetValue(id, out var j) ? j.Dept : "—";
        return new PerformanceAnalytics(cycle?.Name, rated.Count, rated.Count == 0 ? null : Math.Round((decimal)rated.Average(r => r.FinalScore ?? RatingOf(r)!.Value), 2),
            [.. Enumerable.Range(1, 5).Select(n => new CountBy(n.ToString(), rated.Count(r => RatingOf(r) == n)))],
            [.. rated.GroupBy(r => DeptOf(r.EmployeeId)).Select(g => new AmountBy(g.Key, Math.Round(g.Average(r => r.FinalScore ?? RatingOf(r)!.Value), 2), g.Count())).OrderByDescending(a => a.Amount)],
            [.. rated.Where(r => r.Potential is not null).GroupBy(r => PerformanceService.Box(RatingOf(r), r.Potential)!).Select(g => new CountBy(g.Key, g.Count())).OrderByDescending(x => x.Count)],
            goals.Count, goals.Count == 0 ? 0 : Math.Round(goals.Average(PerformanceService.Progress), 1), goals.Count(g => g.Status is GoalStatus.AtRisk or GoalStatus.OffTrack),
            [.. goals.GroupBy(g => DeptOf(g.OwnerEmployeeId!.Value)).Select(g => new AmountBy(g.Key, Math.Round(g.Average(PerformanceService.Progress), 1), g.Count())).OrderByDescending(a => a.Amount)]);
    }

    // ============================================================ employees at risk of leaving
    /// <summary>
    /// A rule-based early warning, not a prediction: each active employee collects points for signals HR commonly watches — a fixed-term contract ending soon, a low or falling review rating,
    /// no pay change for a long time, heavy overtime, many short absences, being new, and colleagues under the same manager leaving. 50+ points is high, 25+ medium. Every view is audited.
    /// </summary>
    public async Task<IReadOnlyList<AtRiskRow>> AtRiskAsync(SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var today = Today; var d90 = today.AddDays(-90); var d180 = today.AddDays(-180);
        var emps = await db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active).Select(e => new { e.Id, e.EmployeeNo, e.FullName, e.HireDate }).ToListAsync(ct);
        var jobs = await JobsAsync(ct);
        var closed = await db.ReviewCycles.AsNoTracking().Where(c => c.Status == ReviewCycleStatus.Closed).OrderByDescending(c => c.ClosedAt).Take(2).Select(c => c.Id).ToListAsync(ct);
        var ratings = closed.Count == 0 ? [] : await db.ReviewResults.AsNoTracking().Where(r => closed.Contains(r.CycleId)).Select(r => new { r.CycleId, r.EmployeeId, r.CalibratedRating }).ToListAsync(ct);
        var lastPay = await db.Compensations.AsNoTracking().GroupBy(c => c.EmployeeId).Select(g => new { g.Key, Last = g.Max(x => x.EffectiveFrom) }).ToDictionaryAsync(x => x.Key, x => x.Last, ct);
        var ot = await db.OvertimeRequests.AsNoTracking().Where(o => o.Status == RequestStatus.Approved && o.Date >= d90).GroupBy(o => o.EmployeeId).Select(g => new { g.Key, H = g.Sum(x => x.Hours) }).ToDictionaryAsync(x => x.Key, x => x.H, ct);
        var shortLeave = await db.LeaveRequests.AsNoTracking().Where(l => l.Status == LeaveStatus.Approved && l.StartDate >= d90 && l.Days <= 1).GroupBy(l => l.EmployeeId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var leftIds = await db.Employees.AsNoTracking().Where(e => e.TerminationDate >= d180 && e.TerminationDate <= today).Select(e => e.Id).ToListAsync(ct);
        var leaversByManager = leftIds.Where(jobs.ContainsKey).GroupBy(id => jobs[id].Manager).Where(g => g.Key != null).ToDictionary(g => g.Key!.Value, g => g.Count());
        var rows = new List<AtRiskRow>();
        foreach (var e in emps)
        {
            var s = new List<RiskSignal>(); jobs.TryGetValue(e.Id, out var j);
            if (j.Type != EmploymentType.Permanent && j.ContractEnd is { } end && end >= today && end <= today.AddDays(60)) s.Add(new("contract_ending", $"Fixed-term contract ends {end:dd MMM yyyy}", 30));
            var mine = ratings.Where(r => r.EmployeeId == e.Id && r.CalibratedRating != null).ToDictionary(r => r.CycleId, r => r.CalibratedRating!.Value);
            if (closed.Count > 0 && mine.TryGetValue(closed[0], out var latest))
            {
                if (latest <= 2) s.Add(new("low_rating", $"Latest review rating {latest} of 5", 25));
                if (closed.Count > 1 && mine.TryGetValue(closed[1], out var before) && latest <= before - 1) s.Add(new("rating_drop", $"Rating fell from {before} to {latest}", 15));
            }
            var tenureMonths = (today.Year - e.HireDate.Year) * 12 + today.Month - e.HireDate.Month;
            if (tenureMonths >= 18 && lastPay.TryGetValue(e.Id, out var pay) && pay <= today.AddMonths(-18)) s.Add(new("no_raise", $"No pay change since {pay:MMM yyyy}", 15));
            if (ot.TryGetValue(e.Id, out var h) && h >= 60) s.Add(new("overtime", $"{h:0.#} overtime hours in the last 90 days", 10));
            if (shortLeave.TryGetValue(e.Id, out var n) && n >= 4) s.Add(new("short_absences", $"{n} one-day absences in the last 90 days", 10));
            if (tenureMonths < 12) s.Add(new("new_joiner", "Joined less than a year ago", 10));
            if (j.Manager is { } m && leaversByManager.TryGetValue(m, out var gone) && gone >= 2) s.Add(new("team_leavers", $"{gone} colleagues under the same manager left in 6 months", 10));
            var score = Math.Min(100, s.Sum(x => x.Points));
            if (score >= 25) rows.Add(new AtRiskRow(e.Id, e.EmployeeNo, e.FullName, j.Job, j.Dept, score, score >= 50 ? "high" : "medium", s));
        }
        audit.Record("hr.analytics.at_risk_viewed", "employee", "-", Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["listed"] = rows.Count.ToString() });
        await db.SaveChangesAsync(ct);
        return [.. rows.OrderByDescending(r => r.Score).ThenBy(r => r.Name)];
    }

    // ============================================================ report builder
    private sealed record Dataset(string Key, string Label, string Permission, IReadOnlyList<ReportColumn> Columns, Func<InsightService, CancellationToken, Task<List<Dictionary<string, object?>>>> Load);
    private static ReportColumn C(string key, string label, string type = "text") => new(key, label, type);

    private static readonly IReadOnlyList<Dataset> Datasets =
    [
        new("employees", "Employees", Permissions.EmployeesRead,
            [C("employeeNo", "Employee no."), C("name", "Name"), C("department", "Department"), C("jobTitle", "Job title"), C("grade", "Grade"), C("location", "Location"), C("type", "Employment type"), C("status", "Status"),
             C("gender", "Gender"), C("age", "Age", "number"), C("hireDate", "Hire date", "date"), C("tenureYears", "Years of service", "number"), C("contractEnd", "Contract end", "date"), C("terminationDate", "Left on", "date"), C("manager", "Manager")],
            (s, ct) => s.EmployeesRowsAsync(ct)),
        new("leave", "Leave requests", Permissions.LeaveReadAll,
            [C("employeeNo", "Employee no."), C("name", "Name"), C("department", "Department"), C("leaveType", "Leave type"), C("startDate", "From", "date"), C("endDate", "To", "date"), C("days", "Days", "number"), C("status", "Status"), C("month", "Month")],
            (s, ct) => s.LeaveRowsAsync(ct)),
        new("overtime", "Overtime", Permissions.AttendanceReadAll,
            [C("employeeNo", "Employee no."), C("name", "Name"), C("department", "Department"), C("date", "Date", "date"), C("hours", "Hours", "number"), C("status", "Status"), C("month", "Month")],
            (s, ct) => s.OvertimeRowsAsync(ct)),
        new("attendance", "Attendance", Permissions.AttendanceReadAll,
            [C("employeeNo", "Employee no."), C("name", "Name"), C("department", "Department"), C("date", "Date", "date"), C("hours", "Hours worked", "number"), C("workMode", "Work mode"), C("source", "Source"), C("month", "Month")],
            (s, ct) => s.AttendanceRowsAsync(ct)),
        new("claims", "Expense claims", Permissions.ClaimsManage,
            [C("employeeNo", "Employee no."), C("name", "Name"), C("department", "Department"), C("category", "Category"), C("expenseDate", "Expense date", "date"), C("amount", "Amount", "number"), C("status", "Status"), C("month", "Month")],
            (s, ct) => s.ClaimRowsAsync(ct)),
        new("training", "Training", Permissions.HrConfigManage,
            [C("employeeNo", "Employee no."), C("name", "Name"), C("department", "Department"), C("course", "Course"), C("category", "Category"), C("status", "Status"), C("dueDate", "Due", "date"), C("completedOn", "Completed on", "date"), C("quizScore", "Quiz score", "number")],
            (s, ct) => s.TrainingRowsAsync(ct)),
        new("applications", "Job applications", Permissions.HrConfigManage,
            [C("posting", "Job"), C("candidate", "Candidate"), C("source", "Source"), C("stage", "Stage"), C("status", "Status"), C("appliedOn", "Applied on", "date"), C("month", "Month"), C("rating", "Rating", "number")],
            (s, ct) => s.ApplicationRowsAsync(ct)),
    ];
    private const int MaxRows = 20000;

    private async Task<HashSet<string>> PermsAsync(SessionPrincipal actor, CancellationToken ct) => [.. await authz.GetEffectivePermissionsAsync(actor.UserId, ct)];

    public async Task<IReadOnlyList<ReportDataset>> DatasetsAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var perms = await PermsAsync(actor, ct);
        return [.. Datasets.Where(d => perms.Contains(d.Permission)).Select(d => new ReportDataset(d.Key, d.Label, d.Columns))];
    }

    private static string Ym(DateOnly d) => d.ToString("yyyy-MM");
    private async Task<List<Dictionary<string, object?>>> EmployeesRowsAsync(CancellationToken ct)
    {
        var jobs = await db.Employments.AsNoTracking().Select(m => new { m.EmployeeId, m.EffectiveFrom, m.EffectiveTo, m.OrgUnitId, m.JobTitle, m.Grade, m.Location, m.Type, m.ContractEnd, m.ManagerId }).ToListAsync(ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var emps = await db.Employees.AsNoTracking().Take(MaxRows).Select(e => new { e.Id, e.EmployeeNo, e.FullName, e.Status, e.Gender, e.BirthDate, e.HireDate, e.TerminationDate }).ToListAsync(ct);
        var names = emps.ToDictionary(e => e.Id, e => e.FullName); var today = Today;
        return [.. emps.Select(e =>
        {
            var j = jobs.Where(x => x.EmployeeId == e.Id).OrderByDescending(x => x.EffectiveTo == null).ThenByDescending(x => x.EffectiveFrom).FirstOrDefault();
            var end = e.TerminationDate ?? today;
            return new Dictionary<string, object?> { ["employeeNo"] = e.EmployeeNo, ["name"] = e.FullName, ["department"] = j?.OrgUnitId is { } u ? units.GetValueOrDefault(u) : null, ["jobTitle"] = j?.JobTitle, ["grade"] = j?.Grade,
                ["location"] = j?.Location, ["type"] = j?.Type.ToString().ToLowerInvariant(), ["status"] = e.Status.ToString().ToLowerInvariant(), ["gender"] = e.Gender?.ToString().ToLowerInvariant(),
                ["age"] = e.BirthDate is { } b ? (decimal?)(today.Year - b.Year - (today < b.AddYears(today.Year - b.Year) ? 1 : 0)) : null, ["hireDate"] = e.HireDate,
                ["tenureYears"] = Math.Round((end.DayNumber - e.HireDate.DayNumber) / 365.25m, 1), ["contractEnd"] = j?.ContractEnd, ["terminationDate"] = e.TerminationDate, ["manager"] = j?.ManagerId is { } m ? names.GetValueOrDefault(m) : null };
        })];
    }
    private async Task<(Dictionary<Guid, string> No, Dictionary<Guid, string> Name, Dictionary<Guid, string> Dept)> PeopleAsync(CancellationToken ct)
    {
        var e = await db.Employees.AsNoTracking().Select(x => new { x.Id, x.EmployeeNo, x.FullName }).ToListAsync(ct);
        var jobs = await JobsAsync(ct);
        return (e.ToDictionary(x => x.Id, x => x.EmployeeNo), e.ToDictionary(x => x.Id, x => x.FullName), jobs.ToDictionary(x => x.Key, x => x.Value.Dept));
    }
    private async Task<List<Dictionary<string, object?>>> LeaveRowsAsync(CancellationToken ct)
    {
        var (no, name, dept) = await PeopleAsync(ct); var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var rows = await db.LeaveRequests.AsNoTracking().OrderByDescending(l => l.StartDate).Take(MaxRows).Select(l => new { l.EmployeeId, l.LeaveTypeId, l.StartDate, l.EndDate, l.Days, l.Status }).ToListAsync(ct);
        return [.. rows.Select(l => new Dictionary<string, object?> { ["employeeNo"] = no.GetValueOrDefault(l.EmployeeId), ["name"] = name.GetValueOrDefault(l.EmployeeId), ["department"] = dept.GetValueOrDefault(l.EmployeeId), ["leaveType"] = types.GetValueOrDefault(l.LeaveTypeId),
            ["startDate"] = l.StartDate, ["endDate"] = l.EndDate, ["days"] = l.Days, ["status"] = l.Status.ToString().ToLowerInvariant(), ["month"] = Ym(l.StartDate) })];
    }
    private async Task<List<Dictionary<string, object?>>> OvertimeRowsAsync(CancellationToken ct)
    {
        var (no, name, dept) = await PeopleAsync(ct);
        var rows = await db.OvertimeRequests.AsNoTracking().OrderByDescending(o => o.Date).Take(MaxRows).Select(o => new { o.EmployeeId, o.Date, o.Hours, o.Status }).ToListAsync(ct);
        return [.. rows.Select(o => new Dictionary<string, object?> { ["employeeNo"] = no.GetValueOrDefault(o.EmployeeId), ["name"] = name.GetValueOrDefault(o.EmployeeId), ["department"] = dept.GetValueOrDefault(o.EmployeeId), ["date"] = o.Date, ["hours"] = o.Hours, ["status"] = o.Status.ToString().ToLowerInvariant(), ["month"] = Ym(o.Date) })];
    }
    private async Task<List<Dictionary<string, object?>>> AttendanceRowsAsync(CancellationToken ct)
    {
        var (no, name, dept) = await PeopleAsync(ct); var since = Today.AddMonths(-12);
        var rows = await db.AttendanceLogs.AsNoTracking().Where(a => a.Date >= since).OrderByDescending(a => a.Date).Take(MaxRows).Select(a => new { a.EmployeeId, a.Date, a.ClockIn, a.ClockOut, a.BreakMinutes, a.WorkMode, a.Source }).ToListAsync(ct);
        return [.. rows.Select(a => new Dictionary<string, object?> { ["employeeNo"] = no.GetValueOrDefault(a.EmployeeId), ["name"] = name.GetValueOrDefault(a.EmployeeId), ["department"] = dept.GetValueOrDefault(a.EmployeeId), ["date"] = a.Date,
            ["hours"] = a.ClockOut is { } o ? Math.Round((decimal)((o - a.ClockIn).TotalMinutes - a.BreakMinutes) / 60m, 2) : null, ["workMode"] = a.WorkMode.ToString().ToLowerInvariant(), ["source"] = a.Source, ["month"] = Ym(a.Date) })];
    }
    private async Task<List<Dictionary<string, object?>>> ClaimRowsAsync(CancellationToken ct)
    {
        var (no, name, dept) = await PeopleAsync(ct);
        var rows = await db.ExpenseClaims.AsNoTracking().OrderByDescending(c => c.ExpenseDate).Take(MaxRows).Select(c => new { c.EmployeeId, c.Category, c.ExpenseDate, c.Amount, c.Status }).ToListAsync(ct);
        return [.. rows.Select(c => new Dictionary<string, object?> { ["employeeNo"] = no.GetValueOrDefault(c.EmployeeId), ["name"] = name.GetValueOrDefault(c.EmployeeId), ["department"] = dept.GetValueOrDefault(c.EmployeeId), ["category"] = c.Category, ["expenseDate"] = c.ExpenseDate,
            ["amount"] = c.Amount, ["status"] = c.Status.ToString().ToLowerInvariant(), ["month"] = Ym(c.ExpenseDate) })];
    }
    private async Task<List<Dictionary<string, object?>>> TrainingRowsAsync(CancellationToken ct)
    {
        var (no, name, dept) = await PeopleAsync(ct); var courses = await db.Courses.AsNoTracking().ToDictionaryAsync(c => c.Id, c => new { c.Title, c.Category }, ct);
        var rows = await db.CourseEnrollments.AsNoTracking().Take(MaxRows).Select(e => new { e.EmployeeId, e.CourseId, e.Status, e.DueDate, e.CompletedAt, e.QuizScore }).ToListAsync(ct);
        return [.. rows.Select(e => new Dictionary<string, object?> { ["employeeNo"] = no.GetValueOrDefault(e.EmployeeId), ["name"] = name.GetValueOrDefault(e.EmployeeId), ["department"] = dept.GetValueOrDefault(e.EmployeeId), ["course"] = courses.GetValueOrDefault(e.CourseId)?.Title,
            ["category"] = courses.GetValueOrDefault(e.CourseId)?.Category, ["status"] = e.Status.ToString().ToLowerInvariant(), ["dueDate"] = e.DueDate, ["completedOn"] = e.CompletedAt is { } c ? DateOnly.FromDateTime(c.UtcDateTime.AddHours(7)) : null, ["quizScore"] = (decimal?)e.QuizScore })];
    }
    private async Task<List<Dictionary<string, object?>>> ApplicationRowsAsync(CancellationToken ct)
    {
        var rows = await (from a in db.JobApplications.AsNoTracking() join c in db.Candidates.AsNoTracking() on a.CandidateId equals c.Id join p in db.JobPostings.AsNoTracking() on a.PostingId equals p.Id orderby a.AppliedAt descending
                          select new { p.Title, c.FullName, c.Source, a.Stage, a.Status, a.AppliedAt, a.Rating }).Take(MaxRows).ToListAsync(ct);
        return [.. rows.Select(a => { var d = DateOnly.FromDateTime(a.AppliedAt.UtcDateTime.AddHours(7)); return new Dictionary<string, object?> { ["posting"] = a.Title, ["candidate"] = a.FullName, ["source"] = a.Source, ["stage"] = a.Stage,
            ["status"] = a.Status.ToString().ToLowerInvariant(), ["appliedOn"] = d, ["month"] = Ym(d), ["rating"] = (decimal?)a.Rating }; })];
    }

    private static object? Parse(string type, string? v) => v is null ? null : type switch
    {
        "number" => decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null,
        "date" => DateOnly.TryParse(v, CultureInfo.InvariantCulture, out var d) ? d : null,
        _ => v,
    };
    private static int Cmp(object? a, object? b) => (a, b) switch
    {
        (null, null) => 0, (null, _) => -1, (_, null) => 1,
        (decimal x, decimal y) => x.CompareTo(y), (DateOnly x, DateOnly y) => x.CompareTo(y),
        _ => string.Compare(Convert.ToString(a, CultureInfo.InvariantCulture), Convert.ToString(b, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase),
    };

    /// <summary>Runs a report: filter the data set, then either list the chosen columns, group by one column with a measure, or pivot (rows × columns) with a measure.</summary>
    public async Task<ReportResult> RunAsync(ReportConfig r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var ds = Datasets.FirstOrDefault(d => d.Key == r.Dataset) ?? throw DomainException.Validation("Choose the data.", new Dictionary<string, string> { ["dataset"] = "Unknown data set." });
        if (!(await PermsAsync(actor, ct)).Contains(ds.Permission)) throw DomainException.Denied(ds.Permission);
        var cols = ds.Columns.ToDictionary(c => c.Key);
        var errors = new Dictionary<string, string>();
        var filters = r.Filters ?? [];
        foreach (var f in filters) if (f.Field is null || !cols.ContainsKey(f.Field) || f.Op is not ("eq" or "neq" or "contains" or "gt" or "gte" or "lt" or "lte" or "empty" or "notempty")) { errors["filters"] = "Check the filters."; break; }
        if (r.GroupBy is { } g && !cols.ContainsKey(g)) errors["groupBy"] = "Choose a column to group by.";
        if (r.PivotBy is { } p && (!cols.ContainsKey(p) || r.GroupBy is null || p == r.GroupBy)) errors["pivotBy"] = "Choose a different column for the pivot, and a row grouping.";
        var measure = r.Measure ?? "count"; if (measure is not ("count" or "sum" or "avg" or "min" or "max")) errors["measure"] = "Use count, sum, average, minimum or maximum.";
        if (measure != "count" && (r.MeasureField is null || !cols.TryGetValue(r.MeasureField, out var mf) || mf.Type != "number")) errors["measureField"] = "Choose a number column to add up.";
        var columns = (r.Columns is { Count: > 0 } c0 ? c0 : ds.Columns.Select(c => c.Key).ToList()).Where(cols.ContainsKey).Distinct().ToList();
        if (errors.Count > 0) throw DomainException.Validation("Please check the report.", errors);
        var rows = await ds.Load(this, ct);
        foreach (var f in filters)
        {
            var col = cols[f.Field!]; var want = Parse(col.Type, f.Value);
            rows = [.. rows.Where(x =>
            {
                var v = x.GetValueOrDefault(f.Field!);
                return f.Op switch
                {
                    "empty" => v is null || v is string s0 && s0.Length == 0, "notempty" => v is not null && !(v is string s1 && s1.Length == 0),
                    "contains" => v is not null && Convert.ToString(v, CultureInfo.InvariantCulture)!.Contains(f.Value ?? "", StringComparison.OrdinalIgnoreCase),
                    "eq" => Cmp(v, want) == 0, "neq" => Cmp(v, want) != 0, "gt" => v is not null && Cmp(v, want) > 0, "gte" => v is not null && Cmp(v, want) >= 0, "lt" => v is not null && Cmp(v, want) < 0, _ => v is not null && Cmp(v, want) <= 0,
                };
            })];
        }
        decimal? Measure(IEnumerable<Dictionary<string, object?>> xs)
        {
            if (measure == "count") return xs.Count();
            var vals = xs.Select(x => x.GetValueOrDefault(r.MeasureField!)).OfType<decimal>().ToList();
            return vals.Count == 0 ? null : measure switch { "sum" => vals.Sum(), "avg" => Math.Round(vals.Average(), 2), "min" => vals.Min(), _ => vals.Max() };
        }
        string Key(object? v) => v switch { null => "—", DateOnly d => d.ToString("yyyy-MM-dd"), decimal n => n.ToString(CultureInfo.InvariantCulture), _ => v.ToString()! };
        var measureLabel = measure == "count" ? "Count" : $"{measure switch { "sum" => "Total", "avg" => "Average", "min" => "Minimum", _ => "Maximum" }} {cols[r.MeasureField!].Label.ToLowerInvariant()}";
        var limit = Math.Clamp(r.Limit ?? 1000, 1, 5000);
        ReportResult result;
        if (r.GroupBy is { } gb && r.PivotBy is { } pb)
        {
            var colKeys = rows.Select(x => Key(x.GetValueOrDefault(pb))).Distinct().OrderBy(k => k, StringComparer.OrdinalIgnoreCase).Take(50).ToList();
            var groups = rows.GroupBy(x => Key(x.GetValueOrDefault(gb))).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
            var body = groups.Select(grp => (IReadOnlyList<object?>)[grp.Key, .. colKeys.Select(ck => (object?)Measure(grp.Where(x => Key(x.GetValueOrDefault(pb)) == ck))), Measure(grp)]).ToList();
            body.Add(["Total", .. colKeys.Select(ck => (object?)Measure(rows.Where(x => Key(x.GetValueOrDefault(pb)) == ck))), Measure(rows)]);
            result = new ReportResult([cols[gb].Label, .. colKeys, "Total"], ["text", .. colKeys.Select(_ => "number"), "number"], [.. body.Take(limit + 1)], groups.Count, groups.Count > limit);
        }
        else if (r.GroupBy is { } gb2)
        {
            var groups = rows.GroupBy(x => Key(x.GetValueOrDefault(gb2))).Select(grp => (Key: grp.Key, Value: Measure(grp))).ToList();
            groups = r.SortBy == "value" ? [.. (r.SortDesc == true ? groups.OrderByDescending(x => x.Value) : groups.OrderBy(x => x.Value))] : [.. groups.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)];
            result = new ReportResult([cols[gb2].Label, measureLabel], ["text", "number"], [.. groups.Take(limit).Select(x => (IReadOnlyList<object?>)[x.Key, x.Value])], groups.Count, groups.Count > limit);
        }
        else
        {
            if (r.SortBy is { } sb && cols.ContainsKey(sb)) rows = r.SortDesc == true ? [.. rows.OrderByDescending(x => x.GetValueOrDefault(sb), Comparer<object?>.Create(Cmp))] : [.. rows.OrderBy(x => x.GetValueOrDefault(sb), Comparer<object?>.Create(Cmp))];
            result = new ReportResult([.. columns.Select(k => cols[k].Label)], [.. columns.Select(k => cols[k].Type)], [.. rows.Take(limit).Select(x => (IReadOnlyList<object?>)[.. columns.Select(k => x.GetValueOrDefault(k) is DateOnly d ? d.ToString("yyyy-MM-dd") : x.GetValueOrDefault(k))])], rows.Count, rows.Count > limit);
        }
        audit.Record("hr.report.run", "report", ds.Key, Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["rows"] = result.TotalRows.ToString() });
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<ExportFile> ExportAsync(ReportConfig r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var res = await RunAsync(r with { Limit = 5000 }, actor, client, ct);
        static string Cell(object? v) { var s = v switch { null => "", decimal d => d.ToString(CultureInfo.InvariantCulture), _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" }; if (s.Length > 0 && "=+-@".Contains(s[0])) s = "'" + s; return s.Contains('"') || s.Contains(',') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s; }
        var sb = new StringBuilder(); sb.AppendLine(string.Join(',', res.Headers.Select(Cell)));
        foreach (var row in res.Rows) sb.AppendLine(string.Join(',', row.Select(Cell)));
        audit.Record("hr.report.export", "report", r.Dataset ?? "", Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["rows"] = res.Rows.Count.ToString() }); await db.SaveChangesAsync(ct);
        return new ExportFile($"report-{r.Dataset}-{Today:yyyyMMdd}.csv", "text/csv", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray());
    }

    // ============================================================ saved reports
    public async Task<IReadOnlyList<SavedReportView>> SavedAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var perms = await PermsAsync(actor, ct); var allowed = Datasets.Where(d => perms.Contains(d.Permission)).Select(d => d.Key).ToList();
        var rows = await db.SavedReports.AsNoTracking().Where(s => (s.OwnerUserId == actor.UserId || s.Shared) && allowed.Contains(s.Dataset)).OrderBy(s => s.Name).ToListAsync(ct);
        var owners = rows.Select(r => r.OwnerUserId).Distinct().ToList(); var names = await db.Users.AsNoTracking().Where(u => owners.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return [.. rows.Select(s => new SavedReportView(s.Id, s.Name, s.Dataset, JsonSerializer.Deserialize<ReportConfig>(s.ConfigJson, Json)!, s.Shared, s.OwnerUserId == actor.UserId, names.GetValueOrDefault(s.OwnerUserId) ?? "", s.UpdatedAt))];
    }

    public async Task<SavedReportView> SaveAsync(Guid? id, SaveReportRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var s = id is { } sid ? await db.SavedReports.FirstOrDefaultAsync(x => x.Id == sid && x.OwnerUserId == actor.UserId, ct) ?? throw DomainException.NotFound("Report") : new SavedReport { OwnerUserId = actor.UserId, CreatedAt = clock.UtcNow };
        var name = r.Name?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 150)] : s.Name; if (name.Length < 2) throw DomainException.Validation("Name the report.", new Dictionary<string, string> { ["name"] = "Name the report." });
        var cfg = r.Config ?? JsonSerializer.Deserialize<ReportConfig>(s.ConfigJson, Json)!;
        var ds = Datasets.FirstOrDefault(d => d.Key == cfg.Dataset) ?? throw DomainException.Validation("Choose the data.", new Dictionary<string, string> { ["dataset"] = "Unknown data set." });
        if (!(await PermsAsync(actor, ct)).Contains(ds.Permission)) throw DomainException.Denied(ds.Permission);
        s.Name = name; s.Dataset = ds.Key; s.ConfigJson = JsonSerializer.Serialize(cfg, Json); s.Shared = r.Shared ?? s.Shared; s.UpdatedAt = clock.UtcNow;
        if (id is null) db.SavedReports.Add(s);
        audit.Record(id is null ? "hr.report.save" : "hr.report.update", "report", s.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name });
        await db.SaveChangesAsync(ct);
        return (await SavedAsync(actor, ct)).First(x => x.Id == s.Id);
    }

    public async Task DeleteAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var s = await db.SavedReports.FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == actor.UserId, ct) ?? throw DomainException.NotFound("Report");
        db.SavedReports.Remove(s); audit.Record("hr.report.delete", "report", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email); await db.SaveChangesAsync(ct);
    }
}
