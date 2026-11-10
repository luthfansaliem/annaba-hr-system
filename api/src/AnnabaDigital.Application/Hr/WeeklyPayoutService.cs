using System.Text;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record PayoutSummary(Guid Id, Guid LegalEntityId, string? LegalEntityName, DateOnly WeekStart, DateOnly WeekEnd, decimal HoldbackPct, string Status, int EmployeeCount, decimal TotalGross, decimal TotalNet,
    string? Note, DateTimeOffset CreatedAt, bool CanCalculate, bool CanApprove, bool CanMarkPaid);
public sealed record PayoutItemView(Guid Id, Guid EmployeeId, string EmployeeNo, string EmployeeName, decimal DailyRate, decimal Days, decimal Gross, decimal Holdback, decimal Net, bool Settled);
public sealed record PayoutDetail(PayoutSummary Payout, IReadOnlyList<PayoutItemView> Items);
public sealed record CreatePayoutRequest(DateOnly? WeekStart, Guid? LegalEntityId, decimal? HoldbackPct, string? Note);
public sealed record MyPayoutRow(Guid Id, DateOnly WeekStart, DateOnly WeekEnd, decimal Days, decimal Gross, decimal Holdback, decimal Net, string Status, bool Settled);

/// <summary>
/// PAY-013. Weekly pay for daily-wage workers, as interim payments that the monthly pay run settles.
/// A payout covers one company and one Monday–Sunday week: each daily-wage worker gets rate × days worked that week, minus a holdback (default 10 %) kept for tax and BPJS.
/// Tax, BPJS and the year-end true-up stay monthly: the next monthly run of that company computes the whole month as usual and deducts every weekly payout not yet settled,
/// so the worker's month adds up to exactly the monthly result, paid in weekly parts. Preparing and approving are done by different people; an approved payout cannot change
/// (service and database trigger); locking the monthly run marks the payouts it deducted as settled, so none is deducted twice.
/// </summary>
public sealed class WeeklyPayoutService(IAppDb db, IClock clock, PayrollService payroll, AuthorizationService authz, AuditWriter audit, ShiftService shifts, LegalEntityService entities, NotificationService notes, ISecretProtector protector)
{
    public const decimal DefaultHoldbackPct = 10m;
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    private PayoutSummary Summary(WeeklyPayout p, SessionPrincipal actor, IReadOnlySet<string> perms, IReadOnlyDictionary<Guid, string> names) => new(p.Id, p.LegalEntityId, names.GetValueOrDefault(p.LegalEntityId), p.WeekStart, p.WeekEnd, p.HoldbackPct,
        p.Status.ToString().ToLowerInvariant(), p.EmployeeCount, p.TotalGross, p.TotalNet, p.Note, p.CreatedAt,
        CanCalculate: perms.Contains(Permissions.PayrollRun) && p.Status is PayoutStatus.Draft or PayoutStatus.Calculated,
        CanApprove: perms.Contains(Permissions.PayrollApprove) && p.Status == PayoutStatus.Calculated && p.CalculatedBy != actor.UserId,
        CanMarkPaid: perms.Contains(Permissions.PayrollLock) && p.Status == PayoutStatus.Approved);

    private async Task<PayoutSummary> SummaryAsync(WeeklyPayout p, SessionPrincipal actor, CancellationToken ct)
        => Summary(p, actor, await authz.GetEffectivePermissionsAsync(actor.UserId, ct), await db.LegalEntities.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct));

    public async Task<IReadOnlyList<PayoutSummary>> ListAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct); var names = await db.LegalEntities.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return [.. (await db.WeeklyPayouts.AsNoTracking().OrderByDescending(p => p.WeekStart).Take(120).ToListAsync(ct)).Select(p => Summary(p, actor, perms, names))];
    }

    public async Task<PayoutDetail> GetAsync(Guid id, SessionPrincipal actor, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var p = await db.WeeklyPayouts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Weekly payout");
        var items = await db.WeeklyPayoutItems.AsNoTracking().Where(i => i.PayoutId == id).OrderBy(i => i.EmployeeName).ToListAsync(ct);
        return new PayoutDetail(await SummaryAsync(p, actor, ct), [.. items.Select(i => new PayoutItemView(i.Id, i.EmployeeId, i.EmployeeNo, i.EmployeeName, i.DailyRate, i.Days, i.Gross, i.Holdback, i.Net, i.SettledRunId != null))]);
    }

    public async Task<PayoutSummary> CreateAsync(CreatePayoutRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var errors = new Dictionary<string, string>();
        if (r.WeekStart is not { } start) errors["weekStart"] = "Choose the Monday the week starts on.";
        else if (start.DayOfWeek != DayOfWeek.Monday) errors["weekStart"] = "A pay week starts on a Monday.";
        else if (start > Today) errors["weekStart"] = "That week has not started yet."; else if (start < Today.AddDays(-92)) errors["weekStart"] = "That week is more than three months back.";
        var holdback = r.HoldbackPct ?? DefaultHoldbackPct; if (holdback is < 0 or > 50) errors["holdbackPct"] = "Enter 0–50 %.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the weekly payout.", errors);
        var entityId = await entities.ResolveAsync(r.LegalEntityId, ct); var from = r.WeekStart!.Value;
        if (await db.WeeklyPayouts.AnyAsync(p => p.LegalEntityId == entityId && p.WeekStart == from, ct)) throw DomainException.Conflict("There is already a payout for that company and week.");
        var payout = new WeeklyPayout { LegalEntityId = entityId, WeekStart = from, WeekEnd = from.AddDays(6), HoldbackPct = holdback, Note = r.Note?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 300)] : null, CreatedBy = actor.UserId, CreatedAt = clock.UtcNow };
        db.WeeklyPayouts.Add(payout);
        audit.Record("hr.payout.create", "weekly_payout", payout.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["week"] = from.ToString("yyyy-MM-dd") });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ResetTracking(); throw DomainException.Conflict("There is already a payout for that company and week."); }
        return await SummaryAsync(payout, actor, ct);
    }

    public async Task DeleteAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var p = await db.WeeklyPayouts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Weekly payout");
        if (p.Status is not (PayoutStatus.Draft or PayoutStatus.Calculated)) throw DomainException.Invariant("An approved payout cannot be deleted.");
        db.WeeklyPayouts.Remove(p);
        audit.Record("hr.payout.delete", "weekly_payout", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["week"] = p.WeekStart.ToString("yyyy-MM-dd") });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Days paid in a period: days with attendance plus approved paid-leave working days (the same counting the monthly run uses for daily-wage workers).</summary>
    internal static decimal DaysPaid(Guid employeeId, DateOnly from, DateOnly to, IReadOnlySet<DateOnly> present, IEnumerable<LeaveRequest> paidLeave, Func<DateOnly, bool> working)
    {
        var days = (decimal)present.Count(d => d >= from && d <= to);
        foreach (var l in paidLeave.Where(x => x.EmployeeId == employeeId))
            for (var d = l.StartDate > from ? l.StartDate : from; d <= (l.EndDate < to ? l.EndDate : to); d = d.AddDays(1)) if (working(d) && !present.Contains(d)) days += l.HalfDay ? 0.5m : 1m;
        return days;
    }

    public async Task<PayoutDetail> CalculateAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        await db.InTransactionAsync(async () =>
        {
            await db.ExecuteSqlAsync($"SELECT id FROM hr_weekly_payouts WHERE id = {id} FOR UPDATE", ct);
            var p = await db.WeeklyPayouts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Weekly payout");
            if (p.Status is not (PayoutStatus.Draft or PayoutStatus.Calculated)) throw DomainException.Invariant("This payout is already approved.");
            if (p.WeekEnd > Today) throw DomainException.Invariant("The week is not over yet. Calculate after its last day so every day worked is counted.");
            var start = p.WeekStart; var end = p.WeekEnd;
            await db.WeeklyPayoutItems.Where(i => i.PayoutId == id).ExecuteDeleteAsync(ct);
            var employees = await db.Employees.AsNoTracking().Where(e => e.LegalEntityId == p.LegalEntityId && e.HireDate <= end && (e.Status == EmployeeStatus.Active || e.TerminationDate >= start)).OrderBy(e => e.FullName).ToListAsync(ct);
            var ids = employees.Select(e => e.Id).ToList();
            var comps = await db.Compensations.AsNoTracking().Where(c => ids.Contains(c.EmployeeId) && c.PayBasis == PayBasis.Daily && c.EffectiveFrom <= end && (c.EffectiveTo == null || c.EffectiveTo >= start)).ToListAsync(ct);
            var attended = (await db.AttendanceLogs.AsNoTracking().Where(l => ids.Contains(l.EmployeeId) && l.Date >= start && l.Date <= end).Select(l => new { l.EmployeeId, l.Date }).ToListAsync(ct)).GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.Select(x => x.Date).ToHashSet());
            var paidLeave = await db.LeaveRequests.AsNoTracking().Where(l => ids.Contains(l.EmployeeId) && l.Status == LeaveStatus.Approved && l.StartDate <= end && l.EndDate >= start && db.LeaveTypes.Any(t => t.Id == l.LeaveTypeId && t.Paid)).ToListAsync(ct);
            var holidays = (await db.Holidays.AsNoTracking().Where(h => h.Date >= start && h.Date <= end).Select(h => h.Date).ToListAsync(ct)).ToHashSet();
            var lookup = await shifts.LookupAsync(ids, start, end, ct);
            var items = new List<WeeklyPayoutItem>();
            foreach (var e in employees)
            {
                var asOf = e.TerminationDate is { } td && td < end ? td : end;
                var c = comps.Where(x => x.EmployeeId == e.Id && x.EffectiveFrom <= asOf && (x.EffectiveTo == null || x.EffectiveTo >= asOf)).OrderByDescending(x => x.EffectiveFrom).FirstOrDefault();
                if (c is null) continue;                                                                    // not a daily-wage worker this week
                var from = e.HireDate > start ? e.HireDate : start; var to = asOf;
                var days = DaysPaid(e.Id, from, to, attended.GetValueOrDefault(e.Id) ?? [], paidLeave, d => lookup.Works(e.Id, d) && !holidays.Contains(d));
                if (days <= 0) continue;
                var (rate, allowance) = payroll.RevealPay(c);
                var gross = PayrollRules.Rupiah((rate + allowance) * days); var held = PayrollRules.Rupiah(gross * p.HoldbackPct / 100m);
                items.Add(new WeeklyPayoutItem { PayoutId = id, EmployeeId = e.Id, EmployeeNo = e.EmployeeNo, EmployeeName = e.FullName, DailyRate = rate + allowance, Days = days, Gross = gross, Holdback = held, Net = gross - held });
            }
            db.WeeklyPayoutItems.AddRange(items);
            p.EmployeeCount = items.Count; p.TotalGross = items.Sum(i => i.Gross); p.TotalNet = items.Sum(i => i.Net);
            p.Status = PayoutStatus.Calculated; p.CalculatedBy = actor.UserId; p.CalculatedAt = clock.UtcNow; p.Version++;
            audit.Record("hr.payout.calculate", "weekly_payout", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["week"] = start.ToString("yyyy-MM-dd"), ["employees"] = items.Count.ToString() });
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        return await GetAsync(id, actor, ct);
    }

    public async Task<PayoutSummary> ApproveAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var p = await db.WeeklyPayouts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Weekly payout");
        if (p.Status != PayoutStatus.Calculated) throw DomainException.Conflict("Only a calculated payout can be approved.");
        if (p.CalculatedBy == actor.UserId)
        {
            audit.Record("hr.payroll.denied", "weekly_payout", id.ToString(), Outcome.Denied, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["rule"] = "maker-checker" }); await db.SaveChangesAsync(ct);
            throw DomainException.Invariant("The person who calculated a payout cannot approve it.");
        }
        if (p.EmployeeCount == 0) throw DomainException.Invariant("Nobody is paid in this payout. Delete it instead.");
        p.Status = PayoutStatus.Approved; p.ApprovedBy = actor.UserId; p.ApprovedAt = clock.UtcNow; p.Version++;
        var week = $"{p.WeekStart:d MMM} – {p.WeekEnd:d MMM yyyy}";
        foreach (var uid in await (from i in db.WeeklyPayoutItems.AsNoTracking() join e in db.Employees.AsNoTracking() on i.EmployeeId equals e.Id where i.PayoutId == id && e.UserId != null select e.UserId!.Value).ToListAsync(ct))
            notes.Notify(uid, "payslip.weekly", $"Your weekly pay for {week} was approved", null, "/hr/my-payslips");
        audit.Record("hr.payout.approve", "weekly_payout", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["week"] = p.WeekStart.ToString("yyyy-MM-dd"), ["net"] = p.TotalNet.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { db.ResetTracking(); throw DomainException.Conflict("This payout was changed by someone else. Reload and try again."); }
        return await SummaryAsync(p, actor, ct);
    }

    public async Task<PayoutSummary> MarkPaidAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var p = await db.WeeklyPayouts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Weekly payout");
        if (p.Status != PayoutStatus.Approved) throw DomainException.Conflict("Only an approved payout can be marked as paid.");
        p.Status = PayoutStatus.Paid; p.PaidAt = clock.UtcNow; p.Version++;
        audit.Record("hr.payout.paid", "weekly_payout", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["week"] = p.WeekStart.ToString("yyyy-MM-dd") });
        await db.SaveChangesAsync(ct);
        return await SummaryAsync(p, actor, ct);
    }

    public async Task<ExportFile> BankFileAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var p = await db.WeeklyPayouts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Weekly payout");
        if (p.Status is not (PayoutStatus.Approved or PayoutStatus.Paid)) throw DomainException.Invariant("The bank file is available once the payout is approved.");
        var items = await db.WeeklyPayoutItems.AsNoTracking().Where(i => i.PayoutId == id && i.Net > 0).OrderBy(i => i.EmployeeNo).ToListAsync(ct);
        var ids = items.Select(i => i.EmployeeId).ToList(); var emps = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var sb = new StringBuilder("employee_no,name,bank,account_no,amount,reference\r\n"); var skipped = 0;
        foreach (var i in items)
        {
            var e = emps[i.EmployeeId]; if (e.BankAccountEnc is null) { skipped++; continue; }
            var parts = e.BankAccountEnc.Split('.');
            var account = Encoding.UTF8.GetString(protector.Unprotect(new ProtectedSecret(Convert.FromBase64String(parts[2]), Convert.FromBase64String(parts[1]), int.Parse(parts[0])), Encoding.UTF8.GetBytes($"employee:{e.Id}:bank_account")));
            sb.Append(string.Join(',', new[] { i.EmployeeNo, i.EmployeeName, e.BankName, account, i.Net.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), $"Upah minggu {p.WeekStart:yyyy-MM-dd}" }.Select(EmployeeService.Csv))).Append("\r\n");
        }
        audit.Record("hr.payout.bank_file", "weekly_payout", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["rows"] = (items.Count - skipped).ToString(), ["skippedNoAccount"] = skipped.ToString() });
        await db.SaveChangesAsync(ct);
        return new ExportFile($"weekly-payout-{p.WeekStart:yyyy-MM-dd}.csv", "text/csv; charset=utf-8", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())]);
    }

    /// <summary>The caller's own weekly pay (approved or paid payouts only).</summary>
    public async Task<IReadOnlyList<MyPayoutRow>> MineAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var emp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == actor.UserId, ct) ?? throw DomainException.Invariant("Your account is not linked to an employee record. Ask HR to link it.");
        var rows = await (from i in db.WeeklyPayoutItems.AsNoTracking() join p in db.WeeklyPayouts.AsNoTracking() on i.PayoutId equals p.Id
                          where i.EmployeeId == emp.Id && (p.Status == PayoutStatus.Approved || p.Status == PayoutStatus.Paid) orderby p.WeekStart descending
                          select new { i.Id, p.WeekStart, p.WeekEnd, i.Days, i.Gross, i.Holdback, i.Net, p.Status, i.SettledRunId }).Take(60).ToListAsync(ct);
        return [.. rows.Select(r => new MyPayoutRow(r.Id, r.WeekStart, r.WeekEnd, r.Days, r.Gross, r.Holdback, r.Net, r.Status.ToString().ToLowerInvariant(), r.SettledRunId != null))];
    }
}
