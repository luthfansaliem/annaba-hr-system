using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record ClaimPolicyView(Guid Id, string Category, string Name, Guid? GradeId, string? Grade, decimal? AnnualLimit, decimal? PerClaimLimit, bool Taxable, bool Active);
public sealed record ClaimPolicyRequest(string? Category, string? Name, Guid? GradeId, decimal? AnnualLimit, decimal? PerClaimLimit, bool? Taxable, bool? Active);
public sealed record ClaimBalance(string Category, string Name, decimal? AnnualLimit, decimal? PerClaimLimit, decimal Used, decimal? Remaining, bool Taxable);

public sealed record CashAdvanceView(Guid Id, Guid EmployeeId, string EmployeeNo, string EmployeeName, decimal Amount, string Purpose, DateOnly? NeededBy, string Status, string? DecisionNote, DateOnly? DisbursedOn,
    decimal? SpentAmount, decimal? Difference, string? SettlementNote, bool HasReceipt, DateTimeOffset CreatedAt, bool CanDecide, bool CanDisburse, bool CanSettle, bool CanApproveSettlement, bool CanCancel);
public sealed record CashAdvanceRequest(decimal? Amount, string? Purpose, DateOnly? NeededBy);
public sealed record SettleAdvanceRequest(decimal? SpentAmount, string? Note, string? ReceiptBase64);
public sealed record AdvanceDecisionRequest(bool? Approve, string? Note);
public sealed record DisburseRequest(DateOnly? On);

public sealed record WageAdvanceView(Guid Id, Guid EmployeeId, string EmployeeNo, string EmployeeName, int Year, int Month, decimal Amount, string? Reason, string Status, string? DecisionNote, DateOnly? DisbursedOn, DateTimeOffset CreatedAt,
    bool CanDecide, bool CanDisburse, bool CanCancel);
public sealed record WageAdvanceAvailability(bool Eligible, string? Reason, decimal Earned, decimal MaxShare, decimal AlreadyTaken, decimal Available, int Year, int Month);
public sealed record WageAdvanceRequest(decimal? Amount, string? Reason);

/// <summary>
/// Benefits and advances around payroll (Talenta: reimbursement policies and balances, flexible benefit, cash advance, earned wage access).
/// Claim policies cap what a person may claim per category per year, optionally per pay grade, and say whether the money is taxable. A cash advance is paid out before a trip and
/// settled afterwards against what was actually spent; the difference goes through the next pay run. An early wage withdrawal hands out part of the wage already earned this
/// month and is deducted in that month's pay run. Nobody decides their own request.
/// </summary>
public sealed class BenefitService(IAppDb db, IClock clock, AuthorizationService authz, AuditWriter audit, ManagerScope managers, NotificationService notes, IFileScanner scanner, PayrollService payroll, PayrollRulesService ruleSets)
{
    public const int MaxOpenAdvances = 3;
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    private async Task<Employee> MeAsync(SessionPrincipal actor, CancellationToken ct)
        => await db.Employees.FirstOrDefaultAsync(e => e.UserId == actor.UserId && e.Status == EmployeeStatus.Active, ct) ?? throw DomainException.Invariant("Your account is not linked to an active employee record. Ask HR to link it.");

    private async Task<bool> ManagesAsync(SessionPrincipal actor, Guid employeeId, CancellationToken ct)
    {
        var acting = await managers.ActingIdsAsync(actor.UserId, ct); if (acting.Count == 0) return false;
        return await db.Employments.AnyAsync(m => m.EmployeeId == employeeId && m.EffectiveTo == null && m.ManagerId != null && acting.Contains(m.ManagerId.Value), ct);
    }

    // ============================================================ claim policies and balances
    public static string NormaliseCategory(string? s) => System.Text.RegularExpressions.Regex.Replace((s ?? "").Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    public async Task<IReadOnlyList<ClaimPolicyView>> PoliciesAsync(CancellationToken ct = default)
    {
        var grades = await db.SalaryGrades.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Code, ct);
        return [.. (await db.ClaimPolicies.AsNoTracking().OrderByDescending(p => p.Active).ThenBy(p => p.Category).ThenBy(p => p.GradeId).ToListAsync(ct))
            .Select(p => new ClaimPolicyView(p.Id, p.Category, p.Name, p.GradeId, p.GradeId is { } g ? grades.GetValueOrDefault(g) : null, p.AnnualLimit, p.PerClaimLimit, p.Taxable, p.Active))];
    }

    public async Task<ClaimPolicyView> SavePolicyAsync(Guid? id, ClaimPolicyRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var p = id is { } pid ? await db.ClaimPolicies.FirstOrDefaultAsync(x => x.Id == pid, ct) ?? throw DomainException.NotFound("Claim policy") : new ClaimPolicy { CreatedAt = clock.UtcNow };
        var errors = new Dictionary<string, string>();
        var category = r.Category is null ? p.Category : NormaliseCategory(r.Category); if (category.Length is < 2 or > 30) errors["category"] = "Enter a category code of 2–30 letters, e.g. medical or wellness.";
        var name = (r.Name ?? p.Name).Trim(); if (name.Length is < 1 or > 100) errors["name"] = "Enter a name (up to 100 characters).";
        var annual = r.AnnualLimit ?? (id is null ? null : p.AnnualLimit); var perClaim = r.PerClaimLimit ?? (id is null ? null : p.PerClaimLimit);
        if (annual is <= 0 or > 10_000_000_000m) errors["annualLimit"] = "Enter a positive amount, or leave it empty for no yearly limit.";
        if (perClaim is <= 0 or > 10_000_000_000m) errors["perClaimLimit"] = "Enter a positive amount, or leave it empty for no limit per claim.";
        if (annual is { } a && perClaim is { } pc && pc > a) errors["perClaimLimit"] = "A single claim cannot be allowed more than the yearly limit.";
        var grade = r.GradeId ?? (id is null ? null : p.GradeId);
        if (r.GradeId is { } gid && !await db.SalaryGrades.AnyAsync(g => g.Id == gid, ct)) errors["gradeId"] = "Choose a grade.";
        var active = r.Active ?? p.Active;
        if (errors.Count == 0 && active && await db.ClaimPolicies.AnyAsync(x => x.Id != p.Id && x.Active && x.Category == category && x.GradeId == grade, ct)) errors["category"] = "There is already an active policy for this category and grade.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the policy.", errors);
        p.Category = category; p.Name = name; p.GradeId = grade; p.AnnualLimit = annual is { } av ? PayrollRules.Rupiah(av) : null; p.PerClaimLimit = perClaim is { } pv ? PayrollRules.Rupiah(pv) : null; p.Taxable = r.Taxable ?? p.Taxable; p.Active = active;
        if (id is null) db.ClaimPolicies.Add(p);
        audit.Record(id is null ? "hr.claim.policy_create" : "hr.claim.policy_update", "claim_policy", p.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["category"] = category, ["annualLimit"] = p.AnnualLimit?.ToString() ?? "none" });
        await db.SaveChangesAsync(ct);
        return (await PoliciesAsync(ct)).First(x => x.Id == p.Id);
    }

    /// <summary>The policy that applies to a person for a category: one for their pay grade first, then the general one. Null = no limits, not taxable.</summary>
    internal async Task<ClaimPolicy?> PolicyForAsync(Guid employeeId, string category, CancellationToken ct)
    {
        var grade = await db.Compensations.AsNoTracking().Where(c => c.EmployeeId == employeeId && c.EffectiveTo == null).Select(c => c.GradeId).FirstOrDefaultAsync(ct);
        var rules = await db.ClaimPolicies.AsNoTracking().Where(p => p.Active && p.Category == category && (p.GradeId == null || p.GradeId == grade)).ToListAsync(ct);
        return rules.FirstOrDefault(p => p.GradeId != null) ?? rules.FirstOrDefault();
    }

    /// <summary>What a person has claimed in a category in a calendar year: waiting, approved and paid claims count (a waiting claim reserves its amount).</summary>
    internal async Task<decimal> UsedAsync(Guid employeeId, string category, int year, Guid? exceptClaim, CancellationToken ct)
    {
        DateOnly from = new(year, 1, 1), to = new(year, 12, 31);
        return await db.ExpenseClaims.Where(c => c.EmployeeId == employeeId && c.Category == category && c.ExpenseDate >= from && c.ExpenseDate <= to && c.Id != exceptClaim
                                                 && (c.Status == ClaimStatus.Pending || c.Status == ClaimStatus.Approved || c.Status == ClaimStatus.Paid)).SumAsync(c => (decimal?)c.Amount, ct) ?? 0;
    }

    /// <summary>Throws a validation error when a claim of <paramref name="amount"/> would break the person's policy for the category.</summary>
    internal async Task<ClaimPolicy?> CheckClaimAsync(Guid employeeId, string category, DateOnly date, decimal amount, Guid? exceptClaim, CancellationToken ct)
    {
        var p = await PolicyForAsync(employeeId, category, ct); if (p is null) return null;
        if (p.PerClaimLimit is { } per && amount > per) throw DomainException.Validation("Please check the claim.", new Dictionary<string, string> { ["amount"] = FormattableString.Invariant($"The most one {p.Name} claim may be is {per:#,##0}.") });
        if (p.AnnualLimit is { } cap)
        {
            var used = await UsedAsync(employeeId, category, date.Year, exceptClaim, ct);
            if (used + amount > cap) throw DomainException.Validation("Please check the claim.", new Dictionary<string, string> { ["amount"] = FormattableString.Invariant($"This would go over the yearly {p.Name} limit of {cap:#,##0}: {Math.Max(cap - used, 0):#,##0} is left for {date.Year}.") });
        }
        return p;
    }

    /// <summary>Categories the person can claim in, with their limits and what is left this year. Built-in categories without a policy are listed with no limit.</summary>
    public async Task<IReadOnlyList<ClaimBalance>> MyBalancesAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct); var year = Today.Year;
        var cats = ClaimService.Categories.Concat(await db.ClaimPolicies.AsNoTracking().Where(p => p.Active).Select(p => p.Category).Distinct().ToListAsync(ct)).Distinct().ToList();
        DateOnly jan1 = new(year, 1, 1), dec31 = new(year, 12, 31);
        var used = (await db.ExpenseClaims.AsNoTracking().Where(c => c.EmployeeId == me.Id && c.ExpenseDate >= jan1 && c.ExpenseDate <= dec31 && (c.Status == ClaimStatus.Pending || c.Status == ClaimStatus.Approved || c.Status == ClaimStatus.Paid))
            .GroupBy(c => c.Category).Select(g => new { g.Key, Sum = g.Sum(c => c.Amount) }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.Sum);
        var list = new List<ClaimBalance>();
        foreach (var c in cats)
        {
            var p = await PolicyForAsync(me.Id, c, ct); var u = used.GetValueOrDefault(c);
            list.Add(new ClaimBalance(c, p?.Name ?? c, p?.AnnualLimit, p?.PerClaimLimit, u, p?.AnnualLimit is { } cap ? Math.Max(cap - u, 0) : null, p?.Taxable ?? false));
        }
        return list;
    }

    // ============================================================ cash advances
    private async Task<IReadOnlyList<CashAdvanceView>> AdvanceViewsAsync(IReadOnlyList<CashAdvance> rows, SessionPrincipal actor, CancellationToken ct)
    {
        var ids = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var emps = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.EmployeeNo, e.FullName, e.UserId }).ToDictionaryAsync(e => e.Id, ct);
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct); var manage = perms.Contains(Permissions.ClaimsManage);
        var managed = new HashSet<Guid>(); if (perms.Contains(Permissions.ClaimsApprove)) foreach (var id in ids) if (await ManagesAsync(actor, id, ct)) managed.Add(id);
        return [.. rows.Select(a =>
        {
            var e = emps[a.EmployeeId]; var own = e.UserId == actor.UserId; var decider = !own && (manage || managed.Contains(a.EmployeeId));
            return new CashAdvanceView(a.Id, a.EmployeeId, e.EmployeeNo, e.FullName, a.Amount, a.Purpose, a.NeededBy, StatusName(a.Status), a.DecisionNote, a.DisbursedOn, a.SpentAmount, a.SpentAmount is { } s ? s - a.Amount : null, a.SettlementNote, a.ReceiptType != null, a.CreatedAt,
                CanDecide: decider && a.Status == AdvanceStatus.Pending, CanDisburse: !own && manage && a.Status == AdvanceStatus.Approved, CanSettle: own && a.Status == AdvanceStatus.Disbursed,
                CanApproveSettlement: decider && a.Status == AdvanceStatus.SettlementPending, CanCancel: own && a.Status == AdvanceStatus.Pending);
        })];
    }

    private static string StatusName(AdvanceStatus s) => s switch { AdvanceStatus.SettlementPending => "settlement_pending", AdvanceStatus.SettlementApproved => "settlement_approved", _ => s.ToString().ToLowerInvariant() };

    public async Task<IReadOnlyList<CashAdvanceView>> AdvancesAsync(string? scope, SessionPrincipal actor, CancellationToken ct = default)
    {
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct); var q = db.CashAdvances.AsNoTracking().AsQueryable();
        switch ((scope ?? "mine").ToLowerInvariant())
        {
            case "mine": { var me = await MeAsync(actor, ct); q = q.Where(a => a.EmployeeId == me.Id); break; }
            case "team":
            {
                if (!perms.Contains(Permissions.ClaimsApprove) && !perms.Contains(Permissions.ClaimsManage)) throw DomainException.Denied(Permissions.ClaimsApprove);
                var acting = await managers.ActingIdsAsync(actor.UserId, ct);
                var reports = db.Employments.Where(m => m.EffectiveTo == null && m.ManagerId != null && acting.Contains(m.ManagerId.Value)).Select(m => m.EmployeeId);
                q = q.Where(a => reports.Contains(a.EmployeeId)); break;
            }
            case "all": if (!perms.Contains(Permissions.ClaimsManage)) throw DomainException.Denied(Permissions.ClaimsManage); break;
            default: throw DomainException.Validation("Unknown scope.", new Dictionary<string, string> { ["scope"] = "Use mine, team or all." });
        }
        var rows = await q.OrderByDescending(a => a.CreatedAt).Take(500)
            .Select(a => new CashAdvance { Id = a.Id, EmployeeId = a.EmployeeId, Amount = a.Amount, Purpose = a.Purpose, NeededBy = a.NeededBy, Status = a.Status, DecisionNote = a.DecisionNote, DisbursedOn = a.DisbursedOn, SpentAmount = a.SpentAmount, SettlementNote = a.SettlementNote, ReceiptType = a.ReceiptType, CreatedAt = a.CreatedAt })
            .ToListAsync(ct);
        return await AdvanceViewsAsync(rows, actor, ct);
    }

    public async Task<CashAdvanceView> RequestAdvanceAsync(CashAdvanceRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct); var errors = new Dictionary<string, string>();
        if (r.Amount is null or <= 0 or > 1_000_000_000m) errors["amount"] = "Enter the amount in rupiah.";
        var purpose = r.Purpose?.Trim(); if (string.IsNullOrEmpty(purpose) || purpose.Length > 300) errors["purpose"] = "Say what the money is for (up to 300 characters).";
        if (r.NeededBy is { } nb && (nb < Today || nb > Today.AddMonths(6))) errors["neededBy"] = "Choose a date within the next six months.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the request.", errors);
        if (await db.CashAdvances.CountAsync(a => a.EmployeeId == me.Id && a.Status != AdvanceStatus.Rejected && a.Status != AdvanceStatus.Cancelled && a.Status != AdvanceStatus.Settled, ct) >= MaxOpenAdvances)
            throw DomainException.Invariant($"You already have {MaxOpenAdvances} advances that are not settled yet. Settle one first.");
        var a = new CashAdvance { EmployeeId = me.Id, Amount = PayrollRules.Rupiah(r.Amount!.Value), Purpose = purpose!, NeededBy = r.NeededBy, CreatedAt = clock.UtcNow };
        db.CashAdvances.Add(a);
        audit.Record("hr.advance.request", "cash_advance", a.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeNo"] = me.EmployeeNo });
        foreach (var uid in await notes.ApproverUsersAsync(me.Id, ct)) notes.Notify(uid, "advance.requested", $"{me.FullName} asked for a cash advance", purpose, "/hr/cash-advances");
        await db.SaveChangesAsync(ct);
        return (await AdvanceViewsAsync([a], actor, ct))[0];
    }

    private async Task<(CashAdvance Advance, Employee Employee, bool Manage)> ForDecisionAsync(Guid id, SessionPrincipal actor, CancellationToken ct)
    {
        var a = await db.CashAdvances.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Cash advance");
        var e = await db.Employees.AsNoTracking().FirstAsync(x => x.Id == a.EmployeeId, ct);
        if (e.UserId == actor.UserId) throw DomainException.Invariant("You cannot decide your own cash advance.");
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct); var manage = perms.Contains(Permissions.ClaimsManage);
        if (!manage && !(perms.Contains(Permissions.ClaimsApprove) && await ManagesAsync(actor, e.Id, ct))) throw DomainException.DeniedBecause("Only the employee's manager or someone who manages claims can decide this.");
        return (a, e, manage);
    }

    public async Task<CashAdvanceView> DecideAdvanceAsync(Guid id, AdvanceDecisionRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var (a, e, _) = await ForDecisionAsync(id, actor, ct);
        var note = r.Note?.Trim(); if (r.Approve != true && string.IsNullOrEmpty(note)) throw DomainException.Validation("Please give a reason.", new Dictionary<string, string> { ["note"] = "Tell the employee why." });
        if (a.Status != AdvanceStatus.Pending) throw DomainException.Conflict("This advance is already decided.");
        a.Status = r.Approve == true ? AdvanceStatus.Approved : AdvanceStatus.Rejected; a.DecidedBy = actor.UserId; a.DecidedAt = clock.UtcNow; a.DecisionNote = note?[..Math.Min(note.Length, 300)]; a.Version++;
        audit.Record(r.Approve == true ? "hr.advance.approve" : "hr.advance.reject", "cash_advance", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeNo"] = e.EmployeeNo });
        notes.Notify(e.UserId, "advance.decided", r.Approve == true ? "Your cash advance was approved" : "Your cash advance was rejected", r.Approve == true ? "Finance will transfer it." : note, "/hr/my-claims");
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { db.ResetTracking(); throw DomainException.Conflict("Someone else changed this advance just now."); }
        return (await AdvanceViewsAsync([a], actor, ct))[0];
    }

    public async Task<CashAdvanceView> DisburseAdvanceAsync(Guid id, DisburseRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var (a, e, manage) = await ForDecisionAsync(id, actor, ct);
        if (!manage) throw DomainException.Denied(Permissions.ClaimsManage);
        if (a.Status != AdvanceStatus.Approved) throw DomainException.Conflict("Only an approved advance can be marked as paid out.");
        var on = r.On ?? Today; if (on > Today || on < Today.AddDays(-60)) throw DomainException.Validation("Please check the date.", new Dictionary<string, string> { ["on"] = "Choose the transfer date (today or up to 60 days back)." });
        a.Status = AdvanceStatus.Disbursed; a.DisbursedBy = actor.UserId; a.DisbursedOn = on; a.Version++;
        audit.Record("hr.advance.disburse", "cash_advance", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeNo"] = e.EmployeeNo, ["on"] = on.ToString("yyyy-MM-dd") });
        notes.Notify(e.UserId, "advance.decided", "Your cash advance was paid out", "Report what you spent afterwards so it can be settled.", "/hr/my-claims");
        await db.SaveChangesAsync(ct);
        return (await AdvanceViewsAsync([a], actor, ct))[0];
    }

    public async Task<CashAdvanceView> SettleAdvanceAsync(Guid id, SettleAdvanceRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct);
        var a = await db.CashAdvances.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == me.Id, ct) ?? throw DomainException.NotFound("Cash advance");
        if (a.Status != AdvanceStatus.Disbursed) throw DomainException.Conflict("Only an advance that was paid out can be settled.");
        var errors = new Dictionary<string, string>();
        if (r.SpentAmount is null or < 0 or > 1_000_000_000m) errors["spentAmount"] = "Enter what you actually spent (0 if nothing).";
        var note = r.Note?.Trim(); if (note is { Length: > 300 }) errors["note"] = "Keep it under 300 characters.";
        byte[]? bytes = null; (string ContentType, string Extension)? kind = null;
        if (!string.IsNullOrWhiteSpace(r.ReceiptBase64))
        {
            try { bytes = Convert.FromBase64String(r.ReceiptBase64); } catch (FormatException) { errors["receipt"] = "The receipt could not be read."; }
            if (bytes is not null) { kind = DocumentService.Sniff(bytes); if (bytes.Length is 0 or > DocumentService.MaxBytes || kind is null) errors["receipt"] = "Only PDF, PNG and JPEG files up to 4 MB are accepted."; }
        }
        if (errors.Count > 0) throw DomainException.Validation("Please check the settlement.", errors);
        if (bytes is not null && scanner.Enabled && (await scanner.ScanAsync(bytes, ct)).Verdict == ScanVerdict.Infected) throw DomainException.Validation("Please check the settlement.", new Dictionary<string, string> { ["receipt"] = "The virus scanner rejected this file." });
        a.SpentAmount = PayrollRules.Rupiah(r.SpentAmount!.Value); a.SettlementNote = note; a.SettlementSubmittedAt = clock.UtcNow; a.Status = AdvanceStatus.SettlementPending; a.Version++;
        if (bytes is not null) { a.Receipt = bytes; a.ReceiptType = kind!.Value.ContentType; a.ReceiptName = $"receipt{kind.Value.Extension}"; }
        audit.Record("hr.advance.settle", "cash_advance", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeNo"] = me.EmployeeNo });
        foreach (var uid in await notes.ApproverUsersAsync(me.Id, ct)) notes.Notify(uid, "advance.requested", $"{me.FullName} settled a cash advance", null, "/hr/cash-advances");
        await db.SaveChangesAsync(ct);
        return (await AdvanceViewsAsync([a], actor, ct))[0];
    }

    /// <summary>Accepting the settlement: with no difference the advance is closed at once; otherwise the next pay run pays or deducts the difference and its lock closes the advance.</summary>
    public async Task<CashAdvanceView> ApproveSettlementAsync(Guid id, AdvanceDecisionRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var (a, e, _) = await ForDecisionAsync(id, actor, ct);
        if (a.Status != AdvanceStatus.SettlementPending) throw DomainException.Conflict("There is no settlement waiting for this advance.");
        var note = r.Note?.Trim();
        if (r.Approve != true)
        {
            if (string.IsNullOrEmpty(note)) throw DomainException.Validation("Please give a reason.", new Dictionary<string, string> { ["note"] = "Tell the employee what to correct." });
            a.Status = AdvanceStatus.Disbursed; a.DecisionNote = note[..Math.Min(note.Length, 300)];   // back to the employee to report again
        }
        else { a.SettlementApprovedBy = actor.UserId; a.Status = a.SpentAmount == a.Amount ? AdvanceStatus.Settled : AdvanceStatus.SettlementApproved; }
        a.Version++;
        audit.Record(r.Approve == true ? "hr.advance.settlement_approve" : "hr.advance.settlement_return", "cash_advance", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeNo"] = e.EmployeeNo });
        notes.Notify(e.UserId, "advance.decided", r.Approve == true ? "Your cash advance settlement was accepted" : "Your cash advance settlement was sent back", r.Approve == true ? (a.SpentAmount > a.Amount ? "The difference will be paid with your next payslip." : a.SpentAmount < a.Amount ? "The unused amount will be deducted from your next payslip." : null) : note, "/hr/my-claims");
        await db.SaveChangesAsync(ct);
        return (await AdvanceViewsAsync([a], actor, ct))[0];
    }

    public async Task<CashAdvanceView> CancelAdvanceAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct);
        var a = await db.CashAdvances.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == me.Id, ct) ?? throw DomainException.NotFound("Cash advance");
        if (a.Status != AdvanceStatus.Pending) throw DomainException.Conflict("Only a waiting request can be cancelled.");
        a.Status = AdvanceStatus.Cancelled; a.Version++;
        audit.Record("hr.advance.cancel", "cash_advance", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return (await AdvanceViewsAsync([a], actor, ct))[0];
    }

    public async Task<DocumentFile> AdvanceReceiptAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var a = await db.CashAdvances.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Cash advance");
        var e = await db.Employees.AsNoTracking().FirstAsync(x => x.Id == a.EmployeeId, ct); var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        var own = e.UserId == actor.UserId;
        if (!own && !perms.Contains(Permissions.ClaimsManage) && !(perms.Contains(Permissions.ClaimsApprove) && await ManagesAsync(actor, e.Id, ct))) throw DomainException.NotFound("Cash advance");
        if (a.Receipt is null || a.ReceiptType is null) throw DomainException.NotFound("Receipt");
        if (!own) { audit.Record("hr.advance.receipt_viewed", "cash_advance", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email); await db.SaveChangesAsync(ct); }
        return new DocumentFile(a.ReceiptName ?? "receipt", a.ReceiptType, a.Receipt);
    }

    // ============================================================ early wage withdrawal (earned wage access, company-funded)
    private async Task<IReadOnlyList<WageAdvanceView>> WageViewsAsync(IReadOnlyList<WageAdvance> rows, SessionPrincipal actor, CancellationToken ct)
    {
        var ids = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var emps = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.EmployeeNo, e.FullName, e.UserId }).ToDictionaryAsync(e => e.Id, ct);
        var run = (await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(Permissions.PayrollRun);
        return [.. rows.Select(w =>
        {
            var e = emps[w.EmployeeId]; var own = e.UserId == actor.UserId;
            return new WageAdvanceView(w.Id, w.EmployeeId, e.EmployeeNo, e.FullName, w.Year, w.Month, w.Amount, w.Reason, w.Status.ToString().ToLowerInvariant(), w.DecisionNote, w.DisbursedOn, w.CreatedAt,
                CanDecide: run && !own && w.Status == WageAdvanceStatus.Pending, CanDisburse: run && !own && w.Status == WageAdvanceStatus.Approved, CanCancel: own && w.Status == WageAdvanceStatus.Pending);
        })];
    }

    /// <summary>How much of this month's wage a person can still take early: earned so far × the allowed share, minus what was already requested this month.</summary>
    public async Task<WageAdvanceAvailability> AvailabilityAsync(SessionPrincipal actor, CancellationToken ct = default) => await AvailabilityForAsync(await MeAsync(actor, ct), ct);

    private async Task<WageAdvanceAvailability> AvailabilityForAsync(Employee me, CancellationToken ct)
    {
        var today = Today; var start = new DateOnly(today.Year, today.Month, 1); var days = DateTime.DaysInMonth(today.Year, today.Month);
        var rules = await ruleSets.EffectiveForAsync(today.Year, today.Month, ct);
        WageAdvanceAvailability No(string why) => new(false, why, 0, rules.WageAdvanceMaxShare, 0, 0, today.Year, today.Month);
        if (await db.PayRuns.AnyAsync(p => p.LegalEntityId == me.LegalEntityId && p.Year == today.Year && p.Month == today.Month && p.Status != PayRunStatus.Draft, ct)) return No("This month's payroll is already being prepared.");
        var c = await db.Compensations.AsNoTracking().Where(x => x.EmployeeId == me.Id && x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today)).OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
        if (c is null) return No("You have no salary record yet.");
        var (basic, allowance) = payroll.RevealPay(c); decimal earned;
        if (c.PayBasis == PayBasis.Daily)
        {
            var worked = await db.AttendanceLogs.AsNoTracking().Where(l => l.EmployeeId == me.Id && l.Date >= start && l.Date <= today).Select(l => l.Date).Distinct().CountAsync(ct);
            earned = (basic + allowance) * worked;
        }
        else
        {
            var from = me.HireDate > start ? me.HireDate : start; var elapsed = Math.Max(today.DayNumber - from.DayNumber + 1, 0);
            earned = (basic + allowance) * elapsed / days;
        }
        earned = PayrollRules.Rupiah(earned);
        var taken = await db.WageAdvances.Where(w => w.EmployeeId == me.Id && w.Year == today.Year && w.Month == today.Month && w.Status != WageAdvanceStatus.Rejected && w.Status != WageAdvanceStatus.Cancelled).SumAsync(w => (decimal?)w.Amount, ct) ?? 0;
        var available = Math.Max(Math.Floor(earned * rules.WageAdvanceMaxShare / 1000m) * 1000m - taken, 0);   // whole thousands
        return new(true, null, earned, rules.WageAdvanceMaxShare, taken, available, today.Year, today.Month);
    }

    public async Task<IReadOnlyList<WageAdvanceView>> WageAdvancesAsync(string? scope, SessionPrincipal actor, CancellationToken ct = default)
    {
        var q = db.WageAdvances.AsNoTracking().AsQueryable();
        if ((scope ?? "mine") == "mine") { var me = await MeAsync(actor, ct); q = q.Where(w => w.EmployeeId == me.Id); }
        else if (scope == "all") { await payroll.GuardAsync(actor, ct); if (!(await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(Permissions.PayrollRead)) throw DomainException.Denied(Permissions.PayrollRead); }
        else throw DomainException.Validation("Unknown scope.", new Dictionary<string, string> { ["scope"] = "Use mine or all." });
        return await WageViewsAsync(await q.OrderByDescending(w => w.CreatedAt).Take(500).ToListAsync(ct), actor, ct);
    }

    public async Task<WageAdvanceView> RequestWageAdvanceAsync(WageAdvanceRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct); var av = await AvailabilityForAsync(me, ct);
        if (!av.Eligible) throw DomainException.Invariant(av.Reason ?? "An early withdrawal is not possible now.");
        var reason = r.Reason?.Trim(); if (reason is { Length: > 200 }) throw DomainException.Validation("Please check the request.", new Dictionary<string, string> { ["reason"] = "Keep it under 200 characters." });
        if (r.Amount is null or <= 0) throw DomainException.Validation("Please check the request.", new Dictionary<string, string> { ["amount"] = "Enter the amount in rupiah." });
        if (r.Amount > av.Available) throw DomainException.Validation("Please check the request.", new Dictionary<string, string> { ["amount"] = FormattableString.Invariant($"You can take at most {av.Available:#,##0} now.") });
        var w = new WageAdvance { EmployeeId = me.Id, Year = av.Year, Month = av.Month, Amount = PayrollRules.Rupiah(r.Amount.Value), Reason = reason, CreatedAt = clock.UtcNow };
        db.WageAdvances.Add(w);
        audit.Record("hr.wage_advance.request", "wage_advance", w.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeNo"] = me.EmployeeNo });
        foreach (var uid in await notes.UsersWithPermissionAsync(Permissions.PayrollRun, ct)) if (uid != actor.UserId) notes.Notify(uid, "wage_advance.requested", $"{me.FullName} asked for an early wage withdrawal", null, "/hr/wage-advances");
        await db.SaveChangesAsync(ct);
        return (await WageViewsAsync([w], actor, ct))[0];
    }

    public async Task<WageAdvanceView> WageAdvanceActAsync(Guid id, string action, AdvanceDecisionRequest r, DateOnly? on, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var w = await db.WageAdvances.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Withdrawal");
        var e = await db.Employees.AsNoTracking().FirstAsync(x => x.Id == w.EmployeeId, ct); var own = e.UserId == actor.UserId;
        switch (action)
        {
            case "cancel":
                if (!own) throw DomainException.NotFound("Withdrawal");
                if (w.Status != WageAdvanceStatus.Pending) throw DomainException.Conflict("Only a waiting request can be cancelled.");
                w.Status = WageAdvanceStatus.Cancelled; break;
            case "decide":
                await payroll.GuardAsync(actor, ct);
                if (!(await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(Permissions.PayrollRun)) throw DomainException.Denied(Permissions.PayrollRun);
                if (own) throw DomainException.Invariant("You cannot decide your own request.");
                if (w.Status != WageAdvanceStatus.Pending) throw DomainException.Conflict("This request is already decided.");
                var note = r.Note?.Trim(); if (r.Approve != true && string.IsNullOrEmpty(note)) throw DomainException.Validation("Please give a reason.", new Dictionary<string, string> { ["note"] = "Tell the employee why." });
                w.Status = r.Approve == true ? WageAdvanceStatus.Approved : WageAdvanceStatus.Rejected; w.DecidedBy = actor.UserId; w.DecidedAt = clock.UtcNow; w.DecisionNote = note?[..Math.Min(note.Length, 300)];
                notes.Notify(e.UserId, "wage_advance.decided", r.Approve == true ? "Your early wage withdrawal was approved" : "Your early wage withdrawal was rejected", r.Approve == true ? "It will be transferred, and deducted from this month's pay." : note, "/hr/my-payslips");
                break;
            case "disburse":
                await payroll.GuardAsync(actor, ct);
                if (!(await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(Permissions.PayrollRun)) throw DomainException.Denied(Permissions.PayrollRun);
                if (own) throw DomainException.Invariant("You cannot pay out your own request.");
                if (w.Status != WageAdvanceStatus.Approved) throw DomainException.Conflict("Only an approved request can be marked as transferred.");
                w.Status = WageAdvanceStatus.Disbursed; w.DisbursedOn = on ?? Today; break;
            default: throw DomainException.Validation("Unknown action.", new Dictionary<string, string> { ["action"] = "Use decide, disburse or cancel." });
        }
        w.Version++;
        audit.Record($"hr.wage_advance.{action}", "wage_advance", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeNo"] = e.EmployeeNo, ["status"] = w.Status.ToString() });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { db.ResetTracking(); throw DomainException.Conflict("Someone else changed this request just now."); }
        return (await WageViewsAsync([w], actor, ct))[0];
    }
}
