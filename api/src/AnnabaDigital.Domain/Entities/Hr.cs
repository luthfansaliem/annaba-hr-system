namespace AnnabaDigital.Domain.Entities;

public enum EmployeeStatus { Active, Terminated }
public enum EmploymentType { Permanent, Contract, Probation, Intern }
public enum Gender { Female, Male }
public enum MaritalStatus { Single, Married, Divorced, Widowed }

/// <summary>Department / division. Hierarchical (ParentId), unique code per tenant.</summary>
public class OrgUnit : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// The person as an employee. Sensitive identifiers are stored encrypted (AES-GCM, see <c>ISecretProtector</c>) in the
/// <c>*Enc</c> columns as "key-version.nonce.ciphertext" (base64) and are only decrypted for callers holding <c>employees.read_sensitive</c>.
/// Job data lives in <see cref="EmployeeEmployment"/> (effective-dated), never here.
/// </summary>
public class Employee : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string EmployeeNo { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? WorkEmail { get; set; }
    public string? Phone { get; set; }
    public DateOnly? BirthDate { get; set; }
    public Gender? Gender { get; set; }
    public MaritalStatus? MaritalStatus { get; set; }
    public string? Address { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? NationalIdEnc { get; set; }
    public string? TaxIdEnc { get; set; }
    public string? BankAccountEnc { get; set; }
    public string? BankName { get; set; }
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;
    public DateOnly HireDate { get; set; }
    public DateOnly? TerminationDate { get; set; }
    public string? TerminationReason { get; set; }
    /// <summary>Optional link to the sign-in account (one account per employee).</summary>
    public Guid? UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
    public List<EmployeeEmployment> Employments { get; set; } = [];
}

/// <summary>One period of job data. The open row (<see cref="EffectiveTo"/> null) is the current one; closed rows are history and are never edited.</summary>
public class EmployeeEmployment : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string JobTitle { get; set; } = "";
    public string? Grade { get; set; }
    public string? Location { get; set; }
    public EmploymentType Type { get; set; } = EmploymentType.Permanent;
    /// <summary>End of a fixed-term contract / probation (drives the renewal reminders).</summary>
    public DateOnly? ContractEnd { get; set; }
    public Guid? OrgUnitId { get; set; }
    public Guid? ManagerId { get; set; }
    public string? ChangeReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum LeaveStatus { Pending, Approved, Rejected, Cancelled }
public enum LedgerKind { Grant, Used, Adjust, Restore, CarryOver }

/// <summary>A kind of leave. <see cref="AnnualQuota"/> null = no balance (sick, unpaid, event leave): it is limited by <see cref="MaxDaysPerRequest"/> only.</summary>
public class LeaveType : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal? AnnualQuota { get; set; }
    /// <summary>How many unused days may move into the next year when the yearly grant is made.</summary>
    public decimal CarryOverMax { get; set; }
    public int? MaxDaysPerRequest { get; set; }
    public bool Paid { get; set; } = true;
    public bool AllowHalfDay { get; set; } = true;
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Public or collective ("cuti bersama") holiday. Not a working day.</summary>
public class Holiday : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Collective leave day (cuti bersama): a day off for everyone but decided by the government separately from the national holiday.</summary>
    public bool Collective { get; set; }
}

/// <summary>Append-only: a balance is the sum of its rows. Nothing is edited; a correction is a new row.</summary>
public class LeaveLedgerEntry : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public int Year { get; set; }
    public decimal Days { get; set; }
    public LedgerKind Kind { get; set; }
    public Guid? RequestId { get; set; }
    public string? Note { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class LeaveRequest : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool HalfDay { get; set; }
    /// <summary>Working days counted when the request was made (weekends and holidays excluded).</summary>
    public decimal Days { get; set; }
    public string? Reason { get; set; }
    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>One work session. Times come from the server clock only; a person has at most one open session.</summary>
public class AttendanceLog : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    /// <summary>Local (WIB) calendar day of the clock-in.</summary>
    public DateOnly Date { get; set; }
    public DateTimeOffset ClockIn { get; set; }
    public DateTimeOffset? ClockOut { get; set; }
    public string? Ip { get; set; }
}
