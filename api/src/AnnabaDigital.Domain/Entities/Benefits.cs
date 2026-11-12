namespace AnnabaDigital.Domain.Entities;

/// <summary>A pay band: the range a basic salary is expected to fall in for jobs of this grade. Policy, not personal data: no amounts of a person are stored here.</summary>
public class SalaryGrade : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal MinPay { get; set; }
    public decimal MidPay { get; set; }
    public decimal MaxPay { get; set; }
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum SalaryReviewStatus { Draft, Submitted, Approved, Applied, Cancelled }

/// <summary>
/// A batch of salary changes (annual increase, promotion round) prepared by one person and approved by another before it becomes new compensation periods on
/// <see cref="EffectiveFrom"/>. Proposed amounts are encrypted like compensation.
/// </summary>
public class SalaryReview : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public DateOnly EffectiveFrom { get; set; }
    public SalaryReviewStatus Status { get; set; } = SalaryReviewStatus.Draft;
    public string? Note { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? SubmittedBy { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public int Version { get; set; } = 1;
}

public class SalaryReviewItem : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ReviewId { get; set; }
    public Guid EmployeeId { get; set; }
    /// <summary>"basic|allowance" before and after, encrypted (bound to this row).</summary>
    public string CurrentEnc { get; set; } = "";
    public string ProposedEnc { get; set; } = "";
    public Guid? GradeId { get; set; }
    public string? Note { get; set; }
    /// <summary>Set when applying failed for this person (for example a locked month in the way); the others are still applied.</summary>
    public string? ApplyError { get; set; }
}

/// <summary>
/// The rule for one claim category (built-in like "medical", or a benefit of the organisation's own like "wellness"): how much one claim and one person per calendar year
/// may reach, and whether reimbursements count as taxable income (most benefits in cash do). <see cref="GradeId"/> null = everyone; a grade-specific policy wins over it.
/// </summary>
public class ClaimPolicy : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid? GradeId { get; set; }
    public decimal? AnnualLimit { get; set; }
    public decimal? PerClaimLimit { get; set; }
    public bool Taxable { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

public enum AdvanceStatus { Pending, Approved, Rejected, Cancelled, Disbursed, SettlementPending, SettlementApproved, Settled }

/// <summary>
/// Money handed to an employee before a work expense (a trip), accounted for afterwards: the employee reports what was actually spent, and the difference is settled
/// through the next pay run (paid out if more was spent, deducted if less). Locking that run closes the advance.
/// </summary>
public class CashAdvance : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public decimal Amount { get; set; }
    public string Purpose { get; set; } = "";
    public DateOnly? NeededBy { get; set; }
    public AdvanceStatus Status { get; set; } = AdvanceStatus.Pending;
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public Guid? DisbursedBy { get; set; }
    public DateOnly? DisbursedOn { get; set; }
    public decimal? SpentAmount { get; set; }
    public string? SettlementNote { get; set; }
    public string? ReceiptName { get; set; }
    public string? ReceiptType { get; set; }
    public byte[]? Receipt { get; set; }
    public DateTimeOffset? SettlementSubmittedAt { get; set; }
    public Guid? SettlementApprovedBy { get; set; }
    public Guid? SettledRunId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public enum WageAdvanceStatus { Pending, Approved, Rejected, Cancelled, Disbursed, Recovered }

/// <summary>
/// Earned wage access, paid by the company itself: part of the wage already earned this month, transferred early and deducted in that month's pay run.
/// </summary>
public class WageAdvance : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    public string? Reason { get; set; }
    public WageAdvanceStatus Status { get; set; } = WageAdvanceStatus.Pending;
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public DateOnly? DisbursedOn { get; set; }
    public Guid? RecoveredRunId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Version { get; set; } = 1;
}
