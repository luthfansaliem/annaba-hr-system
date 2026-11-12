namespace AnnabaDigital.Domain.Entities;

/// <summary>Leave by the hour (izin per jam): leaving early, coming late with permission, an errand. Paid or unpaid; unpaid hours are deducted in the pay run at the hourly wage.</summary>
public class HourlyLeave : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly From { get; set; }
    public TimeOnly To { get; set; }
    public bool Paid { get; set; } = true;
    public string Reason { get; set; } = "";
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Version { get; set; } = 1;
    public decimal Hours => (decimal)(To - From).TotalHours;
}

/// <summary>A planned shift for one person on one day, overriding their standing shift (roster / shift planner). A null shift means a planned day off.</summary>
public class RosterEntry : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public Guid? ShiftId { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>An on-call duty day: the person is reachable outside working hours and receives an allowance, paid as taxable income in that month's pay run.</summary>
public class OnCallDuty : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Allowance { get; set; }
    public string? Note { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A project or client that time is booked against.</summary>
public class Project : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Client { get; set; }
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum TimesheetStatus { Draft, Submitted, Approved, Rejected }

/// <summary>Hours one person spent on one project on one day. Entries are submitted per week and approved by the manager.</summary>
public class TimesheetEntry : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public Guid ProjectId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Hours { get; set; }
    public string? Note { get; set; }
    public TimesheetStatus Status { get; set; } = TimesheetStatus.Draft;
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A shared tablet at a site where people clock in with their employee number and a PIN (Talenta Tab Portal). Only the hash of its token is stored.</summary>
public class KioskDevice : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public Guid? WorkLocationId { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A field visit check-in with the device's position (live tracking for sales and field staff): arriving at or leaving a place.</summary>
public class FieldVisit : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public DateTimeOffset At { get; set; }
    public DateOnly Date { get; set; }
    public string Kind { get; set; } = "arrive";
    public string Place { get; set; } = "";
    public string? Note { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Accuracy { get; set; }
}
