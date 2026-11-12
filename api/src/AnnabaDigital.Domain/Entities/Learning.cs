namespace AnnabaDigital.Domain.Entities;

public enum CourseStatus { Draft, Published, Archived }

/// <summary>
/// A course in the learning catalogue: lessons in order, an optional quiz with a pass mark, and a certificate on completion (valid for <see cref="ValidMonths"/> when it must be renewed,
/// e.g. occupational safety). Mandatory courses are assigned; others people may also take on their own.
/// </summary>
public class Course : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Category { get; set; } = "general";
    public bool Mandatory { get; set; }
    public bool SelfEnroll { get; set; } = true;
    /// <summary>JSON array of quiz questions { id, text, options: [{ text, correct }] }; empty = no quiz.</summary>
    public string QuizJson { get; set; } = "[]";
    public int PassScore { get; set; } = 70;
    public bool Certificate { get; set; } = true;
    public int? ValidMonths { get; set; }
    public CourseStatus Status { get; set; } = CourseStatus.Draft;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum LessonKind { Text, Video, Link, File }

public class CourseLesson : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CourseId { get; set; }
    public int Order { get; set; }
    public string Title { get; set; } = "";
    public LessonKind Kind { get; set; }
    public string? Body { get; set; }
    public string? Url { get; set; }
    public byte[]? File { get; set; }
    public string? FileName { get; set; }
    public int Minutes { get; set; }
}

public enum EnrollmentStatus { NotStarted, InProgress, Completed }

/// <summary>One person taking one course: assigned (with a due date) or self-enrolled; lessons done, quiz attempts and the certificate.</summary>
public class CourseEnrollment : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CourseId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? AssignedBy { get; set; }
    public DateOnly? DueDate { get; set; }
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.NotStarted;
    /// <summary>Comma-separated ids of the lessons marked done.</summary>
    public string LessonsDone { get; set; } = "";
    public int? QuizScore { get; set; }
    public int QuizAttempts { get; set; }
    public string? CertificateNo { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public DateTimeOffset EnrolledAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
