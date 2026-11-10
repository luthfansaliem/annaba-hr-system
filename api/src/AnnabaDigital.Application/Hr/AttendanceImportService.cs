using System.Globalization;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Hr;

public sealed record ImportAttendanceRequest(string? Csv, bool DryRun = true);
public sealed record ImportAttendanceRow(int Line, string? EmployeeNo, DateOnly? Date, string Status, string? Message);
public sealed record ImportAttendanceResult(bool DryRun, int Punches, int Days, int Imported, int Skipped, int Failed, IReadOnlyList<ImportAttendanceRow> Rows);

/// <summary>
/// ATT-003. Turns a fingerprint/access-machine export (employee number + date and time of each punch, local time) into attendance: per person and day the first punch is the clock-in and
/// the last is the clock-out. A day that already has attendance is left alone, a day with a single punch needs a correction instead, closed months are refused, and the whole import is
/// reported per day. Needs the attendance management permission and a recent password check, and is audited.
/// </summary>
public sealed class AttendanceImportService(IAppDb db, IClock clock, SettingsProvider settings, AuditWriter audit)
{
    public const int MaxLines = 20_000;
    public const int MaxChars = 2_000_000;
    private static readonly TimeSpan Wib = TimeSpan.FromHours(7);
    private static readonly string[] Formats = ["yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"];

    public async Task<ImportAttendanceResult> ImportAsync(ImportAttendanceRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, (await settings.GetAsync(ct)) with { RequireReauthForCritical = true }, clock);
        var csv = req.Csv ?? "";
        if (csv.Length > MaxChars) throw DomainException.Validation("The file is too large.", new Dictionary<string, string> { ["csv"] = $"Use at most {MaxChars / 1_000_000} MB." });
        var table = UserImportService.ParseCsv(csv);
        if (table.Count == 0) throw DomainException.Validation("The file is empty.", new Dictionary<string, string> { ["csv"] = "Add a header row and the punches." });
        var header = table[0].Select(h => new string(h.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant()).ToList();
        int cNo = header.FindIndex(h => h is "employeeno" or "employeenumber" or "employeeid" or "nip" or "pin"), cTime = header.FindIndex(h => h is "datetime" or "timestamp" or "time" or "punch" or "punchtime");
        if (cNo < 0 || cTime < 0) throw DomainException.Validation("Missing columns.", new Dictionary<string, string> { ["csv"] = "The first row must contain employee_no and datetime." });
        var data = table.Skip(1).Select((r, i) => (Line: i + 2, Row: r)).Where(x => x.Row.Any(c => c.Trim().Length > 0)).ToList();
        if (data.Count == 0) throw DomainException.Validation("No punches found.", new Dictionary<string, string> { ["csv"] = "Add at least one row under the header." });
        if (data.Count > MaxLines) throw DomainException.Validation("Too many rows.", new Dictionary<string, string> { ["csv"] = $"Import at most {MaxLines} punches at a time." });

        var emps = await db.Employees.AsNoTracking().Select(e => new { e.Id, e.EmployeeNo }).ToDictionaryAsync(e => e.EmployeeNo, e => e.Id, StringComparer.OrdinalIgnoreCase, ct);
        var rows = new List<ImportAttendanceRow>(); var punches = new Dictionary<(Guid Emp, string No, DateOnly Day), List<DateTime>>();
        foreach (var (line, r) in data)
        {
            var no = cNo < r.Count ? r[cNo].Trim() : ""; var raw = cTime < r.Count ? r[cTime].Trim() : "";
            if (!emps.TryGetValue(no, out var id)) { rows.Add(new(line, no, null, "error", $"Employee '{no}' does not exist.")); continue; }
            if (!DateTime.TryParseExact(raw, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) { rows.Add(new(line, no, null, "error", "The date and time are not valid (use yyyy-mm-dd hh:mm).")); continue; }
            var key = (id, no, DateOnly.FromDateTime(t)); if (!punches.TryGetValue(key, out var list)) punches[key] = list = []; list.Add(t);
        }

        var closed = (await db.AttendancePeriods.AsNoTracking().Select(p => new { p.Year, p.Month }).ToListAsync(ct)).Select(p => (p.Year, p.Month)).ToHashSet();
        var days = punches.Keys.Select(k => k.Emp).Distinct().ToList(); var min = punches.Keys.Min(k => k.Day); var max = punches.Keys.Max(k => k.Day);
        var have = (await db.AttendanceLogs.AsNoTracking().Where(l => days.Contains(l.EmployeeId) && l.Date >= min && l.Date <= max).Select(l => new { l.EmployeeId, l.Date }).ToListAsync(ct)).Select(l => (l.EmployeeId, l.Date)).ToHashSet();
        var hire = await db.Employees.AsNoTracking().Where(e => days.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.HireDate, ct);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime + Wib); var toAdd = new List<AttendanceLog>(); int imported = 0, skipped = 0;
        foreach (var (key, list) in punches.OrderBy(k => k.Key.Day).ThenBy(k => k.Key.No))
        {
            var (emp, no, day) = key; list.Sort();
            if (day > today) { rows.Add(new(0, no, day, "error", "That day is in the future.")); continue; }
            if (day < hire[emp]) { rows.Add(new(0, no, day, "error", "That is before the hire date.")); continue; }
            if (closed.Contains((day.Year, day.Month))) { rows.Add(new(0, no, day, "error", "That month is closed for payroll.")); continue; }
            if (have.Contains((emp, day))) { skipped++; rows.Add(new(0, no, day, "skipped", "Attendance already exists for that day.")); continue; }
            if ((list[^1] - list[0]).TotalMinutes < 1) { rows.Add(new(0, no, day, "error", "Only one punch that day: ask for a correction instead.")); continue; }
            toAdd.Add(new AttendanceLog { EmployeeId = emp, Date = day, ClockIn = new DateTimeOffset(list[0], Wib).ToUniversalTime(), ClockOut = new DateTimeOffset(list[^1], Wib).ToUniversalTime(), Source = "import" });
            imported++; rows.Add(new(0, no, day, "ok", null));
        }
        var failed = rows.Count(r => r.Status == "error");
        if (!req.DryRun && toAdd.Count > 0)
        {
            await db.InTransactionAsync(async () =>
            {
                db.AttendanceLogs.AddRange(toAdd);
                audit.Record("hr.attendance.import", "attendance", null, failed == 0 ? Outcome.Success : Outcome.Failure, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["days"] = toAdd.Count.ToString(), ["skipped"] = skipped.ToString(), ["failed"] = failed.ToString() });
                await db.SaveChangesAsync(ct); return 0;
            }, ct);
        }
        return new ImportAttendanceResult(req.DryRun, data.Count, punches.Count, req.DryRun ? 0 : toAdd.Count, skipped, failed, [.. rows.OrderBy(r => r.Status == "ok" ? 1 : 0).ThenBy(r => r.Line).Take(500)]);
    }
}
