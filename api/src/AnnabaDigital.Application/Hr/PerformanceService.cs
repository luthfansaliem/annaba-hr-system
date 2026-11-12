using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record GoalView(Guid Id, Guid? OwnerEmployeeId, string? Owner, Guid? OrgUnitId, string? Unit, Guid? ParentId, string? Parent, string Kind, string Title, string? Description, string? Measure,
    decimal StartValue, decimal TargetValue, decimal CurrentValue, decimal Progress, decimal Weight, int Year, DateOnly? DueDate, string Status, int Children, bool CanEdit, bool CanCheckIn);
public sealed record GoalRequest(Guid? OwnerEmployeeId, Guid? OrgUnitId, Guid? ParentId, string? Kind, string? Title, string? Description, string? Measure, decimal? StartValue, decimal? TargetValue, decimal? Weight, int? Year, DateOnly? DueDate, bool? Company);
public sealed record CheckInRequest(decimal? Value, string? Status, string? Note);
public sealed record CheckInView(Guid Id, decimal Value, string? Note, string By, DateTimeOffset At);
public sealed record CompetencyView(Guid Id, string Code, string Name, string Category, string? Description, bool Archived);
public sealed record CompetencyRequest(string? Code, string? Name, string? Category, string? Description, bool? Archived);
public sealed record CycleView(Guid Id, string Name, DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly? DueDate, Guid? OrgUnitId, bool SelfReview, bool ManagerReview, bool PeerReview, decimal GoalsWeight, decimal CompetencyWeight,
    IReadOnlyList<Guid> CompetencyIds, string Status, int People, int Reviews, int Submitted, DateTimeOffset CreatedAt);
public sealed record CycleRequest(string? Name, DateOnly? PeriodStart, DateOnly? PeriodEnd, DateOnly? DueDate, Guid? OrgUnitId, bool? SelfReview, bool? ManagerReview, bool? PeerReview, decimal? GoalsWeight, IReadOnlyList<Guid>? CompetencyIds);
public sealed record ReviewTask(Guid Id, Guid CycleId, string Cycle, Guid EmployeeId, string EmployeeName, string Kind, bool Submitted, DateOnly? DueDate, string CycleStatus);
public sealed record ReviewForm(ReviewTask Task, IReadOnlyList<GoalView> Goals, IReadOnlyList<CompetencyView> Competencies, JsonElement Answers);
public sealed record ReviewAnswers(Dictionary<Guid, int>? Goals, Dictionary<Guid, int>? Competencies, string? Strengths, string? Improvements, string? Comment, bool? Submit);
public sealed record PeerRequest(Guid? EmployeeId, Guid? ReviewerEmployeeId);
public sealed record ResultRow(Guid EmployeeId, string EmployeeNo, string EmployeeName, string? Department, decimal? GoalScore, decimal? CompetencyScore, decimal? FinalScore, int? CalibratedRating, int? Potential, string? Note, bool Released, int ReviewsDone, int ReviewsTotal, string? Box);
public sealed record CalibrateRequest(Guid? EmployeeId, int? Rating, int? Potential, string? Note);
public sealed record MyResult(Guid CycleId, string Cycle, decimal? GoalScore, decimal? CompetencyScore, decimal? FinalScore, int? Rating, IReadOnlyList<string> Strengths, IReadOnlyList<string> Improvements, IReadOnlyList<string> Comments);
public sealed record IdpView(Guid Id, Guid EmployeeId, string EmployeeName, string Title, string? Aim, Guid? CompetencyId, string? Competency, IReadOnlyList<IdpAction> Actions, string Status, int Done, DateTimeOffset UpdatedAt, bool CanEdit);
public sealed record IdpAction(string Id, string Text, DateOnly? Due, bool Done);
public sealed record IdpRequest(Guid? EmployeeId, string? Title, string? Aim, Guid? CompetencyId, IReadOnlyList<IdpAction>? Actions, string? Status);
public sealed record SuccessionView(Guid Id, string Position, Guid? OrgUnitId, string? Unit, Guid? IncumbentEmployeeId, string? Incumbent, bool Critical, string? Note, IReadOnlyList<SuccessionCandidateView> Candidates);
public sealed record SuccessionCandidateView(Guid Id, Guid EmployeeId, string EmployeeName, string? JobTitle, string Readiness, string? Note, string? Box);
public sealed record SuccessionRequest(string? Position, Guid? OrgUnitId, Guid? IncumbentEmployeeId, bool? Critical, string? Note);
public sealed record CandidateRequest(Guid? EmployeeId, string? Readiness, string? Note);
public sealed record TalentRow(Guid EmployeeId, string EmployeeName, string? JobTitle, string? Department, int Rating, int Potential, string Box, string Cycle);

/// <summary>
/// Stages 15–16, performance and talent (Talenta: KPI &amp; OKR, goal alignment, 360° review, competency review, calibration, 9-box, IDP, succession, talent pool).
/// People set and update their own goals, managers those of their direct reports, HR (hrconfig.manage) company and department goals, cycles, competencies and calibration.
/// Peer reviews are never shown with names; a person sees their own result only after HR releases it.
/// </summary>
public sealed class PerformanceService(IAppDb db, IClock clock, AuthorizationService authz, AuditWriter audit, ManagerScope managers, NotificationService notes)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));
    private async Task<Employee?> MeOrNullAsync(SessionPrincipal actor, CancellationToken ct) => await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == actor.UserId && e.Status == EmployeeStatus.Active, ct);
    private async Task<Employee> MeAsync(SessionPrincipal actor, CancellationToken ct) => await MeOrNullAsync(actor, ct) ?? throw DomainException.Invariant("Your account is not linked to an active employee record. Ask HR to link it.");
    private async Task<bool> IsHrAsync(SessionPrincipal actor, CancellationToken ct) => (await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(Permissions.HrConfigManage);
    private async Task<HashSet<Guid>> ReportsAsync(SessionPrincipal actor, CancellationToken ct)
    {
        var acting = await managers.ActingIdsAsync(actor.UserId, ct); if (acting.Count == 0) return [];
        return [.. await db.Employments.AsNoTracking().Where(m => m.EffectiveTo == null && m.ManagerId != null && acting.Contains(m.ManagerId.Value)).Select(m => m.EmployeeId).ToListAsync(ct)];
    }
    public static decimal Progress(Goal g) => g.Status == GoalStatus.Done ? 100 : g.TargetValue == g.StartValue ? (g.CurrentValue >= g.TargetValue ? 100 : 0) : Math.Round(Math.Clamp((g.CurrentValue - g.StartValue) / (g.TargetValue - g.StartValue), 0, 1) * 100, 1);
    private static string Snake(string s) => string.Concat(s.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    // ============================================================ goals (KPI / OKR)
    private async Task<IReadOnlyList<GoalView>> GoalViewsAsync(IReadOnlyList<Goal> rows, SessionPrincipal actor, CancellationToken ct)
    {
        var me = await MeOrNullAsync(actor, ct); var hr = await IsHrAsync(actor, ct); var reports = await ReportsAsync(actor, ct);
        var empIds = rows.Where(g => g.OwnerEmployeeId != null).Select(g => g.OwnerEmployeeId!.Value).Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(e => empIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var parentIds = rows.Where(g => g.ParentId != null).Select(g => g.ParentId!.Value).Distinct().ToList();
        var parents = await db.Goals.AsNoTracking().Where(g => parentIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Title, ct);
        var ids = rows.Select(g => g.Id).ToList();
        var children = await db.Goals.AsNoTracking().Where(g => g.ParentId != null && ids.Contains(g.ParentId.Value)).GroupBy(g => g.ParentId!.Value).Select(x => new { x.Key, N = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return [.. rows.Select(g =>
        {
            var own = me is not null && g.OwnerEmployeeId == me.Id; var mgr = g.OwnerEmployeeId is { } o && reports.Contains(o);
            var canEdit = hr || mgr || own && g.OwnerEmployeeId != null; var canCheck = canEdit;
            return new GoalView(g.Id, g.OwnerEmployeeId, g.OwnerEmployeeId is { } oe ? names.GetValueOrDefault(oe) : null, g.OrgUnitId, g.OrgUnitId is { } u ? units.GetValueOrDefault(u) : null, g.ParentId, g.ParentId is { } p ? parents.GetValueOrDefault(p) : null,
                Snake(g.Kind.ToString()), g.Title, g.Description, g.Unit, g.StartValue, g.TargetValue, g.CurrentValue, Progress(g), g.Weight, g.Year, g.DueDate, Snake(g.Status.ToString()), children.GetValueOrDefault(g.Id), canEdit, canCheck);
        })];
    }

    /// <summary>scope: mine (own goals), team (direct reports), company (company and department goals), employee (one person; manager or HR), all (HR).</summary>
    public async Task<IReadOnlyList<GoalView>> GoalsAsync(string? scope, int? year, Guid? employeeId, SessionPrincipal actor, CancellationToken ct = default)
    {
        var y = year ?? Today.Year; var q = db.Goals.AsNoTracking().Where(g => g.Year == y && g.Status != GoalStatus.Cancelled);
        switch (scope ?? "mine")
        {
            case "mine": { var me = await MeAsync(actor, ct); q = q.Where(g => g.OwnerEmployeeId == me.Id); break; }
            case "team": { var r = await ReportsAsync(actor, ct); q = q.Where(g => g.OwnerEmployeeId != null && r.Contains(g.OwnerEmployeeId.Value)); break; }
            case "company": q = q.Where(g => g.OwnerEmployeeId == null); break;
            case "employee":
                if (employeeId is not { } eid) throw DomainException.Validation("Choose a person.", new Dictionary<string, string> { ["employeeId"] = "Required." });
                if (!await IsHrAsync(actor, ct) && !(await ReportsAsync(actor, ct)).Contains(eid) && (await MeOrNullAsync(actor, ct))?.Id != eid) throw DomainException.NotFound("Employee");
                q = q.Where(g => g.OwnerEmployeeId == eid); break;
            case "all": if (!await IsHrAsync(actor, ct)) throw DomainException.Denied(Permissions.HrConfigManage); break;
            default: throw DomainException.Validation("Unknown scope.", new Dictionary<string, string> { ["scope"] = "Use mine, team, company, employee or all." });
        }
        return await GoalViewsAsync(await q.OrderBy(g => g.OwnerEmployeeId).ThenBy(g => g.Kind).ThenBy(g => g.Title).Take(1000).ToListAsync(ct), actor, ct);
    }

    public async Task<GoalView> SaveGoalAsync(Guid? id, GoalRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var g = id is { } gid ? await db.Goals.FirstOrDefaultAsync(x => x.Id == gid, ct) ?? throw DomainException.NotFound("Goal") : new Goal { CreatedBy = actor.UserId, CreatedAt = clock.UtcNow, Year = Today.Year };
        var hr = await IsHrAsync(actor, ct); var me = await MeOrNullAsync(actor, ct); var reports = await ReportsAsync(actor, ct);
        Guid? owner = id is null ? (r.Company == true ? null : r.OwnerEmployeeId ?? me?.Id) : g.OwnerEmployeeId;
        // company and department goals belong to HR; a person's goals to the person, their manager and HR
        if (owner is null && !hr) throw DomainException.Denied(Permissions.HrConfigManage);
        if (owner is { } o && !hr && o != me?.Id && !reports.Contains(o)) throw DomainException.DeniedBecause("You can set goals for yourself and the people who report to you.");
        var errors = new Dictionary<string, string>();
        var title = (r.Title ?? g.Title).Trim(); if (title.Length is < 2 or > 200) errors["title"] = "Describe the goal (up to 200 characters).";
        var kind = g.Kind; if (r.Kind is { } k && !Enum.TryParse(k.Replace("_", ""), true, out kind)) errors["kind"] = "Choose kpi, objective or key_result.";
        var start = r.StartValue ?? g.StartValue; var target = r.TargetValue ?? g.TargetValue; if (target == start && kind != GoalKind.Objective) errors["targetValue"] = "The target must differ from the starting value.";
        var weight = r.Weight ?? g.Weight; if (weight is < 0 or > 100) errors["weight"] = "Enter 0–100%.";
        var year = r.Year ?? g.Year; if (year < 2000 || year > Today.Year + 2) errors["year"] = "Check the year.";
        Guid? parent = r.ParentId ?? g.ParentId;
        if (r.ParentId is { } pid)
        {
            if (pid == g.Id) errors["parentId"] = "A goal cannot support itself.";
            else if (!await db.Goals.AnyAsync(x => x.Id == pid && x.Year == year, ct)) errors["parentId"] = "Choose a goal of the same year.";
            else { var cur = (Guid?)pid; for (var hop = 0; cur is { } c && hop < 20; hop++) { if (c == g.Id) { errors["parentId"] = "That would make a loop."; break; } cur = await db.Goals.AsNoTracking().Where(x => x.Id == c).Select(x => x.ParentId).FirstOrDefaultAsync(ct); } }
        }
        if (r.OrgUnitId is { } u && !await db.OrgUnits.AnyAsync(x => x.Id == u, ct)) errors["orgUnitId"] = "Choose a department.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the goal.", errors);
        g.OwnerEmployeeId = owner; g.OrgUnitId = owner is null ? r.OrgUnitId ?? g.OrgUnitId : null; g.ParentId = parent; g.Kind = kind; g.Title = title;
        g.Description = r.Description is null ? g.Description : r.Description.Trim() is { Length: > 0 } d ? d[..Math.Min(d.Length, 1000)] : null;
        g.Unit = r.Measure is null ? g.Unit : r.Measure.Trim() is { Length: > 0 } m ? m[..Math.Min(m.Length, 20)] : null;
        g.StartValue = start; g.TargetValue = kind == GoalKind.Objective && target == start ? 100 : target; if (id is null) g.CurrentValue = start; g.Weight = weight; g.Year = year; g.DueDate = r.DueDate ?? g.DueDate; g.UpdatedAt = clock.UtcNow;
        if (id is null) db.Goals.Add(g); else g.Version++;
        audit.Record(id is null ? "hr.goal.create" : "hr.goal.update", "goal", g.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["owner"] = owner?.ToString() ?? "company" });
        if (id is null && owner is { } ow && ow != me?.Id) { var uid = await db.Employees.AsNoTracking().Where(e => e.Id == ow).Select(e => e.UserId).FirstOrDefaultAsync(ct); notes.Notify(uid, "goal.assigned", $"A new goal was set for you: {title}", null, "/hr/goals"); }
        await db.SaveChangesAsync(ct);
        return (await GoalViewsAsync([g], actor, ct))[0];
    }

    public async Task<GoalView> CheckInAsync(Guid id, CheckInRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var g = await db.Goals.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Goal");
        if (!(await GoalViewsAsync([g], actor, ct))[0].CanCheckIn) throw DomainException.DeniedBecause("Only the goal's owner, their manager or HR can update it.");
        var status = g.Status; if (r.Status is { } s && !Enum.TryParse(s.Replace("_", ""), true, out status)) throw DomainException.Validation("Unknown status.", new Dictionary<string, string> { ["status"] = "Use on_track, at_risk, off_track, done or cancelled." });
        if (r.Value is { } v) { g.CurrentValue = v; db.GoalCheckIns.Add(new GoalCheckIn { GoalId = id, Value = v, Note = r.Note?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 500)] : null, ByUserId = actor.UserId, At = clock.UtcNow }); }
        g.Status = status; g.UpdatedAt = clock.UtcNow; g.Version++;
        audit.Record("hr.goal.check_in", "goal", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { db.ResetTracking(); throw DomainException.Conflict("Someone else updated this goal just now."); }
        return (await GoalViewsAsync([g], actor, ct))[0];
    }

    public async Task<IReadOnlyList<CheckInView>> CheckInsAsync(Guid id, SessionPrincipal actor, CancellationToken ct = default)
    {
        var g = await db.Goals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Goal");
        var v = (await GoalViewsAsync([g], actor, ct))[0]; if (!v.CanEdit && g.OwnerEmployeeId != null) throw DomainException.NotFound("Goal");
        var rows = await db.GoalCheckIns.AsNoTracking().Where(c => c.GoalId == id).OrderByDescending(c => c.At).Take(100).ToListAsync(ct); var users = rows.Select(r => r.ByUserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => users.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return [.. rows.Select(c => new CheckInView(c.Id, c.Value, c.Note, names.GetValueOrDefault(c.ByUserId) ?? "", c.At))];
    }

    // ============================================================ competencies
    public async Task<IReadOnlyList<CompetencyView>> CompetenciesAsync(CancellationToken ct = default)
        => [.. (await db.Competencies.AsNoTracking().OrderBy(c => c.Archived).ThenBy(c => c.Category).ThenBy(c => c.Name).ToListAsync(ct)).Select(c => new CompetencyView(c.Id, c.Code, c.Name, c.Category, c.Description, c.Archived))];

    public async Task<CompetencyView> SaveCompetencyAsync(Guid? id, CompetencyRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = id is { } cid ? await db.Competencies.FirstOrDefaultAsync(x => x.Id == cid, ct) ?? throw DomainException.NotFound("Competency") : new Competency { CreatedAt = clock.UtcNow };
        var errors = new Dictionary<string, string>();
        var code = (r.Code ?? c.Code).Trim().ToUpperInvariant(); if (code.Length is < 1 or > 20) errors["code"] = "Enter a short code.";
        var name = (r.Name ?? c.Name).Trim(); if (name.Length is < 2 or > 120) errors["name"] = "Enter a name (up to 120 characters).";
        var cat = (r.Category ?? c.Category).Trim().ToLowerInvariant(); if (cat is not ("core" or "leadership" or "technical")) errors["category"] = "Choose core, leadership or technical.";
        if (errors.Count == 0 && await db.Competencies.AnyAsync(x => x.Code == code && x.Id != c.Id, ct)) errors["code"] = "Another competency already uses this code.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the competency.", errors);
        c.Code = code; c.Name = name; c.Category = cat; c.Description = r.Description is null ? c.Description : r.Description.Trim() is { Length: > 0 } d ? d[..Math.Min(d.Length, 1000)] : null; c.Archived = r.Archived ?? c.Archived;
        if (id is null) db.Competencies.Add(c);
        audit.Record(id is null ? "hr.competency.create" : "hr.competency.update", "competency", c.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["code"] = code });
        await db.SaveChangesAsync(ct);
        return new CompetencyView(c.Id, c.Code, c.Name, c.Category, c.Description, c.Archived);
    }

    // ============================================================ review cycles
    private static IReadOnlyList<Guid> Ids(string s) => [.. s.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse)];

    private async Task<CycleView> CycleViewAsync(ReviewCycle c, CancellationToken ct)
    {
        var a = await db.ReviewAssignments.AsNoTracking().Where(x => x.CycleId == c.Id).GroupBy(_ => 1).Select(g => new { N = g.Count(), S = g.Count(x => x.Submitted), P = g.Select(x => x.EmployeeId).Distinct().Count() }).FirstOrDefaultAsync(ct);
        return new CycleView(c.Id, c.Name, c.PeriodStart, c.PeriodEnd, c.DueDate, c.OrgUnitId, c.SelfReview, c.ManagerReview, c.PeerReview, c.GoalsWeight, c.CompetencyWeight, Ids(c.CompetencyIds), Snake(c.Status.ToString()), a?.P ?? 0, a?.N ?? 0, a?.S ?? 0, c.CreatedAt);
    }

    public async Task<IReadOnlyList<CycleView>> CyclesAsync(CancellationToken ct = default)
    {
        var list = new List<CycleView>(); foreach (var c in await db.ReviewCycles.AsNoTracking().OrderByDescending(x => x.CreatedAt).ToListAsync(ct)) list.Add(await CycleViewAsync(c, ct)); return list;
    }

    public async Task<CycleView> SaveCycleAsync(Guid? id, CycleRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = id is { } cid ? await db.ReviewCycles.FirstOrDefaultAsync(x => x.Id == cid, ct) ?? throw DomainException.NotFound("Review cycle") : new ReviewCycle { CreatedBy = actor.UserId, CreatedAt = clock.UtcNow };
        if (c.Status != ReviewCycleStatus.Draft) throw DomainException.Invariant("A cycle cannot be changed once it is launched.");
        var errors = new Dictionary<string, string>();
        var name = (r.Name ?? c.Name).Trim(); if (name.Length is < 2 or > 150) errors["name"] = "Name the cycle, e.g. Annual review 2027.";
        var start = r.PeriodStart ?? (id is null ? null : c.PeriodStart); var end = r.PeriodEnd ?? (id is null ? null : c.PeriodEnd);
        if (start is null || end is null || end < start) errors["periodEnd"] = "Choose the period reviewed.";
        var gw = r.GoalsWeight ?? c.GoalsWeight; if (gw is < 0 or > 100) errors["goalsWeight"] = "Enter 0–100%.";
        var self = r.SelfReview ?? c.SelfReview; var mgr = r.ManagerReview ?? c.ManagerReview; var peer = r.PeerReview ?? c.PeerReview;
        if (!self && !mgr && !peer) errors["managerReview"] = "Choose at least one kind of review.";
        var comps = r.CompetencyIds ?? Ids(c.CompetencyIds);
        if (comps.Count > 30) errors["competencyIds"] = "Choose up to 30 competencies.";
        else if (comps.Count > 0 && await db.Competencies.CountAsync(x => comps.Contains(x.Id) && !x.Archived, ct) != comps.Distinct().Count()) errors["competencyIds"] = "Choose active competencies.";
        if (gw < 100 && comps.Count == 0) errors["competencyIds"] = "Competencies count for part of the score: choose some, or give goals 100%.";
        if (r.OrgUnitId is { } u && !await db.OrgUnits.AnyAsync(x => x.Id == u, ct)) errors["orgUnitId"] = "Choose a department.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the cycle.", errors);
        c.Name = name; c.PeriodStart = start!.Value; c.PeriodEnd = end!.Value; c.DueDate = r.DueDate ?? c.DueDate; c.OrgUnitId = r.OrgUnitId ?? c.OrgUnitId; c.SelfReview = self; c.ManagerReview = mgr; c.PeerReview = peer;
        c.GoalsWeight = gw; c.CompetencyWeight = 100 - gw; c.CompetencyIds = string.Join(',', comps.Distinct());
        if (id is null) db.ReviewCycles.Add(c); else c.Version++;
        audit.Record(id is null ? "hr.review_cycle.create" : "hr.review_cycle.update", "review_cycle", c.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name });
        await db.SaveChangesAsync(ct);
        return await CycleViewAsync(c, ct);
    }

    /// <summary>Opens the cycle: one self review and one manager review per person in scope (as chosen); peers are added afterwards by the manager or HR. Everyone with a review to write is notified.</summary>
    public async Task<CycleView> LaunchAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.ReviewCycles.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Review cycle");
        if (c.Status != ReviewCycleStatus.Draft) throw DomainException.Conflict("This cycle is already launched.");
        var q = db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active && e.HireDate <= c.PeriodEnd);
        if (c.OrgUnitId is { } u) q = q.Where(e => e.Employments.Any(m => m.EffectiveTo == null && m.OrgUnitId == u));
        var people = await q.Select(e => new { e.Id, Manager = e.Employments.Where(m => m.EffectiveTo == null).Select(m => m.ManagerId).FirstOrDefault() }).ToListAsync(ct);
        if (people.Count == 0) throw DomainException.Invariant("Nobody is in scope for this cycle.");
        foreach (var p in people)
        {
            if (c.SelfReview) db.ReviewAssignments.Add(new ReviewAssignment { CycleId = id, EmployeeId = p.Id, ReviewerEmployeeId = p.Id, Kind = ReviewKind.Self });
            if (c.ManagerReview && p.Manager is { } m) db.ReviewAssignments.Add(new ReviewAssignment { CycleId = id, EmployeeId = p.Id, ReviewerEmployeeId = m, Kind = ReviewKind.Manager });
            db.ReviewResults.Add(new ReviewResult { CycleId = id, EmployeeId = p.Id, UpdatedAt = clock.UtcNow });
        }
        c.Status = ReviewCycleStatus.Open; c.LaunchedAt = clock.UtcNow; c.Version++;
        await db.SaveChangesAsync(ct);
        var reviewers = await db.ReviewAssignments.AsNoTracking().Where(a => a.CycleId == id).Select(a => a.ReviewerEmployeeId).Distinct().ToListAsync(ct);
        foreach (var uid in await db.Employees.AsNoTracking().Where(e => reviewers.Contains(e.Id) && e.UserId != null).Select(e => e.UserId!.Value).ToListAsync(ct)) notes.Notify(uid, "review.assigned", $"Performance review: {c.Name}", "You have reviews to write.", "/hr/reviews");
        audit.Record("hr.review_cycle.launch", "review_cycle", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["people"] = people.Count.ToString() });
        await db.SaveChangesAsync(ct);
        return await CycleViewAsync(c, ct);
    }

    public async Task AddPeerAsync(Guid id, PeerRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.ReviewCycles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Review cycle");
        if (!c.PeerReview) throw DomainException.Invariant("This cycle has no peer reviews.");
        if (c.Status != ReviewCycleStatus.Open) throw DomainException.Conflict("Peers can be added while the cycle is open.");
        if (r.EmployeeId is not { } who || r.ReviewerEmployeeId is not { } peer || who == peer) throw DomainException.Validation("Choose a person and a different colleague.", new Dictionary<string, string> { ["reviewerEmployeeId"] = "Choose a colleague." });
        if (!await IsHrAsync(actor, ct) && !(await ReportsAsync(actor, ct)).Contains(who)) throw DomainException.DeniedBecause("Peers are chosen by the person's manager or HR.");
        if (!await db.ReviewResults.AnyAsync(x => x.CycleId == id && x.EmployeeId == who, ct)) throw DomainException.Invariant("This person is not in the cycle.");
        if (!await db.Employees.AnyAsync(e => e.Id == peer && e.Status == EmployeeStatus.Active, ct)) throw DomainException.Validation("Choose an active colleague.", new Dictionary<string, string> { ["reviewerEmployeeId"] = "Choose an active colleague." });
        if (await db.ReviewAssignments.AnyAsync(a => a.CycleId == id && a.EmployeeId == who && a.ReviewerEmployeeId == peer, ct)) throw DomainException.Conflict("This colleague already reviews this person.");
        if (await db.ReviewAssignments.CountAsync(a => a.CycleId == id && a.EmployeeId == who && a.Kind == ReviewKind.Peer, ct) >= 8) throw DomainException.Invariant("A person can have at most 8 peer reviewers.");
        db.ReviewAssignments.Add(new ReviewAssignment { CycleId = id, EmployeeId = who, ReviewerEmployeeId = peer, Kind = ReviewKind.Peer });
        var uid = await db.Employees.AsNoTracking().Where(e => e.Id == peer).Select(e => e.UserId).FirstOrDefaultAsync(ct); notes.Notify(uid, "review.assigned", $"You were asked for a peer review ({c.Name})", "Your name is not shown to the person.", "/hr/reviews");
        audit.Record("hr.review.peer_add", "review_cycle", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ReviewTask>> MyTasksAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct);
        var rows = await (from a in db.ReviewAssignments.AsNoTracking() join c in db.ReviewCycles.AsNoTracking() on a.CycleId equals c.Id join e in db.Employees.AsNoTracking() on a.EmployeeId equals e.Id
                          where a.ReviewerEmployeeId == me.Id && c.Status != ReviewCycleStatus.Draft orderby a.Submitted, c.CreatedAt descending
                          select new ReviewTask(a.Id, c.Id, c.Name, e.Id, e.FullName, a.Kind.ToString().ToLower(), a.Submitted, c.DueDate, c.Status.ToString().ToLower())).Take(200).ToListAsync(ct);
        return rows;
    }

    public async Task<ReviewForm> FormAsync(Guid assignmentId, SessionPrincipal actor, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct);
        var a = await db.ReviewAssignments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == assignmentId && x.ReviewerEmployeeId == me.Id, ct) ?? throw DomainException.NotFound("Review");
        var c = await db.ReviewCycles.AsNoTracking().FirstAsync(x => x.Id == a.CycleId, ct); var e = await db.Employees.AsNoTracking().FirstAsync(x => x.Id == a.EmployeeId, ct);
        var goals = await db.Goals.AsNoTracking().Where(g => g.OwnerEmployeeId == a.EmployeeId && g.Status != GoalStatus.Cancelled && g.Year >= c.PeriodStart.Year && g.Year <= c.PeriodEnd.Year).ToListAsync(ct);
        var compIds = Ids(c.CompetencyIds); var comps = await db.Competencies.AsNoTracking().Where(x => compIds.Contains(x.Id)).ToListAsync(ct);
        // peers rate competencies only; goals and their numbers are for the person, the manager and HR
        IReadOnlyList<GoalView> goalViews = a.Kind == ReviewKind.Peer ? [] : await GoalViewsAsync(goals, actor, ct);
        return new ReviewForm(new ReviewTask(a.Id, c.Id, c.Name, e.Id, e.FullName, a.Kind.ToString().ToLower(), a.Submitted, c.DueDate, c.Status.ToString().ToLower()), goalViews,
            [.. comps.Select(x => new CompetencyView(x.Id, x.Code, x.Name, x.Category, x.Description, x.Archived))], JsonDocument.Parse(a.AnswersJson).RootElement.Clone());
    }

    public async Task<ReviewForm> AnswerAsync(Guid assignmentId, ReviewAnswers r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct);
        var a = await db.ReviewAssignments.FirstOrDefaultAsync(x => x.Id == assignmentId && x.ReviewerEmployeeId == me.Id, ct) ?? throw DomainException.NotFound("Review");
        var c = await db.ReviewCycles.AsNoTracking().FirstAsync(x => x.Id == a.CycleId, ct);
        if (c.Status != ReviewCycleStatus.Open) throw DomainException.Conflict("This review cycle is not open.");
        if (a.Submitted) throw DomainException.Conflict("This review is already submitted.");
        var errors = new Dictionary<string, string>();
        var compIds = Ids(c.CompetencyIds).ToHashSet();
        var goalIds = a.Kind == ReviewKind.Peer ? [] : (await db.Goals.AsNoTracking().Where(g => g.OwnerEmployeeId == a.EmployeeId && g.Status != GoalStatus.Cancelled && g.Year >= c.PeriodStart.Year && g.Year <= c.PeriodEnd.Year).Select(g => g.Id).ToListAsync(ct)).ToHashSet();
        var goals = (r.Goals ?? []).Where(kv => goalIds.Contains(kv.Key)).ToDictionary(); var comps = (r.Competencies ?? []).Where(kv => compIds.Contains(kv.Key)).ToDictionary();
        if (goals.Values.Concat(comps.Values).Any(v => v is < 1 or > 5)) errors["ratings"] = "Ratings go from 1 to 5.";
        string? Clip(string? s) => s?.Trim() is { Length: > 0 } t ? t[..Math.Min(t.Length, 2000)] : null;
        if (r.Submit == true)
        {
            if (comps.Count < compIds.Count) errors["competencies"] = "Rate every competency before submitting.";
            if (goals.Count < goalIds.Count) errors["goals"] = "Rate every goal before submitting.";
        }
        if (errors.Count > 0) throw DomainException.Validation("Please check the review.", errors);
        a.AnswersJson = JsonSerializer.Serialize(new { goals, competencies = comps, strengths = Clip(r.Strengths), improvements = Clip(r.Improvements), comment = Clip(r.Comment) }, Json);
        if (r.Submit == true) { a.Submitted = true; a.SubmittedAt = clock.UtcNow; }
        audit.Record(r.Submit == true ? "hr.review.submit" : "hr.review.save", "review_cycle", c.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["kind"] = a.Kind.ToString() });
        await db.SaveChangesAsync(ct);
        return await FormAsync(assignmentId, actor, ct);
    }

    // ============================================================ results, calibration, 9-box
    private sealed record Parsed(Dictionary<Guid, int> Goals, Dictionary<Guid, int> Competencies, string? Strengths, string? Improvements, string? Comment);
    private static Parsed Parse(string json)
    {
        var e = JsonDocument.Parse(json).RootElement;
        Dictionary<Guid, int> Map(string k) => e.TryGetProperty(k, out var o) && o.ValueKind == JsonValueKind.Object ? o.EnumerateObject().Where(p => Guid.TryParse(p.Name, out _) && p.Value.ValueKind == JsonValueKind.Number).ToDictionary(p => Guid.Parse(p.Name), p => p.Value.GetInt32()) : [];
        string? S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        return new Parsed(Map("goals"), Map("competencies"), S("strengths"), S("improvements"), S("comment"));
    }

    /// <summary>
    /// Scores on the 1–5 scale. Goals: the manager's rating per goal weighted by goal weight (the self rating when there is no manager review). Competencies: the manager's ratings 60%,
    /// the average of peers 20% and the self rating 20%, re-weighted over the sources that exist. Final = goals × goal weight + competencies × competency weight of the cycle.
    /// </summary>
    public async Task RecomputeAsync(Guid cycleId, CancellationToken ct)
    {
        var c = await db.ReviewCycles.AsNoTracking().FirstAsync(x => x.Id == cycleId, ct);
        var assignments = await db.ReviewAssignments.AsNoTracking().Where(a => a.CycleId == cycleId && a.Submitted).ToListAsync(ct);
        var results = await db.ReviewResults.Where(r => r.CycleId == cycleId).ToListAsync(ct);
        var weights = await db.Goals.AsNoTracking().Where(g => g.OwnerEmployeeId != null && results.Select(r => r.EmployeeId).Contains(g.OwnerEmployeeId.Value)).ToDictionaryAsync(g => g.Id, g => g.Weight, ct);
        foreach (var res in results)
        {
            var mine = assignments.Where(a => a.EmployeeId == res.EmployeeId).Select(a => (a.Kind, P: Parse(a.AnswersJson))).ToList();
            var goalSource = mine.FirstOrDefault(x => x.Kind == ReviewKind.Manager).P ?? mine.FirstOrDefault(x => x.Kind == ReviewKind.Self).P;
            decimal? goalScore = null;
            if (goalSource is { Goals.Count: > 0 } gs) { var w = gs.Goals.Sum(kv => weights.GetValueOrDefault(kv.Key, 1)); goalScore = w == 0 ? (decimal)gs.Goals.Values.Average() : gs.Goals.Sum(kv => kv.Value * weights.GetValueOrDefault(kv.Key, 1)) / w; }
            decimal? Avg(IEnumerable<Parsed> ps) { var v = ps.SelectMany(p => p.Competencies.Values).ToList(); return v.Count == 0 ? null : (decimal)v.Average(); }
            var parts = new List<(decimal Score, decimal Weight)>();
            if (Avg(mine.Where(x => x.Kind == ReviewKind.Manager).Select(x => x.P)) is { } m) parts.Add((m, 60));
            if (Avg(mine.Where(x => x.Kind == ReviewKind.Peer).Select(x => x.P)) is { } p) parts.Add((p, 20));
            if (Avg(mine.Where(x => x.Kind == ReviewKind.Self).Select(x => x.P)) is { } s) parts.Add((s, 20));
            decimal? compScore = parts.Count == 0 ? null : parts.Sum(x => x.Score * x.Weight) / parts.Sum(x => x.Weight);
            res.GoalScore = goalScore is { } g1 ? Math.Round(g1, 2) : null; res.CompetencyScore = compScore is { } c1 ? Math.Round(c1, 2) : null;
            res.FinalScore = (goalScore, compScore) switch
            {
                ({ } g, { } k) => Math.Round((g * c.GoalsWeight + k * c.CompetencyWeight) / 100, 2),
                ({ } g, null) => Math.Round(g, 2), (null, { } k) => Math.Round(k, 2), _ => null,
            };
            res.UpdatedAt = clock.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>9-box from performance (1–2 low, 3 medium, 4–5 high) and potential (1–3).</summary>
    public static string? Box(int? rating, int? potential)
    {
        if (rating is not { } r || potential is not { } p) return null;
        var perf = r <= 2 ? 1 : r == 3 ? 2 : 3;
        return (perf, p) switch
        {
            (3, 3) => "star", (2, 3) => "high_potential", (1, 3) => "enigma", (3, 2) => "high_performer", (2, 2) => "core_player", (1, 2) => "dilemma",
            (3, 1) => "workhorse", (2, 1) => "effective", _ => "underperformer",
        };
    }

    public async Task<IReadOnlyList<ResultRow>> ResultsAsync(Guid cycleId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await RecomputeAsync(cycleId, ct);
        var rows = await db.ReviewResults.AsNoTracking().Where(r => r.CycleId == cycleId).ToListAsync(ct); var ids = rows.Select(r => r.EmployeeId).ToList();
        var emps = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.EmployeeNo, e.FullName, Unit = e.Employments.Where(m => m.EffectiveTo == null).Select(m => m.OrgUnitId).FirstOrDefault() }).ToDictionaryAsync(e => e.Id, ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var counts = await db.ReviewAssignments.AsNoTracking().Where(a => a.CycleId == cycleId).GroupBy(a => a.EmployeeId).Select(g => new { g.Key, N = g.Count(), S = g.Count(x => x.Submitted) }).ToDictionaryAsync(x => x.Key, ct);
        audit.Record("hr.review.results_viewed", "review_cycle", cycleId.ToString(), Outcome.Success, client, actor.UserId, actor.Email); await db.SaveChangesAsync(ct);
        return [.. rows.Select(r => { var e = emps[r.EmployeeId]; var n = counts.GetValueOrDefault(r.EmployeeId);
            return new ResultRow(r.EmployeeId, e.EmployeeNo, e.FullName, e.Unit is { } u ? units.GetValueOrDefault(u) : null, r.GoalScore, r.CompetencyScore, r.FinalScore, r.CalibratedRating, r.Potential, r.CalibrationNote, r.Released, n?.S ?? 0, n?.N ?? 0,
                Box(r.CalibratedRating ?? (r.FinalScore is { } f ? (int)Math.Round(f, MidpointRounding.AwayFromZero) : null), r.Potential)); }).OrderBy(r => r.EmployeeName)];
    }

    public async Task<CycleView> SetCycleStatusAsync(Guid id, string action, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.ReviewCycles.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Review cycle");
        switch (action)
        {
            case "calibrate": if (c.Status != ReviewCycleStatus.Open) throw DomainException.Conflict("Only an open cycle moves to calibration."); c.Status = ReviewCycleStatus.Calibration; break;
            case "close":
                if (c.Status != ReviewCycleStatus.Calibration) throw DomainException.Conflict("Calibrate before closing.");
                await RecomputeAsync(id, ct);
                c.Status = ReviewCycleStatus.Closed; c.ClosedAt = clock.UtcNow;
                foreach (var r in await db.ReviewResults.Where(x => x.CycleId == id).ToListAsync(ct)) { r.Released = true; r.CalibratedRating ??= r.FinalScore is { } f ? (int)Math.Round(f, MidpointRounding.AwayFromZero) : null; }
                foreach (var uid in await (from r in db.ReviewResults join e in db.Employees on r.EmployeeId equals e.Id where r.CycleId == id && e.UserId != null select e.UserId!.Value).ToListAsync(ct)) notes.Notify(uid, "review.released", $"Your performance review is ready: {c.Name}", null, "/hr/reviews");
                break;
            default: throw DomainException.Validation("Unknown action.", new Dictionary<string, string> { ["action"] = "Use calibrate or close." });
        }
        c.Version++;
        audit.Record($"hr.review_cycle.{action}", "review_cycle", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return await CycleViewAsync(c, ct);
    }

    public async Task<ResultRow> CalibrateAsync(Guid cycleId, CalibrateRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.ReviewCycles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cycleId, ct) ?? throw DomainException.NotFound("Review cycle");
        if (c.Status is not (ReviewCycleStatus.Open or ReviewCycleStatus.Calibration)) throw DomainException.Conflict("Results can be calibrated until the cycle is closed.");
        var res = await db.ReviewResults.FirstOrDefaultAsync(x => x.CycleId == cycleId && x.EmployeeId == r.EmployeeId, ct) ?? throw DomainException.NotFound("Result");
        if (r.Rating is < 1 or > 5 || r.Potential is < 1 or > 3) throw DomainException.Validation("Please check the calibration.", new Dictionary<string, string> { ["rating"] = "Rating 1–5, potential 1–3." });
        if (r.Rating is not null) res.CalibratedRating = r.Rating; if (r.Potential is not null) res.Potential = r.Potential;
        if (r.Note is not null) res.CalibrationNote = r.Note.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 500)] : null;
        res.UpdatedAt = clock.UtcNow;
        audit.Record("hr.review.calibrate", "employee", res.EmployeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["rating"] = res.CalibratedRating?.ToString() ?? "-", ["potential"] = res.Potential?.ToString() ?? "-" });
        await db.SaveChangesAsync(ct);
        return (await ResultsAsync(cycleId, actor, client, ct)).First(x => x.EmployeeId == res.EmployeeId);
    }

    /// <summary>The person's own released results: scores, the settled rating, and what reviewers wrote (peer comments without names).</summary>
    public async Task<IReadOnlyList<MyResult>> MyResultsAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct);
        var rows = await (from r in db.ReviewResults.AsNoTracking() join c in db.ReviewCycles.AsNoTracking() on r.CycleId equals c.Id where r.EmployeeId == me.Id && r.Released orderby c.ClosedAt descending select new { r, c.Name }).ToListAsync(ct);
        var list = new List<MyResult>();
        foreach (var x in rows)
        {
            var answers = (await db.ReviewAssignments.AsNoTracking().Where(a => a.CycleId == x.r.CycleId && a.EmployeeId == me.Id && a.Submitted && a.Kind != ReviewKind.Self).ToListAsync(ct)).Select(a => Parse(a.AnswersJson)).OrderBy(_ => Random.Shared.Next()).ToList();
            list.Add(new MyResult(x.r.CycleId, x.Name, x.r.GoalScore, x.r.CompetencyScore, x.r.FinalScore, x.r.CalibratedRating, [.. answers.Select(a => a.Strengths).OfType<string>()], [.. answers.Select(a => a.Improvements).OfType<string>()], [.. answers.Select(a => a.Comment).OfType<string>()]));
        }
        return list;
    }

    /// <summary>Talent pool: everyone placed in the 9-box in the latest closed cycle.</summary>
    public async Task<IReadOnlyList<TalentRow>> TalentPoolAsync(CancellationToken ct = default)
    {
        var cycle = await db.ReviewCycles.AsNoTracking().Where(c => c.Status == ReviewCycleStatus.Closed).OrderByDescending(c => c.ClosedAt).FirstOrDefaultAsync(ct); if (cycle is null) return [];
        var rows = await db.ReviewResults.AsNoTracking().Where(r => r.CycleId == cycle.Id && r.CalibratedRating != null && r.Potential != null).ToListAsync(ct); var ids = rows.Select(r => r.EmployeeId).ToList();
        var emps = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.FullName, Job = e.Employments.Where(m => m.EffectiveTo == null).Select(m => new { m.JobTitle, m.OrgUnitId }).FirstOrDefault() }).ToDictionaryAsync(e => e.Id, ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return [.. rows.Where(r => emps.ContainsKey(r.EmployeeId)).Select(r => { var e = emps[r.EmployeeId]; return new TalentRow(r.EmployeeId, e.FullName, e.Job?.JobTitle, e.Job?.OrgUnitId is { } u ? units.GetValueOrDefault(u) : null, r.CalibratedRating!.Value, r.Potential!.Value, Box(r.CalibratedRating, r.Potential)!, cycle.Name); }).OrderByDescending(t => t.Rating + t.Potential)];
    }

    // ============================================================ individual development plans
    private static List<IdpAction> Actions(string json) { try { return JsonSerializer.Deserialize<List<IdpAction>>(json, Json) ?? []; } catch (JsonException) { return []; } }

    public async Task<IReadOnlyList<IdpView>> PlansAsync(Guid? employeeId, SessionPrincipal actor, CancellationToken ct = default)
    {
        var me = await MeOrNullAsync(actor, ct); var hr = await IsHrAsync(actor, ct); var reports = await ReportsAsync(actor, ct);
        var q = db.DevelopmentPlans.AsNoTracking().AsQueryable();
        if (employeeId is { } e) { if (!hr && e != me?.Id && !reports.Contains(e)) throw DomainException.NotFound("Employee"); q = q.Where(p => p.EmployeeId == e); }
        else if (me is not null) q = q.Where(p => p.EmployeeId == me.Id); else throw DomainException.Invariant("Your account is not linked to an employee record. Ask HR to link it.");
        var rows = await q.OrderByDescending(p => p.UpdatedAt).ToListAsync(ct); var ids = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var comps = await db.Competencies.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return [.. rows.Select(p => { var acts = Actions(p.ActionsJson); return new IdpView(p.Id, p.EmployeeId, names.GetValueOrDefault(p.EmployeeId) ?? "", p.Title, p.Aim, p.CompetencyId, p.CompetencyId is { } c ? comps.GetValueOrDefault(c) : null, acts, p.Status.ToString().ToLower(), acts.Count(a => a.Done), p.UpdatedAt, hr || p.EmployeeId == me?.Id || reports.Contains(p.EmployeeId)); })];
    }

    public async Task<IdpView> SavePlanAsync(Guid? id, IdpRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var me = await MeOrNullAsync(actor, ct); var hr = await IsHrAsync(actor, ct); var reports = await ReportsAsync(actor, ct);
        var p = id is { } pid ? await db.DevelopmentPlans.FirstOrDefaultAsync(x => x.Id == pid, ct) ?? throw DomainException.NotFound("Plan") : new DevelopmentPlan { CreatedBy = actor.UserId, CreatedAt = clock.UtcNow, EmployeeId = r.EmployeeId ?? me?.Id ?? Guid.Empty };
        if (!hr && p.EmployeeId != me?.Id && !reports.Contains(p.EmployeeId)) throw DomainException.NotFound("Plan");
        var errors = new Dictionary<string, string>();
        var title = (r.Title ?? p.Title).Trim(); if (title.Length is < 2 or > 200) errors["title"] = "Name the plan (up to 200 characters).";
        var acts = r.Actions?.Select(a => a with { Id = string.IsNullOrWhiteSpace(a.Id) ? Guid.NewGuid().ToString("N")[..8] : a.Id[..Math.Min(a.Id.Length, 20)], Text = a.Text?.Trim() ?? "" }).ToList() ?? Actions(p.ActionsJson);
        if (acts.Count > 30 || acts.Any(a => a.Text.Length is < 1 or > 300)) errors["actions"] = "Up to 30 actions, each up to 300 characters.";
        var status = p.Status; if (r.Status is { } s && !Enum.TryParse(s, true, out status)) errors["status"] = "Use draft, active or done.";
        if (r.CompetencyId is { } cid && !await db.Competencies.AnyAsync(x => x.Id == cid, ct)) errors["competencyId"] = "Choose a competency.";
        if (!await db.Employees.AnyAsync(e => e.Id == p.EmployeeId, ct)) errors["employeeId"] = "Choose an employee.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the plan.", errors);
        p.Title = title; p.Aim = r.Aim is null ? p.Aim : r.Aim.Trim() is { Length: > 0 } aim ? aim[..Math.Min(aim.Length, 1000)] : null; p.CompetencyId = r.CompetencyId ?? p.CompetencyId;
        p.ActionsJson = JsonSerializer.Serialize(acts, Json); p.Status = status; p.UpdatedAt = clock.UtcNow;
        if (id is null) db.DevelopmentPlans.Add(p);
        audit.Record(id is null ? "hr.idp.create" : "hr.idp.update", "employee", p.EmployeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return (await PlansAsync(p.EmployeeId, actor, ct)).First(x => x.Id == p.Id);
    }

    // ============================================================ succession
    public async Task<IReadOnlyList<SuccessionView>> SuccessionAsync(CancellationToken ct = default)
    {
        var plans = await db.SuccessionPlans.AsNoTracking().OrderByDescending(p => p.Critical).ThenBy(p => p.Position).ToListAsync(ct); var planIds = plans.Select(p => p.Id).ToList();
        var cands = await db.SuccessionCandidates.AsNoTracking().Where(c => planIds.Contains(c.PlanId)).ToListAsync(ct);
        var empIds = cands.Select(c => c.EmployeeId).Concat(plans.Where(p => p.IncumbentEmployeeId != null).Select(p => p.IncumbentEmployeeId!.Value)).Distinct().ToList();
        var emps = await db.Employees.AsNoTracking().Where(e => empIds.Contains(e.Id)).Select(e => new { e.Id, e.FullName, Job = e.Employments.Where(m => m.EffectiveTo == null).Select(m => m.JobTitle).FirstOrDefault() }).ToDictionaryAsync(e => e.Id, ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var boxes = (await TalentPoolAsync(ct)).ToDictionary(t => t.EmployeeId, t => t.Box);
        return [.. plans.Select(p => new SuccessionView(p.Id, p.Position, p.OrgUnitId, p.OrgUnitId is { } u ? units.GetValueOrDefault(u) : null, p.IncumbentEmployeeId, p.IncumbentEmployeeId is { } i ? emps.GetValueOrDefault(i)?.FullName : null, p.Critical, p.Note,
            [.. cands.Where(c => c.PlanId == p.Id).OrderBy(c => c.Readiness).Select(c => new SuccessionCandidateView(c.Id, c.EmployeeId, emps.GetValueOrDefault(c.EmployeeId)?.FullName ?? "", emps.GetValueOrDefault(c.EmployeeId)?.Job, Snake(c.Readiness.ToString()), c.Note, boxes.GetValueOrDefault(c.EmployeeId)))]))];
    }

    public async Task<SuccessionView> SaveSuccessionAsync(Guid? id, SuccessionRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var p = id is { } pid ? await db.SuccessionPlans.FirstOrDefaultAsync(x => x.Id == pid, ct) ?? throw DomainException.NotFound("Plan") : new SuccessionPlan { CreatedAt = clock.UtcNow };
        var position = (r.Position ?? p.Position).Trim(); if (position.Length is < 2 or > 150) throw DomainException.Validation("Please check the position.", new Dictionary<string, string> { ["position"] = "Name the position (up to 150 characters)." });
        if (r.IncumbentEmployeeId is { } inc && !await db.Employees.AnyAsync(e => e.Id == inc, ct)) throw DomainException.Validation("Please check the position.", new Dictionary<string, string> { ["incumbentEmployeeId"] = "Choose an employee." });
        p.Position = position; p.OrgUnitId = r.OrgUnitId ?? p.OrgUnitId; p.IncumbentEmployeeId = r.IncumbentEmployeeId ?? p.IncumbentEmployeeId; p.Critical = r.Critical ?? p.Critical; p.Note = r.Note is null ? p.Note : r.Note.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 500)] : null;
        if (id is null) db.SuccessionPlans.Add(p);
        audit.Record(id is null ? "hr.succession.create" : "hr.succession.update", "succession", p.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return (await SuccessionAsync(ct)).First(x => x.Id == p.Id);
    }

    public async Task<SuccessionView> AddCandidateAsync(Guid planId, CandidateRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        if (!await db.SuccessionPlans.AnyAsync(x => x.Id == planId, ct)) throw DomainException.NotFound("Plan");
        if (r.EmployeeId is not { } e || !await db.Employees.AnyAsync(x => x.Id == e && x.Status == EmployeeStatus.Active, ct)) throw DomainException.Validation("Please check the candidate.", new Dictionary<string, string> { ["employeeId"] = "Choose an active employee." });
        var readiness = Readiness.OneToTwoYears; if (r.Readiness is { } rs && !Enum.TryParse(rs.Replace("_", ""), true, out readiness)) throw DomainException.Validation("Please check the candidate.", new Dictionary<string, string> { ["readiness"] = "Use ready_now, one_to_two_years or three_plus_years." });
        var c = await db.SuccessionCandidates.FirstOrDefaultAsync(x => x.PlanId == planId && x.EmployeeId == e, ct);
        if (c is null) { c = new SuccessionCandidate { PlanId = planId, EmployeeId = e }; db.SuccessionCandidates.Add(c); }
        c.Readiness = readiness; c.Note = r.Note?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 300)] : null;
        audit.Record("hr.succession.candidate", "succession", planId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeId"] = e.ToString(), ["readiness"] = readiness.ToString() });
        await db.SaveChangesAsync(ct);
        return (await SuccessionAsync(ct)).First(x => x.Id == planId);
    }

    public async Task RemoveCandidateAsync(Guid planId, Guid candidateId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.SuccessionCandidates.FirstOrDefaultAsync(x => x.Id == candidateId && x.PlanId == planId, ct) ?? throw DomainException.NotFound("Candidate");
        db.SuccessionCandidates.Remove(c); audit.Record("hr.succession.candidate_remove", "succession", planId.ToString(), Outcome.Success, client, actor.UserId, actor.Email); await db.SaveChangesAsync(ct);
    }
}
