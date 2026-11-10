using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record SelfProfile(Guid EmployeeId, string EmployeeNo, string FullName, string? WorkEmail, string? JobTitle, string? OrgUnit, string? ManagerName, DateOnly HireDate, string? Phone, string? Address,
    string? EmergencyContactName, string? EmergencyContactPhone, string? MaritalStatus, string? BankName, bool HasBankAccount, bool HasTaxId, bool HasNationalId);
public sealed record UpdateSelfRequest(string? Phone, string? Address, string? EmergencyContactName, string? EmergencyContactPhone);
public sealed record SubmitChangeRequest(string? Field, string? Value, string? Reason);
public sealed record ChangeRequestView(Guid Id, Guid EmployeeId, string EmployeeNo, string EmployeeName, string Field, string? Value, bool ValueHidden, string? Reason, string Status, string? DecisionNote, DateTimeOffset CreatedAt, bool CanDecide, bool CanCancel);

/// <summary>
/// EMP-009 and the data-subject export. An employee edits contact details directly; anything that changes pay or identity (name, marital status, bank, tax ID, national ID) goes through a request that
/// HR approves — and the sensitive ones need the employee's recent password check first, because a hijacked session asking for a new bank account is the classic payroll fraud.
/// The export hands people their own record (and nobody else's) as JSON.
/// </summary>
public sealed class SelfServiceHrService(IAppDb db, IClock clock, EmployeeService employees, AuthorizationService authz, SettingsProvider settings, ISecretProtector protector, AuditWriter audit, NotificationService notes)
{
    public static readonly string[] Fields = ["full_name", "marital_status", "bank_name", "bank_account", "tax_id", "national_id"];
    private static readonly string[] Sensitive = ["bank_account", "tax_id", "national_id"];

    private async Task<Employee> MeAsync(SessionPrincipal actor, CancellationToken ct)
        => await db.Employees.FirstOrDefaultAsync(e => e.UserId == actor.UserId, ct) ?? throw DomainException.Invariant("Your account is not linked to an employee record. Ask HR to link it.");

    private async Task RequireFreshAsync(SessionPrincipal actor, CancellationToken ct)
        => Guards.RequireRecentReauthForCritical(actor, (await settings.GetAsync(ct)) with { RequireReauthForCritical = true }, clock);

    private string SealValue(Guid id, string v) { var s = protector.Protect(Encoding.UTF8.GetBytes(v), Encoding.UTF8.GetBytes($"changereq:{id}")); return $"{s.KeyVersion}.{Convert.ToBase64String(s.Nonce)}.{Convert.ToBase64String(s.Ciphertext)}"; }
    private string OpenValue(Guid id, string stored) { var p = stored.Split('.'); return Encoding.UTF8.GetString(protector.Unprotect(new ProtectedSecret(Convert.FromBase64String(p[2]), Convert.FromBase64String(p[1]), int.Parse(p[0])), Encoding.UTF8.GetBytes($"changereq:{id}"))); }

    // ============================================================ profile
    public async Task<SelfProfile> ProfileAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var e = await db.Employees.AsNoTracking().Include(x => x.Employments).AsSplitQuery().FirstOrDefaultAsync(x => x.UserId == actor.UserId, ct) ?? throw DomainException.Invariant("Your account is not linked to an employee record. Ask HR to link it.");
        var cur = e.Employments.FirstOrDefault(m => m.EffectiveTo == null);
        var unit = cur?.OrgUnitId is { } u ? await db.OrgUnits.AsNoTracking().Where(o => o.Id == u).Select(o => o.Name).FirstOrDefaultAsync(ct) : null;
        var mgr = cur?.ManagerId is { } m ? await db.Employees.AsNoTracking().Where(o => o.Id == m).Select(o => o.FullName).FirstOrDefaultAsync(ct) : null;
        return new SelfProfile(e.Id, e.EmployeeNo, e.FullName, e.WorkEmail, cur?.JobTitle, unit, mgr, e.HireDate, e.Phone, e.Address, e.EmergencyContactName, e.EmergencyContactPhone, e.MaritalStatus?.ToString().ToLowerInvariant(), e.BankName,
            e.BankAccountEnc != null, e.TaxIdEnc != null, e.NationalIdEnc != null);
    }

    public async Task<SelfProfile> UpdateAsync(UpdateSelfRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = await MeAsync(actor, ct); var diff = new List<AuditDiff>(); var errors = new Dictionary<string, string>();
        void Set(string field, string? incoming, int max, string? cur, Action<string?> set)
        {
            if (incoming is null) return; var v = EmployeeService.Clean(incoming, max);
            if (incoming.Trim().Length > max) { errors[field] = $"Keep it under {max} characters."; return; }
            if (v != cur) { diff.Add(new(field, cur ?? "", v ?? "")); set(v); }
        }
        Set("phone", r.Phone, 40, e.Phone, v => e.Phone = v); Set("address", r.Address, 500, e.Address, v => e.Address = v);
        Set("emergencyContactName", r.EmergencyContactName, 200, e.EmergencyContactName, v => e.EmergencyContactName = v); Set("emergencyContactPhone", r.EmergencyContactPhone, 40, e.EmergencyContactPhone, v => e.EmergencyContactPhone = v);
        if (errors.Count > 0) throw DomainException.Validation("Please check your details.", errors);
        if (diff.Count > 0)
        {
            e.UpdatedAt = clock.UtcNow; e.Version++;
            audit.Record("hr.self.update", "employee", e.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, null, diff);
            await db.SaveChangesAsync(ct);
        }
        return await ProfileAsync(actor, ct);
    }

    // ============================================================ change requests
    public async Task<ChangeRequestView> SubmitAsync(SubmitChangeRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = await MeAsync(actor, ct); var errors = new Dictionary<string, string>(); var field = (r.Field ?? "").Trim().ToLowerInvariant(); var value = r.Value?.Trim() ?? "";
        if (!Fields.Contains(field)) errors["field"] = "Choose what to change: " + string.Join(", ", Fields);
        else
        {
            if (value.Length is 0 or > 100) errors["value"] = "Enter the new value (up to 100 characters).";
            else if (field == "full_name" && value.Length < 2) errors["value"] = "Enter your full name.";
            else if (field == "marital_status" && !Enum.TryParse<MaritalStatus>(value, true, out var ms)) errors["value"] = "Use single, married, divorced or widowed.";
            else if (field is "bank_account" or "tax_id" or "national_id" && !value.Any(char.IsDigit)) errors["value"] = "That does not look like a number.";
        }
        var reason = r.Reason?.Trim(); if (reason is { Length: > 300 }) errors["reason"] = "Keep the reason under 300 characters.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the request.", errors);
        if (Sensitive.Contains(field)) await RequireFreshAsync(actor, ct);
        if (await db.ChangeRequests.AnyAsync(c => c.EmployeeId == e.Id && c.Field == field && c.Status == RequestStatus.Pending, ct)) throw DomainException.Conflict("You already have a pending request for that.");
        var cr = new EmployeeChangeRequest { EmployeeId = e.Id, Field = field, Reason = reason, CreatedAt = clock.UtcNow };
        cr.ValueEnc = SealValue(cr.Id, field == "marital_status" ? value.ToLowerInvariant() : value);
        db.ChangeRequests.Add(cr);
        audit.Record("hr.change_request.submit", "employee", e.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["field"] = field });
        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([cr], actor, ct))[0];
    }

    public async Task<IReadOnlyList<ChangeRequestView>> ListAsync(bool mine, string? status, SessionPrincipal actor, CancellationToken ct = default)
    {
        var q = db.ChangeRequests.AsNoTracking().AsQueryable();
        if (mine) { var me = await MeAsync(actor, ct); q = q.Where(c => c.EmployeeId == me.Id); }
        else if (!(await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(Permissions.EmployeesUpdate)) throw DomainException.Denied(Permissions.EmployeesUpdate);
        if (!string.IsNullOrWhiteSpace(status) && status != "all") { if (!Enum.TryParse<RequestStatus>(status, true, out var st)) throw DomainException.Validation("Unknown status.", new Dictionary<string, string> { ["status"] = "Use pending, approved, rejected or cancelled." }); q = q.Where(c => c.Status == st); }
        return await ViewsAsync(await q.OrderByDescending(c => c.Status == RequestStatus.Pending).ThenByDescending(c => c.CreatedAt).Take(200).ToListAsync(ct), actor, ct);
    }

    private async Task<IReadOnlyList<ChangeRequestView>> ViewsAsync(IReadOnlyList<EmployeeChangeRequest> rows, SessionPrincipal actor, CancellationToken ct)
    {
        var ids = rows.Select(r => r.EmployeeId).Distinct().ToList(); var emps = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct); var canUpdate = perms.Contains(Permissions.EmployeesUpdate);
        var fresh = true; try { await RequireFreshAsync(actor, ct); } catch (DomainException) { fresh = false; }
        return [.. rows.Select(r =>
        {
            var e = emps[r.EmployeeId]; var own = e.UserId == actor.UserId; var sensitive = Sensitive.Contains(r.Field);
            // the new value is shown to its owner and to HR; protected values need the viewer's own recent password check and the sensitive permission
            var show = own ? (!sensitive || fresh) : canUpdate && (!sensitive || perms.Contains(Permissions.EmployeesReadSensitive) && fresh);
            return new ChangeRequestView(r.Id, r.EmployeeId, e.EmployeeNo, e.FullName, r.Field, show ? OpenValue(r.Id, r.ValueEnc) : null, !show, r.Reason, r.Status.ToString().ToLowerInvariant(), r.DecisionNote, r.CreatedAt,
                r.Status == RequestStatus.Pending && !own && canUpdate, r.Status == RequestStatus.Pending && own);
        })];
    }

    public async Task<ChangeRequestView> DecideAsync(Guid id, bool approve, string? note, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var cr = await db.ChangeRequests.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Request");
        var e = await db.Employees.FirstAsync(x => x.Id == cr.EmployeeId, ct);
        if (e.UserId == actor.UserId) throw DomainException.Invariant("You cannot decide your own request.");
        var reason = note?.Trim(); if (!approve && string.IsNullOrEmpty(reason)) throw DomainException.Validation("Please give a reason.", new Dictionary<string, string> { ["note"] = "Tell the employee why." });
        if (reason is { Length: > 300 }) throw DomainException.Validation("Note is too long.", new Dictionary<string, string> { ["note"] = "Keep it under 300 characters." });
        if (approve && Sensitive.Contains(cr.Field)) await employees.RequireSensitiveWriteAsync(actor, ct); // sensitive permission + recent password check
        if (cr.Status != RequestStatus.Pending) throw DomainException.Conflict($"This request is already {cr.Status.ToString().ToLowerInvariant()}.");
        if (approve && e.Status != EmployeeStatus.Active) throw DomainException.Invariant("This employee's employment has ended.");
        if (approve)
        {
            var v = OpenValue(cr.Id, cr.ValueEnc);
            switch (cr.Field)
            {
                case "full_name": e.FullName = v; break;
                case "marital_status": e.MaritalStatus = Enum.Parse<MaritalStatus>(v, true); break;
                case "bank_name": e.BankName = v; break;
                case "bank_account": e.BankAccountEnc = employees.Seal(e, "bank_account", v); break;
                case "tax_id": e.TaxIdEnc = employees.Seal(e, "tax_id", v); break;
                case "national_id": e.NationalIdEnc = employees.Seal(e, "national_id", v); break;
            }
            e.UpdatedAt = clock.UtcNow; e.Version++;
        }
        cr.Status = approve ? RequestStatus.Approved : RequestStatus.Rejected; cr.DecidedBy = actor.UserId; cr.DecidedAt = clock.UtcNow; cr.DecisionNote = reason;
        notes.Notify(e.UserId, "change_request.decided", approve ? "Your profile change was approved" : "Your profile change was rejected", string.IsNullOrEmpty(reason) ? null : reason, "/hr/my-profile");
        audit.Record(approve ? "hr.change_request.approve" : "hr.change_request.reject", "employee", e.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["field"] = cr.Field },
            approve ? [new AuditDiff(cr.Field, Sensitive.Contains(cr.Field) ? "[protected]" : "[previous]", Sensitive.Contains(cr.Field) ? "[changed]" : "[new value]")] : null);
        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([cr], actor, ct))[0];
    }

    public async Task<ChangeRequestView> CancelAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct);
        var cr = await db.ChangeRequests.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == me.Id, ct) ?? throw DomainException.NotFound("Request");
        if (cr.Status != RequestStatus.Pending) throw DomainException.Conflict($"This request is already {cr.Status.ToString().ToLowerInvariant()}.");
        cr.Status = RequestStatus.Cancelled;
        audit.Record("hr.change_request.cancel", "employee", me.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["field"] = cr.Field });
        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([cr], actor, ct))[0];
    }

    // ============================================================ data-subject export
    /// <summary>Everything the system holds about the signed-in person and nobody else: profile, protected identifiers (they just re-entered their password), job history, leave, attendance, overtime, corrections, change requests, document list and payslip figures.</summary>
    public async Task<ExportFile> ExportAsync(SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await RequireFreshAsync(actor, ct);
        var e = await db.Employees.AsNoTracking().Include(x => x.Employments).AsSplitQuery().FirstOrDefaultAsync(x => x.UserId == actor.UserId, ct) ?? throw DomainException.Invariant("Your account is not linked to an employee record. Ask HR to link it.");
        string? Reveal(string field, string? stored) { if (stored is null) return null; var p = stored.Split('.'); return Encoding.UTF8.GetString(protector.Unprotect(new ProtectedSecret(Convert.FromBase64String(p[2]), Convert.FromBase64String(p[1]), int.Parse(p[0])), Encoding.UTF8.GetBytes($"employee:{e.Id}:{field}"))); }
        var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var data = new
        {
            exportedAt = clock.UtcNow, employee = new { e.EmployeeNo, e.FullName, e.WorkEmail, e.Phone, e.BirthDate, gender = e.Gender?.ToString(), maritalStatus = e.MaritalStatus?.ToString(), e.Address, e.EmergencyContactName, e.EmergencyContactPhone, e.HireDate, e.TerminationDate, e.TerminationReason,
                status = e.Status.ToString(), e.BankName, bankAccount = Reveal("bank_account", e.BankAccountEnc), taxId = Reveal("tax_id", e.TaxIdEnc), nationalId = Reveal("national_id", e.NationalIdEnc) },
            employments = e.Employments.OrderBy(m => m.EffectiveFrom).Select(m => new { m.EffectiveFrom, m.EffectiveTo, m.JobTitle, m.Grade, m.Location, type = m.Type.ToString(), m.ContractEnd, m.ChangeReason }),
            leaveRequests = (await db.LeaveRequests.AsNoTracking().Where(l => l.EmployeeId == e.Id).OrderBy(l => l.StartDate).ToListAsync(ct)).Select(l => new { type = types.GetValueOrDefault(l.LeaveTypeId), l.StartDate, l.EndDate, l.Days, status = l.Status.ToString(), l.Reason, l.DecisionNote }),
            leaveBalanceEntries = (await db.LeaveLedger.AsNoTracking().Where(l => l.EmployeeId == e.Id).OrderBy(l => l.CreatedAt).ToListAsync(ct)).Select(l => new { type = types.GetValueOrDefault(l.LeaveTypeId), l.Year, l.Days, kind = l.Kind.ToString(), l.Note, l.CreatedAt }),
            attendance = (await db.AttendanceLogs.AsNoTracking().Where(l => l.EmployeeId == e.Id).OrderBy(l => l.ClockIn).ToListAsync(ct)).Select(l => new { l.Date, l.ClockIn, l.ClockOut, l.Source, l.Ip }),
            attendanceCorrections = (await db.AttendanceCorrections.AsNoTracking().Where(c => c.EmployeeId == e.Id).ToListAsync(ct)).Select(c => new { c.Date, clockIn = c.ClockIn.ToString("HH:mm"), clockOut = c.ClockOut.ToString("HH:mm"), c.Reason, status = c.Status.ToString() }),
            overtime = (await db.OvertimeRequests.AsNoTracking().Where(o => o.EmployeeId == e.Id).ToListAsync(ct)).Select(o => new { o.Date, o.Hours, o.Reason, status = o.Status.ToString() }),
            documents = (await db.EmployeeDocuments.AsNoTracking().Where(d => d.EmployeeId == e.Id && d.DeletedAt == null).Select(d => new { d.Category, d.FileName, d.SizeBytes, d.ExpiresOn, d.UploadedAt }).ToListAsync(ct)),
            changeRequests = (await db.ChangeRequests.AsNoTracking().Where(c => c.EmployeeId == e.Id).Select(c => new { c.Field, status = c.Status.ToString(), c.Reason, c.CreatedAt }).ToListAsync(ct)),
            payslips = (await (from i in db.PayRunItems.AsNoTracking() join p in db.PayRuns.AsNoTracking() on i.PayRunId equals p.Id where i.EmployeeId == e.Id && (p.Status == PayRunStatus.Locked || p.Status == PayRunStatus.Paid) orderby p.Year, p.Month
                select new { p.Year, p.Month, i.Gross, i.KesEmployee, i.JhtEmployee, i.JpEmployee, i.Tax, i.Net, i.OvertimePay }).ToListAsync(ct)),
        };
        audit.Record("hr.self.export", "employee", e.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return new ExportFile($"my-hr-data-{e.EmployeeNo}.json", "application/json", JsonSerializer.SerializeToUtf8Bytes(data, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
