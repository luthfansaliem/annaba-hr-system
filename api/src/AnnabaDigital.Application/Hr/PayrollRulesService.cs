using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record RulesUpdateRequest(DateOnly? EffectiveFrom, string? Reason, decimal? KesEmployeeRate, decimal? KesEmployerRate, decimal? KesWageCap, decimal? JhtEmployeeRate, decimal? JhtEmployerRate,
    decimal? JpEmployeeRate, decimal? JpEmployerRate, decimal? JpWageCap, decimal? JkmEmployerRate, decimal? DefaultJkkRate, decimal? PositionCostRate, decimal? PositionCostMonthlyCap, decimal? NoNpwpSurcharge,
    Dictionary<string, decimal>? PtkpTable, List<TerBracket>? TerA, List<TerBracket>? TerB, List<TerBracket>? TerC, decimal? Pph26Rate = null);
public sealed record RuleVersionView(Guid Id, string Label, DateOnly EffectiveFrom, string Reason, string Changes, DateTimeOffset CreatedAt);
public sealed record RulesOverview(string Label, DateOnly? EffectiveFrom, PayrollRules Rules, IReadOnlyList<RuleVersionView> Versions);

/// <summary>
/// ADM-003. Rates, caps, tax-free allowances and TER tables as data. A correction is a new version that applies from the first day of a month that no locked pay run covers yet; a pay run keeps a snapshot of the rules
/// it was calculated with (and re-reads them on every recalculation until it is locked), so history is never rewritten. Needs payroll.configure and a recent password check; the change and the old/new values are audited.
/// </summary>
public sealed class PayrollRulesService(IAppDb db, IClock clock, SettingsProvider settings, AuditWriter audit)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    public static string Serialize(PayrollRules r) => JsonSerializer.Serialize(r, Json);
    public static PayrollRules Deserialize(string json) => JsonSerializer.Deserialize<PayrollRules>(json, Json) ?? PayrollRules.Default;

    /// <summary>The rules for a pay month: the newest version that started on or before it, else the built-in set.</summary>
    public async Task<PayrollRules> EffectiveForAsync(int year, int month, CancellationToken ct = default)
    {
        var first = new DateOnly(year, month, 1);
        var v = await db.PayrollRuleVersions.AsNoTracking().Where(x => x.EffectiveFrom <= first).OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
        return v is null ? PayrollRules.BuiltInFor(year, month) : Deserialize(v.RulesJson) with { Version = v.Label };
    }

    public async Task<RulesOverview> OverviewAsync(int? year, int? month, SessionPrincipal actor, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, (await settings.GetAsync(ct)) with { RequireReauthForCritical = true }, clock);
        var y = year ?? Today.Year; var m = month ?? Today.Month; var first = new DateOnly(y, m, 1);
        var rules = await EffectiveForAsync(y, m, ct);
        var versions = (await db.PayrollRuleVersions.AsNoTracking().OrderByDescending(x => x.EffectiveFrom).ToListAsync(ct)).Select(v => new RuleVersionView(v.Id, v.Label, v.EffectiveFrom, v.Reason, v.Changes, v.CreatedAt)).ToList();
        var cur = versions.FirstOrDefault(v => v.EffectiveFrom <= first);
        return new RulesOverview(rules.Version, cur?.EffectiveFrom, rules, versions);
    }

    public async Task<RulesOverview> CreateAsync(RulesUpdateRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, (await settings.GetAsync(ct)) with { RequireReauthForCritical = true }, clock);
        var errors = new Dictionary<string, string>();
        if (r.EffectiveFrom is null) errors["effectiveFrom"] = "Choose the first day of the month the change starts."; else if (r.EffectiveFrom.Value.Day != 1) errors["effectiveFrom"] = "A change always starts on the first day of a month.";
        var reason = r.Reason?.Trim(); if (string.IsNullOrEmpty(reason) || reason.Length > 300) errors["reason"] = "Say what changed and why (up to 300 characters, e.g. the regulation).";
        if (errors.Count > 0) throw DomainException.Validation("Please check the rules.", errors);
        var from = r.EffectiveFrom!.Value;
        var lastLocked = await db.PayRuns.AsNoTracking().Where(p => p.Status == PayRunStatus.Locked || p.Status == PayRunStatus.Paid).Select(p => (int?)(p.Year * 12 + p.Month)).MaxAsync(ct);
        if (lastLocked is { } ll && from.Year * 12 + from.Month <= ll) throw DomainException.Invariant("A locked pay run already covers that month. A change can only start after the last locked month.");
        if (await db.PayrollRuleVersions.AnyAsync(v => v.EffectiveFrom == from, ct)) throw DomainException.Conflict("There is already a rules version starting that month.");

        var cur = await EffectiveForAsync(from.Year, from.Month, ct); var changes = new List<string>();
        decimal Rate(string field, decimal? incoming, decimal old, decimal max)
        {
            if (incoming is null) return old;
            if (incoming < 0 || incoming > max) { errors[field] = $"Enter a value between 0 and {max}."; return old; }
            if (incoming != old) changes.Add(FormattableString.Invariant($"{field} {old}→{incoming}")); return incoming.Value;
        }
        var next = cur with
        {
            KesEmployeeRate = Rate("kesEmployeeRate", r.KesEmployeeRate, cur.KesEmployeeRate, 1), KesEmployerRate = Rate("kesEmployerRate", r.KesEmployerRate, cur.KesEmployerRate, 1), KesWageCap = Rate("kesWageCap", r.KesWageCap, cur.KesWageCap, 1_000_000_000),
            JhtEmployeeRate = Rate("jhtEmployeeRate", r.JhtEmployeeRate, cur.JhtEmployeeRate, 1), JhtEmployerRate = Rate("jhtEmployerRate", r.JhtEmployerRate, cur.JhtEmployerRate, 1),
            JpEmployeeRate = Rate("jpEmployeeRate", r.JpEmployeeRate, cur.JpEmployeeRate, 1), JpEmployerRate = Rate("jpEmployerRate", r.JpEmployerRate, cur.JpEmployerRate, 1), JpWageCap = Rate("jpWageCap", r.JpWageCap, cur.JpWageCap, 1_000_000_000),
            JkmEmployerRate = Rate("jkmEmployerRate", r.JkmEmployerRate, cur.JkmEmployerRate, 1), DefaultJkkRate = Rate("defaultJkkRate", r.DefaultJkkRate, cur.DefaultJkkRate, 0.05m), PositionCostRate = Rate("positionCostRate", r.PositionCostRate, cur.PositionCostRate, 1),
            PositionCostMonthlyCap = Rate("positionCostMonthlyCap", r.PositionCostMonthlyCap, cur.PositionCostMonthlyCap, 1_000_000_000), NoNpwpSurcharge = Rate("noNpwpSurcharge", r.NoNpwpSurcharge, cur.NoNpwpSurcharge, 1), Pph26Rate = Rate("pph26Rate", r.Pph26Rate, cur.Pph26Rate, 1),
        };
        if (r.PtkpTable is not null)
        {
            var keys = PayrollRules.DefaultPtkp.Keys;
            if (r.PtkpTable.Keys.Except(keys).Any() || keys.Except(r.PtkpTable.Keys).Any() || r.PtkpTable.Values.Any(v => v <= 0 || v > 10_000_000_000m)) errors["ptkpTable"] = "Give a positive amount for each of: " + string.Join(", ", keys) + ".";
            else { next = next with { PtkpTable = new Dictionary<string, decimal>(r.PtkpTable) }; foreach (var k in keys.Where(k => r.PtkpTable[k] != cur.PtkpTable[k])) changes.Add(FormattableString.Invariant($"PTKP {k} {cur.PtkpTable[k]:0.##}→{r.PtkpTable[k]:0.##}")); }
        }
        List<TerBracket>? Ter(string field, List<TerBracket>? incoming, List<TerBracket> old)
        {
            if (incoming is null) return null;
            var norm = incoming.Select(b => b.Upto >= 1e15m ? b with { Upto = decimal.MaxValue } : b).ToList();
            if (norm.Count < 2 || norm[^1].Upto != decimal.MaxValue || norm.Zip(norm.Skip(1), (a, b) => b.Upto > a.Upto).Any(ok => !ok) || norm.Any(b => b.Upto <= 0 || b.Pct is < 0 or > 100)) { errors[field] = "Give brackets in ascending order, rates between 0 and 100 %, and end with a bracket without an upper limit."; return null; }
            if (!norm.SequenceEqual(old)) changes.Add($"{field} replaced ({old.Count}→{norm.Count} brackets)"); return norm;
        }
        if (Ter("terA", r.TerA, cur.TerA) is { } a) next = next with { TerA = a }; if (Ter("terB", r.TerB, cur.TerB) is { } b) next = next with { TerB = b }; if (Ter("terC", r.TerC, cur.TerC) is { } c) next = next with { TerC = c };
        if (errors.Count > 0) throw DomainException.Validation("Please check the rules.", errors);
        if (changes.Count == 0) throw DomainException.Validation("Nothing changed.", new Dictionary<string, string> { ["reason"] = "Change at least one value." });

        var n = await db.PayrollRuleVersions.CountAsync(ct) + 1; var label = $"{PayrollRules.Default.Version}+{n}";
        var v = new PayrollRuleVersion { Label = label, EffectiveFrom = from, RulesJson = Serialize(next with { Version = label }), Reason = reason!, Changes = string.Join("; ", changes) is { Length: > 1000 } ch ? ch[..1000] : string.Join("; ", changes), CreatedAt = clock.UtcNow, CreatedBy = actor.UserId };
        db.PayrollRuleVersions.Add(v);
        audit.Record("hr.payroll.rules_change", "payroll", label, Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["effectiveFrom"] = from.ToString("yyyy-MM-dd"), ["reason"] = reason!, ["changes"] = v.Changes.Length > 500 ? v.Changes[..500] : v.Changes });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ResetTracking(); throw DomainException.Conflict("There is already a rules version starting that month."); }
        return await OverviewAsync(from.Year, from.Month, actor, ct);
    }
}
