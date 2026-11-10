namespace AnnabaDigital.Domain.Entities;

public enum PayRunStatus { Draft, Calculated, Approved, Locked, Paid }
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
    public string BasicEnc { get; set; } = "";
    public string AllowanceEnc { get; set; } = "";
    /// <summary>TK0…TK3, K0…K3 (marital status and dependants for the tax-free allowance).</summary>
    public string PtkpStatus { get; set; } = "TK0";
    public bool HasNpwp { get; set; } = true;
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
    public decimal Tax { get; set; }
    public string TaxMethod { get; set; } = "TER";
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
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>An advance or loan repaid through payroll. Each calculation adds one deduction line per active loan; locking the run reduces the balance.</summary>
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
