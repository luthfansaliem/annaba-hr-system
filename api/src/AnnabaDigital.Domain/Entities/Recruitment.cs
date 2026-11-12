namespace AnnabaDigital.Domain.Entities;

/// <summary>Manpower planning: how many people of a job a department plans to employ in a year, and the monthly salary budget for them.</summary>
public class ManpowerPlan : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public int Year { get; set; }
    public Guid? OrgUnitId { get; set; }
    public string JobTitle { get; set; } = "";
    public int Headcount { get; set; }
    public decimal MonthlyBudget { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum RequisitionStatus { Pending, Approved, Rejected, Cancelled, Filled }

/// <summary>A request to hire (new position or replacement), decided by HR. Postings are opened from an approved request; each hire counts towards <see cref="Openings"/>.</summary>
public class JobRequisition : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Number { get; set; } = "";
    public Guid? OrgUnitId { get; set; }
    public string JobTitle { get; set; } = "";
    public int Openings { get; set; } = 1;
    public EmploymentType Type { get; set; } = EmploymentType.Permanent;
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public bool Replacement { get; set; }
    public string Justification { get; set; } = "";
    public Guid RequestedBy { get; set; }
    public RequisitionStatus Status { get; set; } = RequisitionStatus.Pending;
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public int Hired { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public enum PostingStatus { Draft, Open, Closed }

/// <summary>A job advertised on the public career page (when open). <see cref="Stages"/> are the pipeline steps between "applied" and "hired", in order.</summary>
public class JobPosting : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Random, URL-safe id used in the public link (does not reveal how many postings exist).</summary>
    public string PublicId { get; set; } = "";
    public Guid? RequisitionId { get; set; }
    public string Title { get; set; } = "";
    public Guid? OrgUnitId { get; set; }
    public string? Location { get; set; }
    public EmploymentType Type { get; set; } = EmploymentType.Permanent;
    public string Description { get; set; } = "";
    public string? Requirements { get; set; }
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public bool ShowSalary { get; set; }
    public string Stages { get; set; } = "screening,assessment,interview,offer";
    public PostingStatus Status { get; set; } = PostingStatus.Draft;
    public DateOnly? ClosesOn { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>A person who applied (or was added by HR). One record per email address; the CV is kept with the candidate.</summary>
public class Candidate : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string Source { get; set; } = "career_page";
    public byte[]? Cv { get; set; }
    public string? CvName { get; set; }
    /// <summary>When the person agreed that their data is processed for recruitment (UU PDP).</summary>
    public DateTimeOffset? ConsentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum ApplicationStatus { Active, Rejected, Withdrawn, Hired }

public class JobApplication : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PostingId { get; set; }
    public Guid CandidateId { get; set; }
    /// <summary>"applied", one of the posting's stages, or "hired".</summary>
    public string Stage { get; set; } = "applied";
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Active;
    public string? CoverLetter { get; set; }
    public int? Rating { get; set; }
    public string? RejectReason { get; set; }
    public Guid? HiredEmployeeId { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>The timeline of an application: stage moves, notes, interviews, tests, offers.</summary>
public class ApplicationActivity : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ApplicationId { get; set; }
    public string Kind { get; set; } = "note";
    public string Text { get; set; } = "";
    public Guid? ByUserId { get; set; }
    public DateTimeOffset At { get; set; }
}

public enum InterviewStatus { Scheduled, Done, Cancelled }

public class Interview : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ApplicationId { get; set; }
    public DateTimeOffset At { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public string? Location { get; set; }
    /// <summary>Comma-separated employee ids of the interviewers.</summary>
    public string Interviewers { get; set; } = "";
    public InterviewStatus Status { get; set; } = InterviewStatus.Scheduled;
    public int? Rating { get; set; }
    public string? Recommendation { get; set; }
    public string? Feedback { get; set; }
    public Guid CreatedBy { get; set; }
}

public enum AssessmentKind { Test, Personality }

/// <summary>
/// A test sent to candidates. Test: multiple choice with one correct option per question, scored in percent. Personality: each question offers two statements that lean to one pole
/// of a dimension (E/I, S/N, T/F, J/P); the result is the four leanings.
/// </summary>
public class AssessmentTemplate : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public AssessmentKind Kind { get; set; }
    /// <summary>JSON array of { id, text, options: [{ text, correct?, pole? }] }.</summary>
    public string QuestionsJson { get; set; } = "[]";
    public int? PassScore { get; set; }
    public int TimeLimitMinutes { get; set; }
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum InviteStatus { Sent, Done, Cancelled }

/// <summary>A test sent to one applicant: they open a private link (the token) and answer once before it expires.</summary>
public class AssessmentInvite : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ApplicationId { get; set; }
    public Guid TemplateId { get; set; }
    public string TokenHash { get; set; } = "";
    public InviteStatus Status { get; set; } = InviteStatus.Sent;
    public int? Score { get; set; }
    public string? Result { get; set; }
    public string AnswersJson { get; set; } = "{}";
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
}

public enum OfferStatus { Draft, Sent, Accepted, Declined, Withdrawn }

/// <summary>A job offer. Sent as a private link; the candidate accepts or declines there. An accepted offer can be turned into an employee record.</summary>
public class JobOffer : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ApplicationId { get; set; }
    public string JobTitle { get; set; } = "";
    public decimal MonthlySalary { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly ExpiresOn { get; set; }
    public EmploymentType Type { get; set; } = EmploymentType.Permanent;
    public DateOnly? ContractEnd { get; set; }
    public string? Terms { get; set; }
    public OfferStatus Status { get; set; } = OfferStatus.Draft;
    public string? TokenHash { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
    public string? ResponseNote { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
