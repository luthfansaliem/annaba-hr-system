using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Hr;

/// <summary>Departments (ADM-001). Codes are unique per organisation, parents may not form a cycle, a unit with active staff cannot be archived.</summary>
public sealed class OrgUnitService(IAppDb db, IClock clock, AuditWriter audit)
{
    public async Task<IReadOnlyList<OrgUnitItem>> ListAsync(bool includeArchived, CancellationToken ct = default)
    {
        var units = await db.OrgUnits.AsNoTracking().Where(u => includeArchived || u.ArchivedAt == null).OrderBy(u => u.Code).ToListAsync(ct);
        var counts = await db.Employments.AsNoTracking().Where(e => e.EffectiveTo == null && e.OrgUnitId != null && db.Employees.Any(x => x.Id == e.EmployeeId && x.Status == EmployeeStatus.Active))
            .GroupBy(e => e.OrgUnitId!.Value).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);
        var names = units.ToDictionary(u => u.Id, u => u.Name);
        return [.. units.Select(u => new OrgUnitItem(u.Id, u.Code, u.Name, u.ParentId, u.ParentId is { } p ? names.GetValueOrDefault(p) : null, u.ArchivedAt != null, counts.GetValueOrDefault(u.Id)))];
    }

    public async Task<OrgUnitItem> CreateAsync(OrgUnitRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var (code, name) = Validate(r);
        if (await db.OrgUnits.AnyAsync(u => u.Code == code, ct)) throw DomainException.Conflict($"Department code '{code}' is already used.");
        if (r.ParentId is { } p && !await db.OrgUnits.AnyAsync(u => u.Id == p && u.ArchivedAt == null, ct)) throw DomainException.Validation("Parent department not found.", new Dictionary<string, string> { ["parentId"] = "Choose an existing department." });
        var unit = new OrgUnit { Code = code, Name = name, ParentId = r.ParentId, CreatedAt = clock.UtcNow };
        db.OrgUnits.Add(unit);
        audit.Record("hr.org_unit.create", "org_unit", unit.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["code"] = code, ["name"] = name });
        await db.SaveChangesAsync(ct);
        return (await ListAsync(true, ct)).First(x => x.Id == unit.Id);
    }

    public async Task<OrgUnitItem> UpdateAsync(Guid id, OrgUnitRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var unit = await db.OrgUnits.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw DomainException.NotFound("Department");
        var diff = new List<AuditDiff>();
        if (r.Code is not null || r.Name is not null)
        {
            var (code, name) = Validate(new OrgUnitRequest(r.Code ?? unit.Code, r.Name ?? unit.Name, null, null));
            if (code != unit.Code && await db.OrgUnits.AnyAsync(u => u.Code == code && u.Id != id, ct)) throw DomainException.Conflict($"Department code '{code}' is already used.");
            if (code != unit.Code) diff.Add(new("code", unit.Code, code));
            if (name != unit.Name) diff.Add(new("name", unit.Name, name));
            unit.Code = code; unit.Name = name;
        }
        // The edit form always sends the full parent value (null = top level), so any difference is a move.
        if (r.ParentId != unit.ParentId)
        {
            if (r.ParentId is { } np)
            {
                if (np == id) throw DomainException.Invariant("A department cannot be its own parent.");
                if (!await db.OrgUnits.AnyAsync(u => u.Id == np && u.ArchivedAt == null, ct)) throw DomainException.Validation("Parent department not found.", new Dictionary<string, string> { ["parentId"] = "Choose an existing department." });
                var all = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.ParentId, ct);
                for (Guid? cur = np; cur is { } c; cur = all.GetValueOrDefault(c)) if (c == id) throw DomainException.Invariant("That would make the department its own ancestor.");
            }
            diff.Add(new("parentId", unit.ParentId?.ToString() ?? "", r.ParentId?.ToString() ?? ""));
            unit.ParentId = r.ParentId;
        }
        if (r.Archived is { } arch && arch != (unit.ArchivedAt != null))
        {
            if (arch)
            {
                if (await db.OrgUnits.AnyAsync(u => u.ParentId == id && u.ArchivedAt == null, ct)) throw DomainException.Invariant("Archive or move its sub-departments first.");
                var staff = await db.Employments.CountAsync(e => e.EffectiveTo == null && e.OrgUnitId == id && db.Employees.Any(x => x.Id == e.EmployeeId && x.Status == EmployeeStatus.Active), ct);
                if (staff > 0) throw DomainException.Invariant($"{staff} active employee(s) still belong to this department. Move them first.");
            }
            unit.ArchivedAt = arch ? clock.UtcNow : null; diff.Add(new("archived", (!arch).ToString(), arch.ToString()));
        }
        if (diff.Count > 0) audit.Record("hr.org_unit.update", "org_unit", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, diff: diff);
        await db.SaveChangesAsync(ct);
        return (await ListAsync(true, ct)).First(x => x.Id == id);
    }

    private static (string Code, string Name) Validate(OrgUnitRequest r)
    {
        var errors = new Dictionary<string, string>();
        var code = (r.Code ?? "").Trim().ToUpperInvariant(); var name = (r.Name ?? "").Trim();
        if (code.Length is < 2 or > 30 || !code.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')) errors["code"] = "Use 2–30 letters, digits, - or _.";
        if (name.Length is < 2 or > 150) errors["name"] = "Enter a name of 2–150 characters.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the department details.", errors);
        return (code, name);
    }
}
