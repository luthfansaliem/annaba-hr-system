using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record RecurringPayView(Guid Id, Guid EmployeeId, string EmployeeNo, string EmployeeName, string Kind, string Label, decimal Amount, bool Taxable, DateOnly From, DateOnly? Through, bool Ended, DateTimeOffset CreatedAt);
public sealed record RecurringPayRequest(Guid? EmployeeId, string? Kind, string? Label, decimal? Amount, bool? Taxable, DateOnly? From, DateOnly? Through);

/// <summary>
/// PAY-001. Pay components that repeat every month for one person: a transport or meal allowance, a position allowance, a union fee, a cooperative deduction.
/// Each is added to every pay run from its first month to its last (or until it is ended) as a system line, rebuilt on every calculation, so it can never be forgotten or typed twice.
/// An earning is taxable unless marked otherwise; a deduction never changes tax. The amount is not pro-rated for a partial month. Needs payroll.configure and a recent password check.
/// </summary>
public sealed class RecurringPayService(IAppDb db, IClock clock, PayrollService payroll, AuditWriter audit)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));
    private static DateOnly Month(DateOnly d) => new(d.Year, d.Month, 1);

    public async Task<IReadOnlyList<RecurringPayView>> ListAsync(bool activeOnly, SessionPrincipal actor, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var q = db.RecurringPayItems.AsNoTracking().AsQueryable(); if (activeOnly) q = q.Where(x => x.EndedAt == null);
        var rows = await q.OrderByDescending(x => x.CreatedAt).ToListAsync(ct); var ids = rows.Select(x => x.EmployeeId).Distinct().ToList();
        var emps = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.EmployeeNo, e.FullName }).ToDictionaryAsync(e => e.Id, ct);
        return [.. rows.Select(x => View(x, emps[x.EmployeeId].EmployeeNo, emps[x.EmployeeId].FullName))];
    }

    private static RecurringPayView View(RecurringPayItem x, string no, string name) => new(x.Id, x.EmployeeId, no, name, x.Kind.ToString().ToLowerInvariant(), x.Label, x.Amount, x.Taxable, x.From, x.Through, x.EndedAt != null, x.CreatedAt);

    public async Task<RecurringPayView> CreateAsync(RecurringPayRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var errors = new Dictionary<string, string>();
        var emp = r.EmployeeId is { } id ? await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id && e.Status == EmployeeStatus.Active, ct) : null; if (emp is null) errors["employeeId"] = "Choose an active employee.";
        if (!Enum.TryParse<PayLineKind>(r.Kind, true, out var kind) || !Enum.IsDefined(kind)) errors["kind"] = "Choose earning or deduction.";
        var label = r.Label?.Trim(); if (string.IsNullOrEmpty(label) || label.Length > 100) errors["label"] = "Enter a label (up to 100 characters).";
        if (r.Amount is null or <= 0 or > 10_000_000_000m) errors["amount"] = "Enter an amount above 0.";
        var from = Month(r.From ?? Today); if (from > Month(Today).AddMonths(12)) errors["from"] = "That month is too far ahead.";
        DateOnly? through = r.Through is { } th ? Month(th) : null; if (through < from) errors["through"] = "The last month is before the first.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the recurring item.", errors);
        if (await db.RecurringPayItems.CountAsync(x => x.EmployeeId == emp!.Id && x.EndedAt == null, ct) >= 20) throw DomainException.Invariant("An employee can have at most 20 active recurring items.");
        var item = new RecurringPayItem { EmployeeId = emp!.Id, Kind = kind, Label = label!, Amount = PayrollRules.Rupiah(r.Amount!.Value), Taxable = kind == PayLineKind.Earning && (r.Taxable ?? true), From = from, Through = through, CreatedAt = clock.UtcNow, CreatedBy = actor.UserId };
        db.RecurringPayItems.Add(item);
        audit.Record("hr.payroll.recurring_create", "employee", emp.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeNo"] = emp.EmployeeNo, ["kind"] = kind.ToString(), ["label"] = label!, ["from"] = from.ToString("yyyy-MM") });
        await db.SaveChangesAsync(ct);
        return View(item, emp.EmployeeNo, emp.FullName);
    }

    /// <summary>Stops the item: it no longer appears when a pay run is calculated. Locked pay runs that included it are untouched.</summary>
    public async Task EndAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);
        var x = await db.RecurringPayItems.FirstOrDefaultAsync(i => i.Id == id && i.EndedAt == null, ct) ?? throw DomainException.NotFound("Recurring item");
        x.EndedAt = clock.UtcNow;
        audit.Record("hr.payroll.recurring_end", "employee", x.EmployeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["label"] = x.Label });
        await db.SaveChangesAsync(ct);
    }
}
