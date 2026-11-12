namespace AnnabaDigital.Domain.Entities;

public enum EmployeeStatus { Active, Terminated }
public enum EmploymentType { Permanent, Contract, Probation, Intern }
public enum Gender { Female, Male }
public enum MaritalStatus { Single, Married, Divorced, Widowed }
/// <summary>Only used to plan the religious holiday allowance (THR) date; optional.</summary>
public enum Religion { Islam, Protestant, Catholic, Hindu, Buddhist, Confucian, Other }

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
/// <summary>A message for one person inside the app (leave decided, payslip ready…). Kept for 90 days once read.</summary>
public class UserNotification : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    /// <summary>Machine-readable kind, e.g. leave.decided, payslip.ready.</summary>
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    /// <summary>In-app path to open, always starting with "/".</summary>
    public string? Link { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>Which kinds of messages a person has switched off, per channel (comma-separated category keys). No row = everything on.</summary>
public class NotificationPreference : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid UserId { get; set; }
    public string InAppOff { get; set; } = "";
    public string EmailOff { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum ClaimStatus { Pending, Approved, Rejected, Cancelled, Paid }

/// <summary>Money an employee spent for work and asks back. Approved claims are paid through the next pay run as a non-taxable line; locking that run marks them paid.</summary>
public class ExpenseClaim : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public string Category { get; set; } = "other";
    public DateOnly ExpenseDate { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
    public string? ReceiptName { get; set; }
    public string? ReceiptType { get; set; }
    public byte[]? Receipt { get; set; }
    public ClaimStatus Status { get; set; } = ClaimStatus.Pending;
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public Guid? PaidRunId { get; set; }
    /// <summary>Paid as taxable income (a benefit in cash under a taxable claim policy) instead of a tax-free reimbursement.</summary>
    public bool Taxable { get; set; }
    public string? ApprovalChain { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>A request for proof of employment; once approved it has a number and a frozen copy of the facts printed on the letter.</summary>
public class EmploymentLetter : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public string Purpose { get; set; } = "";
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public string? Number { get; set; }
    public string? SnapshotJson { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A job change entered ahead of time (promotion next month, transfer on the 1st). It becomes a normal effective-dated job record on its date.</summary>
public class ScheduledEmploymentChange : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    /// <summary>The requested change exactly as entered (only the fields that change).</summary>
    public string RequestJson { get; set; } = "{}";
    public string Summary { get; set; } = "";
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? Note { get; set; }
}

/// <summary>Pictures live in their own table so that lists of employees and attendance never drag the bytes along. Kind "employee": the portrait on the record (one per employee).
/// Kind "clock_in": an optional picture taken at clock-in, removed automatically after 90 days.</summary>
public class HrPhoto : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Kind { get; set; } = "employee";
    /// <summary>The employee id (kind employee) or the attendance log id (kind clock_in).</summary>
    public Guid OwnerId { get; set; }
    public Guid EmployeeId { get; set; }
    public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>"Remind me of this report every week/month": an e-mail with a link, sent to the person who asked.</summary>
public class ReportSchedule : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string Report { get; set; } = "";
    /// <summary>"weekly" (Day 1 = Monday … 7 = Sunday) or "monthly" (Day 1–28).</summary>
    public string Frequency { get; set; } = "weekly";
    public int Day { get; set; }
    public DateOnly? LastSentOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Picks the approval chain for a kind of request from a threshold upwards. Steps: comma-separated, a subsequence of manager,manager2,hr.</summary>
public class ApprovalRule : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Kind { get; set; } = "leave";
    public Guid? LeaveTypeId { get; set; }
    /// <summary>Days (leave) or rupiah (claim) from which this rule applies.</summary>
    public decimal MinAmount { get; set; }
    public string Steps { get; set; } = "manager";
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One approval given on the way through a chain.</summary>
public class ApprovalStep : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Kind { get; set; } = "";
    public Guid RequestId { get; set; }
    public int StepNo { get; set; }
    public string Approver { get; set; } = "";
    public Guid DecidedBy { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
}

/// <summary>The verification code of an issued document (employment letter, payslip) and the few facts an outsider may compare.</summary>
public class DocumentSeal : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Kind { get; set; } = "";
    public Guid RefId { get; set; }
    public string Code { get; set; } = "";
    public string Company { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Holder { get; set; } = "";
    public DateTimeOffset IssuedAt { get; set; }
}

/// <summary>A company (with its own tax number) that employs people and runs its own payroll. A tenant has one default entity; pay runs, BPJS reports, journals and the annual tax file are per entity.</summary>
public class LegalEntity : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    /// <summary>Company NPWP, digits only (15 or 16). A company number is not personal data, so it is stored as is.</summary>
    public string? TaxId { get; set; }
    public string? Address { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class Employee : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>The company that employs this person and pays the salary.</summary>
    public Guid LegalEntityId { get; set; }
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
    /// <summary>BPJS Kesehatan and BPJS Ketenagakerjaan membership numbers (encrypted like the other identifiers); they go on the monthly BPJS report.</summary>
    public string? BpjsKesNoEnc { get; set; }
    public string? BpjsTkNoEnc { get; set; }
    public Religion? Religion { get; set; }
    public string? BankName { get; set; }
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;
    public DateOnly HireDate { get; set; }
    public DateOnly? TerminationDate { get; set; }
    public string? TerminationReason { get; set; }
    /// <summary>Optional link to the sign-in account (one account per employee).</summary>
    public Guid? UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Set when the personal data was erased after the retention period (or on a granted erasure request). Payroll figures stay; identity does not.</summary>
    public DateTimeOffset? AnonymizedAt { get; set; }
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
public enum LedgerKind { Grant, Used, Adjust, Restore, CarryOver, Expire }

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
    /// <summary>Carried-over days that are not taken by the end of this month (1–12) of the new year expire. Null = they never expire.</summary>
    public int? CarryOverExpiryMonth { get; set; }
    /// <summary>When carried-over days expire, pay them out through the next pay run (1/25 of the monthly wage per day) instead of just removing them.</summary>
    public bool CashOutExpired { get; set; }
    /// <summary>Second approval step: a request of at least this many days also needs HR after the manager (0 = every request, null = the manager alone decides).</summary>
    public decimal? HrApprovalFromDays { get; set; }
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
    /// <summary>Decided when the request is made (a later change to the leave type does not move requests already on their way).</summary>
    public bool NeedsHrApproval { get; set; }
    public Guid? ManagerApprovedBy { get; set; }
    public DateTimeOffset? ManagerApprovedAt { get; set; }
    /// <summary>The approval chain fixed when the request was made (e.g. "manager,manager2,hr"); null = the built-in default.</summary>
    public string? ApprovalChain { get; set; }
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
    /// <summary>"clock" for a real clock-in, "correction" when an approved correction created it.</summary>
    public string Source { get; set; } = "clock";
    public WorkMode WorkMode { get; set; } = WorkMode.Office;
    /// <summary>Where the browser said the person was when clocking in (only when they shared it), rounded to about a metre.</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    /// <summary>An office clock-in that could not be placed inside any work location (too far, or no location shared) while locations are defined. A flag for HR, not a refusal.</summary>
    public bool OutsideZone { get; set; }
}

/// <summary>Office: at a company site. Remote: working from home. Field: at a client or on a business trip.</summary>
public enum WorkMode { Office, Remote, Field }

/// <summary>A company site with a radius; office clock-ins outside every site are flagged (ATT-002).</summary>
public class WorkLocation : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int RadiusMeters { get; set; } = 200;
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum ChecklistKind { Onboarding, Offboarding }
public enum RequestStatus { Pending, Approved, Rejected, Cancelled }

/// <summary>One item on an employee's onboarding or offboarding checklist.</summary>
public class EmployeeTask : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public ChecklistKind Kind { get; set; }
    public string Title { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public Guid? DoneBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A file kept on an employee's record (contract, ID card, certificate…). Stored inside the database so backups and access rules cover it; the type is decided by the file's own bytes, never by the name or the browser's claim.</summary>
public class EmployeeDocument : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public string Category { get; set; } = "other";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public int SizeBytes { get; set; }
    public byte[] Content { get; set; } = [];
    public DateOnly? ExpiresOn { get; set; }
    public Guid? UploadedBy { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    /// <summary>1 for a first upload; a replacement gets the next number and the older file points to it through <see cref="ReplacedById"/> (kept, but out of the main list).</summary>
    public int Version { get; set; } = 1;
    public Guid? ReplacedById { get; set; }
    /// <summary>"clean" when a virus scanner checked it, "not_scanned" when none is configured.</summary>
    public string ScanStatus { get; set; } = "not_scanned";
}

/// <summary>"I forgot to clock in/out": a corrected work session for one day, applied only once approved.</summary>
public class AttendanceCorrection : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly ClockIn { get; set; }
    public TimeOnly ClockOut { get; set; }
    public string Reason { get; set; } = "";
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>A month whose attendance is closed for payroll: nothing in it changes until HR reopens it (with a reason, audited).</summary>
public class AttendancePeriod : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTimeOffset ClosedAt { get; set; }
    public Guid ClosedBy { get; set; }
}

public class OvertimeRequest : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Hours { get; set; }
    public string Reason { get; set; } = "";
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>Remembers that a reminder was already sent, so the daily job never mails the same thing twice (unique per employee, contract end and threshold).</summary>
public class HrReminder : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public string Kind { get; set; } = "contract_end";
    public DateOnly DueOn { get; set; }
    public int ThresholdDays { get; set; }
    public DateTimeOffset SentAt { get; set; }
}

/// <summary>While a manager is away, someone else (who already holds an approval role) may decide for the manager's direct reports. Time-boxed, revocable, audited.</summary>
public class ApprovalDelegation : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid DelegatorEmployeeId { get; set; }
    public Guid DelegateEmployeeId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>A working pattern: start/end time, how many minutes of grace before "late", and which weekdays are working days (bit 0 = Monday … bit 6 = Sunday; 31 = Monday–Friday). An end time earlier than the start time means the shift runs past midnight (e.g. 22:00–06:00); the working day is the day it starts, and lateness is measured against the start.</summary>
public class Shift : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int GraceMinutes { get; set; } = 10;
    public int WorkDays { get; set; } = 31;
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Which shift an employee works from when. One open period per employee; people with none follow the default (09:00–17:00, grace 10, Monday–Friday).</summary>
public class ShiftAssignment : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public Guid ShiftId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

/// <summary>An employee's request to change something that affects pay or identity (marital status, bank account, tax/national ID, name). HR approves before it takes effect; the new value is stored encrypted.</summary>
public class EmployeeChangeRequest : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public string Field { get; set; } = "";
    public string ValueEnc { get; set; } = "";
    public string? Reason { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A company announcement or policy. With <see cref="RequiresAck"/> every person in the audience must confirm they read it; HR can see who has and who has not.</summary>
public class Announcement : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>Null = everyone; otherwise only people currently in that department.</summary>
    public Guid? OrgUnitId { get; set; }
    public bool RequiresAck { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

public class AnnouncementReceipt : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AnnouncementId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTimeOffset ReadAt { get; set; }
    public DateTimeOffset? AckedAt { get; set; }
}

public enum DeliveryStatus { Pending, Delivered, Dead }

/// <summary>An external system that wants to be told when something happens in HR. Only https URLs to public addresses; the signing secret is encrypted and shown once at creation.</summary>
public class WebhookEndpoint : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Url { get; set; } = "";
    public string SecretEnc { get; set; } = "";
    /// <summary>Comma-separated event names (e.g. "employee.created,leave.approved").</summary>
    public string Events { get; set; } = "";
    public bool Active { get; set; } = true;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastDeliveredAt { get; set; }
}

/// <summary>One event to one endpoint, with its retry state. Payloads carry ids and codes, never salaries or personal details: the receiver fetches what it needs through the API.</summary>
public class WebhookDelivery : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EndpointId { get; set; }
    public string Event { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public int? LastHttpStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}
