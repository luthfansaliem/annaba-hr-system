using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record ApprovalRuleView(Guid Id, string Kind, Guid? LeaveTypeId, string? LeaveType, decimal MinAmount, string[] Steps, bool Active);
public sealed record ApprovalRuleRequest(string? Kind, Guid? LeaveTypeId, decimal? MinAmount, string[]? Steps, bool? Active);
/// <summary>Where a request stands: the steps of its chain, how many are done, who is asked now, and whether the caller may act on it.</summary>
public sealed record FlowState(string[] Chain, int Done, string? Waiting, bool CanAct, bool AlreadyApprovedByCaller);

/// <summary>
/// ADM-002. Who must approve a request, in what order. A chain is made of up to three steps, always in this order: "manager" (the direct manager, or whoever they delegated to),
/// "manager2" (the manager's manager) and "hr" (people with the manage permission of that area: leave.manage, claims.manage or attendance.manage).
/// Rules pick the chain by kind (leave, expense claim, overtime or attendance correction), optionally by leave type, and by size (days, rupiah or overtime hours): the rule with the highest threshold the request reaches wins.
/// The chain is fixed when the request is made. Each step is approved by a different person; a rejection at any step is final; a step nobody can take (no manager's manager) is skipped;
/// and the people with the manage permission can always decide a request outright, so nothing gets stuck.
/// </summary>
public sealed class ApprovalFlowService(IAppDb db, IClock clock, ManagerScope managers, AuditWriter audit, NotificationService notes)
{
    public static readonly string[] Kinds = ["leave", "claim", "overtime", "correction"];
    public static readonly string[] AllSteps = ["manager", "manager2", "hr"];
    public static string ManagePermission(string kind) => kind switch { "leave" => Permissions.LeaveManage, "overtime" or "correction" => Permissions.AttendanceManage, _ => Permissions.ClaimsManage };
    public static string ApprovePermission(string kind) => kind switch { "leave" => Permissions.LeaveApprove, "overtime" or "correction" => Permissions.AttendanceApprove, _ => Permissions.ClaimsApprove };
    public static string[] Parse(string? chain, string[] fallback) => string.IsNullOrEmpty(chain) ? fallback : chain.Split(',', StringSplitOptions.RemoveEmptyEntries);

    // ============================================================ rules
    public async Task<IReadOnlyList<ApprovalRuleView>> RulesAsync(CancellationToken ct = default)
    {
        var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        return [.. (await db.ApprovalRules.AsNoTracking().OrderBy(r => r.Kind).ThenBy(r => r.MinAmount).ToListAsync(ct)).Select(r => new ApprovalRuleView(r.Id, r.Kind, r.LeaveTypeId, r.LeaveTypeId is { } t ? types.GetValueOrDefault(t) : null, r.MinAmount, r.Steps.Split(','), r.Active))];
    }

    public async Task<ApprovalRuleView> SaveRuleAsync(Guid? id, ApprovalRuleRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var rule = id is null ? new ApprovalRule { CreatedAt = clock.UtcNow } : await db.ApprovalRules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Approval rule");
        var errors = new Dictionary<string, string>();
        var kind = (r.Kind ?? rule.Kind).ToLowerInvariant(); if (!Kinds.Contains(kind)) errors["kind"] = "Choose leave, claim, overtime or correction.";
        var steps = r.Steps ?? (id is null ? null : rule.Steps.Split(','));
        if (steps is null || steps.Length == 0 || steps.Any(s => !AllSteps.Contains(s)) || !steps.SequenceEqual(AllSteps.Where(steps.Contains))) errors["steps"] = "Choose one or more of manager, manager2, hr, in that order, each once.";
        var min = r.MinAmount ?? rule.MinAmount; if (min < 0 || min > 10_000_000_000m) errors["minAmount"] = "Enter 0 or more.";
        var leaveType = id is null ? r.LeaveTypeId : r.LeaveTypeId ?? rule.LeaveTypeId;
        if (leaveType is not null && kind != "leave") errors["leaveTypeId"] = "A leave type only applies to leave rules.";
        else if (leaveType is { } lt && !await db.LeaveTypes.AnyAsync(t => t.Id == lt, ct)) errors["leaveTypeId"] = "Choose an existing leave type.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the approval rule.", errors);
        if (await db.ApprovalRules.AnyAsync(x => x.Id != rule.Id && x.Kind == kind && x.LeaveTypeId == leaveType && x.MinAmount == min, ct)) throw DomainException.Conflict("There is already a rule for this kind and threshold.");
        rule.Kind = kind; rule.LeaveTypeId = leaveType; rule.MinAmount = min; rule.Steps = string.Join(',', steps!); rule.Active = r.Active ?? (id is null || rule.Active);
        if (id is null) db.ApprovalRules.Add(rule);
        audit.Record(id is null ? "hr.approval_rule.create" : "hr.approval_rule.update", "approval_rule", rule.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["kind"] = kind, ["from"] = min.ToString(System.Globalization.CultureInfo.InvariantCulture), ["steps"] = rule.Steps, ["active"] = rule.Active.ToString() });
        await db.SaveChangesAsync(ct);
        return (await RulesAsync(ct)).First(x => x.Id == rule.Id);
    }

    public async Task DeleteRuleAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var rule = await db.ApprovalRules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Approval rule");
        db.ApprovalRules.Remove(rule);
        audit.Record("hr.approval_rule.delete", "approval_rule", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["kind"] = rule.Kind, ["steps"] = rule.Steps });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The chain for a new request, or null when no rule applies (the caller then uses its built-in default).</summary>
    public async Task<string?> ChainForAsync(string kind, decimal amount, Guid? leaveTypeId, CancellationToken ct = default)
    {
        var rules = await db.ApprovalRules.AsNoTracking().Where(r => r.Active && r.Kind == kind && r.MinAmount <= amount && (r.LeaveTypeId == null || r.LeaveTypeId == leaveTypeId)).ToListAsync(ct);
        return rules.OrderByDescending(r => r.LeaveTypeId != null).ThenByDescending(r => r.MinAmount).FirstOrDefault()?.Steps;   // a rule for this very leave type beats a general one; then the highest threshold reached
    }

    // ============================================================ state and decisions
    private async Task<(Guid? Manager, Guid? Manager2)> ManagersOfAsync(Guid employeeId, CancellationToken ct)
    {
        var m = await db.Employments.AsNoTracking().Where(x => x.EmployeeId == employeeId && x.EffectiveTo == null).Select(x => x.ManagerId).FirstOrDefaultAsync(ct);
        var m2 = m is null ? null : await db.Employments.AsNoTracking().Where(x => x.EmployeeId == m && x.EffectiveTo == null).Select(x => x.ManagerId).FirstOrDefaultAsync(ct);
        return (m, m2 == employeeId ? null : m2);
    }

    /// <summary>States of several requests of one kind for one caller (one query for the recorded steps).</summary>
    public async Task<Dictionary<Guid, FlowState>> StatesAsync(string kind, IReadOnlyList<(Guid RequestId, Guid EmployeeId, string[] Chain)> requests, SessionPrincipal actor, IReadOnlySet<string> perms, CancellationToken ct = default)
    {
        var ids = requests.Select(r => r.RequestId).ToList();
        var done = (await db.ApprovalSteps.AsNoTracking().Where(s => s.Kind == kind && ids.Contains(s.RequestId)).ToListAsync(ct)).GroupBy(s => s.RequestId).ToDictionary(g => g.Key, g => g.ToList());
        var acting = (await managers.ActingIdsAsync(actor.UserId, ct)).ToHashSet(); var manage = perms.Contains(ManagePermission(kind)); var approve = perms.Contains(ApprovePermission(kind));
        var result = new Dictionary<Guid, FlowState>();
        foreach (var (requestId, employeeId, chain) in requests)
        {
            var taken = done.GetValueOrDefault(requestId) ?? []; var (m, m2) = await ManagersOfAsync(employeeId, ct);
            // the first step not yet taken that somebody can take ("manager2" without a manager's manager is skipped)
            string? waiting = null; var count = 0;
            foreach (var step in chain)
            {
                if (taken.Any(t => t.Approver == step)) { count++; continue; }
                if (step == "manager2" && m2 is null) { count++; continue; }
                waiting = step; break;
            }
            var mine = taken.Any(t => t.DecidedBy == actor.UserId);
            var eligible = waiting switch { "manager" => approve && m is { } a && acting.Contains(a), "manager2" => approve && m2 is { } b && acting.Contains(b), "hr" => manage, _ => false };
            result[requestId] = new FlowState(chain, count, waiting, manage || eligible && !mine, mine);
        }
        return result;
    }

    /// <summary>
    /// Records the caller's approval of the step that is waiting. Returns true when the request is now fully approved (the caller's step was the last one, or the caller holds the manage
    /// permission and decided it outright); false when it moves on to the next step, whose approvers are then notified.
    /// </summary>
    public async Task<bool> ApproveAsync(string kind, Guid requestId, Guid employeeId, string employeeName, string[] chain, SessionPrincipal actor, IReadOnlySet<string> perms, string link, CancellationToken ct = default)
    {
        var state = (await StatesAsync(kind, [(requestId, employeeId, chain)], actor, perms, ct))[requestId];
        if (state.AlreadyApprovedByCaller && !perms.Contains(ManagePermission(kind))) throw DomainException.Conflict($"You already approved this request. It is waiting for {Describe(state.Waiting)}.");
        if (!state.CanAct) throw DomainException.DeniedBecause($"This request is waiting for {Describe(state.Waiting)}.");
        if (perms.Contains(ManagePermission(kind)) && state.Waiting != "hr")
        {
            db.ApprovalSteps.Add(new ApprovalStep { Kind = kind, RequestId = requestId, StepNo = state.Done + 1, Approver = "override", DecidedBy = actor.UserId, DecidedAt = clock.UtcNow });
            return true;
        }
        db.ApprovalSteps.Add(new ApprovalStep { Kind = kind, RequestId = requestId, StepNo = state.Done + 1, Approver = state.Waiting!, DecidedBy = actor.UserId, DecidedAt = clock.UtcNow });
        var (m, m2) = await ManagersOfAsync(employeeId, ct);
        var next = chain.SkipWhile(s => s != state.Waiting).Skip(1).FirstOrDefault(s => s != "manager2" || m2 is not null);
        if (next is null) return true;
        IEnumerable<Guid> who = next == "hr" ? await notes.UsersWithPermissionAsync(ManagePermission(kind), ct)
            : await db.Employees.AsNoTracking().Where(e => e.Id == (next == "manager" ? m : m2) && e.UserId != null).Select(e => e.UserId!.Value).ToListAsync(ct);
        foreach (var uid in who.Where(u => u != actor.UserId)) notes.Notify(uid, next == "hr" ? $"{kind}.awaiting_hr" : $"{kind}.requested", $"{employeeName}'s {Noun(kind)} needs your approval", $"Step {state.Done + 2} of {chain.Length}", link);
        return false;
    }

    public static string Describe(string? step) => step switch { "manager" => "the manager", "manager2" => "the manager's manager", "hr" => "HR", _ => "nobody" };
    private static string Noun(string kind) => kind switch { "leave" => "leave", "overtime" => "overtime", "correction" => "attendance correction", _ => "expense claim" };
}
