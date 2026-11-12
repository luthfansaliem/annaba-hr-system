using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record SalaryGradeView(Guid Id, string Code, string Name, decimal MinPay, decimal MidPay, decimal MaxPay, bool Archived, int Employees);
public sealed record SalaryGradeRequest(string? Code, string? Name, decimal? MinPay, decimal? MidPay, decimal? MaxPay, bool? Archived);
public sealed record SalaryReviewSummary(Guid Id, string Name, DateOnly EffectiveFrom, string Status, string? Note, int Employees, DateTimeOffset CreatedAt, DateTimeOffset? SubmittedAt, DateTimeOffset? ApprovedAt, DateTimeOffset? AppliedAt,
    bool CanEdit, bool CanSubmit, bool CanApprove, bool CanApply, int Version);
public sealed record SalaryReviewLine(Guid Id, Guid EmployeeId, string EmployeeNo, string EmployeeName, string? Department, decimal CurrentBasic, decimal CurrentAllowance, decimal ProposedBasic, decimal ProposedAllowance,
    decimal IncreasePct, string? Grade, string? GradePosition, string? Note, string? ApplyError);
public sealed record SalaryReviewDetail(SalaryReviewSummary Review, IReadOnlyList<SalaryReviewLine> Lines, decimal CurrentTotal, decimal ProposedTotal, decimal IncreasePct);
public sealed record CreateSalaryReviewRequest(string? Name, DateOnly? EffectiveFrom, string? Note);
public sealed record AddReviewEmployeesRequest(IReadOnlyList<Guid>? EmployeeIds, Guid? OrgUnitId, bool? AllEmployees, decimal? IncreasePct);
public sealed record SetReviewLineRequest(decimal? ProposedBasic, decimal? ProposedAllowance, decimal? IncreasePct, Guid? GradeId, string? Note);

/// <summary>
/// Pay bands and salary review rounds (Talenta: salary structure / grade and compensation management). A review is prepared (Draft), handed in (Submitted), approved by someone
/// other than its preparer, then applied: each line becomes a new compensation period on the review's date through the same path as a manual change, so every rule
/// (no change inside a locked month, encryption, audit) still holds. Amounts are encrypted and every screen needs a recent password check, like all payroll data.
/// </summary>
public sealed class CompensationPlanningService(IAppDb db, IClock clock, PayrollService payroll, AuthorizationService authz, AuditWriter audit)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    // ============================================================ grades
    public async Task<IReadOnlyList<SalaryGradeView>> GradesAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var counts = await db.Compensations.AsNoTracking().Where(c => c.EffectiveTo == null && c.GradeId != null).GroupBy(c => c.GradeId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return [.. (await db.SalaryGrades.AsNoTracking().OrderBy(g => g.Archived).ThenBy(g => g.MinPay).ThenBy(g => g.Code).ToListAsync(ct)).Select(g => new SalaryGradeView(g.Id, g.Code, g.Name, g.MinPay, g.MidPay, g.MaxPay, g.Archived, counts.GetValueOrDefault(g.Id)))];
    }

    public async Task<SalaryGradeView> SaveGradeAsync(Guid? id, SalaryGradeRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var g = id is { } gid ? await db.SalaryGrades.FirstOrDefaultAsync(x => x.Id == gid, ct) ?? throw DomainException.NotFound("Grade") : new SalaryGrade { CreatedAt = clock.UtcNow };
        var errors = new Dictionary<string, string>();
        var code = (r.Code ?? g.Code).Trim().ToUpperInvariant(); if (code.Length is < 1 or > 20) errors["code"] = "Enter a short code (up to 20 characters), e.g. G5.";
        var name = (r.Name ?? g.Name).Trim(); if (name.Length is < 1 or > 100) errors["name"] = "Enter a name (up to 100 characters).";
        var min = r.MinPay ?? g.MinPay; var max = r.MaxPay ?? g.MaxPay; var mid = r.MidPay ?? (id is null ? PayrollRules.Rupiah((min + max) / 2) : g.MidPay);
        if (min < 0 || max <= 0 || max > 10_000_000_000m) errors["maxPay"] = "Enter the range in rupiah.";
        else if (!(min <= mid && mid <= max)) errors["midPay"] = "The midpoint must lie between the minimum and the maximum.";
        if (errors.Count == 0 && await db.SalaryGrades.AnyAsync(x => x.Code == code && x.Id != g.Id, ct)) errors["code"] = "Another grade already uses this code.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the grade.", errors);
        g.Code = code; g.Name = name; g.MinPay = PayrollRules.Rupiah(min); g.MidPay = PayrollRules.Rupiah(mid); g.MaxPay = PayrollRules.Rupiah(max); if (r.Archived is { } a) g.Archived = a;
        if (id is null) db.SalaryGrades.Add(g);
        audit.Record(id is null ? "hr.payroll.grade_create" : "hr.payroll.grade_update", "salary_grade", g.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["code"] = code });
        await db.SaveChangesAsync(ct);
        return (await GradesAsync(actor, ct)).First(x => x.Id == g.Id);
    }

    /// <summary>Where a basic salary sits in its band: below, low (under the midpoint), high (above it) or above the maximum.</summary>
    public static string Position(SalaryGrade g, decimal basic) => basic < g.MinPay ? "below" : basic > g.MaxPay ? "above" : basic < g.MidPay ? "low" : "high";

    // ============================================================ reviews
    private SalaryReviewSummary Summary(SalaryReview r, int count, SessionPrincipal actor, IReadOnlySet<string> perms) => new(r.Id, r.Name, r.EffectiveFrom, r.Status.ToString().ToLowerInvariant(), r.Note, count, r.CreatedAt, r.SubmittedAt, r.ApprovedAt, r.AppliedAt,
        CanEdit: perms.Contains(Permissions.PayrollConfigure) && r.Status == SalaryReviewStatus.Draft,
        CanSubmit: perms.Contains(Permissions.PayrollConfigure) && r.Status == SalaryReviewStatus.Draft && count > 0,
        CanApprove: perms.Contains(Permissions.PayrollApprove) && r.Status == SalaryReviewStatus.Submitted && r.CreatedBy != actor.UserId && r.SubmittedBy != actor.UserId,
        CanApply: perms.Contains(Permissions.PayrollConfigure) && r.Status == SalaryReviewStatus.Approved, r.Version);

    public async Task<IReadOnlyList<SalaryReviewSummary>> ReviewsAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        var counts = await db.SalaryReviewItems.AsNoTracking().GroupBy(i => i.ReviewId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return [.. (await db.SalaryReviews.AsNoTracking().OrderByDescending(r => r.CreatedAt).ToListAsync(ct)).Select(r => Summary(r, counts.GetValueOrDefault(r.Id), actor, perms))];
    }

    public async Task<SalaryReviewSummary> CreateReviewAsync(CreateSalaryReviewRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var errors = new Dictionary<string, string>();
        var name = r.Name?.Trim(); if (string.IsNullOrEmpty(name) || name.Length > 120) errors["name"] = "Name the round (up to 120 characters), e.g. Annual review 2027.";
        if (r.EffectiveFrom is null) errors["effectiveFrom"] = "Choose the date the new salaries start."; else if (r.EffectiveFrom > Today.AddYears(1)) errors["effectiveFrom"] = "That date is too far ahead.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the review.", errors);
        var rv = new SalaryReview { Name = name!, EffectiveFrom = r.EffectiveFrom!.Value, Note = r.Note?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 300)] : null, CreatedBy = actor.UserId, CreatedAt = clock.UtcNow };
        db.SalaryReviews.Add(rv);
        audit.Record("hr.payroll.review_create", "salary_review", rv.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = rv.Name, ["effectiveFrom"] = rv.EffectiveFrom.ToString("yyyy-MM-dd") });
        await db.SaveChangesAsync(ct);
        return Summary(rv, 0, actor, await authz.GetEffectivePermissionsAsync(actor.UserId, ct));
    }

    private static (decimal Basic, decimal Allowance) Pair(string plain) { var p = plain.Split('|'); return (PayrollService.Dec(p[0]), PayrollService.Dec(p[1])); }
    private static string Pair(decimal basic, decimal allowance) => FormattableString.Invariant($"{basic}|{allowance}");

    public async Task<SalaryReviewDetail> ReviewAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var rv = await db.SalaryReviews.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Salary review");
        var items = await db.SalaryReviewItems.AsNoTracking().Where(i => i.ReviewId == id).ToListAsync(ct); var ids = items.Select(i => i.EmployeeId).ToList();
        var emps = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.EmployeeNo, e.FullName, Unit = e.Employments.Where(m => m.EffectiveTo == null).Select(m => m.OrgUnitId).FirstOrDefault() }).ToDictionaryAsync(e => e.Id, ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var grades = await db.SalaryGrades.AsNoTracking().ToDictionaryAsync(g => g.Id, ct);
        var lines = items.Select(i =>
        {
            var (cb, ca) = Pair(payroll.Reveal("review", i.Id, "current", i.CurrentEnc)); var (pb, pa) = Pair(payroll.Reveal("review", i.Id, "proposed", i.ProposedEnc)); var e = emps[i.EmployeeId];
            var g = i.GradeId is { } gid ? grades.GetValueOrDefault(gid) : null;
            return new SalaryReviewLine(i.Id, i.EmployeeId, e.EmployeeNo, e.FullName, e.Unit is { } u ? units.GetValueOrDefault(u) : null, cb, ca, pb, pa, cb + ca == 0 ? 0 : Math.Round((pb + pa - cb - ca) / (cb + ca) * 100, 2),
                g?.Code, g is null ? null : Position(g, pb), i.Note, i.ApplyError);
        }).OrderBy(l => l.EmployeeName).ToList();
        audit.Record("hr.payroll.review_viewed", "salary_review", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email); await db.SaveChangesAsync(ct);
        decimal cur = lines.Sum(l => l.CurrentBasic + l.CurrentAllowance), prop = lines.Sum(l => l.ProposedBasic + l.ProposedAllowance);
        return new SalaryReviewDetail(Summary(rv, lines.Count, actor, await authz.GetEffectivePermissionsAsync(actor.UserId, ct)), lines, cur, prop, cur == 0 ? 0 : Math.Round((prop - cur) / cur * 100, 2));
    }

    private async Task<SalaryReview> DraftAsync(Guid id, CancellationToken ct)
    {
        var rv = await db.SalaryReviews.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Salary review");
        if (rv.Status != SalaryReviewStatus.Draft) throw DomainException.Invariant("Only a draft review can be changed.");
        return rv;
    }

    /// <summary>Adds people to a draft review with their current pay and a proposal of current + <paramref name="r"/>.IncreasePct (0 when not given). People already in the review are skipped.</summary>
    public async Task<int> AddEmployeesAsync(Guid id, AddReviewEmployeesRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var rv = await DraftAsync(id, ct);
        if (r.IncreasePct is < -50 or > 200) throw DomainException.Validation("Please check the increase.", new Dictionary<string, string> { ["increasePct"] = "Enter a percentage between -50 and 200." });
        var q = db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active);
        if (r.AllEmployees == true) { }
        else if (r.OrgUnitId is { } unit) q = q.Where(e => e.Employments.Any(m => m.EffectiveTo == null && m.OrgUnitId == unit));
        else if (r.EmployeeIds is { Count: > 0 } list) q = q.Where(e => list.Contains(e.Id));
        else throw DomainException.Validation("Choose who to add.", new Dictionary<string, string> { ["employeeIds"] = "Pick people, a department or everyone." });
        var empIds = await q.Select(e => e.Id).ToListAsync(ct);
        var have = (await db.SalaryReviewItems.AsNoTracking().Where(i => i.ReviewId == id).Select(i => i.EmployeeId).ToListAsync(ct)).ToHashSet();
        var comps = await db.Compensations.AsNoTracking().Where(c => c.EffectiveTo == null && empIds.Contains(c.EmployeeId)).ToListAsync(ct);
        var added = 0; var factor = 1 + (r.IncreasePct ?? 0) / 100m;
        foreach (var c in comps.Where(c => !have.Contains(c.EmployeeId)))
        {
            var (basic, allowance) = payroll.RevealPay(c);
            var item = new SalaryReviewItem { ReviewId = id, EmployeeId = c.EmployeeId, GradeId = c.GradeId };
            item.CurrentEnc = payroll.Seal("review", item.Id, "current", Pair(basic, allowance));
            item.ProposedEnc = payroll.Seal("review", item.Id, "proposed", Pair(PayrollRules.Rupiah(basic * factor), allowance));
            db.SalaryReviewItems.Add(item); added++;
        }
        rv.Version++;
        audit.Record("hr.payroll.review_add", "salary_review", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["added"] = added.ToString(), ["withoutSalary"] = (empIds.Count - comps.Count).ToString() });
        await db.SaveChangesAsync(ct);
        return added;
    }

    public async Task<SalaryReviewLine> SetLineAsync(Guid id, Guid lineId, SetReviewLineRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var rv = await DraftAsync(id, ct);
        var item = await db.SalaryReviewItems.FirstOrDefaultAsync(i => i.Id == lineId && i.ReviewId == id, ct) ?? throw DomainException.NotFound("Line");
        var (cb, ca) = Pair(payroll.Reveal("review", item.Id, "current", item.CurrentEnc)); var (pb, pa) = Pair(payroll.Reveal("review", item.Id, "proposed", item.ProposedEnc));
        var errors = new Dictionary<string, string>();
        if (r.IncreasePct is { } pct) { if (pct is < -50 or > 200) errors["increasePct"] = "Enter a percentage between -50 and 200."; else pb = PayrollRules.Rupiah(cb * (1 + pct / 100m)); }
        if (r.ProposedBasic is { } b) { if (b is <= 0 or > 10_000_000_000m) errors["proposedBasic"] = "Enter the new basic salary in rupiah."; else pb = PayrollRules.Rupiah(b); }
        if (r.ProposedAllowance is { } a) { if (a is < 0 or > 10_000_000_000m) errors["proposedAllowance"] = "Enter 0 or more."; else pa = PayrollRules.Rupiah(a); }
        if (r.GradeId is { } gid && !await db.SalaryGrades.AnyAsync(g => g.Id == gid && !g.Archived, ct)) errors["gradeId"] = "Choose an active grade.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the line.", errors);
        item.ProposedEnc = payroll.Seal("review", item.Id, "proposed", Pair(pb, pa)); if (r.GradeId is not null) item.GradeId = r.GradeId; if (r.Note is not null) item.Note = r.Note.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 200)] : null;
        rv.Version++;
        audit.Record("hr.payroll.review_line", "salary_review", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeId"] = item.EmployeeId.ToString() }, [new AuditDiff("proposal", "[protected]", "[changed]")]);
        await db.SaveChangesAsync(ct);
        return (await ReviewAsync(id, actor, client, ct)).Lines.First(l => l.Id == lineId);
    }

    public async Task RemoveLineAsync(Guid id, Guid lineId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var rv = await DraftAsync(id, ct);
        var item = await db.SalaryReviewItems.FirstOrDefaultAsync(i => i.Id == lineId && i.ReviewId == id, ct) ?? throw DomainException.NotFound("Line");
        db.SalaryReviewItems.Remove(item); rv.Version++;
        audit.Record("hr.payroll.review_remove", "salary_review", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeId"] = item.EmployeeId.ToString() });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Submit (preparer), approve (someone else holding payroll.approve), send back with a reason, cancel, or apply.</summary>
    public async Task<SalaryReviewSummary> ActAsync(Guid id, string action, string? note, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var rv = await db.SalaryReviews.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Salary review");
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct); var count = await db.SalaryReviewItems.CountAsync(i => i.ReviewId == id, ct);
        switch (action)
        {
            case "submit":
                if (!perms.Contains(Permissions.PayrollConfigure)) throw DomainException.Denied(Permissions.PayrollConfigure);
                if (rv.Status != SalaryReviewStatus.Draft) throw DomainException.Conflict("Only a draft can be submitted.");
                if (count == 0) throw DomainException.Invariant("Add at least one employee first.");
                rv.Status = SalaryReviewStatus.Submitted; rv.SubmittedBy = actor.UserId; rv.SubmittedAt = clock.UtcNow; break;
            case "approve":
                if (!perms.Contains(Permissions.PayrollApprove)) throw DomainException.Denied(Permissions.PayrollApprove);
                if (rv.Status != SalaryReviewStatus.Submitted) throw DomainException.Conflict("Only a submitted review can be approved.");
                if (rv.CreatedBy == actor.UserId || rv.SubmittedBy == actor.UserId) throw DomainException.Invariant("The person who prepared a salary review cannot approve it.");
                rv.Status = SalaryReviewStatus.Approved; rv.ApprovedBy = actor.UserId; rv.ApprovedAt = clock.UtcNow; break;
            case "return":
                if (!perms.Contains(Permissions.PayrollApprove)) throw DomainException.Denied(Permissions.PayrollApprove);
                if (rv.Status != SalaryReviewStatus.Submitted) throw DomainException.Conflict("Only a submitted review can be sent back.");
                if (string.IsNullOrWhiteSpace(note)) throw DomainException.Validation("Please give a reason.", new Dictionary<string, string> { ["note"] = "Tell the preparer what to change." });
                rv.Status = SalaryReviewStatus.Draft; rv.Note = note.Trim()[..Math.Min(note.Trim().Length, 300)]; rv.SubmittedAt = null; rv.SubmittedBy = null; break;
            case "cancel":
                if (!perms.Contains(Permissions.PayrollConfigure)) throw DomainException.Denied(Permissions.PayrollConfigure);
                if (rv.Status is SalaryReviewStatus.Applied or SalaryReviewStatus.Cancelled) throw DomainException.Conflict("This review is already closed.");
                rv.Status = SalaryReviewStatus.Cancelled; break;
            case "apply":
                if (!perms.Contains(Permissions.PayrollConfigure)) throw DomainException.Denied(Permissions.PayrollConfigure);
                if (rv.Status != SalaryReviewStatus.Approved) throw DomainException.Conflict("Only an approved review can be applied.");
                await ApplyAsync(rv, actor, client, ct); break;
            default: throw DomainException.Validation("Unknown action.", new Dictionary<string, string> { ["action"] = "Use submit, approve, return, cancel or apply." });
        }
        rv.Version++;
        audit.Record($"hr.payroll.review_{action}", "salary_review", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = rv.Name, ["employees"] = count.ToString() });
        await db.SaveChangesAsync(ct);
        return Summary(rv, count, actor, perms);
    }

    /// <summary>Each line becomes a compensation period through the normal path; one person's failure (a locked month in the way, someone who left) is recorded on that line and does not stop the rest.</summary>
    private async Task ApplyAsync(SalaryReview rv, SessionPrincipal actor, ClientInfo client, CancellationToken ct)
    {
        var items = await db.SalaryReviewItems.Where(i => i.ReviewId == rv.Id).ToListAsync(ct); var failed = 0;
        foreach (var item in items)
        {
            var cur = await db.Compensations.AsNoTracking().FirstOrDefaultAsync(c => c.EmployeeId == item.EmployeeId && c.EffectiveTo == null, ct);
            var (pb, pa) = Pair(payroll.Reveal("review", item.Id, "proposed", item.ProposedEnc));
            try
            {
                if (cur is null) throw DomainException.Invariant("This employee has no salary record any more.");
                // validation happens before anything is written, so a refused line leaves nothing behind in the unit of work
                await payroll.SetCompensationAsync(item.EmployeeId, new SetCompensationRequest(rv.EffectiveFrom, pb, pa, cur.PtkpStatus, cur.HasNpwp, cur.KesEnrolled, cur.TkEnrolled, cur.JkkRate, null, null, null, null, null,
                    "Salary review: " + rv.Name, GradeId: item.GradeId), actor, client, ct);
                item.ApplyError = null;
            }
            catch (DomainException ex)
            {
                failed++;
                var m = ex.Message + (ex.Fields is { Count: > 0 } e ? " " + string.Join(" ", e.Values) : "");
                item.ApplyError = m[..Math.Min(m.Length, 300)];
            }
        }
        rv.Status = SalaryReviewStatus.Applied; rv.AppliedAt = clock.UtcNow;
        if (failed > 0) rv.Note = $"{failed} of {items.Count} could not be applied; see the lines.";
    }
}
