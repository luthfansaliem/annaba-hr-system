namespace AnnabaDigital.Domain.Entities;

public enum GoalKind { Kpi, Objective, KeyResult }
public enum GoalStatus { OnTrack, AtRisk, OffTrack, Done, Cancelled }

/// <summary>
/// A target (KPI, OKR objective or key result) for the company (no owner, no unit), a department (unit) or one person (owner), optionally aligned to a parent goal
/// so that individual goals roll up to team and company goals. Measured from <see cref="StartValue"/> to <see cref="TargetValue"/>; progress = how far the current value has come.
/// </summary>
public class Goal : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? OwnerEmployeeId { get; set; }
    public Guid? OrgUnitId { get; set; }
    public Guid? ParentId { get; set; }
    public GoalKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Unit { get; set; }
    public decimal StartValue { get; set; }
    public decimal TargetValue { get; set; } = 100;
    public decimal CurrentValue { get; set; }
    /// <summary>Share of the owner's goals this one counts for in a review, in percent.</summary>
    public decimal Weight { get; set; } = 100;
    public int Year { get; set; }
    public DateOnly? DueDate { get; set; }
    public GoalStatus Status { get; set; } = GoalStatus.OnTrack;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>A progress update on a goal: the new value and what happened.</summary>
public class GoalCheckIn : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid GoalId { get; set; }
    public decimal Value { get; set; }
    public string? Note { get; set; }
    public Guid ByUserId { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>A skill or behaviour the organisation assesses (communication, leadership, a technical skill), with what each level 1–5 looks like.</summary>
public class Competency : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "core";
    public string? Description { get; set; }
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum ReviewCycleStatus { Draft, Open, Calibration, Closed }

/// <summary>A performance review round: who reviews whom (self, manager, peers), what is rated (goals, competencies) and how the two weigh in the final score.</summary>
public class ReviewCycle : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public DateOnly? DueDate { get; set; }
    public Guid? OrgUnitId { get; set; }
    public bool SelfReview { get; set; } = true;
    public bool ManagerReview { get; set; } = true;
    public bool PeerReview { get; set; }
    /// <summary>Weights of the goal score and the competency score in the final score; together 100.</summary>
    public decimal GoalsWeight { get; set; } = 60;
    public decimal CompetencyWeight { get; set; } = 40;
    /// <summary>Comma-separated ids of the competencies rated in this cycle.</summary>
    public string CompetencyIds { get; set; } = "";
    public ReviewCycleStatus Status { get; set; } = ReviewCycleStatus.Draft;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LaunchedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public int Version { get; set; } = 1;
}

public enum ReviewKind { Self, Manager, Peer }

/// <summary>One review to write: <see cref="ReviewerEmployeeId"/> reviews <see cref="EmployeeId"/> in a cycle. Answers hold ratings (1–5) per goal and competency and comments.</summary>
public class ReviewAssignment : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CycleId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid ReviewerEmployeeId { get; set; }
    public ReviewKind Kind { get; set; }
    public bool Submitted { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    /// <summary>JSON: { goals: { goalId: rating }, competencies: { competencyId: rating }, strengths, improvements, comment }.</summary>
    public string AnswersJson { get; set; } = "{}";
}

/// <summary>The outcome for one person in a cycle: computed scores, the rating HR settles on in calibration, the potential (for the 9-box) and whether the person may see it.</summary>
public class ReviewResult : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CycleId { get; set; }
    public Guid EmployeeId { get; set; }
    public decimal? GoalScore { get; set; }
    public decimal? CompetencyScore { get; set; }
    public decimal? FinalScore { get; set; }
    public int? CalibratedRating { get; set; }
    /// <summary>1 = low, 2 = medium, 3 = high potential.</summary>
    public int? Potential { get; set; }
    public string? CalibrationNote { get; set; }
    public bool Released { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum DevelopmentPlanStatus { Draft, Active, Done }

/// <summary>An individual development plan: what the person wants to grow in, and the agreed actions with due dates.</summary>
public class DevelopmentPlan : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public string Title { get; set; } = "";
    public string? Aim { get; set; }
    public Guid? CompetencyId { get; set; }
    /// <summary>JSON array of { id, text, due, done }.</summary>
    public string ActionsJson { get; set; } = "[]";
    public DevelopmentPlanStatus Status { get; set; } = DevelopmentPlanStatus.Active;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A key position and who could take it over (succession planning).</summary>
public class SuccessionPlan : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Position { get; set; } = "";
    public Guid? OrgUnitId { get; set; }
    public Guid? IncumbentEmployeeId { get; set; }
    public bool Critical { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum Readiness { ReadyNow, OneToTwoYears, ThreePlusYears }

public class SuccessionCandidate : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PlanId { get; set; }
    public Guid EmployeeId { get; set; }
    public Readiness Readiness { get; set; }
    public string? Note { get; set; }
}
