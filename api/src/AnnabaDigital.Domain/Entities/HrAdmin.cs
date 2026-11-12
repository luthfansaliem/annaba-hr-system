namespace AnnabaDigital.Domain.Entities;

public enum CustomFieldType { Text, Number, Date, Choice, YesNo }

/// <summary>An extra field on the employee record that the organisation defines itself (shirt size, licence number, emergency blood type…). Values live on the employee as JSON.</summary>
public class CustomFieldDef : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Stable machine name used in the JSON and in CSV columns (lowercase letters, digits, underscores).</summary>
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public CustomFieldType Type { get; set; }
    /// <summary>Choice fields: the allowed answers, separated by "|".</summary>
    public string? Options { get; set; }
    public bool Required { get; set; }
    /// <summary>Shown to the employee in their own profile; with <see cref="EditableByEmployee"/> they may also change it.</summary>
    public bool VisibleToEmployee { get; set; }
    public bool EditableByEmployee { get; set; }
    public int SortOrder { get; set; }
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A letter the organisation issues from a text with placeholders (offer, warning, assignment, certificate…), numbered with its own prefix.</summary>
public class LetterTemplate : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    /// <summary>Prefix of the letter number, e.g. "SP" gives SP/2027/0001.</summary>
    public string Prefix { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>Whether the employee sees the letter under My Letters once issued.</summary>
    public bool VisibleToEmployee { get; set; } = true;
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A letter issued to one employee: the rendered text is frozen at issue time, so later template changes never alter it.</summary>
public class IssuedLetter : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TemplateId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public bool VisibleToEmployee { get; set; }
    public Guid IssuedBy { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
}

public enum HrFormStatus { Draft, Open, Closed }

/// <summary>A form or survey sent to everyone or to one department. Anonymous forms never reveal who answered what: results are shown in aggregate only.</summary>
public class HrForm : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>JSON array of questions: { id, type: text|choice|multi|rating|yesno, label, options?, required }.</summary>
    public string QuestionsJson { get; set; } = "[]";
    public Guid? OrgUnitId { get; set; }
    public bool Anonymous { get; set; }
    public HrFormStatus Status { get; set; } = HrFormStatus.Draft;
    public DateOnly? ClosesOn { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
}

public class HrFormResponse : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid FormId { get; set; }
    /// <summary>Kept so each person answers once; for an anonymous form it is never shown or exported.</summary>
    public Guid EmployeeId { get; set; }
    public string AnswersJson { get; set; } = "{}";
    public DateTimeOffset SubmittedAt { get; set; }
}

public enum TicketStatus { Open, InProgress, Resolved, Closed }

/// <summary>A question to HR (payslip, leave, documents…) with a conversation; employees see their own, HR sees all and may add internal notes.</summary>
public class HelpdeskTicket : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public int Number { get; set; }
    public Guid EmployeeId { get; set; }
    public string Category { get; set; } = "other";
    public string Subject { get; set; } = "";
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public Guid? AssignedTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public int Version { get; set; } = 1;
}

public class HelpdeskMessage : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TicketId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string Body { get; set; } = "";
    /// <summary>A note between HR staff that the employee never sees.</summary>
    public bool Internal { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A company event on the shared calendar (town hall, outing, training day). Holidays, leave and birthdays are added to the calendar from their own data.</summary>
public class CompanyEvent : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Title { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
    public Guid? OrgUnitId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A branch or business unit of a company (Talenta: multi-branch / SBU). Employees belong to at most one; reports and lists can be filtered by it.</summary>
public class Branch : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid LegalEntityId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>"branch" (a location) or "unit" (a strategic business unit).</summary>
    public string Kind { get; set; } = "branch";
    public string? Address { get; set; }
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
