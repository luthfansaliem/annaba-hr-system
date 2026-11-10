namespace AnnabaDigital.Domain.Entities;

public enum PayRunStatus { Draft, Calculated, Approved, Locked, Paid }
public enum PayLineKind { Earning, Deduction }

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
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
