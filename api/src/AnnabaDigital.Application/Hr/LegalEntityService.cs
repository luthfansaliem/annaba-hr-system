using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Hr;

public sealed record LegalEntityView(Guid Id, string Name, string? TaxId, string? Address, bool IsDefault, bool Archived, int ActiveEmployees, int PayRuns);
public sealed record LegalEntityRequest(string? Name, string? TaxId, string? Address, bool? IsDefault = null, bool? Archived = null);

/// <summary>
/// Companies under one organisation (ADM-005). Each has its own tax number and runs its own payroll: a pay run, the BPJS report, the journal and the annual tax file belong to one entity.
/// There is always exactly one default entity (created on first use) that receives employees when none is chosen. An entity with active staff cannot be archived, and the default cannot be archived.
/// </summary>
public sealed class LegalEntityService(IAppDb db, IClock clock, AuditWriter audit)
{
    /// <summary>The id of the default entity, creating it from the organisation's name the first time it is needed.</summary>
    public async Task<Guid> DefaultIdAsync(CancellationToken ct = default)
    {
        var id = await db.LegalEntities.AsNoTracking().Where(x => x.IsDefault).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (id is { } found) return found;
        var name = (await db.Tenants.AsNoTracking().Select(t => t.Name).FirstOrDefaultAsync(ct))?.Trim();
        var now = clock.UtcNow;
        db.LegalEntities.Add(new LegalEntity { Name = string.IsNullOrWhiteSpace(name) ? "Main company" : name!, IsDefault = true, CreatedAt = now, UpdatedAt = now });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.ResetTracking(); } // another request created it first (unique index: one default per tenant), or the name is taken: read it back below
        return await db.LegalEntities.AsNoTracking().Where(x => x.IsDefault).Select(x => x.Id).FirstAsync(ct);
    }

    /// <summary>Validates a chosen entity (active, exists) or falls back to the default.</summary>
    public async Task<Guid> ResolveAsync(Guid? requested, CancellationToken ct = default)
    {
        if (requested is not { } id) return await DefaultIdAsync(ct);
        if (!await db.LegalEntities.AnyAsync(x => x.Id == id && x.ArchivedAt == null, ct))
            throw DomainException.Validation("Please check the company.", new Dictionary<string, string> { ["legalEntityId"] = "Choose an existing company." });
        return id;
    }

    public async Task<IReadOnlyList<LegalEntityView>> ListAsync(bool includeArchived, CancellationToken ct = default)
    {
        await DefaultIdAsync(ct);
        var rows = await db.LegalEntities.AsNoTracking().Where(x => includeArchived || x.ArchivedAt == null).OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name).ToListAsync(ct);
        var staff = await db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active).GroupBy(e => e.LegalEntityId).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);
        var runs = await db.PayRuns.AsNoTracking().GroupBy(p => p.LegalEntityId).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);
        return [.. rows.Select(x => new LegalEntityView(x.Id, x.Name, x.TaxId, x.Address, x.IsDefault, x.ArchivedAt != null, staff.GetValueOrDefault(x.Id), runs.GetValueOrDefault(x.Id)))];
    }

    public async Task<LegalEntityView> CreateAsync(LegalEntityRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await DefaultIdAsync(ct);
        var (name, taxId, address) = Validate(r);
        if (await db.LegalEntities.AnyAsync(x => x.Name == name, ct)) throw DomainException.Conflict($"A company named '{name}' already exists.");
        var now = clock.UtcNow; var e = new LegalEntity { Name = name, TaxId = taxId, Address = address, CreatedAt = now, UpdatedAt = now };
        db.LegalEntities.Add(e);
        audit.Record("hr.legal_entity.create", "legal_entity", e.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ResetTracking(); throw DomainException.Conflict($"A company named '{name}' already exists."); }
        if (r.IsDefault == true) return await SetDefaultAsync(e.Id, actor, client, ct);
        return (await ListAsync(true, ct)).First(x => x.Id == e.Id);
    }

    public async Task<LegalEntityView> UpdateAsync(Guid id, LegalEntityRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = await db.LegalEntities.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Company");
        var (name, taxId, address) = Validate(r with { Name = r.Name ?? e.Name });
        var diff = new List<AuditDiff>();
        if (name != e.Name)
        {
            if (await db.LegalEntities.AnyAsync(x => x.Name == name && x.Id != id, ct)) throw DomainException.Conflict($"A company named '{name}' already exists.");
            diff.Add(new("name", e.Name, name)); e.Name = name;
        }
        if (taxId != e.TaxId) { diff.Add(new("taxId", e.TaxId ?? "", taxId ?? "")); e.TaxId = taxId; }
        if (address != e.Address) { diff.Add(new("address", e.Address ?? "", address ?? "")); e.Address = address; }
        if (r.Archived is { } arch && arch != (e.ArchivedAt != null))
        {
            if (arch)
            {
                if (e.IsDefault) throw DomainException.Invariant("The default company cannot be archived. Make another company the default first.");
                var staff = await db.Employees.CountAsync(x => x.LegalEntityId == id && x.Status == EmployeeStatus.Active, ct);
                if (staff > 0) throw DomainException.Invariant($"{staff} active employee(s) still belong to this company. Move them first.");
            }
            e.ArchivedAt = arch ? clock.UtcNow : null; diff.Add(new("archived", (!arch).ToString(), arch.ToString()));
        }
        e.UpdatedAt = clock.UtcNow;
        if (diff.Count > 0) audit.Record("hr.legal_entity.update", "legal_entity", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, diff: diff);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ResetTracking(); throw DomainException.Conflict($"A company named '{name}' already exists."); }
        if (r.IsDefault == true && !e.IsDefault) return await SetDefaultAsync(id, actor, client, ct);
        if (r.IsDefault == false && e.IsDefault) throw DomainException.Invariant("Choose another company as the default instead.");
        return (await ListAsync(true, ct)).First(x => x.Id == id);
    }

    private async Task<LegalEntityView> SetDefaultAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct)
    {
        await db.InTransactionAsync(async () =>
        {
            var target = await db.LegalEntities.FirstAsync(x => x.Id == id, ct);
            if (target.ArchivedAt != null) throw DomainException.Invariant("An archived company cannot be the default.");
            var old = await db.LegalEntities.Where(x => x.IsDefault && x.Id != id).ToListAsync(ct);
            foreach (var o in old) { o.IsDefault = false; o.UpdatedAt = clock.UtcNow; }
            await db.SaveChangesAsync(ct);                       // one default at a time: release the old one before the unique index sees the new one
            target.IsDefault = true; target.UpdatedAt = clock.UtcNow;
            audit.Record("hr.legal_entity.default", "legal_entity", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = target.Name });
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
        return (await ListAsync(true, ct)).First(x => x.Id == id);
    }

    private static (string Name, string? TaxId, string? Address) Validate(LegalEntityRequest r)
    {
        var errors = new Dictionary<string, string>();
        var name = (r.Name ?? "").Trim(); if (name.Length is < 2 or > 200) errors["name"] = "Enter a name of 2–200 characters.";
        string? taxId = null;
        if (!string.IsNullOrWhiteSpace(r.TaxId))
        {
            var digits = new string(r.TaxId.Where(char.IsDigit).ToArray());
            if (digits.Length is not (15 or 16) || r.TaxId.Any(c => !char.IsDigit(c) && c is not ('.' or '-' or ' '))) errors["taxId"] = "Enter the 15 or 16 digit tax number (NPWP).";
            else taxId = digits;
        }
        var address = string.IsNullOrWhiteSpace(r.Address) ? null : r.Address.Trim();
        if (address is { Length: > 500 }) errors["address"] = "Keep the address under 500 characters.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the company details.", errors);
        return (name, taxId, address);
    }
}
