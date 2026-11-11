using System.Text;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

/// <summary>
/// EMP-001…007. Rules enforced here (not in the UI): sensitive fields need employees.read_sensitive plus a recent password re-entry, job data is effective-dated
/// (closed periods are never edited), the reporting line cannot loop, ending employment is blocked while people still report to the employee, and every change is audited
/// in the same unit of work (sensitive values are audited as "changed", never as values).
/// </summary>
public sealed class EmployeeService(IAppDb db, IClock clock, ISecretProtector protector, AuthorizationService authz, SettingsProvider settings, SessionService sessions, AuditWriter audit, WebhookService webhooks, LegalEntityService entities)
{
    private static readonly HashSet<string> Sorts = new(StringComparer.OrdinalIgnoreCase) { "name", "employeeNo", "hireDate", "status" };

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7)); // payroll country is Indonesia (WIB); times are stored in UTC

    // ============================================================ reads
    public async Task<PagedResult<EmployeeListItem>> ListAsync(EmployeeQuery q, CancellationToken ct = default)
    {
        var page = Math.Max(1, q.Page); var size = Math.Clamp(q.PageSize, 1, 100);
        var query = Filtered(q);
        var desc = string.Equals(q.Dir, "desc", StringComparison.OrdinalIgnoreCase);
        var sort = q.Sort is not null && Sorts.Contains(q.Sort) ? q.Sort.ToLowerInvariant() : "name";
        query = (sort, desc) switch
        {
            ("employeeno", false) => query.OrderBy(e => e.EmployeeNo), ("employeeno", true) => query.OrderByDescending(e => e.EmployeeNo),
            ("hiredate", false) => query.OrderBy(e => e.HireDate).ThenBy(e => e.Id), ("hiredate", true) => query.OrderByDescending(e => e.HireDate).ThenBy(e => e.Id),
            ("status", false) => query.OrderBy(e => e.Status).ThenBy(e => e.FullName), ("status", true) => query.OrderByDescending(e => e.Status).ThenBy(e => e.FullName),
            (_, true) => query.OrderByDescending(e => e.FullName).ThenBy(e => e.Id), _ => query.OrderBy(e => e.FullName).ThenBy(e => e.Id),
        };
        var total = await query.CountAsync(ct);
        var rows = await ProjectList(query.Skip((page - 1) * size).Take(size)).ToListAsync(ct);
        return new PagedResult<EmployeeListItem>(rows, page, size, total);
    }

    /// <summary>EMP-008. Every active employee with their manager, for drawing the reporting tree (capped so a huge tenant cannot be pulled in one call).</summary>
    public async Task<IReadOnlyList<EmployeeListItem>> OrgChartAsync(CancellationToken ct = default)
        => await ProjectList(db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active).OrderBy(e => e.FullName).Take(5000)).ToListAsync(ct);

    private IQueryable<Employee> Filtered(EmployeeQuery q)
    {
        var query = db.Employees.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var t = q.Q.Trim().ToLowerInvariant();
            query = query.Where(e => e.FullName.ToLower().Contains(t) || e.EmployeeNo.ToLower().Contains(t) || (e.WorkEmail != null && e.WorkEmail.Contains(t)));
        }
        var st = q.Status?.Trim().ToLowerInvariant();
        if (st == "active") query = query.Where(e => e.Status == EmployeeStatus.Active);
        else if (st == "terminated") query = query.Where(e => e.Status == EmployeeStatus.Terminated);
        else if (!string.IsNullOrEmpty(st) && st != "all") throw DomainException.Validation("Unknown status filter.", new Dictionary<string, string> { ["status"] = "Use active, terminated or all." });
        if (q.OrgUnitId is { } ou) query = query.Where(e => e.Employments.Any(m => m.EffectiveTo == null && m.OrgUnitId == ou));
        if (!string.IsNullOrWhiteSpace(q.Type))
        {
            if (!Enum.TryParse<EmploymentType>(q.Type, true, out var type)) throw DomainException.Validation("Unknown employment type.", new Dictionary<string, string> { ["type"] = "Use permanent, contract, probation or intern." });
            query = query.Where(e => e.Employments.Any(m => m.EffectiveTo == null && m.Type == type));
        }
        return query;
    }

    private IQueryable<EmployeeListItem> ProjectList(IQueryable<Employee> q) => q.Select(e => new
    {
        e, m = e.Employments.Where(x => x.EffectiveTo == null).FirstOrDefault(),
    }).Select(x => new EmployeeListItem(x.e.Id, x.e.EmployeeNo, x.e.FullName, x.e.WorkEmail, x.e.Status == EmployeeStatus.Active ? "active" : "terminated",
        x.m == null ? null : x.m.JobTitle, x.m == null ? null : x.m.OrgUnitId, x.m == null ? null : db.OrgUnits.Where(o => o.Id == x.m.OrgUnitId).Select(o => o.Name).FirstOrDefault(),
        x.m == null ? null : x.m.Type.ToString().ToLower(), x.m == null ? null : x.m.ManagerId,
        x.m == null ? null : db.Employees.Where(o => o.Id == x.m.ManagerId).Select(o => o.FullName).FirstOrDefault(), x.e.HireDate, x.m == null ? null : x.m.ContractEnd));

    public async Task<EmployeeDetail> GetAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = await db.Employees.AsNoTracking().Include(x => x.Employments).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Employee");
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        var sensitive = await SensitiveAsync(e, perms, actor, ct);
        if (sensitive.Visible && (sensitive.HasNationalId || sensitive.HasTaxId || sensitive.HasBankAccount || sensitive.HasBpjsKesNo || sensitive.HasBpjsTkNo))
        {
            audit.Record("hr.employee.sensitive_viewed", "employee", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
            await db.SaveChangesAsync(ct);
        }
        var unitIds = e.Employments.Select(m => m.OrgUnitId).Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        var mgrIds = e.Employments.Select(m => m.ManagerId).Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        var units = await db.OrgUnits.AsNoTracking().Where(o => unitIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var mgrs = await db.Employees.AsNoTracking().Where(o => mgrIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.FullName, ct);
        EmploymentView View(EmployeeEmployment m) => new(m.Id, m.EffectiveFrom, m.EffectiveTo, m.JobTitle, m.Grade, m.Location, m.Type.ToString().ToLowerInvariant(), m.ContractEnd,
            m.OrgUnitId, m.OrgUnitId is { } o ? units.GetValueOrDefault(o) : null, m.ManagerId, m.ManagerId is { } g ? mgrs.GetValueOrDefault(g) : null, m.ChangeReason);
        var history = e.Employments.OrderByDescending(m => m.EffectiveFrom).Select(View).ToList();
        var reports = await db.Employees.AsNoTracking().Where(x => x.Status == EmployeeStatus.Active && x.Employments.Any(m => m.EffectiveTo == null && m.ManagerId == id))
            .OrderBy(x => x.FullName).Select(x => new EmployeeRef(x.Id, x.EmployeeNo, x.FullName)).ToListAsync(ct);
        return new EmployeeDetail(e.Id, e.EmployeeNo, e.FullName, e.WorkEmail, e.Phone, e.BirthDate, e.Gender?.ToString().ToLowerInvariant(), e.MaritalStatus?.ToString().ToLowerInvariant(),
            e.Address, e.EmergencyContactName, e.EmergencyContactPhone, e.BankName, e.Status.ToString().ToLowerInvariant(), e.HireDate, e.TerminationDate, e.TerminationReason, e.UserId, e.Version,
            history.FirstOrDefault(h => h.EffectiveTo is null), history, sensitive, reports, e.LegalEntityId, await db.LegalEntities.AsNoTracking().Where(x => x.Id == e.LegalEntityId).Select(x => x.Name).FirstOrDefaultAsync(ct), e.Religion?.ToString().ToLowerInvariant(),
            [.. (await db.ScheduledEmploymentChanges.AsNoTracking().Where(x => x.EmployeeId == id && x.AppliedAt == null && x.CancelledAt == null).OrderBy(x => x.EffectiveFrom).ToListAsync(ct)).Select(x => new ScheduledChangeView(x.Id, x.EffectiveFrom, x.Summary, x.CreatedAt))],
            await db.HrPhotos.AsNoTracking().Where(x => x.Kind == "employee" && x.OwnerId == id).Select(x => (DateTimeOffset?)x.CreatedAt).FirstOrDefaultAsync(ct));
    }

    private async Task<SensitiveView> SensitiveAsync(Employee e, IReadOnlySet<string> perms, SessionPrincipal actor, CancellationToken ct)
    {
        bool hasN = e.NationalIdEnc != null, hasT = e.TaxIdEnc != null, hasB = e.BankAccountEnc != null, hasK = e.BpjsKesNoEnc != null, hasW = e.BpjsTkNoEnc != null;
        if (!perms.Contains(Permissions.EmployeesReadSensitive) || !ReauthFresh(actor, await settings.GetAsync(ct))) return new(false, null, null, null, hasN, hasT, hasB, null, null, hasK, hasW);
        return new(true, Reveal(e, "national_id", e.NationalIdEnc), Reveal(e, "tax_id", e.TaxIdEnc), Reveal(e, "bank_account", e.BankAccountEnc), hasN, hasT, hasB,
            Reveal(e, "bpjs_kes_no", e.BpjsKesNoEnc), Reveal(e, "bpjs_tk_no", e.BpjsTkNoEnc), hasK, hasW);
    }

    private bool ReauthFresh(SessionPrincipal actor, SecuritySettings s)
    {
        try { Guards.RequireRecentReauthForCritical(actor, s with { RequireReauthForCritical = true }, clock); return true; }
        catch (DomainException) { return false; }
    }

    // ============================================================ create
    public async Task<EmployeeDetail> CreateAsync(CreateEmployeeRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        var name = Clean(r.FullName, 200); if (name is null || name.Length < 2) errors["fullName"] = "Enter the full name.";
        if (r.HireDate is null) errors["hireDate"] = "Choose the hire date.";
        else if (r.HireDate > Today.AddYears(1)) errors["hireDate"] = "Hire date is too far in the future.";
        var jobTitle = Clean(r.JobTitle, 150); if (jobTitle is null) errors["jobTitle"] = "Enter the job title.";
        var type = ParseEnum<EmploymentType>(r.Type, "type", errors) ?? EmploymentType.Permanent;
        var gender = ParseEnum<Gender>(r.Gender, "gender", errors); var marital = ParseEnum<MaritalStatus>(r.MaritalStatus, "maritalStatus", errors); var religion = ParseEnum<Religion>(r.Religion, "religion", errors);
        var email = Email(r.WorkEmail, errors); CheckBirth(r.BirthDate, errors);
        if (type is EmploymentType.Contract or EmploymentType.Probation or EmploymentType.Intern && r.ContractEnd is null) errors["contractEnd"] = "A fixed-term position needs an end date.";
        if (r.ContractEnd is { } ce && r.HireDate is { } hd && ce < hd) errors["contractEnd"] = "The end date is before the hire date.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the employee details.", errors);

        var hasSensitive = !string.IsNullOrWhiteSpace(r.NationalId) || !string.IsNullOrWhiteSpace(r.TaxId) || !string.IsNullOrWhiteSpace(r.BankAccount) || !string.IsNullOrWhiteSpace(r.BpjsKesNo) || !string.IsNullOrWhiteSpace(r.BpjsTkNo);
        if (hasSensitive) await RequireSensitiveWriteAsync(actor, ct);
        await CheckOrgUnitAsync(r.OrgUnitId, ct);
        if (r.ManagerId is { } mid && !await db.Employees.AnyAsync(x => x.Id == mid && x.Status == EmployeeStatus.Active, ct)) throw DomainException.Validation("Manager not found.", new Dictionary<string, string> { ["managerId"] = "Choose an active employee." });
        if (r.UserId is { } uid) await CheckUserLinkAsync(uid, null, ct);
        var entityId = await entities.ResolveAsync(r.LegalEntityId, ct);

        var newId = await db.InTransactionAsync(async () =>
        {
            var now = clock.UtcNow;
            var no = Clean(r.EmployeeNo, 30)?.ToUpperInvariant() ?? await NextNumberAsync(ct);
            if (await db.Employees.AnyAsync(x => x.EmployeeNo == no, ct)) throw DomainException.Conflict($"Employee number '{no}' is already used.");
            var emp = new Employee
            {
                EmployeeNo = no, FullName = name!, WorkEmail = email, Phone = Clean(r.Phone, 40), BirthDate = r.BirthDate, Gender = gender, MaritalStatus = marital, Address = Clean(r.Address, 500),
                EmergencyContactName = Clean(r.EmergencyContactName, 200), EmergencyContactPhone = Clean(r.EmergencyContactPhone, 40), BankName = Clean(r.BankName, 80),
                HireDate = r.HireDate!.Value, UserId = r.UserId, LegalEntityId = entityId, Religion = religion, CreatedAt = now, UpdatedAt = now,
            };
            emp.NationalIdEnc = Seal(emp, "national_id", r.NationalId); emp.TaxIdEnc = Seal(emp, "tax_id", r.TaxId); emp.BankAccountEnc = Seal(emp, "bank_account", r.BankAccount);
            emp.BpjsKesNoEnc = Seal(emp, "bpjs_kes_no", r.BpjsKesNo); emp.BpjsTkNoEnc = Seal(emp, "bpjs_tk_no", r.BpjsTkNo);
            emp.Employments.Add(new EmployeeEmployment
            {
                EmployeeId = emp.Id, EffectiveFrom = emp.HireDate, JobTitle = jobTitle!, Grade = Clean(r.Grade, 30), Location = Clean(r.Location, 100), Type = type,
                ContractEnd = r.ContractEnd, OrgUnitId = r.OrgUnitId, ManagerId = r.ManagerId, ChangeReason = "Hired", CreatedAt = now,
            });
            db.Employees.Add(emp);
            db.EmployeeTasks.AddRange(ChecklistService.Defaults(emp.Id, ChecklistKind.Onboarding, emp.HireDate, now));
            await webhooks.EnqueueAsync("employee.created", new { employeeId = emp.Id, employeeNo = no, legalEntityId = emp.LegalEntityId, hireDate = emp.HireDate }, ct);
            audit.Record("hr.employee.create", "employee", emp.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
                new Dictionary<string, string> { ["employeeNo"] = no, ["name"] = emp.FullName, ["sensitiveSet"] = hasSensitive.ToString() });
            await db.SaveChangesAsync(ct);
            return emp.Id;
        }, ct);
        return await GetAsync(newId, actor, client, ct);
    }

    private async Task<string> NextNumberAsync(CancellationToken ct)
    {
        var nos = await db.Employees.AsNoTracking().Where(e => e.EmployeeNo.StartsWith("EMP-")).Select(e => e.EmployeeNo).ToListAsync(ct);
        var max = nos.Select(n => int.TryParse(n[4..], out var v) ? v : 0).DefaultIfEmpty(0).Max();
        return $"EMP-{max + 1:D4}";
    }

    // ============================================================ update
    public async Task<EmployeeDetail> UpdateAsync(Guid id, UpdateEmployeeRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        if (r.Version is null) throw DomainException.Validation("Version is required.", new Dictionary<string, string> { ["version"] = "Reload the employee and try again." });
        var e = await db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Employee");
        if (e.Version != r.Version) throw DomainException.Conflict("This employee was changed by someone else. Reload and try again.");
        var errors = new Dictionary<string, string>(); var diff = new List<AuditDiff>();

        void Text(string field, string? incoming, int max, string? current, Action<string?> set)
        {
            if (incoming is null) return; var v = Clean(incoming, max);
            if (v != current) { diff.Add(new(field, current ?? "", v ?? "")); set(v); }
        }
        if (r.FullName is not null)
        {
            var n = Clean(r.FullName, 200); if (n is null || n.Length < 2) errors["fullName"] = "Enter the full name."; else if (n != e.FullName) { diff.Add(new("fullName", e.FullName, n)); e.FullName = n; }
        }
        if (r.WorkEmail is not null) { var em = Email(r.WorkEmail, errors); if (em != e.WorkEmail && !errors.ContainsKey("workEmail")) { diff.Add(new("workEmail", e.WorkEmail ?? "", em ?? "")); e.WorkEmail = em; } }
        Text("phone", r.Phone, 40, e.Phone, v => e.Phone = v); Text("address", r.Address, 500, e.Address, v => e.Address = v);
        Text("emergencyContactName", r.EmergencyContactName, 200, e.EmergencyContactName, v => e.EmergencyContactName = v);
        Text("emergencyContactPhone", r.EmergencyContactPhone, 40, e.EmergencyContactPhone, v => e.EmergencyContactPhone = v);
        Text("bankName", r.BankName, 80, e.BankName, v => e.BankName = v);
        if (r.BirthDate is { } bd) { CheckBirth(bd, errors); if (bd != e.BirthDate) { diff.Add(new("birthDate", e.BirthDate?.ToString("yyyy-MM-dd") ?? "", bd.ToString("yyyy-MM-dd"))); e.BirthDate = bd; } }
        if (r.Gender is not null) { var g = ParseEnum<Gender>(r.Gender, "gender", errors); if (g != e.Gender && !errors.ContainsKey("gender")) { diff.Add(new("gender", e.Gender?.ToString() ?? "", g?.ToString() ?? "")); e.Gender = g; } }
        if (r.MaritalStatus is not null) { var m = ParseEnum<MaritalStatus>(r.MaritalStatus, "maritalStatus", errors); if (m != e.MaritalStatus && !errors.ContainsKey("maritalStatus")) { diff.Add(new("maritalStatus", e.MaritalStatus?.ToString() ?? "", m?.ToString() ?? "")); e.MaritalStatus = m; } }
        if (r.Religion is not null) { var rg = ParseEnum<Religion>(r.Religion, "religion", errors); if (rg != e.Religion && !errors.ContainsKey("religion")) { diff.Add(new("religion", e.Religion?.ToString() ?? "", rg?.ToString() ?? "")); e.Religion = rg; } }
        if (errors.Count > 0) throw DomainException.Validation("Please check the employee details.", errors);

        if (r.NationalId is not null || r.TaxId is not null || r.BankAccount is not null || r.BpjsKesNo is not null || r.BpjsTkNo is not null)
        {
            await RequireSensitiveWriteAsync(actor, ct);
            if (r.BpjsKesNo is not null) { e.BpjsKesNoEnc = Seal(e, "bpjs_kes_no", r.BpjsKesNo); diff.Add(new("bpjsKesNo", "[protected]", "[changed]")); }
            if (r.BpjsTkNo is not null) { e.BpjsTkNoEnc = Seal(e, "bpjs_tk_no", r.BpjsTkNo); diff.Add(new("bpjsTkNo", "[protected]", "[changed]")); }
            if (r.NationalId is not null) { e.NationalIdEnc = Seal(e, "national_id", r.NationalId); diff.Add(new("nationalId", "[protected]", "[changed]")); }
            if (r.TaxId is not null) { e.TaxIdEnc = Seal(e, "tax_id", r.TaxId); diff.Add(new("taxId", "[protected]", "[changed]")); }
            if (r.BankAccount is not null) { e.BankAccountEnc = Seal(e, "bank_account", r.BankAccount); diff.Add(new("bankAccount", "[protected]", "[changed]")); }
        }
        if (r.UserId != e.UserId && r.UserId is not null) { await CheckUserLinkAsync(r.UserId.Value, id, ct); diff.Add(new("userId", e.UserId?.ToString() ?? "", r.UserId.ToString()!)); e.UserId = r.UserId; }

        if (r.LegalEntityId is { } nid && nid != e.LegalEntityId)
        {
            var target = await entities.ResolveAsync(nid, ct);
            // Moving someone mid-run would leave them in the old company's unfinished pay run: finish (lock) or recalculate that run first.
            if (await db.PayRunItems.AnyAsync(i => i.EmployeeId == id && db.PayRuns.Any(p => p.Id == i.PayRunId && p.LegalEntityId == e.LegalEntityId && p.Status != PayRunStatus.Locked && p.Status != PayRunStatus.Paid), ct))
                throw DomainException.Invariant("This employee is in an unfinished pay run of their current company. Lock that run, or delete it, before moving them.");
            diff.Add(new("legalEntityId", e.LegalEntityId.ToString(), target.ToString())); e.LegalEntityId = target;
        }
        if (diff.Count > 0)
        {
            e.UpdatedAt = clock.UtcNow; e.Version++;
            audit.Record("hr.employee.update", "employee", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeNo"] = e.EmployeeNo }, diff);
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(id, actor, client, ct);
    }

    // ============================================================ job data (effective-dated)
    public async Task<EmployeeDetail> ChangeEmploymentAsync(Guid id, ChangeEmploymentRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await ApplyEmploymentChangeAsync(id, r, actor.UserId, actor.Email, client, schedule: true, ct);
        return await GetAsync(id, actor, client, ct);
    }

    /// <summary>
    /// The one place a job record changes. A date up to today is applied at once (closing the current period the day before). A future date is validated now and stored as a scheduled
    /// change (when <paramref name="schedule"/> is allowed); <see cref="ApplyScheduledAsync"/> turns it into a job record on its day, so "current job" always means what is true today.
    /// </summary>
    private async Task ApplyEmploymentChangeAsync(Guid id, ChangeEmploymentRequest r, Guid? actorId, string actorLabel, ClientInfo client, bool schedule, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        var e = await db.Employees.Include(x => x.Employments).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Employee");
        if (e.Status != EmployeeStatus.Active) throw DomainException.Invariant("This employee's employment has ended.");
        var cur = e.Employments.Single(m => m.EffectiveTo == null);
        var from = r.EffectiveFrom ?? Today;
        if (from <= cur.EffectiveFrom) errors["effectiveFrom"] = $"Must be after the current period started ({cur.EffectiveFrom:yyyy-MM-dd}).";
        else if (from > Today.AddYears(1)) errors["effectiveFrom"] = "That date is more than a year ahead.";
        var title = r.JobTitle is null ? cur.JobTitle : Clean(r.JobTitle, 150); if (title is null) errors["jobTitle"] = "Enter the job title.";
        var type = r.Type is null ? cur.Type : ParseEnum<EmploymentType>(r.Type, "type", errors) ?? cur.Type;
        if (type != EmploymentType.Permanent && (r.ContractEnd ?? (r.Type is null ? cur.ContractEnd : null)) is null) errors["contractEnd"] = "A fixed-term position needs an end date.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the job details.", errors);
        await CheckOrgUnitAsync(r.OrgUnitId ?? cur.OrgUnitId, ct);
        var managerId = r.ManagerId ?? cur.ManagerId;
        await CheckManagerAsync(id, managerId, ct);

        var now = clock.UtcNow;
        var next = new EmployeeEmployment
        {
            EmployeeId = id, EffectiveFrom = from, JobTitle = title!, Grade = r.Grade is null ? cur.Grade : Clean(r.Grade, 30), Location = r.Location is null ? cur.Location : Clean(r.Location, 100),
            Type = type, ContractEnd = type == EmploymentType.Permanent ? null : r.ContractEnd ?? cur.ContractEnd, OrgUnitId = r.OrgUnitId ?? cur.OrgUnitId, ManagerId = managerId,
            ChangeReason = Clean(r.Reason, 300), CreatedAt = now,
        };
        var diff = Diff(cur, next);
        if (diff.Count == 0) throw DomainException.Validation("Nothing changed.", new Dictionary<string, string> { ["jobTitle"] = "Change at least one job detail." });
        if (from > Today)
        {
            if (!schedule) throw DomainException.Invariant("This change is not due yet.");
            if (await db.ScheduledEmploymentChanges.AnyAsync(x => x.EmployeeId == id && x.EffectiveFrom == from && x.AppliedAt == null && x.CancelledAt == null, ct)) throw DomainException.Conflict("A change is already scheduled for that date. Cancel it first.");
            if (await db.ScheduledEmploymentChanges.CountAsync(x => x.EmployeeId == id && x.AppliedAt == null && x.CancelledAt == null, ct) >= 5) throw DomainException.Invariant("At most 5 changes can wait for one employee.");
            var summary = string.Join(", ", diff.Select(d => d.Field)); if (summary.Length > 300) summary = summary[..300];
            db.ScheduledEmploymentChanges.Add(new ScheduledEmploymentChange { EmployeeId = id, EffectiveFrom = from, RequestJson = System.Text.Json.JsonSerializer.Serialize(r), Summary = summary, CreatedBy = actorId, CreatedAt = now });
            audit.Record("hr.employee.employment_scheduled", "employee", id.ToString(), Outcome.Success, client, actorId, actorLabel, new Dictionary<string, string> { ["employeeNo"] = e.EmployeeNo, ["effectiveFrom"] = from.ToString("yyyy-MM-dd") }, diff);
            await db.SaveChangesAsync(ct);
            return;
        }
        await db.InTransactionAsync(async () =>
        {
            cur.EffectiveTo = from.AddDays(-1);
            await db.SaveChangesAsync(ct); // close the open period first: the unique index allows one open period per employee
            db.Employments.Add(next);
            e.UpdatedAt = now; e.Version++;
            audit.Record("hr.employee.employment_change", "employee", id.ToString(), Outcome.Success, client, actorId, actorLabel,
                new Dictionary<string, string> { ["employeeNo"] = e.EmployeeNo, ["effectiveFrom"] = from.ToString("yyyy-MM-dd") }, diff);
            await db.SaveChangesAsync(ct);
            return 0;
        });
    }

    public async Task CancelScheduledChangeAsync(Guid changeId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.ScheduledEmploymentChanges.FirstOrDefaultAsync(x => x.Id == changeId && x.AppliedAt == null && x.CancelledAt == null, ct) ?? throw DomainException.NotFound("Scheduled change");
        c.CancelledAt = clock.UtcNow; c.Note = "Cancelled";
        audit.Record("hr.employee.employment_schedule_cancel", "employee", c.EmployeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["effectiveFrom"] = c.EffectiveFrom.ToString("yyyy-MM-dd") });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Applies every scheduled job change whose day has come (current organisation). One that no longer fits (the employee left, a later change was made meanwhile) is closed with a note instead of applied.</summary>
    public async Task<int> ApplyScheduledAsync(CancellationToken ct = default)
    {
        var today = Today; var applied = 0;
        foreach (var c in await db.ScheduledEmploymentChanges.Where(x => x.AppliedAt == null && x.CancelledAt == null && x.EffectiveFrom <= today).OrderBy(x => x.EffectiveFrom).ToListAsync(ct))
        {
            var request = System.Text.Json.JsonSerializer.Deserialize<ChangeEmploymentRequest>(c.RequestJson)! with { EffectiveFrom = c.EffectiveFrom };
            try
            {
                await ApplyEmploymentChangeAsync(c.EmployeeId, request, c.CreatedBy, "scheduled change", new ClientInfo(null, "scheduler", null), schedule: false, ct);
                c.AppliedAt = clock.UtcNow; applied++;
            }
            catch (DomainException ex) { db.ResetTracking(); db.ScheduledEmploymentChanges.Attach(c); c.CancelledAt = clock.UtcNow; c.Note = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message; }
            await db.SaveChangesAsync(ct);
        }
        return applied;
    }

    private static List<AuditDiff> Diff(EmployeeEmployment a, EmployeeEmployment b)
    {
        var d = new List<AuditDiff>();
        void C(string f, string? x, string? y) { if ((x ?? "") != (y ?? "")) d.Add(new(f, x ?? "", y ?? "")); }
        C("jobTitle", a.JobTitle, b.JobTitle); C("grade", a.Grade, b.Grade); C("location", a.Location, b.Location); C("type", a.Type.ToString(), b.Type.ToString());
        C("contractEnd", a.ContractEnd?.ToString("yyyy-MM-dd"), b.ContractEnd?.ToString("yyyy-MM-dd")); C("orgUnitId", a.OrgUnitId?.ToString(), b.OrgUnitId?.ToString()); C("managerId", a.ManagerId?.ToString(), b.ManagerId?.ToString());
        return d;
    }

    // ============================================================ offboarding
    public async Task<EmployeeDetail> TerminateAsync(Guid id, TerminateRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = await db.Employees.Include(x => x.Employments).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Employee");
        if (e.Status != EmployeeStatus.Active) throw DomainException.Invariant("This employee's employment has already ended.");
        var cur = e.Employments.Single(m => m.EffectiveTo == null);
        var date = r.Date ?? Today; var reason = Clean(r.Reason, 300);
        var errors = new Dictionary<string, string>();
        if (date < cur.EffectiveFrom || date < e.HireDate) errors["date"] = "The last day cannot be before the current position started.";
        else if (date > Today.AddYears(1)) errors["date"] = "That date is too far in the future.";
        if (reason is null) errors["reason"] = "Enter the reason (resignation, end of contract, ...).";
        if (errors.Count > 0) throw DomainException.Validation("Please check the details.", errors);
        var reports = await db.Employments.CountAsync(m => m.EffectiveTo == null && m.ManagerId == id && db.Employees.Any(x => x.Id == m.EmployeeId && x.Status == EmployeeStatus.Active), ct);
        if (reports > 0) throw DomainException.Invariant($"{reports} employee(s) still report to this person. Assign them a new manager first.");

        var now = clock.UtcNow;
        await db.InTransactionAsync(async () =>
        {
            if (!await db.EmployeeTasks.AnyAsync(t => t.EmployeeId == id && t.Kind == ChecklistKind.Offboarding, ct)) db.EmployeeTasks.AddRange(ChecklistService.Defaults(id, ChecklistKind.Offboarding, date, now));
            await webhooks.EnqueueAsync("employee.terminated", new { employeeId = id, employeeNo = e.EmployeeNo, lastDay = date }, ct);
            e.Status = EmployeeStatus.Terminated; e.TerminationDate = date; e.TerminationReason = reason; e.UpdatedAt = now; e.Version++;
            cur.EffectiveTo = date;
            var revoked = 0; var deactivated = false;
            if (e.UserId is { } uid && date <= Today && await db.Users.FirstOrDefaultAsync(u => u.Id == uid, ct) is { } user && user.Status != UserStatus.Deactivated)
            {
                // Access ends with employment (ONB-003). The last Super Admin keeps working: that guard lives in the user service, so we only deactivate ordinary accounts.
                var isAdmin = await db.UserRoles.AnyAsync(ur => ur.UserId == uid && ur.Role!.Key == SystemRoles.SuperAdmin, ct);
                if (!isAdmin)
                {
                    user.Status = UserStatus.Deactivated; user.DeactivatedAt = now; user.UpdatedAt = now; user.Version++; deactivated = true;
                    revoked = await sessions.RevokeAllForUserAsync(uid, RevokeReason.Admin, null, ct);
                }
            }
            audit.Record("hr.employee.terminate", "employee", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
                new Dictionary<string, string> { ["employeeNo"] = e.EmployeeNo, ["date"] = date.ToString("yyyy-MM-dd"), ["reason"] = reason!, ["accountDeactivated"] = deactivated.ToString(), ["sessionsRevoked"] = revoked.ToString() });
            await db.SaveChangesAsync(ct);
            return 0;
        });
        return await GetAsync(id, actor, client, ct);
    }

    // ============================================================ export
    public async Task<ExportFile> ExportAsync(EmployeeQuery q, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var rows = await ProjectList(Filtered(q).OrderBy(e => e.EmployeeNo).Take(20000)).ToListAsync(ct);
        var sb = new StringBuilder("employee_no,full_name,work_email,status,job_title,department,type,manager,hire_date,contract_end\r\n");
        foreach (var r in rows)
            sb.Append(string.Join(',', new[] { r.EmployeeNo, r.FullName, r.WorkEmail, r.Status, r.JobTitle, r.OrgUnit, r.Type, r.ManagerName, r.HireDate.ToString("yyyy-MM-dd"), r.ContractEnd?.ToString("yyyy-MM-dd") }.Select(Csv))).Append("\r\n");
        audit.Record("hr.employee.export", "employee", null, Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["rows"] = rows.Count.ToString() });
        await db.SaveChangesAsync(ct);
        return new ExportFile($"employees-{Today:yyyyMMdd}.csv", "text/csv; charset=utf-8", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray());
    }

    /// <summary>RFC 4180 quoting plus spreadsheet-formula neutralisation (a name starting with = + - @ must not run as a formula when HR opens the file in Excel).</summary>
    internal static string Csv(string? v)
    {
        v ??= ""; if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@' or '\t' or '\r') v = "'" + v;
        return v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    // ============================================================ helpers
    internal async Task RequireSensitiveWriteAsync(SessionPrincipal actor, CancellationToken ct)
    {
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        if (!perms.Contains(Permissions.EmployeesReadSensitive)) throw DomainException.Denied(Permissions.EmployeesReadSensitive);
        Guards.RequireRecentReauthForCritical(actor, (await settings.GetAsync(ct)) with { RequireReauthForCritical = true }, clock);
    }

    private async Task CheckOrgUnitAsync(Guid? id, CancellationToken ct)
    {
        if (id is { } u && !await db.OrgUnits.AnyAsync(o => o.Id == u && o.ArchivedAt == null, ct)) throw DomainException.Validation("Department not found.", new Dictionary<string, string> { ["orgUnitId"] = "Choose an existing department." });
    }

    /// <summary>The reporting line must end at someone: no self-management and no loops (A→B→A).</summary>
    private async Task CheckManagerAsync(Guid employeeId, Guid? managerId, CancellationToken ct)
    {
        if (managerId is not { } m) return;
        if (m == employeeId) throw DomainException.Invariant("An employee cannot be their own manager.");
        if (!await db.Employees.AnyAsync(x => x.Id == m && x.Status == EmployeeStatus.Active, ct)) throw DomainException.Validation("Manager not found.", new Dictionary<string, string> { ["managerId"] = "Choose an active employee." });
        var lines = await db.Employments.AsNoTracking().Where(x => x.EffectiveTo == null).Select(x => new { x.EmployeeId, x.ManagerId }).ToDictionaryAsync(x => x.EmployeeId, x => x.ManagerId, ct);
        var seen = 0;
        for (Guid? cur = m; cur is { } c && seen++ < 1000; cur = lines.GetValueOrDefault(c))
            if (c == employeeId) throw DomainException.Invariant("That would create a reporting loop.");
    }

    private async Task CheckUserLinkAsync(Guid userId, Guid? employeeId, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) throw DomainException.Validation("User account not found.", new Dictionary<string, string> { ["userId"] = "Choose an existing user." });
        if (await db.Employees.AnyAsync(x => x.UserId == userId && x.Id != employeeId, ct)) throw DomainException.Conflict("That user account is already linked to another employee.");
    }

    internal string? Seal(Employee e, string field, string? plain)
    {
        var v = Clean(plain, 100); if (v is null) return null;
        var s = protector.Protect(Encoding.UTF8.GetBytes(v), Aad(e.Id, field));
        return $"{s.KeyVersion}.{Convert.ToBase64String(s.Nonce)}.{Convert.ToBase64String(s.Ciphertext)}";
    }

    private string? Reveal(Employee e, string field, string? stored)
    {
        if (stored is null) return null;
        var p = stored.Split('.'); if (p.Length != 3 || !int.TryParse(p[0], out var kv)) return null;
        return Encoding.UTF8.GetString(protector.Unprotect(new ProtectedSecret(Convert.FromBase64String(p[2]), Convert.FromBase64String(p[1]), kv), Aad(e.Id, field)));
    }

    private static byte[] Aad(Guid id, string field) => Encoding.UTF8.GetBytes($"employee:{id}:{field}"); // binds the ciphertext to its row and column

    internal static string? Clean(string? s, int max) { var t = s?.Trim(); if (string.IsNullOrEmpty(t)) return null; return t.Length > max ? t[..max] : t; }

    internal static string? Email(string? s, Dictionary<string, string> errors)
    {
        var v = Clean(s, 254)?.ToLowerInvariant(); if (v is null) return null;
        if (!System.Net.Mail.MailAddress.TryCreate(v, out var a) || a.Address != v || !v.Contains('.')) errors["workEmail"] = "Enter a valid email address.";
        return v;
    }

    private void CheckBirth(DateOnly? d, Dictionary<string, string> errors)
    {
        if (d is { } b && (b > Today.AddYears(-15) || b < Today.AddYears(-100))) errors["birthDate"] = "Check the date of birth.";
    }

    internal static T? ParseEnum<T>(string? s, string field, Dictionary<string, string> errors) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (Enum.TryParse<T>(s.Trim(), true, out var v) && Enum.IsDefined(v)) return v;
        errors[field] = "Choose one of: " + string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant())); return null;
    }
}
