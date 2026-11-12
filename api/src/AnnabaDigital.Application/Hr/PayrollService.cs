using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

/// <summary>
/// PAY-001…011. Rules enforced here: every payroll operation needs a recent password check (payroll permissions are critical); the person who calculated a run cannot approve it
/// (maker–checker); a run moves Draft → Calculated → Approved → Locked → Paid and a locked run is immutable (service + database trigger); a month cannot be calculated while an earlier
/// month of the same year is unlocked (year-to-date tax depends on it); salaries are encrypted at rest; money is decimal and every run keeps the rules version it used.
/// </summary>
public sealed class PayrollService(IAppDb db, IClock clock, ISecretProtector protector, AuthorizationService authz, SettingsProvider settings, AuditWriter audit, ShiftService shifts, WebhookService webhooks, PayrollRulesService ruleSets, LegalEntityService entities, NotificationService notes, IEmailQueue email, IAppLinks links)
{
    private static readonly PayrollRules Rules = PayrollRules.Default;
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    internal async Task GuardAsync(SessionPrincipal actor, CancellationToken ct)
        => Guards.RequireRecentReauthForCritical(actor, (await settings.GetAsync(ct)) with { RequireReauthForCritical = true }, clock);

    // ============================================================ encryption helpers
    internal string Seal(string scope, Guid rowId, string field, string plain)
    {
        var s = protector.Protect(Encoding.UTF8.GetBytes(plain), Encoding.UTF8.GetBytes($"{scope}:{rowId}:{field}"));
        return $"{s.KeyVersion}.{Convert.ToBase64String(s.Nonce)}.{Convert.ToBase64String(s.Ciphertext)}";
    }

    internal string Reveal(string scope, Guid rowId, string field, string stored)
    {
        var p = stored.Split('.');
        return Encoding.UTF8.GetString(protector.Unprotect(new ProtectedSecret(Convert.FromBase64String(p[2]), Convert.FromBase64String(p[1]), int.Parse(p[0])), Encoding.UTF8.GetBytes($"{scope}:{rowId}:{field}")));
    }

    internal static decimal Dec(string s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
    /// <summary>The decrypted basic (monthly salary or daily rate) and allowance of a compensation record.</summary>
    internal (decimal Basic, decimal Allowance) RevealPay(EmployeeCompensation c) => (Dec(Reveal("comp", c.Id, "basic", c.BasicEnc)), Dec(Reveal("comp", c.Id, "allowance", c.AllowanceEnc)));
    /// <summary>Monthly wage (basic + fixed allowance) of a monthly-paid employee on a date; null for daily-wage workers and people without a salary record.</summary>
    internal async Task<decimal?> MonthlyWageAsync(Guid employeeId, DateOnly asOf, CancellationToken ct)
    {
        var c = await db.Compensations.AsNoTracking().Where(x => x.EmployeeId == employeeId && x.EffectiveFrom <= asOf && (x.EffectiveTo == null || x.EffectiveTo >= asOf)).OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
        if (c is null || c.PayBasis == PayBasis.Daily) return null;
        var (basic, allowance) = RevealPay(c); return basic + allowance;
    }
    private static string Str(decimal d) => d.ToString(System.Globalization.CultureInfo.InvariantCulture);
    internal static string TreatmentName(TaxTreatment t) => t switch { TaxTreatment.GrossUp => "gross-up", TaxTreatment.Nett => "nett", _ => "gross" };
    private static TaxTreatment? ParseTreatment(string? s) => s?.Trim().ToLowerInvariant() switch { "gross" => TaxTreatment.Gross, "gross-up" or "grossup" => TaxTreatment.GrossUp, "nett" or "net" => TaxTreatment.Nett, _ => null };

    // ============================================================ compensation
    /// <summary>Back pay waiting for the next pay run.</summary>
    public async Task<IReadOnlyList<RetroRow>> PendingRetroAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var rows = await db.RetroAdjustments.AsNoTracking().Where(x => x.AppliedRunId == null).OrderBy(x => x.CreatedAt).ToListAsync(ct); var ids = rows.Select(x => x.EmployeeId).Distinct().ToList();
        var emps = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        return [.. rows.Select(x => new RetroRow(x.Id, x.EmployeeId, emps[x.EmployeeId].EmployeeNo, emps[x.EmployeeId].FullName, x.Note, x.Amount, x.Note, x.CreatedAt))];
    }

    public async Task<IReadOnlyList<CompensationRow>> CompensationListAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var rows = await db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active).OrderBy(e => e.FullName)
            .Select(e => new { e.Id, e.EmployeeNo, e.FullName, Job = e.Employments.Where(m => m.EffectiveTo == null).Select(m => m.JobTitle).FirstOrDefault() }).ToListAsync(ct);
        var comps = await db.Compensations.AsNoTracking().Where(c => c.EffectiveTo == null).ToDictionaryAsync(c => c.EmployeeId, ct);
        var grades = await db.SalaryGrades.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Code, ct);
        return [.. rows.Select(r => comps.TryGetValue(r.Id, out var c) ? new CompensationRow(r.Id, r.EmployeeNo, r.FullName, r.Job, true, c.EffectiveFrom, c.PtkpStatus, TreatmentName(c.TaxTreatment), c.NonResident, c.GradeId is { } g ? grades.GetValueOrDefault(g) : null) : new CompensationRow(r.Id, r.EmployeeNo, r.FullName, r.Job, false, null, null))];
    }

    public async Task<CompensationView> CompensationAsync(Guid employeeId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var view = await BuildCompensationAsync(employeeId, ct);
        if (view.HasCompensation) { audit.Record("hr.payroll.compensation_viewed", "employee", employeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email); await db.SaveChangesAsync(ct); }
        return view;
    }

    private async Task<CompensationView> BuildCompensationAsync(Guid employeeId, CancellationToken ct)
    {
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employeeId, ct) ?? throw DomainException.NotFound("Employee");
        var c = await db.Compensations.AsNoTracking().FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.EffectiveTo == null, ct);
        if (c is null) return new CompensationView(e.Id, e.EmployeeNo, e.FullName, false, null, null, null, null, true, true, true, Rules.DefaultJkkRate, null, 0, 0, 0, 0);
        var (og, op, ot) = Opening(c);
        var grade = c.GradeId is { } gid ? await db.SalaryGrades.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gid, ct) : null;
        return new CompensationView(e.Id, e.EmployeeNo, e.FullName, true, c.EffectiveFrom, Dec(Reveal("comp", c.Id, "basic", c.BasicEnc)), Dec(Reveal("comp", c.Id, "allowance", c.AllowanceEnc)), c.PtkpStatus, c.HasNpwp,
            c.KesEnrolled, c.TkEnrolled, c.JkkRate, c.OpeningYear, c.OpeningMonths, og, op, ot, await db.RetroAdjustments.AsNoTracking().Where(x => x.EmployeeId == employeeId && x.AppliedRunId == null).SumAsync(x => (decimal?)x.Amount, ct) ?? 0,
            c.PayBasis.ToString().ToLowerInvariant(), c.TaxScheme == TaxScheme.DailyTer ? "daily" : "monthly", TreatmentName(c.TaxTreatment), c.NonResident, c.TreatyRate, c.GradeId, grade?.Code,
            grade is null ? null : CompensationPlanningService.Position(grade, Dec(Reveal("comp", c.Id, "basic", c.BasicEnc))));
    }

    private (decimal Gross, decimal Pension, decimal Tax) Opening(EmployeeCompensation c)
    {
        if (c.OpeningEnc is null) return (0, 0, 0);
        var p = Reveal("comp", c.Id, "opening", c.OpeningEnc).Split('|');
        return (Dec(p[0]), Dec(p[1]), Dec(p[2]));
    }

    public async Task<CompensationView> SetCompensationAsync(Guid employeeId, SetCompensationRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var e = await db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, ct) ?? throw DomainException.NotFound("Employee");
        if (e.Status != EmployeeStatus.Active) throw DomainException.Invariant("This employee's employment has ended.");
        var errors = new Dictionary<string, string>();
        var basisIn = r.PayBasis?.Trim(); PayBasis basis;
        if (string.IsNullOrEmpty(basisIn)) basis = await db.Compensations.AsNoTracking().Where(x => x.EmployeeId == employeeId && x.EffectiveTo == null).Select(x => (PayBasis?)x.PayBasis).FirstOrDefaultAsync(ct) ?? PayBasis.Monthly;
        else if (!Enum.TryParse(basisIn, true, out basis) || !Enum.IsDefined(basis)) { errors["payBasis"] = "Choose monthly or daily."; basis = PayBasis.Monthly; }
        var scheme = TaxScheme.MonthlyTer; var schemeIn = r.TaxScheme?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(schemeIn)) { if (basis == PayBasis.Daily) scheme = await db.Compensations.AsNoTracking().Where(x => x.EmployeeId == employeeId && x.EffectiveTo == null).Select(x => (TaxScheme?)x.TaxScheme).FirstOrDefaultAsync(ct) ?? TaxScheme.MonthlyTer; }
        else if (schemeIn is "daily") { if (basis != PayBasis.Daily) errors["taxScheme"] = "Daily withholding is only for daily-wage workers."; scheme = TaxScheme.DailyTer; }
        else if (schemeIn is not "monthly") errors["taxScheme"] = "Choose monthly or daily.";
        if (r.Basic is null or <= 0 or > 10_000_000_000m) errors["basic"] = basis == PayBasis.Daily ? "Enter the daily rate in rupiah." : "Enter the monthly basic salary in rupiah.";
        if (r.Allowance is < 0 or > 10_000_000_000m) errors["allowance"] = "Enter 0 or more.";
        var ptkp = (r.PtkpStatus ?? "").Trim().ToUpperInvariant(); if (!PayrollRules.DefaultPtkp.ContainsKey(ptkp)) errors["ptkpStatus"] = "Choose TK0–TK3 or K0–K3.";
        var jkk = r.JkkRate ?? Rules.DefaultJkkRate; if (jkk is < 0 or > 0.05m) errors["jkkRate"] = "Enter a rate between 0 and 0.05 (5%).";
        var from = r.EffectiveFrom ?? Today;
        var cur = await db.Compensations.FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.EffectiveTo == null, ct);
        // Tax treatment and residency carry over from the current period unless given.
        var treatment = cur?.TaxTreatment ?? TaxTreatment.Gross;
        if (!string.IsNullOrWhiteSpace(r.TaxTreatment)) { if (ParseTreatment(r.TaxTreatment) is { } tt) treatment = tt; else errors["taxTreatment"] = "Choose gross, gross-up or nett."; }
        var nonResident = r.NonResident ?? cur?.NonResident ?? false;
        decimal? treaty = nonResident ? (r.NonResident is null && r.TreatyRate is null ? cur?.TreatyRate : r.TreatyRate) : null;
        if (r.TreatyRate is not null && !nonResident) errors["treatyRate"] = "A treaty rate only applies to a non-resident (PPh 26).";
        else if (treaty is < 0 or > 0.20m) errors["treatyRate"] = "Enter a rate between 0 and 0.20 (20%).";
        var gradeId = r.ClearGrade == true ? null : r.GradeId ?? cur?.GradeId;
        if (r.GradeId is { } wantGrade && !await db.SalaryGrades.AnyAsync(g => g.Id == wantGrade && !g.Archived, ct)) errors["gradeId"] = "Choose an active grade.";
        if (cur is not null && from <= cur.EffectiveFrom) errors["effectiveFrom"] = $"Must be after the current period started ({cur.EffectiveFrom:yyyy-MM-dd}).";
        if (cur is null && from < e.HireDate) errors["effectiveFrom"] = "Cannot start before the hire date.";
        if (from > Today.AddYears(1)) errors["effectiveFrom"] = "That date is too far ahead.";
        if (r.OpeningYear is { } oy && (oy < 2000 || oy > Today.Year)) errors["openingYear"] = "Check the year.";
        if (r.OpeningMonths is < 0 or > 12) errors["openingMonths"] = "Enter 0–12.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the compensation.", errors);
        // A change dated inside months that were already paid would silently disagree with what was paid: it is only allowed as an explicit back-dated change, which then pays (or recovers) the difference.
        var covered = await db.PayRuns.AnyAsync(p => p.LegalEntityId == e.LegalEntityId && (p.Status == PayRunStatus.Locked || p.Status == PayRunStatus.Paid) && (p.Year > from.Year || p.Year == from.Year && p.Month >= from.Month), ct);
        if (covered && r.AllowRetro == true && (basis == PayBasis.Daily || cur?.PayBasis == PayBasis.Daily)) throw DomainException.Invariant("Back-dating is not supported for daily-wage workers: their pay follows the days actually worked.");
        if (covered && r.AllowRetro != true) throw DomainException.Invariant("A locked pay run already covers that month. Choose a later effective date, or back-date the change to pay the difference for the months already paid.");

        await db.InTransactionAsync(async () =>
        {
            var now = clock.UtcNow;
            if (cur is not null) { cur.EffectiveTo = from.AddDays(-1); await db.SaveChangesAsync(ct); } // close the open period first: one open period per employee
            var c = new EmployeeCompensation
            {
                EmployeeId = employeeId, EffectiveFrom = from, PayBasis = basis, TaxScheme = basis == PayBasis.Daily ? scheme : TaxScheme.MonthlyTer, PtkpStatus = ptkp, HasNpwp = r.HasNpwp ?? true, TaxTreatment = treatment, NonResident = nonResident, TreatyRate = treaty, GradeId = gradeId, KesEnrolled = r.KesEnrolled ?? true, TkEnrolled = r.TkEnrolled ?? true, JkkRate = jkk,
                ChangeReason = r.Reason?.Trim() is { Length: > 0 } rs ? rs[..Math.Min(rs.Length, 300)] : null, CreatedAt = now, CreatedBy = actor.UserId,
            };
            c.BasicEnc = Seal("comp", c.Id, "basic", Str(r.Basic!.Value)); c.AllowanceEnc = Seal("comp", c.Id, "allowance", Str(r.Allowance ?? 0));
            if (r.OpeningYear is { } y) { c.OpeningYear = y; c.OpeningMonths = r.OpeningMonths ?? 0; c.OpeningEnc = Seal("comp", c.Id, "opening", $"{Str(r.OpeningGross ?? 0)}|{Str(r.OpeningPension ?? 0)}|{Str(r.OpeningTax ?? 0)}"); }
            else if (cur?.OpeningYear is { } py && py == from.Year) { c.OpeningYear = py; c.OpeningMonths = cur.OpeningMonths; c.OpeningEnc = cur.OpeningEnc is null ? null : Seal("comp", c.Id, "opening", Reveal("comp", cur.Id, "opening", cur.OpeningEnc)); }
            db.Compensations.Add(c);
            var retro = 0m;
            if (covered)
            {
                var newBasic = r.Basic!.Value; var newAllow = r.Allowance ?? 0; var since = from;
                var paid = await (from i in db.PayRunItems.AsNoTracking() join p in db.PayRuns.AsNoTracking() on i.PayRunId equals p.Id
                                  where i.EmployeeId == employeeId && (p.Status == PayRunStatus.Locked || p.Status == PayRunStatus.Paid) && (p.Year > since.Year || p.Year == since.Year && p.Month >= since.Month)
                                  select new { p.Year, p.Month, i.Basic, i.Allowance, i.ProrateFactor }).ToListAsync(ct);
                foreach (var m in paid) retro += PayrollRules.Rupiah(newBasic * m.ProrateFactor) + PayrollRules.Rupiah(newAllow * m.ProrateFactor) - PayrollRules.Rupiah(m.Basic * m.ProrateFactor) - PayrollRules.Rupiah(m.Allowance * m.ProrateFactor);
                if (retro != 0)
                {
                    var first = paid.OrderBy(m => m.Year * 100 + m.Month).First(); var last = paid.OrderBy(m => m.Year * 100 + m.Month).Last();
                    db.RetroAdjustments.Add(new RetroAdjustment { EmployeeId = employeeId, FromYear = first.Year, FromMonth = first.Month, ThroughYear = last.Year, ThroughMonth = last.Month, Amount = retro,
                        Note = first.Year == last.Year && first.Month == last.Month ? $"{first.Year}-{first.Month:D2}" : $"{first.Year}-{first.Month:D2} to {last.Year}-{last.Month:D2}", CreatedAt = now, CreatedBy = actor.UserId });
                }
            }
            audit.Record("hr.payroll.compensation_set", "employee", employeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
                new Dictionary<string, string> { ["employeeNo"] = e.EmployeeNo, ["effectiveFrom"] = from.ToString("yyyy-MM-dd"), ["ptkp"] = ptkp, ["payBasis"] = basis.ToString(), ["taxTreatment"] = TreatmentName(treatment), ["nonResident"] = nonResident.ToString(), ["backDated"] = covered.ToString() },
                [new AuditDiff("salary", cur is null ? "[none]" : "[protected]", "[changed]")]);
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        return await BuildCompensationAsync(employeeId, ct);
    }

    // ============================================================ pay runs
    private async Task<IReadOnlyDictionary<Guid, string>> EntityNamesAsync(CancellationToken ct) => await db.LegalEntities.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);

    private PayRunSummary Summary(PayRun r, SessionPrincipal actor, IReadOnlySet<string> perms, IReadOnlyDictionary<Guid, string> entityNames) => new(r.Id, r.Year, r.Month, r.Status.ToString().ToLowerInvariant(), r.EmployeeCount, r.TotalGross, r.TotalTax, r.TotalNet,
        r.TotalEmployerCost, r.RulesVersion, r.CreatedAt, r.CalculatedAt, r.ApprovedAt, r.LockedAt, r.PaidAt, r.Note, r.Version,
        CanCalculate: perms.Contains(Permissions.PayrollRun) && r.Status is PayRunStatus.Draft or PayRunStatus.Calculated,
        CanApprove: perms.Contains(Permissions.PayrollApprove) && r.Status == PayRunStatus.Calculated && r.CalculatedBy != actor.UserId,
        CanLock: perms.Contains(Permissions.PayrollLock) && r.Status == PayRunStatus.Approved,
        CanMarkPaid: perms.Contains(Permissions.PayrollLock) && r.Status == PayRunStatus.Locked, DeductAbsence: r.DeductAbsence, LegalEntityId: r.LegalEntityId, LegalEntityName: entityNames.GetValueOrDefault(r.LegalEntityId));

    public async Task<IReadOnlyList<PayRunSummary>> RunsAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct); var names = await EntityNamesAsync(ct);
        return [.. (await db.PayRuns.AsNoTracking().OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ToListAsync(ct)).Select(r => Summary(r, actor, perms, names))];
    }

    public async Task<PayRunSummary> CreateRunAsync(CreateRunRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var errors = new Dictionary<string, string>();
        if (r.Year is null or < 2000 || r.Year > Today.Year + 1) errors["year"] = "Check the year.";
        if (r.Month is null or < 1 or > 12) errors["month"] = "Choose a month.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the pay run.", errors);
        if (new DateOnly(r.Year!.Value, r.Month!.Value, 1) > Today.AddMonths(1)) throw DomainException.Validation("Please check the pay run.", new Dictionary<string, string> { ["month"] = "That month is too far ahead." });
        var entityId = await entities.ResolveAsync(r.LegalEntityId, ct);
        if (await db.PayRuns.AnyAsync(p => p.LegalEntityId == entityId && p.Year == r.Year && p.Month == r.Month, ct)) throw DomainException.Conflict("There is already a pay run for that company and month.");
        var rules0 = await ruleSets.EffectiveForAsync(r.Year.Value, r.Month.Value, ct);
        var run = new PayRun { LegalEntityId = entityId, Year = r.Year.Value, Month = r.Month.Value, RulesVersion = rules0.Version, RulesJson = PayrollRulesService.Serialize(rules0), CreatedBy = actor.UserId, CreatedAt = clock.UtcNow, DeductAbsence = r.DeductAbsence ?? false, Note = r.Note?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 300)] : null };
        db.PayRuns.Add(run);
        audit.Record("hr.payroll.run_create", "pay_run", run.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["period"] = $"{run.Year}-{run.Month:D2}" });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ResetTracking(); throw DomainException.Conflict("There is already a pay run for that company and month."); }
        return Summary(run, actor, await authz.GetEffectivePermissionsAsync(actor.UserId, ct), await EntityNamesAsync(ct));
    }

    public async Task<PayRunDetail> RunAsync(Guid id, SessionPrincipal actor, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var run = await db.PayRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Pay run");
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        var items = await db.PayRunItems.AsNoTracking().Where(i => i.PayRunId == id).OrderBy(i => i.EmployeeName).ToListAsync(ct);
        var names = items.ToDictionary(i => i.EmployeeId, i => i.EmployeeName);
        var lines = await db.PayRunLines.AsNoTracking().Where(l => l.PayRunId == id).OrderBy(l => l.CreatedAt).ToListAsync(ct);
        var lineEmps = lines.Select(l => l.EmployeeId).Distinct().ToList();
        var empNames = await db.Employees.AsNoTracking().Where(e => lineEmps.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        IReadOnlyList<string> missing = [];
        if (run.Status is PayRunStatus.Draft or PayRunStatus.Calculated)
        {
            var (start, end) = Bounds(run);
            // "Missing" means no compensation in effect for the month, whatever the calculation state (before calculating there are no items to compare with).
            var comps = await db.Compensations.AsNoTracking().Where(c => c.EffectiveFrom <= end && (c.EffectiveTo == null || c.EffectiveTo >= start)).Select(c => new { c.EmployeeId, c.EffectiveFrom, c.EffectiveTo }).ToListAsync(ct);
            missing = [.. (await db.Employees.AsNoTracking().Where(e => e.LegalEntityId == run.LegalEntityId && e.HireDate <= end && (e.Status == EmployeeStatus.Active || e.TerminationDate >= start)).OrderBy(e => e.FullName).Select(e => new { e.Id, e.EmployeeNo, e.FullName, e.TerminationDate }).ToListAsync(ct))
                .Where(e => { var asOf = e.TerminationDate is { } td && td < end ? td : end; return !comps.Any(c => c.EmployeeId == e.Id && c.EffectiveFrom <= asOf && (c.EffectiveTo == null || c.EffectiveTo >= asOf)); })
                .Select(e => $"{e.FullName} ({e.EmployeeNo})")];
        }
        var closed = await db.AttendancePeriods.AnyAsync(p => p.Year == run.Year && p.Month == run.Month, ct);
        return new PayRunDetail(Summary(run, actor, perms, await EntityNamesAsync(ct)), [.. items.Select(ItemView)], [.. lines.Select(l => LineView(l, empNames.GetValueOrDefault(l.EmployeeId) ?? ""))], missing, closed);
    }

    internal static (DateOnly Start, DateOnly End) Bounds(PayRun r) { var s = new DateOnly(r.Year, r.Month, 1); return (s, s.AddMonths(1).AddDays(-1)); }

    private static PayItemView ItemView(PayRunItem i) => new(i.Id, i.EmployeeId, i.EmployeeNo, i.EmployeeName, i.Ptkp, i.Basic, i.Allowance, i.ProrateFactor, i.UnpaidDeduction, i.TaxableExtras, i.NonTaxableExtras, i.OtherDeductions, i.OvertimePay,
        i.Gross, i.KesEmployee, i.JhtEmployee, i.JpEmployee, i.KesEmployer, i.JhtEmployer, i.JpEmployer, i.JkkEmployer, i.JkmEmployer, i.Tax, i.TaxMethod, i.Net, i.EmployerCost, i.Warnings, i.DailyRate, i.DaysPaid,
        TreatmentName(i.TaxTreatment), i.TaxAllowance, i.TaxBorne);
    private static PayLineView LineView(PayRunLine l, string name) => new(l.Id, l.EmployeeId, name, l.Kind.ToString().ToLowerInvariant(), l.Label, l.Amount, l.Taxable, l.Auto);

    public async Task DeleteRunAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var run = await db.PayRuns.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Pay run");
        if (run.Status != PayRunStatus.Draft) throw DomainException.Invariant("Only a draft pay run can be deleted.");
        db.PayRuns.Remove(run);
        audit.Record("hr.payroll.run_delete", "pay_run", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["period"] = $"{run.Year}-{run.Month:D2}" });
        await db.SaveChangesAsync(ct);
    }

    public async Task<PayLineView> AddLineAsync(Guid runId, AddLineRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var run = await db.PayRuns.FirstOrDefaultAsync(x => x.Id == runId, ct) ?? throw DomainException.NotFound("Pay run");
        if (run.Status is not (PayRunStatus.Draft or PayRunStatus.Calculated)) throw DomainException.Invariant("This pay run can no longer be changed.");
        var errors = new Dictionary<string, string>();
        if (!Enum.TryParse<PayLineKind>(r.Kind, true, out var kind)) errors["kind"] = "Choose earning or deduction.";
        var label = r.Label?.Trim(); if (string.IsNullOrEmpty(label) || label.Length > 100) errors["label"] = "Enter a label (up to 100 characters).";
        if (r.Amount is null or <= 0 or > 10_000_000_000m) errors["amount"] = "Enter an amount above 0.";
        var emp = r.EmployeeId is { } eid ? await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eid, ct) : null; if (emp is null) errors["employeeId"] = "Choose an employee."; else if (emp.LegalEntityId != run.LegalEntityId) errors["employeeId"] = "This employee belongs to another company than this pay run.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the line.", errors);
        var line = new PayRunLine { PayRunId = runId, EmployeeId = emp!.Id, Kind = kind, Label = label!, Amount = PayrollRules.Rupiah(r.Amount!.Value), Taxable = kind == PayLineKind.Earning ? r.Taxable ?? true : false, CreatedBy = actor.UserId, CreatedAt = clock.UtcNow };
        db.PayRunLines.Add(line);
        if (run.Status == PayRunStatus.Calculated) Invalidate(run); // the numbers on screen no longer include this line
        audit.Record("hr.payroll.line_add", "pay_run", runId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeNo"] = emp.EmployeeNo, ["kind"] = kind.ToString(), ["label"] = label! });
        await db.SaveChangesAsync(ct);
        return LineView(line, emp.FullName);
    }

    /// <summary>
    /// PAY-005 THR (religious holiday allowance). Adds one taxable earning per active employee: one month's wage (basic + fixed allowance) after 12 or more months of service, otherwise
    /// months/12 of it (at least one full month of service). Run it in the month the THR is paid. Safe to repeat: someone who already has a THR line in this run is skipped.
    /// </summary>
    public async Task<int> AddThrAsync(Guid runId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var run = await db.PayRuns.FirstOrDefaultAsync(x => x.Id == runId, ct) ?? throw DomainException.NotFound("Pay run");
        if (run.Status is not (PayRunStatus.Draft or PayRunStatus.Calculated)) throw DomainException.Invariant("This pay run can no longer be changed.");
        var (start, end) = Bounds(run); var label = $"THR {run.Year}";
        var have = (await db.PayRunLines.AsNoTracking().Where(l => l.PayRunId == runId && l.Label == label).Select(l => l.EmployeeId).ToListAsync(ct)).ToHashSet();
        var emps = await db.Employees.AsNoTracking().Where(e => e.LegalEntityId == run.LegalEntityId && e.Status == EmployeeStatus.Active && e.HireDate <= end).ToListAsync(ct);
        var comps = await db.Compensations.AsNoTracking().Where(c => c.EffectiveFrom <= end && (c.EffectiveTo == null || c.EffectiveTo >= end)).ToListAsync(ct);
        var added = 0;
        foreach (var e in emps)
        {
            if (have.Contains(e.Id) || comps.FirstOrDefault(c => c.EmployeeId == e.Id) is not { } c) continue;
            var months = (end.Year - e.HireDate.Year) * 12 + end.Month - e.HireDate.Month - (end.Day < e.HireDate.Day ? 1 : 0);
            if (months < 1) continue;
            var wage = Dec(Reveal("comp", c.Id, "basic", c.BasicEnc)) + Dec(Reveal("comp", c.Id, "allowance", c.AllowanceEnc));
            var amount = PayrollRules.Rupiah(wage * Math.Min(months, 12) / 12m);
            if (amount <= 0) continue;
            db.PayRunLines.Add(new PayRunLine { PayRunId = runId, EmployeeId = e.Id, Kind = PayLineKind.Earning, Label = label, Amount = amount, Taxable = true, CreatedBy = actor.UserId, CreatedAt = clock.UtcNow });
            added++;
        }
        if (added > 0 && run.Status == PayRunStatus.Calculated) Invalidate(run);
        audit.Record("hr.payroll.thr", "pay_run", runId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["period"] = $"{run.Year}-{run.Month:D2}", ["added"] = added.ToString() });
        await db.SaveChangesAsync(ct);
        return added;
    }

    public async Task DeleteLineAsync(Guid runId, Guid lineId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var run = await db.PayRuns.FirstOrDefaultAsync(x => x.Id == runId, ct) ?? throw DomainException.NotFound("Pay run");
        if (run.Status is not (PayRunStatus.Draft or PayRunStatus.Calculated)) throw DomainException.Invariant("This pay run can no longer be changed.");
        var line = await db.PayRunLines.FirstOrDefaultAsync(l => l.Id == lineId && l.PayRunId == runId, ct) ?? throw DomainException.NotFound("Line");
        if (line.Auto) throw DomainException.Invariant("This line is generated by the system (a loan instalment). Change the loan instead.");
        db.PayRunLines.Remove(line);
        if (run.Status == PayRunStatus.Calculated) Invalidate(run);
        audit.Record("hr.payroll.line_delete", "pay_run", runId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["label"] = line.Label });
        await db.SaveChangesAsync(ct);
    }

    private static void Invalidate(PayRun run) { run.Status = PayRunStatus.Draft; run.CalculatedBy = null; run.CalculatedAt = null; run.Version++; }

    // ============================================================ calculation
    public async Task<PayRunDetail> CalculateAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var run0 = await db.PayRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Pay run");
        if (run0.Status is not (PayRunStatus.Draft or PayRunStatus.Calculated)) throw DomainException.Invariant("This pay run is already approved or locked.");
        if (await db.PayRuns.AnyAsync(p => p.LegalEntityId == run0.LegalEntityId && p.Year == run0.Year && p.Month < run0.Month && p.Status != PayRunStatus.Locked && p.Status != PayRunStatus.Paid, ct))
            throw DomainException.Invariant("An earlier month of this year is not locked yet. Year-to-date tax depends on it.");

        await db.InTransactionAsync(async () =>
        {
            await db.ExecuteSqlAsync($"SELECT id FROM hr_pay_runs WHERE id = {id} FOR UPDATE", ct);
            var run = await db.PayRuns.FirstAsync(x => x.Id == id, ct);
            if (run.Status is not (PayRunStatus.Draft or PayRunStatus.Calculated)) throw DomainException.Invariant("This pay run is already approved or locked.");
            // Until a run is locked it always uses the rules in force for its month *now* (a correction made after the run was created applies); the snapshot is then frozen with the run.
            var rules = await ruleSets.EffectiveForAsync(run.Year, run.Month, ct); run.RulesVersion = rules.Version; run.RulesJson = PayrollRulesService.Serialize(rules);
            var (start, end) = Bounds(run);
            var daysInMonth = DateTime.DaysInMonth(run.Year, run.Month);
            var holidays = (await db.Holidays.AsNoTracking().Where(h => h.Date >= start && h.Date <= end).Select(h => h.Date).ToListAsync(ct)).ToHashSet();

            await db.PayRunItems.Where(i => i.PayRunId == id).ExecuteDeleteAsync(ct);
            var employees = await db.Employees.AsNoTracking().Where(e => e.LegalEntityId == run.LegalEntityId && e.HireDate <= end && (e.Status == EmployeeStatus.Active || e.TerminationDate >= start)).OrderBy(e => e.FullName).ToListAsync(ct);
            var lookup = await shifts.LookupAsync(employees.Select(e => e.Id), start, end, ct);
            // System-made lines (loan instalments) are rebuilt on every calculation so they always match the balance.
            await db.PayRunLines.Where(l => l.PayRunId == id && l.Auto).ExecuteDeleteAsync(ct);
            var empIds = employees.Select(e => e.Id).ToList();
            foreach (var loan in await db.Loans.AsNoTracking().Where(l => l.Status == LoanStatus.Active && l.Remaining > 0 && empIds.Contains(l.EmployeeId)).ToListAsync(ct))
                db.PayRunLines.Add(new PayRunLine { PayRunId = id, EmployeeId = loan.EmployeeId, Kind = PayLineKind.Deduction, Label = loan.Note is { Length: > 0 } ln ? $"Loan instalment: {ln}" : "Loan instalment", Amount = PayrollRules.Rupiah(Math.Min(loan.Installment, loan.Remaining)), Taxable = false, Auto = true, LoanId = loan.Id, CreatedAt = clock.UtcNow });
            foreach (var rt in await db.RetroAdjustments.AsNoTracking().Where(x => x.AppliedRunId == null && empIds.Contains(x.EmployeeId)).ToListAsync(ct))
                db.PayRunLines.Add(new PayRunLine { PayRunId = id, EmployeeId = rt.EmployeeId, Kind = rt.Amount > 0 ? PayLineKind.Earning : PayLineKind.Deduction, Label = $"Back pay adjustment ({rt.Note})", Amount = Math.Abs(rt.Amount), Taxable = rt.Amount > 0, Auto = true, RetroId = rt.Id, CreatedAt = clock.UtcNow });
            // Approved expense claims not yet paid: reimbursed here, tax-free. A claim that already sits in another unfinished run of this company is left there.
            var claimedElsewhere = await (from l in db.PayRunLines.AsNoTracking() join p in db.PayRuns.AsNoTracking() on l.PayRunId equals p.Id where l.ClaimId != null && p.Id != id && p.Status != PayRunStatus.Locked && p.Status != PayRunStatus.Paid select l.ClaimId!.Value).ToListAsync(ct);
            foreach (var cl in await db.ExpenseClaims.AsNoTracking().Where(c => c.Status == ClaimStatus.Approved && empIds.Contains(c.EmployeeId) && !claimedElsewhere.Contains(c.Id)).OrderBy(c => c.CreatedAt)
                         .Select(c => new { c.Id, c.EmployeeId, c.Category, c.ExpenseDate, c.Amount, c.Taxable }).ToListAsync(ct))
                db.PayRunLines.Add(new PayRunLine { PayRunId = id, EmployeeId = cl.EmployeeId, Kind = PayLineKind.Earning, Label = $"{(cl.Taxable ? "Benefit" : "Reimbursement")}: {cl.Category} {cl.ExpenseDate:yyyy-MM-dd}", Amount = cl.Amount, Taxable = cl.Taxable, Auto = true, ClaimId = cl.Id, CreatedAt = clock.UtcNow });
            // Cash advances whose settlement was accepted: the difference between what was spent and what was advanced (paid out if more was spent, deducted if less).
            var advanceElsewhere = await (from l in db.PayRunLines.AsNoTracking() join p in db.PayRuns.AsNoTracking() on l.PayRunId equals p.Id where l.AdvanceId != null && p.Id != id && p.Status != PayRunStatus.Locked && p.Status != PayRunStatus.Paid select l.AdvanceId!.Value).ToListAsync(ct);
            foreach (var ad in await db.CashAdvances.AsNoTracking().Where(a => a.Status == AdvanceStatus.SettlementApproved && empIds.Contains(a.EmployeeId) && !advanceElsewhere.Contains(a.Id)).OrderBy(a => a.CreatedAt)
                         .Select(a => new { a.Id, a.EmployeeId, a.Amount, Spent = a.SpentAmount ?? 0, a.Purpose }).ToListAsync(ct))
            {
                var diff = ad.Spent - ad.Amount; if (diff == 0) continue;
                var purpose = ad.Purpose.Length > 60 ? ad.Purpose[..60] : ad.Purpose;
                db.PayRunLines.Add(new PayRunLine { PayRunId = id, EmployeeId = ad.EmployeeId, Kind = diff > 0 ? PayLineKind.Earning : PayLineKind.Deduction, Label = diff > 0 ? $"Cash advance overspent: {purpose}" : $"Cash advance not spent: {purpose}", Amount = Math.Abs(diff), Taxable = false, Auto = true, AdvanceId = ad.Id, CreatedAt = clock.UtcNow });
            }
            // Early wage withdrawals transferred for this month: recovered here.
            foreach (var wa in await db.WageAdvances.AsNoTracking().Where(w => w.Status == WageAdvanceStatus.Disbursed && w.Year == run.Year && w.Month == run.Month && empIds.Contains(w.EmployeeId)).OrderBy(w => w.CreatedAt).ToListAsync(ct))
                db.PayRunLines.Add(new PayRunLine { PayRunId = id, EmployeeId = wa.EmployeeId, Kind = PayLineKind.Deduction, Label = $"Early wage withdrawal {wa.DisbursedOn:d MMM}", Amount = wa.Amount, Taxable = false, Auto = true, WageAdvanceId = wa.Id, CreatedAt = clock.UtcNow });
            // Weekly payouts already paid to daily-wage workers: the month is computed in full and what they received during the weeks is taken off here, once.
            var payoutElsewhere = await (from l in db.PayRunLines.AsNoTracking() join pr in db.PayRuns.AsNoTracking() on l.PayRunId equals pr.Id where l.PayoutItemId != null && pr.Id != id && pr.Status != PayRunStatus.Locked && pr.Status != PayRunStatus.Paid select l.PayoutItemId!.Value).ToListAsync(ct);
            foreach (var w in await (from i in db.WeeklyPayoutItems.AsNoTracking() join wp in db.WeeklyPayouts.AsNoTracking() on i.PayoutId equals wp.Id
                                     where wp.LegalEntityId == run.LegalEntityId && (wp.Status == PayoutStatus.Approved || wp.Status == PayoutStatus.Paid) && wp.WeekEnd <= end && i.SettledRunId == null && i.Net > 0 && empIds.Contains(i.EmployeeId) && !payoutElsewhere.Contains(i.Id)
                                     orderby wp.WeekStart select new { i.Id, i.EmployeeId, i.Net, wp.WeekStart, wp.WeekEnd }).ToListAsync(ct))
                db.PayRunLines.Add(new PayRunLine { PayRunId = id, EmployeeId = w.EmployeeId, Kind = PayLineKind.Deduction, Label = $"Paid in weekly payout {w.WeekStart:d MMM} – {w.WeekEnd:d MMM}", Amount = w.Net, Taxable = false, Auto = true, PayoutItemId = w.Id, CreatedAt = clock.UtcNow });
            // On-call duty days of the month: one taxable allowance line per person.
            foreach (var oc in (await db.OnCallDuties.AsNoTracking().Where(o => o.Date >= start && o.Date <= end && empIds.Contains(o.EmployeeId)).ToListAsync(ct)).GroupBy(o => o.EmployeeId))
                if (oc.Sum(o => o.Allowance) > 0) db.PayRunLines.Add(new PayRunLine { PayRunId = id, EmployeeId = oc.Key, Kind = PayLineKind.Earning, Label = $"On-call allowance ({oc.Count()} day(s))", Amount = oc.Sum(o => o.Allowance), Taxable = true, Auto = true, CreatedAt = clock.UtcNow });
            // Recurring components (allowances, fees) in force this month.
            foreach (var rc in await db.RecurringPayItems.AsNoTracking().Where(x => x.EndedAt == null && x.From <= start && (x.Through == null || x.Through >= start) && empIds.Contains(x.EmployeeId)).OrderBy(x => x.CreatedAt).ToListAsync(ct))
                db.PayRunLines.Add(new PayRunLine { PayRunId = id, EmployeeId = rc.EmployeeId, Kind = rc.Kind, Label = rc.Label, Amount = rc.Amount, Taxable = rc.Kind == PayLineKind.Earning && rc.Taxable, Auto = true, CreatedAt = clock.UtcNow });
            await db.SaveChangesAsync(ct);
            var comps = await db.Compensations.AsNoTracking().Where(c => c.EffectiveFrom <= end && (c.EffectiveTo == null || c.EffectiveTo >= start)).ToListAsync(ct);
            // Daily-wage workers are paid for the days they worked, so the month's attendance must be final before their pay is calculated.
            var anyDaily = comps.Any(c => c.PayBasis == PayBasis.Daily && empIds.Contains(c.EmployeeId));
            Dictionary<Guid, HashSet<DateOnly>> attended = []; List<LeaveRequest> anyLeave = [];
            if (run.DeductAbsence || anyDaily)
            {
                if (!await db.AttendancePeriods.AnyAsync(p => p.Year == run.Year && p.Month == run.Month, ct)) throw DomainException.Invariant(anyDaily ? "Close the month's attendance before calculating: daily-wage workers are paid for the days they worked." : "Close the month's attendance before deducting absences.");
                attended = (await db.AttendanceLogs.AsNoTracking().Where(l => l.Date >= start && l.Date <= end).Select(l => new { l.EmployeeId, l.Date }).ToListAsync(ct)).GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.Select(x => x.Date).ToHashSet());
                anyLeave = await db.LeaveRequests.AsNoTracking().Where(l => l.Status == LeaveStatus.Approved && l.StartDate <= end && l.EndDate >= start).ToListAsync(ct);
            }
            var lines = await db.PayRunLines.AsNoTracking().Where(l => l.PayRunId == id).ToListAsync(ct);
            // Locked history of this and last year in one query: year-to-date tax inputs come from this year, "last month's net" (anomaly check) from the most recent period.
            var history = await (from i in db.PayRunItems.AsNoTracking() join p in db.PayRuns.AsNoTracking() on i.PayRunId equals p.Id
                                 where (p.Status == PayRunStatus.Locked || p.Status == PayRunStatus.Paid) && (p.Year == run.Year && p.Month < run.Month || p.Year == run.Year - 1)
                                 select new { i.EmployeeId, p.Year, p.Month, i.Net, i.TaxableGross, i.JhtEmployee, i.JpEmployee, i.Tax }).ToListAsync(ct);
            var prior = history.Where(x => x.Year == run.Year).ToList();
            var lastNet = history.GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Year * 100 + x.Month).First().Net);
            var unpaidLeave = await db.LeaveRequests.AsNoTracking().Where(l => l.Status == LeaveStatus.Approved && l.StartDate <= end && l.EndDate >= start && db.LeaveTypes.Any(t => t.Id == l.LeaveTypeId && !t.Paid)).ToListAsync(ct);
            var overtime = await db.OvertimeRequests.AsNoTracking().Where(o => o.Status == RequestStatus.Approved && o.Date >= start && o.Date <= end).ToListAsync(ct);
            var unpaidHours = (await db.HourlyLeaves.AsNoTracking().Where(h => h.Status == RequestStatus.Approved && !h.Paid && h.Date >= start && h.Date <= end).ToListAsync(ct)).GroupBy(h => h.EmployeeId).ToDictionary(g => g.Key, g => g.Sum(h => h.Hours));
            var bankSet = employees.Where(e => e.BankAccountEnc != null).Select(e => e.Id).ToHashSet();

            var results = new List<PayRunItem>();
            foreach (var e in employees)
            {
                bool Working(DateOnly d) => lookup.Works(e.Id, d) && !holidays.Contains(d);
                var workingInMonth = Enumerable.Range(0, daysInMonth).Count(i => Working(start.AddDays(i)));
                var asOf = e.TerminationDate is { } td && td < end ? td : end;
                var c = comps.Where(x => x.EmployeeId == e.Id && x.EffectiveFrom <= asOf && (x.EffectiveTo == null || x.EffectiveTo >= asOf)).OrderByDescending(x => x.EffectiveFrom).FirstOrDefault();
                if (c is null) continue;
                var basic = Dec(Reveal("comp", c.Id, "basic", c.BasicEnc)); var allow = Dec(Reveal("comp", c.Id, "allowance", c.AllowanceEnc));
                var from = e.HireDate > start ? e.HireDate : start; var to = e.TerminationDate is { } t2 && t2 < end ? t2 : end;
                var prorate = Math.Round((decimal)(to.DayNumber - from.DayNumber + 1) / daysInMonth, 6);
                var warnings = new List<string>();
                if (prorate < 1) warnings.Add("Part of the month");
                if (c.EffectiveFrom > start && c.EffectiveFrom > e.HireDate) warnings.Add("Compensation changed during the month (the new rate is used for the whole month)");
                var daily = c.PayBasis == PayBasis.Daily; decimal? dailyRate = null, daysPaid = null; var allowPerDay = allow;
                if (daily)
                {
                    var present = (attended.GetValueOrDefault(e.Id) ?? []).Where(d => d >= from && d <= to).ToHashSet(); var days = (decimal)present.Count;
                    foreach (var l in anyLeave.Where(x => x.EmployeeId == e.Id && !unpaidLeave.Any(u => u.Id == x.Id)))   // approved paid leave counts as a paid day
                        for (var d = l.StartDate > from ? l.StartDate : from; d <= (l.EndDate < to ? l.EndDate : to); d = d.AddDays(1)) if (Working(d) && !present.Contains(d)) days += l.HalfDay ? 0.5m : 1m;
                    dailyRate = basic; daysPaid = days; basic = PayrollRules.Rupiah(basic * days); allow = PayrollRules.Rupiah(allow * days); prorate = 1;
                    warnings.Add(days == 0 ? "No days worked this month" : $"Daily wage: {days:0.##} day(s)");
                }

                decimal unpaid = 0;
                foreach (var l in unpaidLeave.Where(x => !daily && x.EmployeeId == e.Id))
                {
                    var days = 0m;
                    for (var d = l.StartDate > start ? l.StartDate : start; d <= (l.EndDate < end ? l.EndDate : end); d = d.AddDays(1)) if (Working(d)) days += l.HalfDay ? 0.5m : 1m;
                    unpaid += workingInMonth == 0 ? 0 : PayrollRules.Rupiah((basic + allow) * days / workingInMonth);
                }
                if (run.DeductAbsence && !daily)
                {
                    var present = attended.GetValueOrDefault(e.Id) ?? []; var absent = 0;
                    for (var d = from; d <= to; d = d.AddDays(1)) if (Working(d) && !present.Contains(d) && !anyLeave.Any(l => l.EmployeeId == e.Id && l.StartDate <= d && l.EndDate >= d)) absent++;
                    if (absent > 0) { unpaid += PayrollRules.Rupiah((basic + allow) * absent / Math.Max(workingInMonth, 1)); warnings.Add($"Absent {absent} day(s) deducted"); }
                }
                // Unpaid leave by the hour: the hourly wage (monthly wage / 173, or a day's wage / 8 for daily workers) for each hour away.
                if (unpaidHours.TryGetValue(e.Id, out var awayHours) && awayHours > 0)
                {
                    unpaid += PayrollRules.Rupiah(daily ? (dailyRate!.Value + allowPerDay) / 8m * awayHours : (basic + allow) / 173m * awayHours);
                    warnings.Add($"Unpaid leave by the hour: {awayHours:0.##} h");
                }
                // Daily wage → monthly wage for the overtime rate (Kepmenaker 102/2004): × 25 for a six-day working week, × 21 for five days.
                var monthFactor = System.Numerics.BitOperations.PopCount((uint)lookup.For(e.Id, end).WorkDays) >= 6 ? 25m : 21m;
                var overtimePay = PayrollCalculator.OvertimePay(daily ? (dailyRate!.Value + allowPerDay) * monthFactor : basic + allow, overtime.Where(o => o.EmployeeId == e.Id).Select(o => (o.Hours, !Working(o.Date))));
                if (overtimePay > 0) warnings.Add("Includes approved overtime");
                var mine = lines.Where(l => l.EmployeeId == e.Id).ToList();
                var taxableExtras = mine.Where(l => l.Kind == PayLineKind.Earning && l.Taxable).Sum(l => l.Amount);
                var nonTaxable = mine.Where(l => l.Kind == PayLineKind.Earning && !l.Taxable).Sum(l => l.Amount);
                var deductions = mine.Where(l => l.Kind == PayLineKind.Deduction).Sum(l => l.Amount);

                var p = prior.Where(x => x.EmployeeId == e.Id).ToList();
                decimal ytdGross = p.Sum(x => x.TaxableGross), ytdPension = p.Sum(x => x.JhtEmployee + x.JpEmployee), ytdTax = p.Sum(x => x.Tax); var months = p.Count + 1;
                if (c.OpeningYear == run.Year && c.OpeningEnc is not null) { var (og, op, ot) = Opening(c); ytdGross += og; ytdPension += op; ytdTax += ot; months += c.OpeningMonths; }
                var finalMonth = run.Month == 12 || e.TerminationDate is { } tt && tt.Year == run.Year && tt.Month == run.Month;

                var res = PayrollCalculator.Calculate(new PayInput(c.PtkpStatus, c.HasNpwp, c.KesEnrolled, c.TkEnrolled, c.JkkRate, basic, allow, prorate, unpaid, taxableExtras + overtimePay, nonTaxable, deductions, finalMonth, months, ytdGross, ytdPension, ytdTax, NonPermanent: daily, DailyTer: daily && c.TaxScheme == TaxScheme.DailyTer, DaysPaid: daysPaid ?? 0,
                    Treatment: c.TaxTreatment, NonResident: c.NonResident, TreatyRate: c.TreatyRate), rules);
                if (res.Net <= 0) warnings.Add("Net pay is zero or negative");
                if (lastNet.TryGetValue(e.Id, out var ln) && ln > 0 && Math.Abs(res.Net - ln) / ln > 0.30m) warnings.Add("Net pay differs by more than 30% from last month");
                if (!bankSet.Contains(e.Id)) warnings.Add("No bank account on file");
                if (!c.HasNpwp && !c.NonResident) warnings.Add("No NPWP: 20% surcharge applied");
                if (c.NonResident) warnings.Add(c.TreatyRate is { } tr ? $"Non-resident: PPh 26 at the treaty rate {tr * 100:0.##}%" : "Non-resident: PPh 26");
                if (c.TaxTreatment == TaxTreatment.GrossUp) warnings.Add("Tax grossed up: PPh allowance paid on top");
                else if (c.TaxTreatment == TaxTreatment.Nett) warnings.Add("Tax borne by the company (nett)");
                results.Add(new PayRunItem
                {
                    PayRunId = id, EmployeeId = e.Id, EmployeeNo = e.EmployeeNo, EmployeeName = e.FullName, Ptkp = c.PtkpStatus, Basic = basic, Allowance = allow, DailyRate = dailyRate, DaysPaid = daysPaid, ProrateFactor = prorate, UnpaidDeduction = unpaid,
                    TaxableExtras = taxableExtras, NonTaxableExtras = nonTaxable, OtherDeductions = deductions, OvertimePay = overtimePay, Gross = res.Gross, TaxableGross = res.TaxableGross, KesEmployee = res.KesEmployee, JhtEmployee = res.JhtEmployee,
                    JpEmployee = res.JpEmployee, KesEmployer = res.KesEmployer, JhtEmployer = res.JhtEmployer, JpEmployer = res.JpEmployer, JkkEmployer = res.JkkEmployer, JkmEmployer = res.JkmEmployer, Tax = res.Tax,
                    TaxMethod = res.TaxMethod, TaxTreatment = c.TaxTreatment, TaxAllowance = res.TaxAllowance, TaxBorne = res.TaxBorne, Net = res.Net, EmployerCost = res.EmployerCost, Warnings = warnings.Count == 0 ? null : string.Join("; ", warnings),
                });
            }
            db.PayRunItems.AddRange(results);
            run.EmployeeCount = results.Count; run.TotalGross = results.Sum(x => x.Gross); run.TotalTax = results.Sum(x => x.Tax); run.TotalNet = results.Sum(x => x.Net); run.TotalEmployerCost = results.Sum(x => x.EmployerCost);
            run.Status = PayRunStatus.Calculated; run.CalculatedBy = actor.UserId; run.CalculatedAt = clock.UtcNow; run.ApprovedBy = null; run.ApprovedAt = null; run.Version++;
            foreach (var uid in await notes.UsersWithPermissionAsync(Permissions.PayrollApprove, ct))
                if (uid != actor.UserId) await notes.NotifyOnceAsync(uid, "payroll.ready", $"Pay run {new DateOnly(run.Year, run.Month, 1):MMMM yyyy} is ready to approve", $"{results.Count} employee(s)", $"/hr/payroll/{id}", ct);
            audit.Record("hr.payroll.calculate", "pay_run", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
                new Dictionary<string, string> { ["period"] = $"{run.Year}-{run.Month:D2}", ["employees"] = results.Count.ToString(), ["rules"] = run.RulesVersion });
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        return await RunAsync(id, actor, ct);
    }

    // ============================================================ approval, lock, payment
    public async Task<PayRunSummary> ApproveAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var run = await db.PayRuns.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Pay run");
        if (run.Status != PayRunStatus.Calculated) throw DomainException.Conflict("Only a calculated pay run can be approved.");
        if (run.CalculatedBy == actor.UserId) { Deny(actor, client, id, "maker-checker"); await db.SaveChangesAsync(ct); throw DomainException.Invariant("The person who calculated a pay run cannot approve it."); }
        run.Status = PayRunStatus.Approved; run.ApprovedBy = actor.UserId; run.ApprovedAt = clock.UtcNow; run.Version++;
        audit.Record("hr.payroll.approve", "pay_run", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["period"] = $"{run.Year}-{run.Month:D2}", ["net"] = run.TotalNet.ToString() });
        await db.SaveChangesAsync(ct);
        return Summary(run, actor, await authz.GetEffectivePermissionsAsync(actor.UserId, ct), await EntityNamesAsync(ct));
    }

    private void Deny(SessionPrincipal actor, ClientInfo client, Guid id, string rule)
        => audit.Record("hr.payroll.denied", "pay_run", id.ToString(), Outcome.Denied, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["rule"] = rule });

    public async Task<PayRunSummary> RejectAsync(Guid id, string? note, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var run = await db.PayRuns.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Pay run");
        if (run.Status != PayRunStatus.Calculated) throw DomainException.Conflict("Only a calculated pay run can be sent back.");
        if (string.IsNullOrWhiteSpace(note)) throw DomainException.Validation("Please give a reason.", new Dictionary<string, string> { ["note"] = "Tell the preparer what to fix." });
        Invalidate(run); run.Note = note.Trim()[..Math.Min(note.Trim().Length, 300)];
        audit.Record("hr.payroll.reject", "pay_run", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["note"] = run.Note });
        await db.SaveChangesAsync(ct);
        return Summary(run, actor, await authz.GetEffectivePermissionsAsync(actor.UserId, ct), await EntityNamesAsync(ct));
    }

    public async Task<PayRunSummary> LockAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var run = await db.PayRuns.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Pay run");
        if (run.Status != PayRunStatus.Approved) throw DomainException.Conflict("Only an approved pay run can be locked.");
        if (run.CalculatedBy == actor.UserId) { Deny(actor, client, id, "maker-checker"); await db.SaveChangesAsync(ct); throw DomainException.Invariant("The person who calculated a pay run cannot lock it."); }
        run.Status = PayRunStatus.Locked; run.LockedBy = actor.UserId; run.LockedAt = clock.UtcNow; run.Version++;
        var payoutItemIds = await db.PayRunLines.AsNoTracking().Where(l => l.PayRunId == id && l.PayoutItemId != null).Select(l => l.PayoutItemId!.Value).ToListAsync(ct);
        foreach (var wi in await db.WeeklyPayoutItems.Where(i => payoutItemIds.Contains(i.Id) && i.SettledRunId == null).ToListAsync(ct)) wi.SettledRunId = id;   // saved together with the lock
        var claimIds = await db.PayRunLines.AsNoTracking().Where(l => l.PayRunId == id && l.ClaimId != null).Select(l => l.ClaimId!.Value).ToListAsync(ct);
        foreach (var cl in await db.ExpenseClaims.Where(c => claimIds.Contains(c.Id) && c.Status == ClaimStatus.Approved).ToListAsync(ct)) { cl.Status = ClaimStatus.Paid; cl.PaidRunId = id; cl.Version++; }
        var advanceIds = await db.PayRunLines.AsNoTracking().Where(l => l.PayRunId == id && l.AdvanceId != null).Select(l => l.AdvanceId!.Value).ToListAsync(ct);
        foreach (var ad in await db.CashAdvances.Where(a => advanceIds.Contains(a.Id) && a.Status == AdvanceStatus.SettlementApproved).ToListAsync(ct)) { ad.Status = AdvanceStatus.Settled; ad.SettledRunId = id; ad.Version++; }
        var wageIds = await db.PayRunLines.AsNoTracking().Where(l => l.PayRunId == id && l.WageAdvanceId != null).Select(l => l.WageAdvanceId!.Value).ToListAsync(ct);
        foreach (var wa in await db.WageAdvances.Where(w => wageIds.Contains(w.Id) && w.Status == WageAdvanceStatus.Disbursed).ToListAsync(ct)) { wa.Status = WageAdvanceStatus.Recovered; wa.RecoveredRunId = id; wa.Version++; }
        var retroIds = await db.PayRunLines.AsNoTracking().Where(l => l.PayRunId == id && l.RetroId != null).Select(l => l.RetroId!.Value).ToListAsync(ct);
        foreach (var rt in await db.RetroAdjustments.Where(x => retroIds.Contains(x.Id)).ToListAsync(ct)) { rt.AppliedRunId = id; rt.AppliedAt = clock.UtcNow; }
        foreach (var ll in await db.PayRunLines.AsNoTracking().Where(l => l.PayRunId == id && l.LoanId != null).Select(l => new { l.LoanId, l.Amount }).ToListAsync(ct))
            if (await db.Loans.FirstOrDefaultAsync(x => x.Id == ll.LoanId, ct) is { } loan) { loan.Remaining = Math.Max(0, loan.Remaining - ll.Amount); if (loan.Remaining == 0) loan.Status = LoanStatus.PaidOff; }
        await webhooks.EnqueueAsync("payroll.locked", new { payRunId = id, legalEntityId = run.LegalEntityId, year = run.Year, month = run.Month, employees = run.EmployeeCount }, ct);
        var period = new DateOnly(run.Year, run.Month, 1).ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var slipUsers = await (from i in db.PayRunItems.AsNoTracking() join e in db.Employees.AsNoTracking() on i.EmployeeId equals e.Id join u in db.Users.AsNoTracking() on e.UserId equals u.Id
                               where i.PayRunId == id && u.Status == UserStatus.Active select new { u.Id, u.Email, e.FullName }).ToListAsync(ct);
        foreach (var u in slipUsers)
        {
            notes.Notify(u.Id, "payslip.ready", $"Your payslip for {period} is ready", null, "/hr/my-payslips");
            if (notes.EmailAllowed(u.Id, "payslip.ready")) email.Enqueue(EmailTemplates.PayslipReady(u.Email, u.FullName, period, links.Origin + "/hr/my-payslips"));   // PAY-009: told by e-mail, without amounts
        }
        audit.Record("hr.payroll.lock", "pay_run", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["period"] = $"{run.Year}-{run.Month:D2}" });
        await db.SaveChangesAsync(ct);
        return Summary(run, actor, await authz.GetEffectivePermissionsAsync(actor.UserId, ct), await EntityNamesAsync(ct));
    }

    public async Task<PayRunSummary> MarkPaidAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var run = await db.PayRuns.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Pay run");
        if (run.Status != PayRunStatus.Locked) throw DomainException.Conflict("Only a locked pay run can be marked as paid.");
        run.Status = PayRunStatus.Paid; run.PaidAt = clock.UtcNow; run.Version++;
        await webhooks.EnqueueAsync("payroll.paid", new { payRunId = id, legalEntityId = run.LegalEntityId, year = run.Year, month = run.Month }, ct);
        audit.Record("hr.payroll.paid", "pay_run", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["period"] = $"{run.Year}-{run.Month:D2}" });
        await db.SaveChangesAsync(ct);
        return Summary(run, actor, await authz.GetEffectivePermissionsAsync(actor.UserId, ct), await EntityNamesAsync(ct));
    }

    // ============================================================ exports
    /// <summary>The transfer file for the bank: net pay per employee with a bank account, in the chosen bank layout (or a generic CSV).</summary>
    public async Task<ExportFile> BankFileAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default, Guid? layoutId = null)
    {
        await GuardAsync(actor, ct);
        var run = await db.PayRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Pay run");
        if (run.Status is not (PayRunStatus.Locked or PayRunStatus.Paid)) throw DomainException.Invariant("The bank file is available once the pay run is locked.");
        var layout = layoutId is { } lid ? await db.BankFileLayouts.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lid, ct) ?? throw DomainException.NotFound("Bank file layout") : null;
        var items = await db.PayRunItems.AsNoTracking().Where(i => i.PayRunId == id && i.Net > 0).OrderBy(i => i.EmployeeNo).ToListAsync(ct);
        var itemEmps = items.Select(i => i.EmployeeId).ToList();
        var emps = await db.Employees.AsNoTracking().Where(e => itemEmps.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var rows = new List<BankTransfer>(); var skipped = 0;
        foreach (var i in items)
        {
            var e = emps[i.EmployeeId];
            if (e.BankAccountEnc is null) { skipped++; continue; }
            var p = e.BankAccountEnc.Split('.');
            var account = Encoding.UTF8.GetString(protector.Unprotect(new ProtectedSecret(Convert.FromBase64String(p[2]), Convert.FromBase64String(p[1]), int.Parse(p[0])), Encoding.UTF8.GetBytes($"employee:{e.Id}:bank_account")));
            rows.Add(new BankTransfer(i.EmployeeNo, i.EmployeeName, e.BankName, account, i.Net, $"Gaji {run.Year}-{run.Month:D2}", e.WorkEmail));
        }
        audit.Record("hr.payroll.bank_file", "pay_run", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["rows"] = rows.Count.ToString(), ["skippedNoAccount"] = skipped.ToString(), ["layout"] = layout?.Name ?? "generic" });
        await db.SaveChangesAsync(ct);
        var payDay = new DateOnly(run.Year, run.Month, DateTime.DaysInMonth(run.Year, run.Month));
        var bytes = IntegrationService.Render(layout, rows, payDay);
        var ext = layout?.Delimiter == "\t" ? "txt" : "csv";
        return new ExportFile($"bank-transfer-{run.Year}-{run.Month:D2}{(layout is null ? "" : "-" + new string(layout.Name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()))}.{ext}", "text/csv; charset=utf-8", Encoding.UTF8.GetPreamble().Concat(bytes).ToArray());
    }

    public async Task<ExportFile> RegisterAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        var run = await db.PayRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Pay run");
        var items = await db.PayRunItems.AsNoTracking().Where(i => i.PayRunId == id).OrderBy(i => i.EmployeeNo).ToListAsync(ct);
        string M(decimal d) => d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var sb = new StringBuilder("employee_no,name,ptkp,basic,allowance,gross,bpjs_kes_ee,jht_ee,jp_ee,bpjs_kes_er,jht_er,jp_er,jkk_er,jkm_er,pph21,tax_method,net,employer_cost,tax_treatment,pph_allowance,pph_borne_by_company\r\n");
        foreach (var i in items)
            sb.Append(string.Join(',', new[] { i.EmployeeNo, i.EmployeeName, i.Ptkp, M(i.Basic), M(i.Allowance), M(i.Gross), M(i.KesEmployee), M(i.JhtEmployee), M(i.JpEmployee), M(i.KesEmployer), M(i.JhtEmployer), M(i.JpEmployer), M(i.JkkEmployer), M(i.JkmEmployer), M(i.Tax), i.TaxMethod, M(i.Net), M(i.EmployerCost), TreatmentName(i.TaxTreatment), M(i.TaxAllowance), M(i.TaxBorne) }.Select(EmployeeService.Csv))).Append("\r\n");
        audit.Record("hr.payroll.register_export", "pay_run", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["rows"] = items.Count.ToString() });
        await db.SaveChangesAsync(ct);
        return new ExportFile($"payroll-register-{run.Year}-{run.Month:D2}.csv", "text/csv; charset=utf-8", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray());
    }

    // ============================================================ payslips (self-service: your own, and only once the run is locked)
    /// <summary>The employee's own choice to be asked for their password before payslips are shown.</summary>
    public async Task<bool> SetPayslipLockAsync(bool locked, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var emp = await db.Employees.FirstOrDefaultAsync(e => e.UserId == actor.UserId, ct) ?? throw DomainException.Invariant("Your account is not linked to an employee record. Ask HR to link it.");
        if (!locked && emp.PayslipLocked) await GuardAsync(actor, ct);   // turning the lock off needs the password too
        emp.PayslipLocked = locked;
        audit.Record(locked ? "hr.payslip.lock_on" : "hr.payslip.lock_off", "employee", emp.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct); return locked;
    }

    public async Task<bool> PayslipLockedAsync(SessionPrincipal actor, CancellationToken ct = default)
        => await db.Employees.AsNoTracking().Where(e => e.UserId == actor.UserId).Select(e => e.PayslipLocked).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<PayslipRow>> MyPayslipsAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var emp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == actor.UserId, ct) ?? throw DomainException.Invariant("Your account is not linked to an employee record. Ask HR to link it.");
        if (emp.PayslipLocked) await GuardAsync(actor, ct);
        var rows = await (from i in db.PayRunItems.AsNoTracking() join p in db.PayRuns.AsNoTracking() on i.PayRunId equals p.Id
                          where i.EmployeeId == emp.Id && (p.Status == PayRunStatus.Locked || p.Status == PayRunStatus.Paid)
                          select new { i.Id, i.PayRunId, i.Gross, i.Net, p.Year, p.Month, p.Status }).ToListAsync(ct);
        return [.. rows.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).Select(r => new PayslipRow(r.Id, r.PayRunId, r.Year, r.Month, r.Status.ToString().ToLowerInvariant(), r.Gross, r.Net))];
    }

    public async Task<Payslip> MyPayslipAsync(Guid itemId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var emp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == actor.UserId, ct) ?? throw DomainException.NotFound("Payslip");
        if (emp.PayslipLocked) await GuardAsync(actor, ct);
        var item = await db.PayRunItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.EmployeeId == emp.Id, ct) ?? throw DomainException.NotFound("Payslip"); // someone else's slip looks like a missing one
        var run = await db.PayRuns.AsNoTracking().FirstAsync(p => p.Id == item.PayRunId, ct);
        if (run.Status is not (PayRunStatus.Locked or PayRunStatus.Paid)) throw DomainException.NotFound("Payslip");
        var lines = await db.PayRunLines.AsNoTracking().Where(l => l.PayRunId == run.Id && l.EmployeeId == emp.Id).OrderBy(l => l.CreatedAt).ToListAsync(ct);
        audit.Record("hr.payroll.payslip_viewed", "pay_run", run.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return new Payslip(run.Id, run.Year, run.Month, run.Status.ToString().ToLowerInvariant(), run.PaidAt, ItemView(item), [.. lines.Select(l => LineView(l, emp.FullName))]);
    }
}
