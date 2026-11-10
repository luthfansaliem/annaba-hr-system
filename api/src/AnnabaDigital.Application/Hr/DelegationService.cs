using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record DelegationView(Guid Id, Guid DelegatorEmployeeId, string Delegator, Guid DelegateEmployeeId, string Delegate, DateOnly StartDate, DateOnly EndDate, bool Active, bool Mine, bool CanRevoke);
public sealed record CreateDelegationRequest(Guid? DelegatorEmployeeId, Guid? DelegateEmployeeId, DateOnly? StartDate, DateOnly? EndDate);
public sealed record DelegateCandidate(Guid EmployeeId, string EmployeeNo, string Name);

/// <summary>Whose requests a person may decide as a manager: their own direct reports plus those of anyone who has handed their approvals to them for today.</summary>
public sealed class ManagerScope(IAppDb db, IClock clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    /// <summary>The employee ids this user manages for: themself and every manager currently delegating to them.</summary>
    public async Task<List<Guid>> ActingIdsAsync(Guid userId, CancellationToken ct = default)
    {
        var me = await db.Employees.AsNoTracking().Where(e => e.UserId == userId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        if (me is null) return [];
        var today = Today;
        var ids = await db.ApprovalDelegations.AsNoTracking().Where(d => d.DelegateEmployeeId == me && d.RevokedAt == null && d.StartDate <= today && d.EndDate >= today).Select(d => d.DelegatorEmployeeId).ToListAsync(ct);
        ids.Add(me.Value); return ids;
    }

    /// <summary>Employees who currently decide in place of <paramref name="managerEmployeeId"/> (so they are told about new requests too).</summary>
    public async Task<List<Guid>> DelegatesOfAsync(Guid? managerEmployeeId, CancellationToken ct = default)
    {
        if (managerEmployeeId is null) return [];
        var today = Today;
        return await db.ApprovalDelegations.AsNoTracking().Where(d => d.DelegatorEmployeeId == managerEmployeeId && d.RevokedAt == null && d.StartDate <= today && d.EndDate >= today).Select(d => d.DelegateEmployeeId).ToListAsync(ct);
    }
}

/// <summary>LVE-004. A manager hands their approvals to a colleague for up to 90 days (HR can do it for them). The colleague must already hold the approval permissions: delegation moves a manager's scope, it never grants a permission.</summary>
public sealed class DelegationService(IAppDb db, IClock clock, AuthorizationService authz, AuditWriter audit)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    public async Task<IReadOnlyList<DelegationView>> ListAsync(bool all, SessionPrincipal actor, CancellationToken ct = default)
    {
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct); var hr = perms.Contains(Permissions.LeaveManage);
        if (all && !hr) throw DomainException.Denied(Permissions.LeaveManage);
        var me = await db.Employees.AsNoTracking().Where(e => e.UserId == actor.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        var q = db.ApprovalDelegations.AsNoTracking().Where(d => d.RevokedAt == null && d.EndDate >= Today);
        if (!all) q = q.Where(d => d.DelegatorEmployeeId == me || d.DelegateEmployeeId == me);
        var rows = await q.OrderBy(d => d.StartDate).ToListAsync(ct);
        var ids = rows.SelectMany(d => new[] { d.DelegatorEmployeeId, d.DelegateEmployeeId }).Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var today = Today;
        return [.. rows.Select(d => new DelegationView(d.Id, d.DelegatorEmployeeId, names[d.DelegatorEmployeeId], d.DelegateEmployeeId, names[d.DelegateEmployeeId], d.StartDate, d.EndDate, d.StartDate <= today, d.DelegatorEmployeeId == me, d.DelegatorEmployeeId == me || hr))];
    }

    /// <summary>People who could stand in: active employees whose login holds the leave approval permission.</summary>
    public async Task<IReadOnlyList<DelegateCandidate>> CandidatesAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var me = await db.Employees.AsNoTracking().Where(e => e.UserId == actor.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        return await db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active && e.UserId != null && e.Id != me
            && db.Users.Any(u => u.Id == e.UserId && u.Status == UserStatus.Active && u.UserRoles.Any(r => r.Role!.ArchivedAt == null && r.Role.Permissions.Any(p => p.PermissionKey == Permissions.LeaveApprove))))
            .OrderBy(e => e.FullName).Select(e => new DelegateCandidate(e.Id, e.EmployeeNo, e.FullName)).ToListAsync(ct);
    }

    public async Task<DelegationView> CreateAsync(CreateDelegationRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct); var hr = perms.Contains(Permissions.LeaveManage);
        var me = await db.Employees.AsNoTracking().Where(e => e.UserId == actor.UserId && e.Status == EmployeeStatus.Active).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        var delegatorId = r.DelegatorEmployeeId ?? me;
        if (delegatorId is null) throw DomainException.Invariant("Your account is not linked to an active employee record. Ask HR to link it.");
        if (delegatorId != me && !hr) throw DomainException.Denied(Permissions.LeaveManage);
        var errors = new Dictionary<string, string>(); var start = r.StartDate ?? Today; var end = r.EndDate ?? default;
        if (r.DelegateEmployeeId is null) errors["delegateEmployeeId"] = "Choose who will approve in your place.";
        if (r.EndDate is null) errors["endDate"] = "Choose the last day."; else if (end < start) errors["endDate"] = "The last day is before the first day."; else if (end.DayNumber - start.DayNumber > 90) errors["endDate"] = "A delegation lasts at most 90 days.";
        if (start < Today) errors["startDate"] = "The first day cannot be in the past.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the delegation.", errors);
        var delegateId = r.DelegateEmployeeId!.Value;
        if (delegateId == delegatorId) throw DomainException.Validation("Please check the delegation.", new Dictionary<string, string> { ["delegateEmployeeId"] = "You cannot delegate to yourself." });
        var cand = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == delegateId && e.Status == EmployeeStatus.Active && e.UserId != null, ct)
                   ?? throw DomainException.Validation("Please check the delegation.", new Dictionary<string, string> { ["delegateEmployeeId"] = "Choose an active employee who has a sign-in account." });
        if (!(await authz.GetEffectivePermissionsAsync(cand.UserId!.Value, ct)).Contains(Permissions.LeaveApprove))
            throw DomainException.Validation("Please check the delegation.", new Dictionary<string, string> { ["delegateEmployeeId"] = "That person cannot approve requests. Choose a manager or HR colleague." });
        if (await db.ApprovalDelegations.AnyAsync(d => d.DelegatorEmployeeId == delegatorId && d.RevokedAt == null && d.StartDate <= end && d.EndDate >= start, ct)) throw DomainException.Conflict("There is already a delegation in that period. Revoke it first.");
        var d = new ApprovalDelegation { DelegatorEmployeeId = delegatorId.Value, DelegateEmployeeId = delegateId, StartDate = start, EndDate = end, CreatedAt = clock.UtcNow, CreatedBy = actor.UserId };
        db.ApprovalDelegations.Add(d);
        audit.Record("hr.delegation.create", "employee", delegatorId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["delegate"] = cand.EmployeeNo, ["from"] = start.ToString("yyyy-MM-dd"), ["to"] = end.ToString("yyyy-MM-dd") });
        await db.SaveChangesAsync(ct);
        return (await ListAsync(hr && delegatorId != me, actor, ct)).First(x => x.Id == d.Id);
    }

    public async Task RevokeAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var d = await db.ApprovalDelegations.FirstOrDefaultAsync(x => x.Id == id && x.RevokedAt == null, ct) ?? throw DomainException.NotFound("Delegation");
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        var mine = await db.Employees.AsNoTracking().AnyAsync(e => e.UserId == actor.UserId && e.Id == d.DelegatorEmployeeId, ct);
        if (!mine && !perms.Contains(Permissions.LeaveManage)) throw DomainException.NotFound("Delegation");
        d.RevokedAt = clock.UtcNow;
        audit.Record("hr.delegation.revoke", "employee", d.DelegatorEmployeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
    }
}
