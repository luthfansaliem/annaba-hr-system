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

public sealed record LoanView(Guid Id, Guid EmployeeId, string EmployeeNo, string EmployeeName, decimal Principal, decimal Installment, decimal Remaining, string Status, string? Note, DateTimeOffset CreatedAt);
public sealed record CreateLoanRequest(Guid? EmployeeId, decimal? Principal, decimal? Installment, string? Note);
public sealed record SeveranceRequest(Guid? EmployeeId, string? Reason, DateOnly? LastDay);
public sealed record SeveranceLine(string Label, string Kind, decimal Amount, bool Taxable, string Basis);
public sealed record SeverancePreview(Guid EmployeeId, string EmployeeName, string Reason, DateOnly LastDay, int Years, int Months, decimal Wage, IReadOnlyList<SeveranceLine> Lines, decimal NetTotal);

/// <summary>
/// PAY-006/010 and PP 35/2021 termination pay. Loans, the severance calculator (pesangon, UPMK, unused-leave pay, fixed-term compensation, with the final tax on pesangon/UPMK under PP 68/2009),
/// the BPJS and accounting-journal exports, the year-end income and tax summary, and the payslip as a PDF. All figures are baseline rules to be confirmed by a tax/labour consultant.
/// </summary>
public sealed class PayrollExtrasService(IAppDb db, IClock clock, PayrollService payroll, AuthorizationService authz, ISecretProtector protector, ShiftService shifts, AuditWriter audit)
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
        var sb = new StringBuilder("employee_no,name,kes_employee,kes_employer,jht_employee,jht_employer,jp_employee,jp_employer,jkk_employer,jkm_employer,total\r\n");
        foreach (var i in items)
            sb.Append(string.Join(',', new[] { i.EmployeeNo, i.EmployeeName, M(i.KesEmployee), M(i.KesEmployer), M(i.JhtEmployee), M(i.JhtEmployer), M(i.JpEmployee), M(i.JpEmployer), M(i.JkkEmployer), M(i.JkmEmployer),
                M(i.KesEmployee + i.KesEmployer + i.JhtEmployee + i.JhtEmployer + i.JpEmployee + i.JpEmployer + i.JkkEmployer + i.JkmEmployer) }.Select(EmployeeService.Csv))).Append("\r\n");
        audit.Record("hr.payroll.bpjs_report", "pay_run", runId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["rows"] = items.Count.ToString() });
        await db.SaveChangesAsync(ct);
        return Csv($"bpjs-{run.Year}-{run.Month:D2}.csv", sb);
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
        pdf.Text($"Generated {clock.UtcNow:yyyy-MM-dd HH:mm} UTC. This payslip is confidential.", 50, 800, 8);
        return new ExportFile($"payslip-{s.Year}-{s.Month:D2}.pdf", "application/pdf", pdf.Build());
    }
}
