using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

/// <summary>
/// LVE-001…009. Rules enforced here: working days exclude weekends and holidays; a request lies within one calendar year, never overlaps another live request,
/// and cannot exceed the available balance (pending requests are reserved, so two requests cannot spend the same day); a manager decides only for direct reports
/// (HR with leave.manage for anyone) and never for themself; balances are an append-only ledger; each change is audited in the same transaction.
/// Baseline (Indonesian practice, all configurable per type): annual leave of 12 days is granted once 12 months of service are complete.
/// </summary>
public sealed class LeaveService(IAppDb db, IClock clock, AuthorizationService authz, SettingsProvider settings, AuditWriter audit, IEmailQueue email, IAppLinks links, ManagerScope managers, ShiftService shifts, WebhookService webhooks, NotificationService notes, PayrollService payroll, ApprovalFlowService flow)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    // ============================================================ leave types
    private static readonly (string Code, string Name, decimal? Quota, int? Max, bool Paid, bool Half)[] Defaults =
    [
        ("ANNUAL", "Annual leave", 12m, null, true, true), ("SICK", "Sick leave", null, null, true, true), ("MATERNITY", "Maternity leave", null, 90, true, false),
        ("PATERNITY", "Paternity leave", null, 2, true, false), ("MARRIAGE", "Marriage leave", null, 3, true, false), ("BEREAVEMENT", "Bereavement leave", null, 2, true, false),
        ("UNPAID", "Unpaid leave", null, null, false, true),
    ];

    private async Task EnsureDefaultTypesAsync(CancellationToken ct)
    {
        if (await db.LeaveTypes.AnyAsync(ct)) return;
        foreach (var d in Defaults) db.LeaveTypes.Add(new LeaveType { Code = d.Code, Name = d.Name, AnnualQuota = d.Quota, MaxDaysPerRequest = d.Max, Paid = d.Paid, AllowHalfDay = d.Half, CreatedAt = clock.UtcNow });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ResetTracking(); } // another request seeded them first
    }

    private static LeaveTypeView View(LeaveType t) => new(t.Id, t.Code, t.Name, t.AnnualQuota, t.CarryOverMax, t.MaxDaysPerRequest, t.Paid, t.AllowHalfDay, t.ArchivedAt != null, t.HrApprovalFromDays, t.CarryOverExpiryMonth, t.CashOutExpired);

    public async Task<IReadOnlyList<LeaveTypeView>> TypesAsync(bool includeArchived, CancellationToken ct = default)
    {
        await EnsureDefaultTypesAsync(ct);
        return [.. (await db.LeaveTypes.AsNoTracking().Where(t => includeArchived || t.ArchivedAt == null).OrderBy(t => t.Code).ToListAsync(ct)).Select(View)];
    }

    public async Task<LeaveTypeView> SaveTypeAsync(Guid? id, LeaveTypeRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await EnsureDefaultTypesAsync(ct);
        var t = id is null ? new LeaveType { CreatedAt = clock.UtcNow } : await db.LeaveTypes.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Leave type");
        var errors = new Dictionary<string, string>();
        var code = (r.Code ?? t.Code).Trim().ToUpperInvariant(); var name = (r.Name ?? t.Name).Trim();
        if (code.Length is < 2 or > 30 || !code.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')) errors["code"] = "Use 2–30 letters, digits, - or _.";
        if (name.Length is < 2 or > 100) errors["name"] = "Enter a name of 2–100 characters.";
        decimal? quota = r.Unlimited == true ? null : r.AnnualQuota ?? t.AnnualQuota;
        if (quota is < 0 or > 365) errors["annualQuota"] = "Enter 0–365 days.";
        var carry = r.CarryOverMax ?? t.CarryOverMax; if (carry < 0 || carry > 365) errors["carryOverMax"] = "Enter 0–365 days.";
        var max = r.MaxDaysPerRequest ?? t.MaxDaysPerRequest; if (max is < 1 or > 366) errors["maxDaysPerRequest"] = "Enter 1–366 days.";
        var expiry = r.NoCarryOverExpiry == true ? null : r.CarryOverExpiryMonth ?? t.CarryOverExpiryMonth; if (expiry is < 1 or > 12) errors["carryOverExpiryMonth"] = "Choose a month (1–12).";
        var hrFrom = r.NoHrApproval == true ? null : r.HrApprovalFromDays ?? t.HrApprovalFromDays; if (hrFrom is < 0 or > 366) errors["hrApprovalFromDays"] = "Enter 0–366 days (0 = every request).";
        if (errors.Count > 0) throw DomainException.Validation("Please check the leave type.", errors);
        if (await db.LeaveTypes.AnyAsync(x => x.Code == code && x.Id != t.Id, ct)) throw DomainException.Conflict($"Leave type code '{code}' is already used.");
        t.Code = code; t.Name = name; t.AnnualQuota = quota; t.CarryOverMax = carry; t.MaxDaysPerRequest = max; t.Paid = r.Paid ?? t.Paid; t.AllowHalfDay = r.AllowHalfDay ?? t.AllowHalfDay; t.HrApprovalFromDays = hrFrom; t.CarryOverExpiryMonth = expiry; t.CashOutExpired = r.CashOutExpired ?? t.CashOutExpired;
        if (r.Archived is { } a) t.ArchivedAt = a ? t.ArchivedAt ?? clock.UtcNow : null;
        if (id is null) db.LeaveTypes.Add(t);
        audit.Record(id is null ? "hr.leave_type.create" : "hr.leave_type.update", "leave_type", t.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["code"] = code });
        await db.SaveChangesAsync(ct);
        return View(t);
    }

    // ============================================================ holidays
    public async Task<IReadOnlyList<HolidayView>> HolidaysAsync(int year, CancellationToken ct = default)
    {
        var from = new DateOnly(year, 1, 1); var to = new DateOnly(year, 12, 31);
        return await db.Holidays.AsNoTracking().Where(h => h.Date >= from && h.Date <= to).OrderBy(h => h.Date).Select(h => new HolidayView(h.Id, h.Date, h.Name, h.Collective)).ToListAsync(ct);
    }

    public async Task<HolidayView> AddHolidayAsync(HolidayRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>(); var name = r.Name?.Trim();
        if (r.Date is null) errors["date"] = "Choose the date.";
        if (string.IsNullOrEmpty(name) || name.Length > 150) errors["name"] = "Enter a name (up to 150 characters).";
        if (errors.Count > 0) throw DomainException.Validation("Please check the holiday.", errors);
        if (await db.Holidays.AnyAsync(h => h.Date == r.Date, ct)) throw DomainException.Conflict("That date is already a holiday.");
        var h = new Holiday { Date = r.Date!.Value, Name = name!, Collective = r.Collective ?? false };
        db.Holidays.Add(h);
        audit.Record("hr.holiday.create", "holiday", h.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["date"] = h.Date.ToString("yyyy-MM-dd"), ["name"] = h.Name });
        await db.SaveChangesAsync(ct);
        return new HolidayView(h.Id, h.Date, h.Name, h.Collective);
    }

    public async Task DeleteHolidayAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var h = await db.Holidays.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Holiday");
        db.Holidays.Remove(h);
        audit.Record("hr.holiday.delete", "holiday", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["date"] = h.Date.ToString("yyyy-MM-dd") });
        await db.SaveChangesAsync(ct);
    }

    // ============================================================ balances
    private async Task<Employee> MeAsync(SessionPrincipal actor, CancellationToken ct)
        => await db.Employees.FirstOrDefaultAsync(e => e.UserId == actor.UserId && e.Status == EmployeeStatus.Active, ct)
           ?? throw DomainException.Invariant("Your account is not linked to an active employee record. Ask HR to link it.");

    /// <summary>Annual-type grant for the current year once 12 months of service are complete, plus capped carry-over from last year. Lazy and idempotent (guarded by the row lock callers hold or a retry).</summary>
    private async Task EnsureGrantAsync(Employee emp, LeaveType type, CancellationToken ct)
    {
        if (type.AnnualQuota is not { } quota) return;
        var year = Today.Year;
        if (emp.HireDate.AddMonths(12) > Today) return;
        if (await db.LeaveLedger.AnyAsync(l => l.EmployeeId == emp.Id && l.LeaveTypeId == type.Id && l.Year == year && (l.Kind == LedgerKind.Grant), ct)) return;
        var now = clock.UtcNow;
        db.LeaveLedger.Add(new LeaveLedgerEntry { EmployeeId = emp.Id, LeaveTypeId = type.Id, Year = year, Days = quota, Kind = LedgerKind.Grant, Note = $"Annual entitlement {year}", CreatedAt = now });
        if (type.CarryOverMax > 0)
        {
            var left = await db.LeaveLedger.Where(l => l.EmployeeId == emp.Id && l.LeaveTypeId == type.Id && l.Year == year - 1).SumAsync(l => (decimal?)l.Days, ct) ?? 0;
            var carry = Math.Min(Math.Max(left, 0), type.CarryOverMax);
            if (carry > 0) db.LeaveLedger.Add(new LeaveLedgerEntry { EmployeeId = emp.Id, LeaveTypeId = type.Id, Year = year, Days = carry, Kind = LedgerKind.CarryOver, Note = $"Carried over from {year - 1}", CreatedAt = now });
        }
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ResetTracking(); }
    }

    /// <summary>The last day carried-over days of <paramref name="year"/> can be used, or null when they do not expire.</summary>
    internal static DateOnly? CarryDeadline(LeaveType type, int year) => type.CarryOverExpiryMonth is { } m ? new DateOnly(year, m, DateTime.DaysInMonth(year, m)) : null;

    /// <summary>Carried-over days still unused: what was carried over minus approved leave starting on or before the deadline (older days are used first).</summary>
    internal async Task<decimal> CarryLeftAsync(Guid empId, LeaveType type, int year, DateOnly deadline, CancellationToken ct)
    {
        var carry = await db.LeaveLedger.Where(l => l.EmployeeId == empId && l.LeaveTypeId == type.Id && l.Year == year && l.Kind == LedgerKind.CarryOver).SumAsync(l => (decimal?)l.Days, ct) ?? 0;
        if (carry <= 0) return 0;
        var jan = new DateOnly(year, 1, 1);
        var taken = await db.LeaveRequests.Where(r => r.EmployeeId == empId && r.LeaveTypeId == type.Id && r.Status == LeaveStatus.Approved && r.StartDate >= jan && r.StartDate <= deadline).SumAsync(r => (decimal?)r.Days, ct) ?? 0;
        return Math.Max(0, carry - taken);
    }

    /// <summary>Once the deadline has passed, removes the carried-over days that were not taken. Lazy like the grant and written at most once a year (unique index).</summary>
    private async Task EnsureExpiryAsync(Employee emp, LeaveType type, CancellationToken ct)
    {
        var year = Today.Year; if (CarryDeadline(type, year) is not { } deadline || Today <= deadline) return;
        if (await db.LeaveLedger.AnyAsync(l => l.EmployeeId == emp.Id && l.LeaveTypeId == type.Id && l.Year == year && l.Kind == LedgerKind.Expire, ct)) return;
        var lost = await CarryLeftAsync(emp.Id, type, year, deadline, ct); if (lost <= 0) return;
        var note = $"Carried-over days not taken by {deadline:yyyy-MM-dd}";
        // LVE-010: where the leave type says so, expired days are paid out instead of lost: 1/25 of the monthly wage per day, as a taxable line in the next pay run.
        // It rides on the same save as the expiry entry (written once a year), so it can never be paid twice. Daily-wage workers and people without a salary record get no pay-out.
        if (type.CashOutExpired && await payroll.MonthlyWageAsync(emp.Id, deadline, ct) is { } wage and > 0)
        {
            var amount = PayrollRules.Rupiah(wage / 25m * lost);
            db.RetroAdjustments.Add(new RetroAdjustment { EmployeeId = emp.Id, FromYear = year, FromMonth = deadline.Month, ThroughYear = year, ThroughMonth = deadline.Month, Amount = amount, Note = $"unused leave paid out: {lost:0.##} day(s)", CreatedAt = clock.UtcNow });
            note += $"; paid out {amount:0}";
        }
        db.LeaveLedger.Add(new LeaveLedgerEntry { EmployeeId = emp.Id, LeaveTypeId = type.Id, Year = year, Days = -lost, Kind = LedgerKind.Expire, Note = note, CreatedAt = clock.UtcNow });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ResetTracking(); }
    }

    public async Task<IReadOnlyList<BalanceView>> BalancesAsync(Guid? employeeId, SessionPrincipal actor, CancellationToken ct = default)
    {
        await EnsureDefaultTypesAsync(ct);
        var mine = await db.Employees.FirstOrDefaultAsync(e => e.UserId == actor.UserId && e.Status == EmployeeStatus.Active, ct);
        Employee emp;
        if (employeeId is { } id && mine?.Id != id)
        {
            var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
            if (!perms.Contains(Permissions.LeaveReadAll) && !perms.Contains(Permissions.LeaveManage) && !await IsManagerOfAsync(actor, id, ct)) throw DomainException.Denied(Permissions.LeaveReadAll);
            emp = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw DomainException.NotFound("Employee");
        }
        else emp = mine ?? throw DomainException.Invariant("Your account is not linked to an active employee record. Ask HR to link it.");

        var year = Today.Year;
        var types = await db.LeaveTypes.AsNoTracking().Where(t => t.ArchivedAt == null && t.AnnualQuota != null).OrderBy(t => t.Code).ToListAsync(ct);
        var result = new List<BalanceView>();
        foreach (var t in types)
        {
            await EnsureGrantAsync(emp, t, ct); await EnsureExpiryAsync(emp, t, ct);
            result.Add(await BalanceAsync(emp.Id, t, year, ct));
        }
        return result;
    }

    private async Task<BalanceView> BalanceAsync(Guid empId, LeaveType t, int year, CancellationToken ct)
    {
        var rows = await db.LeaveLedger.AsNoTracking().Where(l => l.EmployeeId == empId && l.LeaveTypeId == t.Id && l.Year == year).Select(l => new { l.Kind, l.Days }).ToListAsync(ct);
        decimal S(params LedgerKind[] k) => rows.Where(r => k.Contains(r.Kind)).Sum(r => r.Days);
        var jan = new DateOnly(year, 1, 1); var dec = new DateOnly(year, 12, 31);
        var pending = await db.LeaveRequests.AsNoTracking().Where(r => r.EmployeeId == empId && r.LeaveTypeId == t.Id && r.Status == LeaveStatus.Pending && r.StartDate >= jan && r.StartDate <= dec).SumAsync(r => (decimal?)r.Days, ct) ?? 0;
        var total = rows.Sum(r => r.Days);
        decimal expiring = 0; DateOnly? expiresOn = null;
        if (year == Today.Year && CarryDeadline(t, year) is { } deadline && Today <= deadline) { expiring = await CarryLeftAsync(empId, t, year, deadline, ct); if (expiring > 0) expiresOn = deadline; }
        return new BalanceView(t.Id, t.Code, t.Name, year, S(LedgerKind.Grant, LedgerKind.CarryOver), -S(LedgerKind.Used) - S(LedgerKind.Restore), S(LedgerKind.Adjust), pending, total - pending, expiring, expiresOn, -S(LedgerKind.Expire));
    }

    public async Task<BalanceView> AdjustAsync(AdjustBalanceRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);
        var errors = new Dictionary<string, string>();
        if (r.Days is null or 0 || Math.Abs(r.Days.Value) > 365) errors["days"] = "Enter a number of days other than 0 (up to ±365).";
        if (string.IsNullOrWhiteSpace(r.Note)) errors["note"] = "Explain the adjustment.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the adjustment.", errors);
        var emp = await db.Employees.FirstOrDefaultAsync(e => e.Id == r.EmployeeId, ct) ?? throw DomainException.NotFound("Employee");
        var type = await db.LeaveTypes.FirstOrDefaultAsync(t => t.Id == r.LeaveTypeId, ct) ?? throw DomainException.NotFound("Leave type");
        if (type.AnnualQuota is null) throw DomainException.Invariant("This leave type has no balance to adjust.");
        var year = r.Year ?? Today.Year;
        return await db.InTransactionAsync(async () =>
        {
            await db.ExecuteSqlAsync($"SELECT id FROM hr_employees WHERE id = {emp.Id} FOR UPDATE", ct);
            await EnsureGrantAsync(emp, type, ct); await EnsureExpiryAsync(emp, type, ct);
            db.LeaveLedger.Add(new LeaveLedgerEntry { EmployeeId = emp.Id, LeaveTypeId = type.Id, Year = year, Days = r.Days!.Value, Kind = LedgerKind.Adjust, Note = r.Note!.Trim(), CreatedBy = actor.UserId, CreatedAt = clock.UtcNow });
            audit.Record("hr.leave.adjust", "employee", emp.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["type"] = type.Code, ["days"] = r.Days!.Value.ToString(), ["year"] = year.ToString(), ["note"] = r.Note!.Trim() });
            await db.SaveChangesAsync(ct);
            return await BalanceAsync(emp.Id, type, year, ct);
        }, ct);
    }

    // ============================================================ requests
    public async Task<LeaveRequestView> SubmitAsync(SubmitLeaveRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await EnsureDefaultTypesAsync(ct);
        var emp = await MeAsync(actor, ct);
        var errors = new Dictionary<string, string>();
        var type = r.LeaveTypeId is { } tid ? await db.LeaveTypes.FirstOrDefaultAsync(t => t.Id == tid && t.ArchivedAt == null, ct) : null;
        if (type is null) errors["leaveTypeId"] = "Choose a leave type.";
        if (r.StartDate is null) errors["startDate"] = "Choose the first day.";
        if (r.EndDate is null) errors["endDate"] = "Choose the last day.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the request.", errors);
        DateOnly start = r.StartDate!.Value, end = r.EndDate!.Value; var half = r.HalfDay == true;
        if (end < start) errors["endDate"] = "The last day is before the first day.";
        else if (start.Year != end.Year) errors["endDate"] = "A request cannot span two calendar years. Submit one request for each year.";
        else if (start < emp.HireDate) errors["startDate"] = "Leave cannot start before the hire date.";
        else if (start < Today.AddDays(-30)) errors["startDate"] = "Leave can be requested up to 30 days back.";
        else if (start > Today.AddYears(1)) errors["startDate"] = "That date is too far ahead.";
        if (half && (!type!.AllowHalfDay || start != end)) errors["halfDay"] = type!.AllowHalfDay ? "A half day applies to a single day." : "This leave type does not allow half days.";
        var reason = r.Reason?.Trim(); if (reason is { Length: > 500 }) errors["reason"] = "Keep the reason under 500 characters.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the request.", errors);

        var days = await CountWorkingDaysAsync(emp.Id, start, end, ct);
        if (days == 0) throw DomainException.Validation("Please check the request.", new Dictionary<string, string> { ["startDate"] = "These dates have no working days (weekend or holiday)." });
        var amount = half ? 0.5m : days;
        if (type!.MaxDaysPerRequest is { } cap && amount > cap) throw DomainException.Validation("Please check the request.", new Dictionary<string, string> { ["endDate"] = $"{type.Name} allows up to {cap} days per request." });

        return await db.InTransactionAsync(async () =>
        {
            await db.ExecuteSqlAsync($"SELECT id FROM hr_employees WHERE id = {emp.Id} FOR UPDATE", ct); // serialises this person's requests
            if (await db.LeaveRequests.AnyAsync(x => x.EmployeeId == emp.Id && (x.Status == LeaveStatus.Pending || x.Status == LeaveStatus.Approved) && x.StartDate <= end && x.EndDate >= start, ct))
                throw DomainException.Conflict("You already have leave requested or approved on some of these days.");
            if (type.AnnualQuota is not null)
            {
                await EnsureGrantAsync(emp, type, ct); await EnsureExpiryAsync(emp, type, ct); await EnsureExpiryAsync(emp, type, ct);
                var bal = await BalanceAsync(emp.Id, type, start.Year, ct);
                if (amount > bal.Available) throw DomainException.Conflict($"Not enough {type.Name.ToLowerInvariant()}: {bal.Available:0.##} day(s) available, {amount:0.##} requested.");
            }
            var req = new LeaveRequest { EmployeeId = emp.Id, LeaveTypeId = type.Id, StartDate = start, EndDate = end, HalfDay = half, Days = amount, Reason = reason, NeedsHrApproval = type.HrApprovalFromDays is { } hrN && amount >= hrN, ApprovalChain = await flow.ChainForAsync("leave", amount, type.Id, ct), CreatedAt = clock.UtcNow };
            db.LeaveRequests.Add(req);
            audit.Record("hr.leave.request", "leave_request", req.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
                new Dictionary<string, string> { ["type"] = type.Code, ["start"] = start.ToString("yyyy-MM-dd"), ["end"] = end.ToString("yyyy-MM-dd"), ["days"] = amount.ToString() });
            var managerId = await db.Employments.AsNoTracking().Where(m => m.EmployeeId == emp.Id && m.EffectiveTo == null).Select(m => m.ManagerId).FirstOrDefaultAsync(ct);
            var notify = new List<Guid?> { managerId }; notify.AddRange((await managers.DelegatesOfAsync(managerId, ct)).Select(x => (Guid?)x));
            foreach (var who in notify.Distinct())
                if (await RecipientAsync(who, ct) is { } mgr && notes.EmailAllowed(mgr.UserId, "leave.requested"))
                    email.Enqueue(EmailTemplates.LeaveRequested(mgr.Email, mgr.Name, emp.FullName, type.Name, Span(start, end), amount.ToString("0.##"), links.Origin + "/hr/leave-approvals"));
            foreach (var uid in await notes.ApproverUsersAsync(emp.Id, ct)) notes.Notify(uid, "leave.requested", $"{emp.FullName} asked for leave", $"{type.Name}, {Span(start, end)}", "/hr/leave-approvals");
            await db.SaveChangesAsync(ct);
            return (await ViewsAsync([req], actor, ct))[0];
        }, ct);
    }

    /// <summary>Mon–Fri minus holidays. (A 6-day week would be a per-organisation setting; the 5-day week is the default.)</summary>
    private async Task<int> CountWorkingDaysAsync(Guid employeeId, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var holidays = (await db.Holidays.AsNoTracking().Where(h => h.Date >= start && h.Date <= end).Select(h => h.Date).ToListAsync(ct)).ToHashSet();
        var lookup = await shifts.LookupAsync([employeeId], start, end, ct);
        var n = 0;
        for (var d = start; d <= end; d = d.AddDays(1)) if (lookup.Works(employeeId, d) && !holidays.Contains(d)) n++;
        return n;
    }

    public async Task<PagedResult<LeaveRequestView>> ListAsync(LeaveQuery q, SessionPrincipal actor, CancellationToken ct = default)
    {
        var scope = (q.Scope ?? "mine").ToLowerInvariant(); var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        var query = db.LeaveRequests.AsNoTracking().AsQueryable();
        switch (scope)
        {
            case "mine": { var me = await MeAsync(actor, ct); query = query.Where(x => x.EmployeeId == me.Id); break; }
            case "team":
            {
                if (!perms.Contains(Permissions.LeaveApprove) && !perms.Contains(Permissions.LeaveManage)) throw DomainException.Denied(Permissions.LeaveApprove);
                var meId = (await db.Employees.AsNoTracking().Where(e => e.UserId == actor.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct));
                var acting = await managers.ActingIdsAsync(actor.UserId, ct);
                var reports = db.Employments.Where(m => m.EffectiveTo == null && m.ManagerId != null && acting.Contains(m.ManagerId.Value)).Select(m => m.EmployeeId);
                var skipLevel = db.Employments.Where(m => m.EffectiveTo == null && m.ManagerId != null && reports.Contains(m.ManagerId.Value)).Select(m => m.EmployeeId);   // the reports of my reports: I may be the second approver
                query = query.Where(x => (reports.Contains(x.EmployeeId) || skipLevel.Contains(x.EmployeeId)) && x.EmployeeId != meId); break;
            }
            case "all":
                if (!perms.Contains(Permissions.LeaveReadAll) && !perms.Contains(Permissions.LeaveManage)) throw DomainException.Denied(Permissions.LeaveReadAll);
                break;
            default: throw DomainException.Validation("Unknown scope.", new Dictionary<string, string> { ["scope"] = "Use mine, team or all." });
        }
        if (!string.IsNullOrWhiteSpace(q.Status) && q.Status != "all")
        {
            if (!Enum.TryParse<LeaveStatus>(q.Status, true, out var st)) throw DomainException.Validation("Unknown status.", new Dictionary<string, string> { ["status"] = "Use pending, approved, rejected or cancelled." });
            query = query.Where(x => x.Status == st);
        }
        if (q.From is { } f) query = query.Where(x => x.EndDate >= f);
        if (q.To is { } t) query = query.Where(x => x.StartDate <= t);
        var page = Math.Max(1, q.Page); var size = Math.Clamp(q.PageSize, 1, 500);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.Status == LeaveStatus.Pending).ThenByDescending(x => x.StartDate).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<LeaveRequestView>(await ViewsAsync(rows, actor, ct), page, size, total);
    }

    private async Task<IReadOnlyList<LeaveRequestView>> ViewsAsync(IReadOnlyList<LeaveRequest> rows, SessionPrincipal actor, CancellationToken ct)
    {
        var empIds = rows.Select(r => r.EmployeeId).Distinct().ToList(); var typeIds = rows.Select(r => r.LeaveTypeId).Distinct().ToList();
        var emps = await db.Employees.AsNoTracking().Where(e => empIds.Contains(e.Id)).Select(e => new { e.Id, e.EmployeeNo, e.FullName, e.UserId }).ToDictionaryAsync(e => e.Id, ct);
        var types = await db.LeaveTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        var meId = await db.Employees.AsNoTracking().Where(e => e.UserId == actor.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        var states = await flow.StatesAsync("leave", [.. rows.Where(r => r.Status == LeaveStatus.Pending).Select(r => (r.Id, r.EmployeeId, Chain(r)))], actor, perms, ct);
        var today = Today;
        return [.. rows.Select(r =>
        {
            var e = emps[r.EmployeeId]; var own = e.UserId == actor.UserId; var st = states.GetValueOrDefault(r.Id);
            var awaitingHr = st is { Waiting: "hr", Done: > 0 };
            var canDecide = r.Status == LeaveStatus.Pending && !own && st is { CanAct: true };
            var canCancel = (own && (r.Status == LeaveStatus.Pending || r.Status == LeaveStatus.Approved && r.StartDate > today)) || (perms.Contains(Permissions.LeaveManage) && r.Status is LeaveStatus.Pending or LeaveStatus.Approved);
            return new LeaveRequestView(r.Id, r.EmployeeId, e.EmployeeNo, e.FullName, r.LeaveTypeId, types.GetValueOrDefault(r.LeaveTypeId) ?? "", r.StartDate, r.EndDate, r.HalfDay, r.Days, r.Reason,
                r.Status.ToString().ToLowerInvariant(), r.DecidedAt, r.DecisionNote, r.CreatedAt, canDecide, canCancel, Chain(r).Contains("hr"), awaitingHr, st?.Waiting, st is null ? null : $"{st.Done}/{st.Chain.Length}");
        })];
    }

    /// <summary>The approval chain of a request: the one fixed at submission by a rule, else the built-in default (manager, plus HR for leave types that ask for it).</summary>
    private static string[] Chain(LeaveRequest r) => ApprovalFlowService.Parse(r.ApprovalChain, r.NeedsHrApproval ? ["manager", "hr"] : ["manager"]);

    private static string Span(DateOnly a, DateOnly b) => a == b ? a.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) : $"{a:d MMM} – {b:d MMM yyyy}";

    /// <summary>Best effort: a person with no linked, active login simply gets no e-mail (the request is still visible in the app).</summary>
    private async Task<(string Email, string Name, Guid UserId)?> RecipientAsync(Guid? employeeId, CancellationToken ct)
    {
        if (employeeId is null) return null;
        var r = await db.Employees.AsNoTracking().Where(e => e.Id == employeeId && e.UserId != null)
            .Join(db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active), e => e.UserId, u => u.Id, (e, u) => new { u.Email, e.FullName, u.Id }).FirstOrDefaultAsync(ct);
        return r is null ? null : (r.Email, r.FullName, r.Id);
    }

    private async Task<bool> IsManagerOfAsync(SessionPrincipal actor, Guid employeeId, CancellationToken ct)
    {
        var acting = await managers.ActingIdsAsync(actor.UserId, ct);
        return acting.Count > 0 && await db.Employments.AnyAsync(m => m.EffectiveTo == null && m.EmployeeId == employeeId && m.ManagerId != null && acting.Contains(m.ManagerId.Value), ct);
    }

    public async Task<LeaveRequestView> DecideAsync(Guid id, bool approve, string? note, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var req0 = await db.LeaveRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Leave request");
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        var emp = await db.Employees.FirstAsync(e => e.Id == req0.EmployeeId, ct);
        if (emp.UserId == actor.UserId) throw DomainException.Invariant("You cannot decide your own leave request.");
        var state = (await flow.StatesAsync("leave", [(req0.Id, emp.Id, Chain(req0))], actor, perms, ct))[req0.Id];
        if (state.AlreadyApprovedByCaller && !perms.Contains(Permissions.LeaveManage)) throw DomainException.Conflict($"You already approved this request. It is waiting for {ApprovalFlowService.Describe(state.Waiting)}.");
        if (!state.CanAct) throw DomainException.DeniedBecause("Only the employee's manager or HR can decide this request, each at their step.");
        var reason = note?.Trim(); if (reason is { Length: > 300 }) throw DomainException.Validation("Note is too long.", new Dictionary<string, string> { ["note"] = "Keep it under 300 characters." });
        if (!approve && string.IsNullOrEmpty(reason)) throw DomainException.Validation("Please give a reason.", new Dictionary<string, string> { ["note"] = "Tell the employee why." });

        return await db.InTransactionAsync(async () =>
        {
            await db.ExecuteSqlAsync($"SELECT id FROM hr_employees WHERE id = {emp.Id} FOR UPDATE", ct);
            var req = await db.LeaveRequests.FirstAsync(x => x.Id == id, ct);
            if (req.Status != LeaveStatus.Pending) throw DomainException.Conflict($"This request is already {req.Status.ToString().ToLowerInvariant()}.");
            if (emp.Status != EmployeeStatus.Active) throw DomainException.Invariant("This employee's employment has ended.");
            var type = await db.LeaveTypes.FirstAsync(t => t.Id == req.LeaveTypeId, ct);
            // ADM-002: an approval either finishes the chain or hands the request to the next step (whose approvers are told). A rejection by anyone entitled is final.
            if (approve && !await flow.ApproveAsync("leave", id, emp.Id, emp.FullName, Chain(req), actor, perms, "/hr/approvals", ct))
            {
                req.ManagerApprovedBy ??= actor.UserId; req.ManagerApprovedAt ??= clock.UtcNow; req.Version++;
                audit.Record("hr.leave.manager_approve", "leave_request", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employee"] = emp.EmployeeNo, ["days"] = req.Days.ToString() });
                notes.Notify(emp.UserId, "leave.progress", "Your leave moved one step forward", "It now waits for the next approver.", "/hr/my-leave");
                await db.SaveChangesAsync(ct);
                return (await ViewsAsync([req], actor, ct))[0];
            }
            if (approve && type.AnnualQuota is not null)
            {
                var bal = await BalanceAsync(emp.Id, type, req.StartDate.Year, ct); // the ledger total (granted + adjusted − used) must cover it; this request is itself pending, so Available would double count it
                if (bal.Granted + bal.Adjusted - bal.Used < req.Days) throw DomainException.Conflict("The balance no longer covers this request.");
                db.LeaveLedger.Add(new LeaveLedgerEntry { EmployeeId = emp.Id, LeaveTypeId = type.Id, Year = req.StartDate.Year, Days = -req.Days, Kind = LedgerKind.Used, RequestId = req.Id, CreatedBy = actor.UserId, CreatedAt = clock.UtcNow });
            }
            req.Status = approve ? LeaveStatus.Approved : LeaveStatus.Rejected; req.DecidedBy = actor.UserId; req.DecidedAt = clock.UtcNow; req.DecisionNote = reason; req.Version++;
            audit.Record(approve ? "hr.leave.approve" : "hr.leave.reject", "leave_request", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employee"] = emp.EmployeeNo, ["days"] = req.Days.ToString() });
            await webhooks.EnqueueAsync(approve ? "leave.approved" : "leave.rejected", new { requestId = id, employeeId = emp.Id, employeeNo = emp.EmployeeNo, leaveType = type.Code, startDate = req.StartDate, endDate = req.EndDate, days = req.Days }, ct);
            notes.Notify(emp.UserId, "leave.decided", approve ? "Your leave was approved" : "Your leave was rejected", $"{type.Name}, {Span(req.StartDate, req.EndDate)}" + (string.IsNullOrEmpty(reason) ? "" : $" — {reason}"), "/hr/my-leave");
            if (await RecipientAsync(emp.Id, ct) is { } who && notes.EmailAllowed(who.UserId, "leave.decided"))
                email.Enqueue(EmailTemplates.LeaveDecided(who.Email, who.Name, type.Name, Span(req.StartDate, req.EndDate), approve, reason, links.Origin + "/hr/my-leave"));
            await db.SaveChangesAsync(ct);
            return (await ViewsAsync([req], actor, ct))[0];
        }, ct);
    }

    public async Task<LeaveRequestView> CancelAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var req0 = await db.LeaveRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Leave request");
        var emp = await db.Employees.FirstAsync(e => e.Id == req0.EmployeeId, ct);
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        var own = emp.UserId == actor.UserId; var hr = perms.Contains(Permissions.LeaveManage);
        if (!own && !hr) throw DomainException.NotFound("Leave request"); // other people's requests are not even visible
        return await db.InTransactionAsync(async () =>
        {
            await db.ExecuteSqlAsync($"SELECT id FROM hr_employees WHERE id = {emp.Id} FOR UPDATE", ct);
            var req = await db.LeaveRequests.FirstAsync(x => x.Id == id, ct);
            if (req.Status is LeaveStatus.Cancelled or LeaveStatus.Rejected) throw DomainException.Conflict($"This request is already {req.Status.ToString().ToLowerInvariant()}.");
            if (req.Status == LeaveStatus.Approved && !hr && req.StartDate <= Today) throw DomainException.Invariant("Leave that has started can only be cancelled by HR.");
            var wasApproved = req.Status == LeaveStatus.Approved;
            req.Status = LeaveStatus.Cancelled; req.DecidedAt ??= clock.UtcNow; req.Version++;
            if (wasApproved && await db.LeaveTypes.AnyAsync(t => t.Id == req.LeaveTypeId && t.AnnualQuota != null, ct))
                db.LeaveLedger.Add(new LeaveLedgerEntry { EmployeeId = emp.Id, LeaveTypeId = req.LeaveTypeId, Year = req.StartDate.Year, Days = req.Days, Kind = LedgerKind.Restore, RequestId = req.Id, CreatedBy = actor.UserId, CreatedAt = clock.UtcNow });
            audit.Record("hr.leave.cancel", "leave_request", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employee"] = emp.EmployeeNo, ["wasApproved"] = wasApproved.ToString() });
            await db.SaveChangesAsync(ct);
            return (await ViewsAsync([req], actor, ct))[0];
        }, ct);
    }
}
