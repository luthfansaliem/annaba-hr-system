using System.Globalization;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Hr;

public sealed record CompareRequest(string? Csv, decimal? Tolerance);
public sealed record CompareAmount(decimal? System, decimal? Legacy, decimal? Difference);
public sealed record CompareRow(string EmployeeNo, string? Name, string Status, CompareAmount Gross, CompareAmount Bpjs, CompareAmount Tax, CompareAmount Net);
public sealed record CompareResult(int Matched, int Different, int OnlyInSystem, int OnlyInLegacy, decimal Tolerance, IReadOnlyList<string> ComparedFields, CompareAmount Gross, CompareAmount Bpjs, CompareAmount Tax, CompareAmount Net, IReadOnlyList<CompareRow> Rows);

/// <summary>
/// Parallel run: the same month is paid by the old payroll and calculated here, and the two results are laid side by side per employee (PRD §11: two cycles without unexplained differences).
/// The old payroll's figures come in as a CSV file and are compared in memory; nothing of the file is stored. Columns are found by their heading, in English or Indonesian;
/// only the columns that are present are compared.
/// </summary>
public sealed class ParallelRunService(IAppDb db, PayrollService payroll, AuditWriter audit)
{
    private static readonly Dictionary<string, string[]> Headings = new()
    {
        ["employee_no"] = ["employee_no", "employee no", "employee number", "employee_id", "nik", "nik karyawan", "no karyawan", "no_karyawan", "nomor karyawan", "nip", "id karyawan"],
        ["gross"] = ["gross", "gross pay", "bruto", "penghasilan bruto", "gaji kotor", "total pendapatan", "total penghasilan"],
        ["bpjs"] = ["bpjs", "bpjs_employee", "bpjs karyawan", "potongan bpjs", "bpjs ee", "iuran bpjs karyawan"],
        ["tax"] = ["tax", "pph21", "pph 21", "pph", "pajak", "income tax"],
        ["net"] = ["net", "net pay", "take home pay", "take-home pay", "thp", "netto", "gaji bersih", "penghasilan bersih", "dibayarkan"],
    };

    /// <summary>"9.338.650", "9,338,650.00", "Rp 9.338.650,00" and "9338650" all mean the same amount; an empty cell means "not given".</summary>
    public static decimal? ParseAmount(string? raw)
    {
        var s = new string((raw ?? "").Where(c => char.IsDigit(c) || c is '.' or ',' or '-' or '(').ToArray()); if (!s.Any(char.IsDigit)) return null;
        var negative = s.Contains('-') || s.Contains('('); s = s.Replace("-", "").Replace("(", "");
        var dot = s.LastIndexOf('.'); var comma = s.LastIndexOf(',');
        char? dec = null;
        if (dot >= 0 && comma >= 0) dec = dot > comma ? '.' : ',';                                           // both: the last one is the decimal mark
        else if (dot >= 0 || comma >= 0)
        {
            var mark = dot >= 0 ? '.' : ','; var parts = s.Split(mark);
            if (!(parts.Length > 1 && parts.Skip(1).All(p => p.Length == 3) && parts[0].Length is >= 1 and <= 3)) dec = mark;   // groups of three are thousands; anything else is a decimal mark
        }
        var clean = new string(s.Where(c => char.IsDigit(c) || c == dec).ToArray()); if (dec is { } d) clean = clean.Replace(d, '.');
        return decimal.TryParse(clean, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? (negative ? -v : v) : null;
    }

    private static List<string> Cells(string line, char sep)
    {
        var cells = new List<string>(); var cur = new System.Text.StringBuilder(); var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted) { if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else if (c == '"') quoted = false; else cur.Append(c); }
            else if (c == '"') quoted = true;
            else if (c == sep) { cells.Add(cur.ToString().Trim()); cur.Clear(); }
            else cur.Append(c);
        }
        cells.Add(cur.ToString().Trim()); return cells;
    }

    public async Task<CompareResult> CompareAsync(Guid runId, CompareRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await payroll.GuardAsync(actor, ct);                                                                 // pay data: a recent password check, like the register
        var run = await db.PayRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == runId, ct) ?? throw DomainException.NotFound("Pay run");
        var csv = (req.Csv ?? "").TrimStart('﻿'); var tolerance = req.Tolerance ?? 1m;
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(csv)) errors["csv"] = "Choose the file exported from the old payroll.";
        else if (csv.Length > 2_000_000) errors["csv"] = "The file is larger than 2 MB.";
        if (tolerance is < 0 or > 1_000_000) errors["tolerance"] = "Between 0 and 1,000,000.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the file.", errors);

        var lines = csv.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToList();
        var sep = lines[0].Count(c => c == ';') > lines[0].Count(c => c == ',') ? ';' : lines[0].Contains('\t') && !lines[0].Contains(',') ? '\t' : ',';
        var head = Cells(lines[0], sep).Select(h => h.ToLowerInvariant().Replace('_', ' ').Trim()).ToList();
        int Col(string field) => head.FindIndex(h => Headings[field].Any(a => a.Replace('_', ' ') == h));
        var cNo = Col("employee_no"); var cols = new[] { "gross", "bpjs", "tax", "net" }.ToDictionary(f => f, Col);
        if (cNo < 0) throw DomainException.Validation("Please check the file.", new Dictionary<string, string> { ["csv"] = "No employee number column found (expected a heading such as employee_no or NIK)." });
        var compared = cols.Where(c => c.Value >= 0).Select(c => c.Key).ToList();
        if (compared.Count == 0) throw DomainException.Validation("Please check the file.", new Dictionary<string, string> { ["csv"] = "No amount column found (expected headings such as gross, bpjs, pph21, net)." });
        if (lines.Count > 20_001) throw DomainException.Validation("Please check the file.", new Dictionary<string, string> { ["csv"] = "More than 20,000 rows." });

        var legacy = new Dictionary<string, Dictionary<string, decimal?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var cells = Cells(line, sep); var no = cNo < cells.Count ? cells[cNo] : ""; if (no.Length == 0) continue;
            if (legacy.ContainsKey(no)) throw DomainException.Validation("Please check the file.", new Dictionary<string, string> { ["csv"] = $"Employee number {no} appears twice in the file." });
            legacy[no] = compared.ToDictionary(f => f, f => cols[f] < cells.Count ? ParseAmount(cells[cols[f]]) : null);
        }

        var items = await db.PayRunItems.AsNoTracking().Where(i => i.PayRunId == runId).OrderBy(i => i.EmployeeNo).ToListAsync(ct);
        CompareAmount Pair(string field, decimal? sys, Dictionary<string, decimal?>? old)
        {
            if (!compared.Contains(field)) return new CompareAmount(sys, null, null);
            decimal? l = old is not null && old.TryGetValue(field, out var v) ? v : null;
            return new CompareAmount(sys, l, sys is { } a && l is { } b ? a - b : null);
        }
        bool Off(CompareAmount a) => a.Difference is { } d && Math.Abs(d) > tolerance;
        var rows = new List<CompareRow>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in items)
        {
            seen.Add(i.EmployeeNo); legacy.TryGetValue(i.EmployeeNo, out var old);
            var g = Pair("gross", i.Gross, old); var b = Pair("bpjs", i.KesEmployee + i.JhtEmployee + i.JpEmployee, old); var t = Pair("tax", i.Tax, old); var n = Pair("net", i.Net, old);
            rows.Add(new CompareRow(i.EmployeeNo, i.EmployeeName, old is null ? "only_system" : Off(g) || Off(b) || Off(t) || Off(n) ? "different" : "match", g, b, t, n));
        }
        foreach (var (no, old) in legacy.Where(l => !seen.Contains(l.Key)))
            rows.Add(new CompareRow(no, null, "only_legacy", Pair("gross", null, old), Pair("bpjs", null, old), Pair("tax", null, old), Pair("net", null, old)));

        CompareAmount Total(Func<CompareRow, CompareAmount> pick)
        {
            var both = rows.Where(r => r.Status is "match" or "different").Select(pick).Where(a => a.Legacy is not null).ToList();
            return both.Count == 0 ? new CompareAmount(null, null, null) : new CompareAmount(both.Sum(a => a.System ?? 0), both.Sum(a => a.Legacy ?? 0), both.Sum(a => a.Difference ?? 0));
        }
        var order = new Dictionary<string, int> { ["different"] = 0, ["only_system"] = 1, ["only_legacy"] = 2, ["match"] = 3 };
        var result = new CompareResult(rows.Count(r => r.Status == "match"), rows.Count(r => r.Status == "different"), rows.Count(r => r.Status == "only_system"), rows.Count(r => r.Status == "only_legacy"),
            tolerance, compared, Total(r => r.Gross), Total(r => r.Bpjs), Total(r => r.Tax), Total(r => r.Net), [.. rows.OrderBy(r => order[r.Status]).ThenBy(r => r.EmployeeNo)]);
        // who compared what and how it came out; the amounts themselves are not written to the audit trail
        audit.Record("hr.payroll.parallel_compare", "pay_run", runId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string>
        {
            ["period"] = $"{run.Year}-{run.Month:D2}", ["matched"] = result.Matched.ToString(), ["different"] = result.Different.ToString(), ["onlyInSystem"] = result.OnlyInSystem.ToString(), ["onlyInLegacy"] = result.OnlyInLegacy.ToString(), ["tolerance"] = tolerance.ToString(CultureInfo.InvariantCulture),
        });
        await db.SaveChangesAsync(ct);
        return result;
    }
}
