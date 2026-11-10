using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record ClaimView(Guid Id, Guid EmployeeId, string EmployeeNo, string EmployeeName, string Category, DateOnly ExpenseDate, decimal Amount, string Description, bool HasReceipt, string Status,
    string? DecisionNote, DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt, bool CanDecide, bool CanCancel);
public sealed record SubmitClaimRequest(string? Category, DateOnly? ExpenseDate, decimal? Amount, string? Description, string? ReceiptName, string? ReceiptBase64);
public sealed record ClaimQuery(string? Scope, string? Status, int Page = 1, int PageSize = 20);

/// <summary>
/// ESS-004. Money an employee spent for work and asks back: submitted with an optional receipt (PDF/PNG/JPEG, typed by its bytes and virus-scanned like every upload),
/// decided by the manager or by someone who manages claims (never by the claimant), then paid through payroll: the next pay run of the employee's company adds each approved claim
/// as a non-taxable line, and locking that run marks the claim paid. Nothing is paid twice: a claim sits in at most one unlocked run, and a paid claim is never picked up again.
/// </summary>
public sealed class ClaimService(IAppDb db, IClock clock, AuthorizationService authz, AuditWriter audit, ManagerScope managers, NotificationService notes, IFileScanner scanner)
{
    public static readonly string[] Categories = ["transport", "travel", "meal", "medical", "training", "other"];
    public const int MaxAgeDays = 90; public const int MaxPending = 20; public const decimal MaxAmount = 100_000_000m;
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    private async Task<Employee> MeAsync(SessionPrincipal actor, CancellationToken ct)
        => await db.Employees.FirstOrDefaultAsync(e => e.UserId == actor.UserId && e.Status == EmployeeStatus.Active, ct) ?? throw DomainException.Invariant("Your account is not linked to an active employee record. Ask HR to link it.");

    private async Task<HashSet<Guid>> ManagedAsync(SessionPrincipal actor, IEnumerable<Guid> employeeIds, CancellationToken ct)
    {
        var acting = await managers.ActingIdsAsync(actor.UserId, ct); var ids = employeeIds.ToList(); if (acting.Count == 0 || ids.Count == 0) return [];
        return [.. await db.Employments.AsNoTracking().Where(m => m.EffectiveTo == null && m.ManagerId != null && acting.Contains(m.ManagerId.Value) && ids.Contains(m.EmployeeId)).Select(m => m.EmployeeId).ToListAsync(ct)];
    }

    private async Task<IReadOnlyList<ClaimView>> ViewsAsync(IReadOnlyList<ExpenseClaim> rows, SessionPrincipal actor, CancellationToken ct)
    {
        var ids = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var emps = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.EmployeeNo, e.FullName, e.UserId }).ToDictionaryAsync(e => e.Id, ct);
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct); var managed = await ManagedAsync(actor, ids, ct);
        return [.. rows.Select(c =>
        {
            var e = emps[c.EmployeeId]; var own = e.UserId == actor.UserId;
            var canDecide = c.Status == ClaimStatus.Pending && !own && (perms.Contains(Permissions.ClaimsManage) || perms.Contains(Permissions.ClaimsApprove) && managed.Contains(c.EmployeeId));
            return new ClaimView(c.Id, c.EmployeeId, e.EmployeeNo, e.FullName, c.Category, c.ExpenseDate, c.Amount, c.Description, c.ReceiptType != null, c.Status.ToString().ToLowerInvariant(), c.DecisionNote, c.CreatedAt, c.DecidedAt, canDecide, own && c.Status == ClaimStatus.Pending);
        })];
    }

    public async Task<PagedResult<ClaimView>> ListAsync(ClaimQuery q, SessionPrincipal actor, CancellationToken ct = default)
    {
        var scope = (q.Scope ?? "mine").ToLowerInvariant(); var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        // The receipt bytes are never loaded for a list.
        var query = db.ExpenseClaims.AsNoTracking().AsQueryable();
        switch (scope)
        {
            case "mine": { var me = await MeAsync(actor, ct); query = query.Where(c => c.EmployeeId == me.Id); break; }
            case "team":
            {
                if (!perms.Contains(Permissions.ClaimsApprove) && !perms.Contains(Permissions.ClaimsManage)) throw DomainException.Denied(Permissions.ClaimsApprove);
                var acting = await managers.ActingIdsAsync(actor.UserId, ct);
                var reports = db.Employments.Where(m => m.EffectiveTo == null && m.ManagerId != null && acting.Contains(m.ManagerId.Value)).Select(m => m.EmployeeId);
                query = query.Where(c => reports.Contains(c.EmployeeId)); break;
            }
            case "all": if (!perms.Contains(Permissions.ClaimsManage)) throw DomainException.Denied(Permissions.ClaimsManage); break;
            default: throw DomainException.Validation("Unknown scope.", new Dictionary<string, string> { ["scope"] = "Use mine, team or all." });
        }
        if (!string.IsNullOrWhiteSpace(q.Status) && q.Status != "all")
        {
            if (!Enum.TryParse<ClaimStatus>(q.Status, true, out var st)) throw DomainException.Validation("Unknown status.", new Dictionary<string, string> { ["status"] = "Use pending, approved, rejected, cancelled or paid." });
            query = query.Where(c => c.Status == st);
        }
        var page = Math.Max(1, q.Page); var size = Math.Clamp(q.PageSize, 1, 200); var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(c => c.Status == ClaimStatus.Pending).ThenByDescending(c => c.CreatedAt).Skip((page - 1) * size).Take(size)
            .Select(c => new ExpenseClaim { Id = c.Id, EmployeeId = c.EmployeeId, Category = c.Category, ExpenseDate = c.ExpenseDate, Amount = c.Amount, Description = c.Description, ReceiptType = c.ReceiptType, Status = c.Status, DecisionNote = c.DecisionNote, CreatedAt = c.CreatedAt, DecidedAt = c.DecidedAt }).ToListAsync(ct);
        return new PagedResult<ClaimView>(await ViewsAsync(rows, actor, ct), page, size, total);
    }

    public async Task<ClaimView> SubmitAsync(SubmitClaimRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var emp = await MeAsync(actor, ct); var errors = new Dictionary<string, string>();
        var category = (r.Category ?? "").Trim().ToLowerInvariant(); if (!Categories.Contains(category)) errors["category"] = "Choose: " + string.Join(", ", Categories);
        if (r.ExpenseDate is null) errors["expenseDate"] = "Choose the date of the expense."; else if (r.ExpenseDate > Today) errors["expenseDate"] = "The date is in the future."; else if (r.ExpenseDate < Today.AddDays(-MaxAgeDays)) errors["expenseDate"] = $"Claims are accepted up to {MaxAgeDays} days back.";
        if (r.Amount is null or <= 0 or > MaxAmount) errors["amount"] = "Enter the amount in rupiah.";
        var description = r.Description?.Trim(); if (string.IsNullOrEmpty(description) || description.Length > 300) errors["description"] = "Say what the expense was for (up to 300 characters).";
        byte[]? bytes = null; (string ContentType, string Extension)? kind = null;
        if (!string.IsNullOrWhiteSpace(r.ReceiptBase64))
        {
            if (r.ReceiptBase64.Length > DocumentService.MaxBytes * 4 / 3 + 16) errors["receipt"] = $"The receipt is larger than {DocumentService.MaxBytes / 1024 / 1024} MB.";
            else
            {
                try { bytes = Convert.FromBase64String(r.ReceiptBase64); } catch (FormatException) { errors["receipt"] = "The receipt could not be read."; }
                if (bytes is not null) { kind = DocumentService.Sniff(bytes); if (bytes.Length is 0 or > DocumentService.MaxBytes || kind is null) errors["receipt"] = "Only PDF, PNG and JPEG files up to 4 MB are accepted."; }
            }
        }
        if (errors.Count > 0) throw DomainException.Validation("Please check the claim.", errors);
        if (await db.ExpenseClaims.CountAsync(c => c.EmployeeId == emp.Id && c.Status == ClaimStatus.Pending, ct) >= MaxPending) throw DomainException.Invariant($"You already have {MaxPending} claims waiting for a decision.");
        if (bytes is not null && scanner.Enabled)
        {
            ScanResult scan;
            try { scan = await scanner.ScanAsync(bytes, ct); }
            catch (Exception scanError) when (scanError is not OperationCanceledException || !ct.IsCancellationRequested) { throw DomainException.Invariant("The virus scanner cannot be reached right now, so the claim was not stored. Try again in a moment."); }
            if (scan.Verdict == ScanVerdict.Infected) throw DomainException.Validation("Please check the claim.", new Dictionary<string, string> { ["receipt"] = "The virus scanner rejected this file." });
        }
        var claim = new ExpenseClaim { EmployeeId = emp.Id, Category = category, ExpenseDate = r.ExpenseDate!.Value, Amount = PayrollRules.Rupiah(r.Amount!.Value), Description = description!, Receipt = bytes, ReceiptType = kind?.ContentType,
            ReceiptName = bytes is null ? null : $"receipt{kind!.Value.Extension}", CreatedAt = clock.UtcNow };
        db.ExpenseClaims.Add(claim);
        audit.Record("hr.claim.submit", "expense_claim", claim.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeNo"] = emp.EmployeeNo, ["category"] = category, ["receipt"] = (bytes is not null).ToString() });
        foreach (var uid in await notes.ApproverUsersAsync(emp.Id, ct)) notes.Notify(uid, "claim.requested", $"{emp.FullName} submitted an expense claim", category, "/hr/approvals");
        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([claim], actor, ct))[0];
    }

    public async Task<ClaimView> DecideAsync(Guid id, bool approve, string? note, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var claim = await db.ExpenseClaims.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound("Claim");
        var emp = await db.Employees.AsNoTracking().FirstAsync(e => e.Id == claim.EmployeeId, ct); var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        if (emp.UserId == actor.UserId) throw DomainException.Invariant("You cannot decide your own claim.");
        if (!perms.Contains(Permissions.ClaimsManage) && !(perms.Contains(Permissions.ClaimsApprove) && (await ManagedAsync(actor, [emp.Id], ct)).Contains(emp.Id))) throw DomainException.DeniedBecause("Only the employee's manager or someone who manages claims can decide this claim.");
        var reason = note?.Trim(); if (reason is { Length: > 300 }) throw DomainException.Validation("Note is too long.", new Dictionary<string, string> { ["note"] = "Keep it under 300 characters." });
        if (!approve && string.IsNullOrEmpty(reason)) throw DomainException.Validation("Please give a reason.", new Dictionary<string, string> { ["note"] = "Tell the employee why." });
        if (claim.Status != ClaimStatus.Pending) throw DomainException.Conflict($"This claim is already {claim.Status.ToString().ToLowerInvariant()}.");
        claim.Status = approve ? ClaimStatus.Approved : ClaimStatus.Rejected; claim.DecidedBy = actor.UserId; claim.DecidedAt = clock.UtcNow; claim.DecisionNote = reason; claim.Version++;
        audit.Record(approve ? "hr.claim.approve" : "hr.claim.reject", "expense_claim", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeNo"] = emp.EmployeeNo, ["category"] = claim.Category });
        notes.Notify(emp.UserId, "claim.decided", approve ? "Your expense claim was approved" : "Your expense claim was rejected", approve ? "It will be paid with your next payslip." : reason, "/hr/my-claims");
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { db.ResetTracking(); throw DomainException.Conflict("This claim was decided by someone else just now."); }
        return (await ViewsAsync([claim], actor, ct))[0];
    }

    public async Task<ClaimView> CancelAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct);
        var claim = await db.ExpenseClaims.FirstOrDefaultAsync(c => c.Id == id && c.EmployeeId == me.Id, ct) ?? throw DomainException.NotFound("Claim");
        if (claim.Status != ClaimStatus.Pending) throw DomainException.Conflict($"This claim is already {claim.Status.ToString().ToLowerInvariant()}.");
        claim.Status = ClaimStatus.Cancelled; claim.Version++;
        audit.Record("hr.claim.cancel", "expense_claim", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([claim], actor, ct))[0];
    }

    /// <summary>The receipt, for the claimant, the people who may decide the claim, and whoever manages claims. Opening someone else's receipt is audited.</summary>
    public async Task<DocumentFile> ReceiptAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var claim = await db.ExpenseClaims.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound("Claim");
        var emp = await db.Employees.AsNoTracking().FirstAsync(e => e.Id == claim.EmployeeId, ct); var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        var own = emp.UserId == actor.UserId;
        if (!own && !perms.Contains(Permissions.ClaimsManage) && !(perms.Contains(Permissions.ClaimsApprove) && (await ManagedAsync(actor, [emp.Id], ct)).Contains(emp.Id))) throw DomainException.NotFound("Claim");
        if (claim.Receipt is null || claim.ReceiptType is null) throw DomainException.NotFound("Receipt");
        if (!own) { audit.Record("hr.claim.receipt_viewed", "expense_claim", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeNo"] = emp.EmployeeNo }); await db.SaveChangesAsync(ct); }
        return new DocumentFile(claim.ReceiptName ?? "receipt", claim.ReceiptType, claim.Receipt);
    }
}
