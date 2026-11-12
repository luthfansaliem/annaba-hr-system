namespace AnnabaDigital.Domain.Entities;

public enum PayRunStatus { Draft, Calculated, Approved, Locked, Paid }

/// <summary>Monthly: a fixed monthly salary. Daily: a rate per day actually worked (attendance plus paid leave), paid in the same monthly run.</summary>
public enum PayBasis { Monthly, Daily }
public enum TaxScheme { MonthlyTer, DailyTer }
/// <summary>
/// Who bears the income tax. Gross: the employee (withheld from pay). GrossUp: the company pays a taxable "PPh 21 allowance" equal to the tax, so take-home pay is as if untaxed
/// but taxable income rises by the allowance. Nett: the company pays the tax itself without adding it to the employee's income.
/// </summary>
public enum TaxTreatment { Gross, GrossUp, Nett }
public enum PayLineKind { Earning, Deduction }
public enum LoanStatus { Active, PaidOff, Cancelled }

/// <summary>
/// What an employee is paid, effective-dated like the job data. Amounts are encrypted at rest (AES-GCM, bound to the row and column); they are only decrypted
/// to calculate a run or to show them to someone holding payroll.read who re-entered their password recently.
/// </summary>
public class EmployeeCompensation : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    /// <summary>The amount in <see cref="BasicEnc"/> is per month (Monthly) or per day worked (Daily); the allowance follows the same basis.</summary>
    public PayBasis PayBasis { get; set; } = PayBasis.Monthly;
    /// <summary>Daily-wage workers only: how PPh 21 is withheld. MonthlyTer when they are paid once a month, DailyTer when they are paid per day or week (weekly payouts).</summary>
    public TaxScheme TaxScheme { get; set; } = TaxScheme.MonthlyTer;
    public string BasicEnc { get; set; } = "";
    public string AllowanceEnc { get; set; } = "";
    /// <summary>TK0…TK3, K0…K3 (marital status and dependants for the tax-free allowance).</summary>
    public string PtkpStatus { get; set; } = "TK0";
    public bool HasNpwp { get; set; } = true;
    public TaxTreatment TaxTreatment { get; set; } = TaxTreatment.Gross;
    /// <summary>A foreign tax subject (in Indonesia under 183 days in 12 months, or not intending to stay): PPh 26 at a flat rate on gross pay instead of PPh 21, no PTKP, no year-end recalculation.</summary>
    public bool NonResident { get; set; }
    /// <summary>Non-residents only: the reduced PPh 26 rate under a tax treaty (needs the DGT form on file). Null = the rules' standard rate.</summary>
    public decimal? TreatyRate { get; set; }
    public bool KesEnrolled { get; set; } = true;
    public bool TkEnrolled { get; set; } = true;
    public decimal JkkRate { get; set; } = 0.0024m;
    /// <summary>Year-to-date figures from the previous system, used only for <see cref="OpeningYear"/> so the first year-end true-up is right after a mid-year start.</summary>
    public int? OpeningYear { get; set; }
    public int OpeningMonths { get; set; }
    public string? OpeningEnc { get; set; }
    public string? ChangeReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

public class PayRun : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>One run per legal entity per month: the run pays only that entity's employees.</summary>
    public Guid LegalEntityId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public PayRunStatus Status { get; set; } = PayRunStatus.Draft;
    public string RulesVersion { get; set; } = "";
    /// <summary>The exact rules used, so the run can be explained and reproduced later.</summary>
    public string RulesJson { get; set; } = "{}";
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CalculatedBy { get; set; }
    public DateTimeOffset? CalculatedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? LockedBy { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? Note { get; set; }
    /// <summary>When set, days someone was expected to work but has no attendance and no approved leave are deducted (needs the month's attendance to be closed first).</summary>
    public bool DeductAbsence { get; set; }
    public int EmployeeCount { get; set; }
    public decimal TotalGross { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalNet { get; set; }
    public decimal TotalEmployerCost { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>One employee's pay for the month: a snapshot, never recalculated once the run is locked (a database trigger refuses changes then).</summary>
public class PayRunItem : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PayRunId { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeNo { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string Ptkp { get; set; } = "TK0";
    public decimal Basic { get; set; }
    public decimal Allowance { get; set; }
    /// <summary>Daily-wage workers only: the rate per day and the days paid. Basic and Allowance then hold the totals earned (rate × days).</summary>
    public decimal? DailyRate { get; set; }
    public decimal? DaysPaid { get; set; }
    public decimal ProrateFactor { get; set; } = 1;
    public decimal UnpaidDeduction { get; set; }
    public decimal TaxableExtras { get; set; }
    public decimal NonTaxableExtras { get; set; }
    public decimal OtherDeductions { get; set; }
    /// <summary>Approved overtime for the month, paid per PP 35/2021 (see <c>PayrollCalculator.OvertimePay</c>). Included in gross and taxed as ordinary income.</summary>
    public decimal OvertimePay { get; set; }
    public decimal Gross { get; set; }
    public decimal TaxableGross { get; set; }
    public decimal KesEmployee { get; set; }
    public decimal JhtEmployee { get; set; }
    public decimal JpEmployee { get; set; }
    public decimal KesEmployer { get; set; }
    public decimal JhtEmployer { get; set; }
    public decimal JpEmployer { get; set; }
    public decimal JkkEmployer { get; set; }
    public decimal JkmEmployer { get; set; }
    /// <summary>Income tax withheld and paid to the state for this month: PPh 21, or PPh 26 when <see cref="TaxMethod"/> is PPH26. Negative = refunded at the year-end recalculation.</summary>
    public decimal Tax { get; set; }
    public string TaxMethod { get; set; } = "TER";
    public TaxTreatment TaxTreatment { get; set; } = TaxTreatment.Gross;
    /// <summary>GrossUp only: the PPh allowance paid on top (equal to <see cref="Tax"/>), already included in Gross and TaxableGross.</summary>
    public decimal TaxAllowance { get; set; }
    /// <summary>Nett only: the tax the company pays itself (equal to <see cref="Tax"/>); not deducted from the employee and included in EmployerCost.</summary>
    public decimal TaxBorne { get; set; }
    public decimal Net { get; set; }
    public decimal EmployerCost { get; set; }
    public string? Warnings { get; set; }
}

/// <summary>An extra earning (bonus, THR, reimbursement) or deduction (loan instalment, penalty) entered for one employee in a run.</summary>
public class PayRunLine : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PayRunId { get; set; }
    public Guid EmployeeId { get; set; }
    public PayLineKind Kind { get; set; }
    public string Label { get; set; } = "";
    public decimal Amount { get; set; }
    /// <summary>Earnings only: whether the amount counts as taxable income (a bonus does, a travel reimbursement does not).</summary>
    public bool Taxable { get; set; } = true;
    /// <summary>Generated by the system (a loan instalment): rebuilt on every calculation and not editable by hand.</summary>
    public bool Auto { get; set; }
    public Guid? LoanId { get; set; }
    public Guid? RetroId { get; set; }
    /// <summary>The approved expense claim this line reimburses.</summary>
    public Guid? ClaimId { get; set; }
    /// <summary>The weekly payment this line deducts.</summary>
    public Guid? PayoutItemId { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>An advance or loan repaid through payroll. Each calculation adds one deduction line per active loan; locking the run reduces the balance.</summary>
public enum PayoutStatus { Draft, Calculated, Approved, Paid }

/// <summary>One week's interim pay for the daily-wage workers of one company. The monthly pay run later deducts what was paid here (see WeeklyPayoutItem.SettledRunId).</summary>
public class WeeklyPayout : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid LegalEntityId { get; set; }
    /// <summary>Always a Monday; the week ends on the Sunday after.</summary>
    public DateOnly WeekStart { get; set; }
    public DateOnly WeekEnd { get; set; }
    /// <summary>Share of the week's wage kept back for tax and BPJS, which are settled in the monthly run.</summary>
    public decimal HoldbackPct { get; set; }
    public PayoutStatus Status { get; set; } = PayoutStatus.Draft;
    public int EmployeeCount { get; set; }
    public decimal TotalGross { get; set; }
    public decimal TotalNet { get; set; }
    public string? Note { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CalculatedBy { get; set; }
    public DateTimeOffset? CalculatedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public int Version { get; set; } = 1;
}

public class WeeklyPayoutItem : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PayoutId { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeNo { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public decimal DailyRate { get; set; }
    public decimal Days { get; set; }
    public decimal Gross { get; set; }
    public decimal Holdback { get; set; }
    public decimal Net { get; set; }
    /// <summary>The locked monthly pay run that deducted this payment. Null = still to be settled.</summary>
    public Guid? SettledRunId { get; set; }
}

/// <summary>A pay component that repeats every month for one person (an allowance, a fee) from <see cref="From"/> to <see cref="Through"/> (first days of months), until ended.</summary>
public class RecurringPayItem : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public PayLineKind Kind { get; set; }
    public string Label { get; set; } = "";
    public decimal Amount { get; set; }
    public bool Taxable { get; set; } = true;
    public DateOnly From { get; set; }
    public DateOnly? Through { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

public class Loan : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public decimal Principal { get; set; }
    public decimal Installment { get; set; }
    public decimal Remaining { get; set; }
    public string? Note { get; set; }
    public LoanStatus Status { get; set; } = LoanStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

/// <summary>
/// Back pay (or back deduction) after a salary change dated inside months that were already paid. The difference is worked out from what those locked runs actually paid, waits here, and is added
/// to the next pay run as a system line; locking that run marks it applied. Basic and fixed allowance only: BPJS contributions on the difference are not re-opened.
/// </summary>
public class RetroAdjustment : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public int FromYear { get; set; }
    public int FromMonth { get; set; }
    public int ThroughYear { get; set; }
    public int ThroughMonth { get; set; }
    /// <summary>Signed: positive = the employee is owed money, negative = the employee was overpaid.</summary>
    public decimal Amount { get; set; }
    public string Note { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? AppliedRunId { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
}

/// <summary>
/// One complete, immutable set of payroll rules (rates, caps, tax-free allowances, TER tables) that applies from the first day of <see cref="EffectiveFrom"/>'s month. Corrections to the law's numbers are new versions, never edits,
/// so every past pay run can still be explained by the version it used.
/// </summary>
public class PayrollRuleVersion : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Label { get; set; } = "";
    public DateOnly EffectiveFrom { get; set; }
    public string RulesJson { get; set; } = "{}";
    public string Reason { get; set; } = "";
    public string Changes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
