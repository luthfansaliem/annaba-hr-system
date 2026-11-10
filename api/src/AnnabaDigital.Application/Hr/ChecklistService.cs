using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Hr;

public sealed record TaskView(Guid Id, string Kind, string Title, DateOnly? DueDate, bool Done, DateTimeOffset? DoneAt, bool Overdue);
public sealed record AddTaskRequest(string? Kind, string? Title, DateOnly? DueDate);
public sealed record ExpiryItem(string Kind, Guid EmployeeId, string EmployeeNo, string EmployeeName, string Label, DateOnly Date, int DaysLeft);

/// <summary>ONB-002/005. Starter checklists (editable per person) and the "what ends soon" list for contracts, probation and documents.</summary>
public sealed class ChecklistService(IAppDb db, IClock clock, AuditWriter audit)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    private static readonly (string Title, int DueOffsetDays)[] Onboarding =
    [
        ("Collect ID card (KTP), tax ID (NPWP) and BPJS numbers", 0), ("Sign the employment agreement", 0), ("Set salary, tax status and BPJS enrolment in Compensation", 3),
        ("Create company e-mail and system accounts, then link the sign-in account", 1), ("Hand over equipment and workspace", 1), ("Orientation and policy briefing", 7), ("Probation review scheduled with the manager", 30),
    ];
    private static readonly (string Title, int DueOffsetDays)[] Offboarding =
    [
        ("Collect handover notes", -7), ("Return equipment and access cards", 0), ("Settle final pay, unused leave and any loan balance", 0), ("Exit interview", -3), ("Issue the employment reference letter", 3), ("Archive the personal file", 7),
    ];

    /// <summary>The starter list, due dates counted from <paramref name="baseDate"/> (hire date, or last working day).</summary>
    public static IEnumerable<EmployeeTask> Defaults(Guid employeeId, ChecklistKind kind, DateOnly baseDate, DateTimeOffset now)
        => (kind == ChecklistKind.Onboarding ? Onboarding : Offboarding).Select((t, i) => new EmployeeTask { EmployeeId = employeeId, Kind = kind, Title = t.Title, DueDate = baseDate.AddDays(t.DueOffsetDays), SortOrder = i, CreatedAt = now });

    public async Task<IReadOnlyList<TaskView>> ListAsync(Guid employeeId, CancellationToken ct = default)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, ct)) throw DomainException.NotFound("Employee");
        var today = Today;
        return [.. (await db.EmployeeTasks.AsNoTracking().Where(t => t.EmployeeId == employeeId).OrderBy(t => t.Kind).ThenBy(t => t.SortOrder).ThenBy(t => t.CreatedAt).ToListAsync(ct))
            .Select(t => new TaskView(t.Id, t.Kind.ToString().ToLowerInvariant(), t.Title, t.DueDate, t.DoneAt != null, t.DoneAt, t.DoneAt == null && t.DueDate < today))];
    }

    public async Task<TaskView> SetDoneAsync(Guid taskId, bool done, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var t = await db.EmployeeTasks.FirstOrDefaultAsync(x => x.Id == taskId, ct) ?? throw DomainException.NotFound("Task");
        if ((t.DoneAt != null) != done) { t.DoneAt = done ? clock.UtcNow : null; t.DoneBy = done ? actor.UserId : null; }
        audit.Record(done ? "hr.task.done" : "hr.task.reopen", "employee", t.EmployeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["task"] = t.Title });
        await db.SaveChangesAsync(ct);
        return new TaskView(t.Id, t.Kind.ToString().ToLowerInvariant(), t.Title, t.DueDate, t.DoneAt != null, t.DoneAt, t.DoneAt == null && t.DueDate < Today);
    }

    public async Task<TaskView> AddAsync(Guid employeeId, AddTaskRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, ct)) throw DomainException.NotFound("Employee");
        var errors = new Dictionary<string, string>(); var title = r.Title?.Trim();
        if (string.IsNullOrEmpty(title) || title.Length > 200) errors["title"] = "Enter a task (up to 200 characters).";
        if (!Enum.TryParse<ChecklistKind>(r.Kind, true, out var kind)) errors["kind"] = "Choose onboarding or offboarding.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the task.", errors);
        if (await db.EmployeeTasks.CountAsync(x => x.EmployeeId == employeeId, ct) >= 100) throw DomainException.Invariant("That is the most tasks one person can have.");
        var order = (await db.EmployeeTasks.Where(x => x.EmployeeId == employeeId && x.Kind == kind).MaxAsync(x => (int?)x.SortOrder, ct) ?? -1) + 1;
        var t = new EmployeeTask { EmployeeId = employeeId, Kind = kind, Title = title!, DueDate = r.DueDate, SortOrder = order, CreatedAt = clock.UtcNow };
        db.EmployeeTasks.Add(t);
        audit.Record("hr.task.add", "employee", employeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["task"] = title! });
        await db.SaveChangesAsync(ct);
        return new TaskView(t.Id, kind.ToString().ToLowerInvariant(), t.Title, t.DueDate, false, null, false);
    }

    public async Task DeleteAsync(Guid taskId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var t = await db.EmployeeTasks.FirstOrDefaultAsync(x => x.Id == taskId, ct) ?? throw DomainException.NotFound("Task");
        db.EmployeeTasks.Remove(t);
        audit.Record("hr.task.delete", "employee", t.EmployeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["task"] = t.Title });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Contracts, probation and internships that end within <paramref name="days"/> (already-ended ones show with a negative number), and documents that expire in the same window.</summary>
    public async Task<IReadOnlyList<ExpiryItem>> ExpiringAsync(int days, bool includeDocuments, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 365); var today = Today; var until = today.AddDays(days);
        var items = new List<ExpiryItem>();
        var contracts = await db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active)
            .SelectMany(e => e.Employments.Where(m => m.EffectiveTo == null && m.Type != EmploymentType.Permanent && m.ContractEnd != null && m.ContractEnd <= until).Select(m => new { e.Id, e.EmployeeNo, e.FullName, m.Type, End = m.ContractEnd!.Value })).ToListAsync(ct);
        items.AddRange(contracts.Select(c => new ExpiryItem("contract", c.Id, c.EmployeeNo, c.FullName, c.Type.ToString().ToLowerInvariant(), c.End, c.End.DayNumber - today.DayNumber)));
        if (includeDocuments)
        {
            var docs = await db.EmployeeDocuments.AsNoTracking().Where(d => d.DeletedAt == null && d.ExpiresOn != null && d.ExpiresOn <= until).Join(db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active), d => d.EmployeeId, e => e.Id,
                (d, e) => new { e.Id, e.EmployeeNo, e.FullName, d.Category, d.FileName, End = d.ExpiresOn!.Value }).ToListAsync(ct);
            items.AddRange(docs.Select(d => new ExpiryItem("document", d.Id, d.EmployeeNo, d.FullName, $"{d.Category}: {d.FileName}", d.End, d.End.DayNumber - today.DayNumber)));
        }
        return [.. items.OrderBy(i => i.Date).ThenBy(i => i.EmployeeName)];
    }
}
