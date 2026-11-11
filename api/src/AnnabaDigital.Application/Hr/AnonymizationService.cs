using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Hr;

public sealed record AnonymizeRequest(string? Reason, bool Force = false);

public sealed class HrRetentionOptions
{
    /// <summary>Years after leaving before personal data is erased (payroll/tax evidence is commonly kept 10 years: confirm with the tax consultant).</summary>
    public int AnonymizeAfterYears { get; set; } = 10;
    /// <summary>Off unless an operator turns it on: erasure cannot be undone.</summary>
    public bool AutoAnonymize { get; set; }
}

/// <summary>
/// Retention and erasure. A former employee's identity (name, contact, birth date, address, protected identifiers, bank details, documents, tasks, change requests) is erased once the retention
/// period after leaving has passed — or earlier on a granted erasure request (<c>Force</c>), which needs a reason and the HR-configuration permission. Payroll amounts stay (they are tax evidence);
/// the person's name on locked pay-run items is replaced by a neutral label through the one controlled exception in the database trigger. Irreversible, so it is never on by default.
/// </summary>
public sealed class AnonymizationService(IAppDb db, IClock clock, ITenantContext tenant, AuditWriter audit)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    public async Task<int> RunAllTenantsAsync(int years, CancellationToken ct = default)
    {
        var n = 0;
        foreach (var id in await db.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct)) { using var _ = tenant.Switch(id); n += await RunAsync(years, ct); }
        return n;
    }

    public async Task<int> RunAsync(int years, CancellationToken ct = default)
    {
        var cutoff = Today.AddYears(-Math.Max(years, 1));
        var ids = await db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Terminated && e.AnonymizedAt == null && e.TerminationDate != null && e.TerminationDate < cutoff).Select(e => e.Id).ToListAsync(ct);
        foreach (var id in ids) await EraseAsync(id, "Retention period passed", null, null, null, ct);
        return ids.Count;
    }

    public async Task AnonymizeAsync(Guid employeeId, AnonymizeRequest r, int retentionYears, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employeeId, ct) ?? throw DomainException.NotFound("Employee");
        if (e.Status != EmployeeStatus.Terminated) throw DomainException.Invariant("Only a former employee can be anonymised. End the employment first.");
        if (e.AnonymizedAt != null) throw DomainException.Conflict("This employee is already anonymised.");
        if (string.IsNullOrWhiteSpace(r.Reason)) throw DomainException.Validation("Please give a reason.", new Dictionary<string, string> { ["reason"] = "Say why (retention passed, erasure request…)." });
        var due = e.TerminationDate is { } t && t < Today.AddYears(-Math.Max(retentionYears, 1));
        if (!due && !r.Force) throw DomainException.Invariant($"Payroll records must be kept for {retentionYears} years after leaving. Use an explicit erasure request (force) only after legal sign-off.");
        await EraseAsync(employeeId, r.Reason!.Trim(), actor, client, r.Force && !due, ct);
    }

    private async Task EraseAsync(Guid id, string reason, SessionPrincipal? actor, ClientInfo? client, bool? forced, CancellationToken ct)
    {
        await db.InTransactionAsync(async () =>
        {
            await db.ExecuteSqlAsync($"SELECT set_config('hr.anonymizing', 'on', true)", ct); // the trigger allows only name changes on locked items while this is set, inside this transaction
            var e = await db.Employees.FirstAsync(x => x.Id == id, ct); var label = $"Former employee {e.EmployeeNo}";
            e.FullName = label; e.WorkEmail = null; e.Phone = null; e.BirthDate = null; e.Gender = null; e.MaritalStatus = null; e.Address = null; e.EmergencyContactName = null; e.EmergencyContactPhone = null;
            e.NationalIdEnc = null; e.TaxIdEnc = null; e.BankAccountEnc = null; e.BpjsKesNoEnc = null; e.BpjsTkNoEnc = null; e.Religion = null; e.BankName = null; e.TerminationReason = null; e.UserId = null; e.AnonymizedAt = clock.UtcNow; e.UpdatedAt = clock.UtcNow; e.Version++;
            await db.EmployeeDocuments.Where(d => d.EmployeeId == id).ExecuteDeleteAsync(ct);
            await db.EmployeeTasks.Where(t => t.EmployeeId == id).ExecuteDeleteAsync(ct);
            await db.ChangeRequests.Where(c => c.EmployeeId == id).ExecuteDeleteAsync(ct);
            await db.PayRunItems.Where(i => i.EmployeeId == id).ExecuteUpdateAsync(s => s.SetProperty(i => i.EmployeeName, label), ct);
            await db.WeeklyPayoutItems.Where(i => i.EmployeeId == id).ExecuteUpdateAsync(s => s.SetProperty(i => i.EmployeeName, label), ct);   // amounts stay; the payout trigger allows a name-only change
            audit.Record("hr.employee.anonymize", "employee", id.ToString(), Outcome.Success, client ?? new ClientInfo(null, null, null), actor?.UserId, actor?.Email ?? "system",
                new Dictionary<string, string> { ["employeeNo"] = e.EmployeeNo, ["reason"] = reason, ["forced"] = (forced ?? false).ToString() });
            await db.SaveChangesAsync(ct); return 0;
        }, ct);
    }
}
