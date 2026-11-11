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

public sealed record AnnualTaxMonth(int Month, decimal TaxableIncome, decimal Pension, decimal Tax);
public sealed record AnnualTaxCompany(string Company, string? CompanyTaxId, string TaxStatus, int Months, decimal TaxableIncome, decimal Pension, decimal Tax, IReadOnlyList<AnnualTaxMonth> ByMonth);
public sealed record AnnualTaxStatement(int Year, string EmployeeNo, string EmployeeName, bool HasTaxId, IReadOnlyList<AnnualTaxCompany> Companies);
public sealed record LoanView(Guid Id, Guid EmployeeId, string EmployeeNo, string EmployeeName, decimal Principal, decimal Installment, decimal Remaining, string Status, string? Note, DateTimeOffset CreatedAt);
public sealed record CreateLoanRequest(Guid? EmployeeId, decimal? Principal, decimal? Installment, string? Note);
public sealed record SeveranceRequest(Guid? EmployeeId, string? Reason, DateOnly? LastDay);
public sealed record SeveranceLine(string Label, string Kind, decimal Amount, bool Taxable, string Basis);
public sealed record SeverancePreview(Guid EmployeeId, string EmployeeName, string Reason, DateOnly LastDay, int Years, int Months, decimal Wage, IReadOnlyList<SeveranceLine> Lines, decimal NetTotal);

/// <summary>
/// PAY-006/010 and PP 35/2021 termination pay. Loans, the severance calculator (pesangon, UPMK, unused-leave pay, fixed-term compensation, with the final tax on pesangon/UPMK under PP 68/2009),
/// the BPJS and accounting-journal exports, the year-end income and tax summary, and the payslip as a PDF. All figures are baseline rules to be confirmed by a tax/labour consultant.
/// </summary>
public sealed class PayrollExtrasService(IAppDb db, IClock clock, PayrollService payroll, AuthorizationService authz, ISecretProtector protector, ShiftService shifts, AuditWriter audit, PayrollRulesService ruleSets, DocumentSealService seals, IAppLinks links)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    // ============================================================ loans
    public async Task<IReadOnlyList<LoanView>> LoansAsync(bool activeOnly, SessionPrincipal actor, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var q = db.Loans.AsNoTracking().AsQueryable(); if (activeOnly) q = q.Where(l => l.Status == LoanStatus.Active);
        var rows = await q.OrderByDescending(l => l.CreatedAt).ToListAsync(ct); var ids = rows.Select(l => l.EmployeeId).Distinct().ToList();
        var emps = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        return [.. rows.Select(l => new LoanView(l.Id, l.EmployeeId, emps[l.EmployeeId].EmployeeNo, emps[l.EmployeeId].FullName, l.Principal, l.Installment, l.Remaining, l.Status.ToString().ToLowerInvariant(), l.Note, l.CreatedAt))];
    }

    public async Task<LoanView> CreateLoanAsync(CreateLoanRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var errors = new Dictionary<string, string>();
        var emp = r.EmployeeId is { } id ? await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id && e.Status == EmployeeStatus.Active, ct) : null; if (emp is null) errors["employeeId"] = "Choose an active employee.";
        if (r.Principal is null or <= 0 or > 1_000_000_000m) errors["principal"] = "Enter the loan amount in rupiah.";
        if (r.Installment is null or <= 0) errors["installment"] = "Enter the monthly instalment."; else if (r.Principal is { } p && r.Installment > p) errors["installment"] = "The instalment cannot exceed the loan.";
        var note = r.Note?.Trim(); if (note is { Length: > 200 }) errors["note"] = "Keep the note under 200 characters.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the loan.", errors);
        if (await db.Loans.CountAsync(l => l.EmployeeId == emp!.Id && l.Status == LoanStatus.Active, ct) >= 3) throw DomainException.Invariant("An employee can have at most 3 active loans.");
        var loan = new Loan { EmployeeId = emp!.Id, Principal = PayrollRules.Rupiah(r.Principal!.Value), Installment = PayrollRules.Rupiah(r.Installment!.Value), Remaining = PayrollRules.Rupiah(r.Principal.Value), Note = string.IsNullOrEmpty(note) ? null : note, CreatedAt = clock.UtcNow, CreatedBy = actor.UserId };
        db.Loans.Add(loan);
        audit.Record("hr.payroll.loan_create", "employee", emp.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeNo"] = emp.EmployeeNo });
        await db.SaveChangesAsync(ct);
        return new LoanView(loan.Id, emp.Id, emp.EmployeeNo, emp.FullName, loan.Principal, loan.Installment, loan.Remaining, "active", loan.Note, loan.CreatedAt);
    }

    public async Task CancelLoanAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var l = await db.Loans.FirstOrDefaultAsync(x => x.Id == id && x.Status == LoanStatus.Active, ct) ?? throw DomainException.NotFound("Loan");
        l.Status = LoanStatus.Cancelled;
        audit.Record("hr.payroll.loan_cancel", "employee", l.EmployeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["remaining"] = l.Remaining.ToString() });
        await db.SaveChangesAsync(ct);
    }

    // ============================================================ severance
    public static readonly string[] Reasons = ["resignation", "end_of_contract", "layoff_efficiency", "layoff_other", "retirement", "death", "misconduct"];

    private static int PesangonMonths(int years) => Math.Min(years + 1, 9);
    private static int UpmkMonths(int years) => years switch { < 3 => 0, < 6 => 2, < 9 => 3, < 12 => 4, < 15 => 5, < 18 => 6, < 21 => 7, < 24 => 8, _ => 10 };
    private static decimal PesangonFactor(string reason) => reason switch { "layoff_efficiency" => 0.5m, "layoff_other" => 1m, "retirement" => 1.75m, "death" => 2m, _ => 0m };

    /// <summary>PP 68/2009: final tax on pesangon/UPMK, progressive on the total: 0 % to 50 m, 5 % to 100 m, 15 % to 500 m, 25 % to 1 bn, 30 % above.</summary>
    public static decimal SeveranceTax(decimal amount)
    {
        (decimal Cap, decimal Rate)[] layers = [(50_000_000m, 0m), (100_000_000m, 0.05m), (500_000_000m, 0.15m), (1_000_000_000m, 0.25m), (decimal.MaxValue, 0.30m)];
        decimal tax = 0, floor = 0;
        foreach (var (cap, rate) in layers) { if (amount <= floor) break; tax += (Math.Min(amount, cap) - floor) * rate; floor = cap; }
        return PayrollRules.Rupiah(tax);
    }

    public async Task<SeverancePreview> PreviewSeveranceAsync(SeveranceRequest r, SessionPrincipal actor, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var reason = (r.Reason ?? "").Trim().ToLowerInvariant();
        if (!Reasons.Contains(reason)) throw DomainException.Validation("Please check the request.", new Dictionary<string, string> { ["reason"] = "Choose: " + string.Join(", ", Reasons) });
        var e = r.EmployeeId is { } id ? await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) : null; if (e is null) throw DomainException.NotFound("Employee");
        var last = r.LastDay ?? e.TerminationDate ?? Today;
        if (last < e.HireDate) throw DomainException.Validation("Please check the request.", new Dictionary<string, string> { ["lastDay"] = "The last day is before the hire date." });
        var c = await db.Compensations.AsNoTracking().Where(x => x.EmployeeId == e.Id && x.EffectiveFrom <= last).OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct)
                ?? throw DomainException.Invariant("Set this employee's compensation first.");
        var wage = PayrollService.Dec(payroll.Reveal("comp", c.Id, "basic", c.BasicEnc)) + PayrollService.Dec(payroll.Reveal("comp", c.Id, "allowance", c.AllowanceEnc));
        var years = last.Year - e.HireDate.Year; if (last < e.HireDate.AddYears(years)) years--;
        var months = (last.Year - e.HireDate.Year) * 12 + last.Month - e.HireDate.Month - (last.Day < e.HireDate.Day ? 1 : 0);
        var lines = new List<SeveranceLine>(); var R = PayrollRules.Rupiah;

        var factor = PesangonFactor(reason);
        decimal pesangon = 0, upmk = 0;
        if (factor > 0)
        {
            pesangon = R(wage * PesangonMonths(years) * factor); upmk = R(wage * UpmkMonths(years));
            lines.Add(new("Severance: Pesangon", "earning", pesangon, false, $"{PesangonMonths(years)} month(s) × {factor:0.##} for {years} full year(s) of service"));
            if (upmk > 0) lines.Add(new("Severance: UPMK (long-service pay)", "earning", upmk, false, $"{UpmkMonths(years)} month(s) for {years} full year(s) of service"));
        }
        if (reason == "end_of_contract" && months >= 1)
            lines.Add(new("Severance: Fixed-term compensation", "earning", R(wage * months / 12m), true, $"{months} month(s) / 12 × one month's wage"));
        // Unused-leave pay is owed whatever the reason for leaving (misconduct included)
        {
            var year = last.Year;
            var ledger = await db.LeaveLedger.AsNoTracking().Where(l => l.EmployeeId == e.Id && l.Year == year).SumAsync(l => (decimal?)l.Days, ct) ?? 0;
            var lookup = await shifts.LookupAsync([e.Id], last, last, ct); var perWeek = Enumerable.Range(0, 7).Count(i => ShiftService.WorksOn(lookup.For(e.Id, last).WorkDays, (DayOfWeek)i));
            var unused = Math.Max(ledger, 0);
            if (unused > 0) lines.Add(new("Severance: Unused leave pay (UPH)", "earning", R(wage / (perWeek >= 6 ? 25m : 21m) * unused), true, $"{unused:0.##} unused day(s) × wage / {(perWeek >= 6 ? 25 : 21)}"));
        }
        if (pesangon + upmk > 0) lines.Add(new("Severance: Final tax on pesangon/UPMK (PP 68/2009)", "deduction", SeveranceTax(pesangon + upmk), false, "Progressive 0/5/15/25/30 % on pesangon + UPMK"));
        var net = lines.Where(l => l.Kind == "earning").Sum(l => l.Amount) - lines.Where(l => l.Kind == "deduction").Sum(l => l.Amount);
        return new SeverancePreview(e.Id, e.FullName, reason, last, years, months, wage, lines, net);
    }

    /// <summary>Adds the previewed amounts to a pay run as lines. Pesangon/UPMK are not taxable in the run (their final tax is its own deduction line); the rest is ordinary income.</summary>
    public async Task<int> ApplySeveranceAsync(Guid runId, SeveranceRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var p = await PreviewSeveranceAsync(r, actor, ct);
        var run = await db.PayRuns.FirstOrDefaultAsync(x => x.Id == runId, ct) ?? throw DomainException.NotFound("Pay run");
        if (run.Status is not (PayRunStatus.Draft or PayRunStatus.Calculated)) throw DomainException.Invariant("This pay run can no longer be changed.");
        if (!await db.Employees.AnyAsync(e => e.Id == p.EmployeeId && e.LegalEntityId == run.LegalEntityId, ct)) throw DomainException.Invariant("This employee belongs to another company than this pay run.");
        if (await db.PayRunLines.AnyAsync(l => l.PayRunId == runId && l.EmployeeId == p.EmployeeId && l.Label.StartsWith("Severance:"), ct)) throw DomainException.Conflict("Severance was already added for this employee in this pay run.");
        foreach (var l in p.Lines.Where(x => x.Amount > 0))
            db.PayRunLines.Add(new PayRunLine { PayRunId = runId, EmployeeId = p.EmployeeId, Kind = l.Kind == "earning" ? PayLineKind.Earning : PayLineKind.Deduction, Label = l.Label, Amount = l.Amount, Taxable = l.Taxable, CreatedBy = actor.UserId, CreatedAt = clock.UtcNow });
        if (run.Status == PayRunStatus.Calculated) { run.Status = PayRunStatus.Draft; run.CalculatedBy = null; run.CalculatedAt = null; run.Version++; }
        audit.Record("hr.payroll.severance", "pay_run", runId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employee"] = p.EmployeeName, ["reason"] = p.Reason, ["net"] = p.NetTotal.ToString() });
        await db.SaveChangesAsync(ct);
        return p.Lines.Count(x => x.Amount > 0);
    }

    // ============================================================ exports
    private static string M(decimal d) => d.ToString("0.##", CultureInfo.InvariantCulture);
    private static ExportFile Csv(string name, StringBuilder sb) => new(name, "text/csv; charset=utf-8", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray());

    public async Task<ExportFile> BpjsReportAsync(Guid runId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var run = await db.PayRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == runId, ct) ?? throw DomainException.NotFound("Pay run");
        var items = await db.PayRunItems.AsNoTracking().Where(i => i.PayRunId == runId).OrderBy(i => i.EmployeeNo).ToListAsync(ct);
        var ids = items.Select(i => i.EmployeeId).ToList(); var people = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var sb = new StringBuilder("employee_no,name,bpjs_kes_no,bpjs_tk_no,kes_employee,kes_employer,jht_employee,jht_employer,jp_employee,jp_employer,jkk_employer,jkm_employer,total\r\n");
        foreach (var i in items)
            sb.Append(string.Join(',', new[] { i.EmployeeNo, i.EmployeeName, Open(people.GetValueOrDefault(i.EmployeeId), "bpjs_kes_no", x => x.BpjsKesNoEnc), Open(people.GetValueOrDefault(i.EmployeeId), "bpjs_tk_no", x => x.BpjsTkNoEnc), M(i.KesEmployee), M(i.KesEmployer), M(i.JhtEmployee), M(i.JhtEmployer), M(i.JpEmployee), M(i.JpEmployer), M(i.JkkEmployer), M(i.JkmEmployer),
                M(i.KesEmployee + i.KesEmployer + i.JhtEmployee + i.JhtEmployer + i.JpEmployee + i.JpEmployer + i.JkkEmployer + i.JkmEmployer) }.Select(EmployeeService.Csv))).Append("\r\n");
        audit.Record("hr.payroll.bpjs_report", "pay_run", runId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["rows"] = items.Count.ToString() });
        await db.SaveChangesAsync(ct);
        return Csv($"bpjs-{run.Year}-{run.Month:D2}.csv", sb);
    }

    private string Open(Employee? e, string field, Func<Employee, string?> pick)
    {
        if (e is null || pick(e) is not { } enc) return "";
        var pp = enc.Split('.'); return Encoding.UTF8.GetString(protector.Unprotect(new ProtectedSecret(Convert.FromBase64String(pp[2]), Convert.FromBase64String(pp[1]), int.Parse(pp[0])), Encoding.UTF8.GetBytes($"employee:{e.Id}:{field}")));
    }

    /// <summary>Double-entry journal for the finished run, with neutral account names the finance team maps to its chart of accounts. Debits always equal credits.</summary>
    public async Task<ExportFile> JournalAsync(Guid runId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var run = await db.PayRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == runId, ct) ?? throw DomainException.NotFound("Pay run");
        if (run.Status is not (PayRunStatus.Locked or PayRunStatus.Paid)) throw DomainException.Invariant("The journal is available once the pay run is locked.");
        var i = await db.PayRunItems.AsNoTracking().Where(x => x.PayRunId == runId).GroupBy(_ => 1).Select(g => new
        {
            Gross = g.Sum(x => x.Gross), Net = g.Sum(x => x.Net), Tax = g.Sum(x => x.Tax), Other = g.Sum(x => x.OtherDeductions), EeBpjs = g.Sum(x => x.KesEmployee + x.JhtEmployee + x.JpEmployee),
            ErBpjs = g.Sum(x => x.KesEmployer + x.JhtEmployer + x.JpEmployer + x.JkkEmployer + x.JkmEmployer),
        }).FirstOrDefaultAsync(ct);
        var rows = new List<(string Account, decimal Debit, decimal Credit, string Memo)>();
        void Dr(string a, decimal v, string memo) { if (v > 0) rows.Add((a, v, 0, memo)); else if (v < 0) rows.Add((a, 0, -v, memo)); }
        void Cr(string a, decimal v, string memo) { if (v > 0) rows.Add((a, 0, v, memo)); else if (v < 0) rows.Add((a, -v, 0, memo)); }
        if (i is not null)
        {
            Dr("Salaries and wages expense", i.Gross, "Gross pay incl. overtime, THR, bonuses"); Dr("Employer BPJS expense", i.ErBpjs, "Employer BPJS contributions");
            Cr("Salaries payable", i.Net, "Net pay to employees"); Cr("PPh 21 payable", i.Tax, "Income tax withheld (negative = refund)");
            Cr("BPJS payable", i.EeBpjs + i.ErBpjs, "Employee and employer BPJS"); Cr("Other payroll deductions payable", i.Other, "Loans and other deductions");
        }
        var sb = new StringBuilder("date,account,debit,credit,memo\r\n"); var date = new DateOnly(run.Year, run.Month, DateTime.DaysInMonth(run.Year, run.Month)).ToString("yyyy-MM-dd");
        foreach (var r in rows) sb.Append(string.Join(',', new[] { date, r.Account, M(r.Debit), M(r.Credit), r.Memo + $" ({run.Year}-{run.Month:D2})" }.Select(EmployeeService.Csv))).Append("\r\n");
        audit.Record("hr.payroll.journal", "pay_run", runId.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return Csv($"payroll-journal-{run.Year}-{run.Month:D2}.csv", sb);
    }

    /// <summary>Year-end figures per employee from locked runs, as the starting data for the annual withholding slip (1721-A1 / e-Bupot). The official forms are still produced with the tax office's own tools.</summary>
    public async Task<ExportFile> AnnualTaxAsync(int year, Guid? legalEntityId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var rows = await (from i in db.PayRunItems.AsNoTracking() join p in db.PayRuns.AsNoTracking() on i.PayRunId equals p.Id
                          where p.Year == year && (legalEntityId == null || p.LegalEntityId == legalEntityId) && (p.Status == PayRunStatus.Locked || p.Status == PayRunStatus.Paid) select new { p.LegalEntityId, i.EmployeeId, i.EmployeeNo, i.EmployeeName, i.Ptkp, p.Month, i.TaxableGross, i.JhtEmployee, i.JpEmployee, i.Tax }).ToListAsync(ct);
        var ids = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var emps = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var companies = await db.LegalEntities.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var sb = new StringBuilder("company,company_npwp,employee_no,name,npwp,ptkp,months,taxable_income,pension_contributions,pph21_withheld\r\n");
        // One line per employee per company: the withholding certificate is issued by the company that paid (its own NPWP).
        foreach (var g in rows.GroupBy(r => new { r.EmployeeId, r.LegalEntityId }).OrderBy(g => companies[g.Key.LegalEntityId].Name).ThenBy(g => g.First().EmployeeNo))
        {
            var e = emps[g.Key.EmployeeId]; var co = companies[g.Key.LegalEntityId]; string npwp = "";
            if (e.TaxIdEnc is { } enc) { var pp = enc.Split('.'); npwp = Encoding.UTF8.GetString(protector.Unprotect(new ProtectedSecret(Convert.FromBase64String(pp[2]), Convert.FromBase64String(pp[1]), int.Parse(pp[0])), Encoding.UTF8.GetBytes($"employee:{e.Id}:tax_id"))); }
            sb.Append(string.Join(',', new[] { co.Name, co.TaxId ?? "", g.First().EmployeeNo, g.First().EmployeeName, npwp, g.OrderByDescending(x => x.Month).First().Ptkp, g.Select(x => x.Month).Distinct().Count().ToString(), M(g.Sum(x => x.TaxableGross)), M(g.Sum(x => x.JhtEmployee + x.JpEmployee)), M(g.Sum(x => x.Tax)) }.Select(EmployeeService.Csv))).Append("\r\n");
        }
        audit.Record("hr.payroll.annual_tax", "payroll", year.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employees"] = ids.Count.ToString(), ["companies"] = rows.Select(r => r.LegalEntityId).Distinct().Count().ToString() });
        await db.SaveChangesAsync(ct);
        return Csv($"annual-tax-{year}.csv", sb);
    }

    /// <summary>
    /// RPT-002. What each department cost in locked pay runs of a year (optionally one month, one company): gross pay plus the employer's BPJS contributions.
    /// A person counts under the department they belonged to at the end of each month.
    /// </summary>
    public async Task<ExportFile> LabourCostAsync(int year, int? month, Guid? legalEntityId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        if (month is < 1 or > 12) throw DomainException.Validation("Unknown month.", new Dictionary<string, string> { ["month"] = "Use 1–12." });
        var rows = await (from i in db.PayRunItems.AsNoTracking() join p in db.PayRuns.AsNoTracking() on i.PayRunId equals p.Id
                          where p.Year == year && (month == null || p.Month == month) && (legalEntityId == null || p.LegalEntityId == legalEntityId) && (p.Status == PayRunStatus.Locked || p.Status == PayRunStatus.Paid)
                          select new { i.EmployeeId, p.Month, i.Gross, i.OvertimePay, Employer = i.KesEmployer + i.JhtEmployer + i.JpEmployer + i.JkkEmployer + i.JkmEmployer, i.EmployerCost, i.Tax, i.Net }).ToListAsync(ct);
        var ids = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var jobs = await db.Employments.AsNoTracking().Where(m => ids.Contains(m.EmployeeId)).Select(m => new { m.EmployeeId, m.EffectiveFrom, m.EffectiveTo, m.OrgUnitId }).ToListAsync(ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        string Dept(Guid emp, int m)
        {
            var end = new DateOnly(year, m, 1).AddMonths(1).AddDays(-1);
            var job = jobs.Where(j => j.EmployeeId == emp && j.EffectiveFrom <= end).OrderByDescending(j => j.EffectiveFrom).FirstOrDefault();
            return job?.OrgUnitId is { } u ? units.GetValueOrDefault(u) ?? "(no department)" : "(no department)";
        }
        var sb = new StringBuilder("department,employees,gross_pay,of_which_overtime,employer_contributions,total_cost,pph21_withheld,net_pay\r\n");
        foreach (var g in rows.GroupBy(r => Dept(r.EmployeeId, r.Month)).OrderBy(g => g.Key))
            sb.Append(string.Join(',', new[] { g.Key, g.Select(x => x.EmployeeId).Distinct().Count().ToString(), M(g.Sum(x => x.Gross)), M(g.Sum(x => x.OvertimePay)), M(g.Sum(x => x.Employer)), M(g.Sum(x => x.EmployerCost)), M(g.Sum(x => x.Tax)), M(g.Sum(x => x.Net)) }.Select(EmployeeService.Csv))).Append("\r\n");
        sb.Append(string.Join(',', new[] { "TOTAL", ids.Count.ToString(), M(rows.Sum(x => x.Gross)), M(rows.Sum(x => x.OvertimePay)), M(rows.Sum(x => x.Employer)), M(rows.Sum(x => x.EmployerCost)), M(rows.Sum(x => x.Tax)), M(rows.Sum(x => x.Net)) }.Select(EmployeeService.Csv))).Append("\r\n");
        audit.Record("hr.payroll.labour_cost", "payroll", year.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["month"] = month?.ToString() ?? "all", ["employees"] = ids.Count.ToString() });
        await db.SaveChangesAsync(ct);
        return Csv(month is null ? $"labour-cost-{year}.csv" : $"labour-cost-{year}-{month:D2}.csv", sb);
    }

    /// <summary>
    /// PAY-010. The figures of the yearly withholding slip for permanent employees (form 1721-A1 / "BPA1" in Coretax), one line per employee and company, in the order of the form:
    /// income by kind, gross, position cost, pension contributions, net, tax-free allowance, taxable income, tax due for the year and tax withheld.
    /// It is the data to type or paste into the tax office's own template (their XML is produced by their converter); this system does not file anything.
    /// Daily-wage (non-permanent) workers are not on this form: their withholding is reported monthly.
    /// </summary>
    public async Task<ExportFile> A1Async(int year, Guid? legalEntityId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var rows = await (from i in db.PayRunItems.AsNoTracking() join p in db.PayRuns.AsNoTracking() on i.PayRunId equals p.Id
                          where p.Year == year && (legalEntityId == null || p.LegalEntityId == legalEntityId) && (p.Status == PayRunStatus.Locked || p.Status == PayRunStatus.Paid) && i.DailyRate == null
                          select new { p.LegalEntityId, p.Month, i.EmployeeId, i.EmployeeNo, i.EmployeeName, i.Ptkp, i.Basic, i.Allowance, i.ProrateFactor, i.UnpaidDeduction, i.TaxableExtras, i.OvertimePay, i.TaxableGross,
                              Premiums = i.KesEmployer + i.JkkEmployer + i.JkmEmployer, Pension = i.JhtEmployee + i.JpEmployee, i.Tax }).ToListAsync(ct);
        var ids = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var emps = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var companies = await db.LegalEntities.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var rules = await ruleSets.EffectiveForAsync(year, 12, ct);
        var sb = new StringBuilder("company,company_npwp,employee_no,name,npwp,nik,ptkp,first_month,last_month,months,1_salary,3_allowances_overtime,5_employer_premiums,7_bonus_thr_other,8_gross,9_position_cost,10_pension_contributions,12_net,15_ptkp,16_taxable_income,17_tax_due_for_the_year,20_tax_withheld,over_or_under_withheld,final_slip\r\n");
        foreach (var g in rows.GroupBy(r => new { r.EmployeeId, r.LegalEntityId }).OrderBy(g => companies[g.Key.LegalEntityId].Name).ThenBy(g => g.First().EmployeeNo))
        {
            var e = emps[g.Key.EmployeeId]; var co = companies[g.Key.LegalEntityId]; var months = g.Select(x => x.Month).Distinct().Count();
            var salary = g.Sum(x => PayrollRules.Rupiah(x.Basic * x.ProrateFactor) - x.UnpaidDeduction); var allowances = g.Sum(x => PayrollRules.Rupiah(x.Allowance * x.ProrateFactor) + x.OvertimePay);
            var premiums = g.Sum(x => x.Premiums); var gross = g.Sum(x => x.TaxableGross); var other = gross - salary - allowances - premiums;   // bonuses, THR and other taxable lines: whatever the gross holds beyond the three above
            var position = Math.Min(PayrollRules.Rupiah(gross * rules.PositionCostRate), rules.PositionCostMonthlyCap * months); var pension = g.Sum(x => x.Pension);
            var net = gross - position - pension; var ptkpStatus = g.OrderByDescending(x => x.Month).First().Ptkp; var ptkp = rules.PtkpTable.GetValueOrDefault(ptkpStatus);
            var pkp = Math.Floor(Math.Max(net - ptkp, 0) / 1000m) * 1000m; var due = PayrollRules.Rupiah(PayrollRules.ProgressiveTax(pkp)); var withheld = g.Sum(x => x.Tax);
            // The slip is final once the year (December) or the employment (the leaving month) has been paid; until then it is a running total.
            var final = g.Any(x => x.Month == 12) || e.TerminationDate is { } td && td.Year == year && g.Any(x => x.Month == td.Month);
            sb.Append(string.Join(',', new[] { co.Name, co.TaxId ?? "", e.EmployeeNo, g.First().EmployeeName, Open(e, "tax_id", x => x.TaxIdEnc), Open(e, "national_id", x => x.NationalIdEnc), ptkpStatus,
                g.Min(x => x.Month).ToString(), g.Max(x => x.Month).ToString(), months.ToString(), M(salary), M(allowances), M(premiums), M(other), M(gross), M(position), M(pension), M(net), M(ptkp), M(pkp), M(due), M(withheld), M(withheld - due), final ? "yes" : "no" }.Select(EmployeeService.Csv))).Append("\r\n");
        }
        audit.Record("hr.payroll.a1_export", "payroll", year.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employees"] = ids.Count.ToString() });
        await db.SaveChangesAsync(ct);
        return Csv($"1721-A1-data-{year}.csv", sb);
    }

    // ============================================================ your own yearly income and tax statement (ESS-002)
    /// <summary>What each company paid you in the year and the PPh 21 it withheld, from locked pay runs only. It is the data behind form 1721-A1; the official form itself is issued through the tax office's e-Bupot.</summary>
    public async Task<AnnualTaxStatement> MyAnnualTaxAsync(int year, SessionPrincipal actor, CancellationToken ct = default)
    {
        var emp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == actor.UserId, ct) ?? throw DomainException.Invariant("Your account is not linked to an employee record. Ask HR to link it.");
        var rows = await (from i in db.PayRunItems.AsNoTracking() join p in db.PayRuns.AsNoTracking() on i.PayRunId equals p.Id
                          where i.EmployeeId == emp.Id && p.Year == year && (p.Status == PayRunStatus.Locked || p.Status == PayRunStatus.Paid)
                          select new { p.LegalEntityId, p.Month, i.Ptkp, i.TaxableGross, Pension = i.JhtEmployee + i.JpEmployee, i.Tax }).ToListAsync(ct);
        var companies = await db.LegalEntities.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        return new AnnualTaxStatement(year, emp.EmployeeNo, emp.FullName, emp.TaxIdEnc != null, [.. rows.GroupBy(r => r.LegalEntityId).OrderBy(g => g.Min(x => x.Month)).Select(g =>
        {
            var c = companies.GetValueOrDefault(g.Key); var ordered = g.OrderBy(x => x.Month).ToList();
            return new AnnualTaxCompany(c?.Name ?? "", c?.TaxId, ordered[^1].Ptkp, ordered.Count, ordered.Sum(x => x.TaxableGross), ordered.Sum(x => x.Pension), ordered.Sum(x => x.Tax),
                [.. ordered.Select(x => new AnnualTaxMonth(x.Month, x.TaxableGross, x.Pension, x.Tax))]);
        })]);
    }

    public async Task<ExportFile> MyAnnualTaxPdfAsync(int year, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var s = await MyAnnualTaxAsync(year, actor, ct);
        if (s.Companies.Count == 0) throw DomainException.NotFound("Statement");
        var emp = await db.Employees.AsNoTracking().FirstAsync(e => e.UserId == actor.UserId, ct);
        var f = (decimal d) => d.ToString("#,##0", CultureInfo.InvariantCulture);
        var pdf = new SimplePdf(); double y = 60;
        pdf.Text($"Income and PPh 21 statement {year}", 50, y, 15, true); y += 22;
        pdf.Text($"{s.EmployeeName} ({s.EmployeeNo})", 50, y, 11); y += 15;
        pdf.Text($"NPWP: {(Open(emp, "tax_id", x => x.TaxIdEnc) is { Length: > 0 } npwp ? npwp : "not on file")}", 50, y, 10); y += 20;
        foreach (var c in s.Companies)
        {
            pdf.Line(50, y, 545); y += 16; pdf.Text(c.Company, 50, y, 12, true); y += 14;
            pdf.Text($"Company NPWP: {(string.IsNullOrEmpty(c.CompanyTaxId) ? "-" : c.CompanyTaxId)}   Tax status: {c.TaxStatus}   Months paid: {c.Months}", 50, y, 9); y += 18;
            pdf.Text("Month", 50, y, 9, true); pdf.Right("Taxable income", 300, y, 9); pdf.Right("Pension contributions", 430, y, 9); pdf.Right("PPh 21 withheld", 545, y, 9); y += 13;
            foreach (var m in c.ByMonth) { pdf.Text(new DateOnly(year, m.Month, 1).ToString("MMMM", CultureInfo.InvariantCulture), 50, y, 9); pdf.Right(f(m.TaxableIncome), 300, y, 9); pdf.Right(f(m.Pension), 430, y, 9); pdf.Right(f(m.Tax), 545, y, 9); y += 12; }
            pdf.Line(50, y - 3, 545); y += 4; pdf.Text("Total (IDR)", 50, y, 10, true); pdf.Right(f(c.TaxableIncome), 300, y, 10); pdf.Right(f(c.Pension), 430, y, 10); pdf.Right(f(c.Tax), 545, y, 10); y += 24;
        }
        pdf.Text("Taxable income includes the health, work-accident and death premiums the company pays for you. This is a summary of locked pay runs for your records;", 50, y, 8); y += 10;
        pdf.Text("the official withholding slip (form 1721-A1) is issued by the company through the tax office's e-Bupot.", 50, y, 8);
        pdf.Text($"Generated {clock.UtcNow:yyyy-MM-dd HH:mm} UTC. Confidential.", 50, 800, 8);
        audit.Record("hr.payroll.annual_statement", "employee", emp.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["year"] = year.ToString() });
        await db.SaveChangesAsync(ct);
        return new ExportFile($"income-tax-statement-{year}.pdf", "application/pdf", pdf.Build());
    }

    // ============================================================ payslip PDF (your own)
    public async Task<ExportFile> PayslipPdfAsync(Guid itemId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var s = await payroll.MyPayslipAsync(itemId, actor, client, ct); var i = s.Item;
        var company = await (from p in db.PayRuns.AsNoTracking() join c in db.LegalEntities.AsNoTracking() on p.LegalEntityId equals c.Id where p.Id == s.PayRunId select new { c.Name, c.TaxId }).FirstOrDefaultAsync(ct);
        var tenant = company?.Name ?? await db.Tenants.AsNoTracking().Select(t => t.Name).FirstOrDefaultAsync(ct) ?? "";
        var f = (decimal d) => d.ToString("#,##0", CultureInfo.InvariantCulture);
        var pdf = new SimplePdf(); double y = 60;
        pdf.Text(string.IsNullOrWhiteSpace(tenant) ? "Payslip" : tenant, 50, y, 15, true); y += 22;
        if (company?.TaxId is { Length: > 0 } npwpDigits) { pdf.Text($"NPWP {npwpDigits}", 50, y - 6, 8); y += 8; }
        pdf.Text($"Payslip {new DateOnly(s.Year, s.Month, 1):MMMM yyyy}", 50, y, 12, true); y += 20;
        pdf.Text($"{i.EmployeeName} ({i.EmployeeNo})   Tax status: {i.Ptkp}", 50, y, 10); y += 14; pdf.Line(50, y, 545); y += 18;
        void Row(string label, decimal amount, bool bold = false) { pdf.Text(label, 50, y, 10, bold); pdf.Right(f(amount), 545, y, 10); y += 15; }
        pdf.Text("Earnings", 50, y, 11, true); y += 16;
        if (i.DailyRate is { } dr) { Row($"Daily wage ({i.DaysPaid:0.##} days x {f(dr)})", i.Basic); if (i.Allowance > 0) Row("Daily allowance", i.Allowance); }
        else { Row("Basic salary", i.Basic * i.ProrateFactor); Row("Fixed allowance", i.Allowance * i.ProrateFactor); }
        if (i.UnpaidDeduction > 0) Row("Unpaid leave / absence", -i.UnpaidDeduction);
        if (i.OvertimePay > 0) Row("Overtime", i.OvertimePay);
        foreach (var l in s.Lines.Where(x => x.Kind == "earning")) Row(l.Label, l.Amount);
        pdf.Line(50, y - 4, 545); Row("Gross", i.Gross, true); y += 8;
        pdf.Text("Deductions", 50, y, 11, true); y += 16;
        Row("BPJS Kesehatan", -i.KesEmployee); Row("BPJS JHT", -i.JhtEmployee); Row("BPJS JP", -i.JpEmployee); Row($"PPh 21 ({i.TaxMethod})", -i.Tax);
        foreach (var l in s.Lines.Where(x => x.Kind == "deduction")) Row(l.Label, -l.Amount);
        pdf.Line(50, y - 4, 545, 1); y += 2; Row("Take-home pay (IDR)", i.Net, true); y += 14;
        pdf.Text($"Employer contributions (not deducted from you): BPJS Kesehatan {f(i.KesEmployer)}, JHT {f(i.JhtEmployer)}, JP {f(i.JpEmployer)}, JKK {f(i.JkkEmployer)}, JKM {f(i.JkmEmployer)}.", 50, y, 8);
        var seal = await seals.IssueAsync("payslip", itemId, tenant, $"{s.Year}-{s.Month:D2}", DocumentSealService.Mask(i.EmployeeName), ct); await db.SaveChangesAsync(ct);
        pdf.Text($"Verification code: {DocumentSealService.Format(seal)}  -  check it at {links.Origin.TrimEnd('/')}/verify", 50, 788, 8);
        pdf.Text($"Generated {clock.UtcNow:yyyy-MM-dd HH:mm} UTC. This payslip is confidential.", 50, 800, 8);
        return new ExportFile($"payslip-{s.Year}-{s.Month:D2}.pdf", "application/pdf", pdf.Build());
    }
}
