using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Hr;

public sealed record AttendanceLogView(Guid Id, DateOnly Date, DateTimeOffset ClockIn, DateTimeOffset? ClockOut, decimal? Hours, bool Late, string WorkMode = "office", bool OutsideZone = false, bool HasPhoto = false);
public sealed record ClockInRequest(string? Mode, double? Latitude, double? Longitude, string? PhotoBase64 = null);
public sealed record WorkLocationView(Guid Id, string Name, double Latitude, double Longitude, int RadiusMeters, bool Archived);
public sealed record WorkLocationRequest(string? Name, double? Latitude, double? Longitude, int? RadiusMeters, bool? Archived);
public sealed record MyAttendance(AttendanceLogView? Open, IReadOnlyList<AttendanceLogView> Logs, int DaysPresent, int LateDays, decimal Hours, bool Geofence = false);
public sealed record AttendanceReportRow(Guid EmployeeId, string EmployeeNo, string EmployeeName, int DaysPresent, int LateDays, decimal Hours, int OpenSessions, int RemoteDays = 0, int FieldDays = 0, int OutsideZone = 0);

/// <summary>
/// ATT-002/006 (web clock). Times come only from the server clock (the browser cannot choose them), a person has at most one open session
/// (database-enforced), and "late" means the first clock-in of a day after 09:10 local time (default policy; becomes a setting with shifts).
/// </summary>
public sealed class AttendanceService(IAppDb db, IClock clock, AuditWriter audit, ShiftService shifts, PhotoService photos)
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(7); // WIB
    private DateOnly Local(DateTimeOffset t) => DateOnly.FromDateTime(t.UtcDateTime + Offset);

    private async Task<Employee> MeAsync(SessionPrincipal actor, CancellationToken ct)
        => await db.Employees.FirstOrDefaultAsync(e => e.UserId == actor.UserId && e.Status == EmployeeStatus.Active, ct)
           ?? throw DomainException.Invariant("Your account is not linked to an active employee record. Ask HR to link it.");

    private static bool IsLate(AttendanceLog l, bool firstOfDay, ShiftLookup shifts) => firstOfDay && TimeOnly.FromDateTime(l.ClockIn.UtcDateTime + Offset) > shifts.LateAfter(l.EmployeeId, l.Date);
    private static decimal HoursOf(AttendanceLog l) => l.ClockOut is { } o ? Math.Round((decimal)(o - l.ClockIn).TotalHours, 2) : 0;

    private static List<AttendanceLogView> Views(IEnumerable<AttendanceLog> logs, Func<AttendanceLog, bool, bool> late)
    {
        var firsts = logs.GroupBy(l => l.Date).ToDictionary(g => g.Key, g => g.OrderBy(x => x.ClockIn).First().Id);
        return [.. logs.OrderByDescending(l => l.ClockIn).Select(l => new AttendanceLogView(l.Id, l.Date, l.ClockIn, l.ClockOut, l.ClockOut is null ? null : HoursOf(l), late(l, firsts[l.Date] == l.Id), Mode(l), l.OutsideZone))];
    }

    private static string Mode(AttendanceLog l) => l.WorkMode.ToString().ToLowerInvariant();

    /// <summary>Great-circle distance in metres (haversine); plenty accurate for "is this person at the office".</summary>
    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000; static double Rad(double d) => d * Math.PI / 180;
        var a = Math.Pow(Math.Sin(Rad(lat2 - lat1) / 2), 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(Rad(lon2 - lon1) / 2), 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    public async Task<AttendanceLogView> ClockInAsync(SessionPrincipal actor, ClientInfo client, CancellationToken ct = default, ClockInRequest? r = null)
    {
        var emp = await MeAsync(actor, ct); var now = clock.UtcNow;
        var mode = WorkMode.Office;
        if (!string.IsNullOrWhiteSpace(r?.Mode) && (!Enum.TryParse(r.Mode, true, out mode) || !Enum.IsDefined(mode))) throw DomainException.Validation("Please check the clock-in.", new Dictionary<string, string> { ["mode"] = "Choose office, remote or field." });
        double? lat = r?.Latitude, lon = r?.Longitude;
        if (lat is null != lon is null || lat is < -90 or > 90 || lon is < -180 or > 180) throw DomainException.Validation("Please check the clock-in.", new Dictionary<string, string> { ["latitude"] = "The location is not valid." });
        if (lat is { } la && lon is { } lo) { lat = Math.Round(la, 5); lon = Math.Round(lo, 5); }
        // ATT-002: with work locations defined, an office clock-in is checked against them. Outside (or no location shared) is flagged for HR, not refused: positions from a laptop are too unreliable to lock people out.
        var outside = false;
        if (mode == WorkMode.Office)
        {
            var sites = await db.WorkLocations.AsNoTracking().Where(w => w.ArchivedAt == null).ToListAsync(ct);
            outside = sites.Count > 0 && (lat is null || !sites.Any(w => DistanceMeters(lat.Value, lon!.Value, w.Latitude, w.Longitude) <= w.RadiusMeters));
        }
        var open = await db.AttendanceLogs.AsNoTracking().FirstOrDefaultAsync(l => l.EmployeeId == emp.Id && l.ClockOut == null, ct);
        if (open is not null) throw DomainException.Conflict($"You are already clocked in since {(open.ClockIn + Offset):HH:mm}.");
        (byte[] Bytes, string ContentType)? photo = string.IsNullOrWhiteSpace(r?.PhotoBase64) ? null : await photos.AcceptAsync(r!.PhotoBase64, "photo", ct);
        var log = new AttendanceLog { EmployeeId = emp.Id, Date = Local(now), ClockIn = now, Ip = client.Ip, WorkMode = mode, Latitude = lat, Longitude = lon, OutsideZone = outside };
        db.AttendanceLogs.Add(log);
        if (photo is { } ph) db.HrPhotos.Add(new HrPhoto { Kind = "clock_in", OwnerId = log.Id, EmployeeId = emp.Id, ContentType = ph.ContentType, Content = ph.Bytes, CreatedAt = now });
        audit.Record("hr.attendance.clock_in", "employee", emp.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["mode"] = mode.ToString(), ["outsideZone"] = outside.ToString() });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.ResetTracking(); throw DomainException.Conflict("You are already clocked in."); } // lost a race with a second tab
        var first = !await db.AttendanceLogs.AnyAsync(l => l.EmployeeId == emp.Id && l.Date == log.Date && l.ClockIn < log.ClockIn, ct);
        return new AttendanceLogView(log.Id, log.Date, log.ClockIn, null, null, IsLate(log, first, await shifts.LookupAsync([emp.Id], log.Date, log.Date, ct)), Mode(log), log.OutsideZone, photo is not null);
    }

    public async Task<AttendanceLogView> ClockOutAsync(SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var emp = await MeAsync(actor, ct);
        var log = await db.AttendanceLogs.FirstOrDefaultAsync(l => l.EmployeeId == emp.Id && l.ClockOut == null, ct) ?? throw DomainException.Conflict("You are not clocked in.");
        log.ClockOut = clock.UtcNow;
        audit.Record("hr.attendance.clock_out", "employee", emp.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return new AttendanceLogView(log.Id, log.Date, log.ClockIn, log.ClockOut, HoursOf(log), false, Mode(log), log.OutsideZone);
    }

    public async Task<MyAttendance> MineAsync(string? month, SessionPrincipal actor, CancellationToken ct = default)
    {
        var emp = await MeAsync(actor, ct);
        var (from, to) = Month(month);
        var logs = await db.AttendanceLogs.AsNoTracking().Where(l => l.EmployeeId == emp.Id && l.Date >= from && l.Date <= to).ToListAsync(ct);
        var lookup = await shifts.LookupAsync([emp.Id], from, to, ct);
        var logIds = logs.Select(l => l.Id).ToList(); var withPhoto = (await db.HrPhotos.AsNoTracking().Where(p => p.Kind == "clock_in" && logIds.Contains(p.OwnerId)).Select(p => p.OwnerId).ToListAsync(ct)).ToHashSet();
        var views = Views(logs, (l, first) => IsLate(l, first, lookup)).Select(v => v with { HasPhoto = withPhoto.Contains(v.Id) }).ToList();
        var open = await db.AttendanceLogs.AsNoTracking().Where(l => l.EmployeeId == emp.Id && l.ClockOut == null).FirstOrDefaultAsync(ct);
        return new MyAttendance(open is null ? null : new AttendanceLogView(open.Id, open.Date, open.ClockIn, null, null, false, Mode(open), open.OutsideZone), views,
            logs.Select(l => l.Date).Distinct().Count(), views.Count(v => v.Late), views.Sum(v => v.Hours ?? 0), await db.WorkLocations.AnyAsync(w => w.ArchivedAt == null, ct));
    }

    /// <summary>One person's clock-ins of a month, for HR: where they worked, whether the place was confirmed, whether a picture was taken.</summary>
    public async Task<IReadOnlyList<AttendanceLogView>> EmployeeLogsAsync(Guid employeeId, string? month, CancellationToken ct = default)
    {
        var (from, to) = Month(month);
        var logs = await db.AttendanceLogs.AsNoTracking().Where(l => l.EmployeeId == employeeId && l.Date >= from && l.Date <= to).ToListAsync(ct);
        var lookup = await shifts.LookupAsync([employeeId], from, to, ct); var logIds = logs.Select(l => l.Id).ToList();
        var withPhoto = (await db.HrPhotos.AsNoTracking().Where(p => p.Kind == "clock_in" && logIds.Contains(p.OwnerId)).Select(p => p.OwnerId).ToListAsync(ct)).ToHashSet();
        return [.. Views(logs, (l, first) => IsLate(l, first, lookup)).Select(v => v with { HasPhoto = withPhoto.Contains(v.Id) })];
    }

    public async Task<IReadOnlyList<AttendanceReportRow>> ReportAsync(string? month, Guid? orgUnitId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var (from, to) = Month(month);
        var emps = db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active || e.TerminationDate >= from);
        if (orgUnitId is { } ou) emps = emps.Where(e => e.Employments.Any(m => m.EffectiveTo == null && m.OrgUnitId == ou));
        var people = await emps.OrderBy(e => e.FullName).Select(e => new { e.Id, e.EmployeeNo, e.FullName }).ToListAsync(ct);
        var ids = people.Select(p => p.Id).ToList();
        var logs = (await db.AttendanceLogs.AsNoTracking().Where(l => ids.Contains(l.EmployeeId) && l.Date >= from && l.Date <= to).ToListAsync(ct)).GroupBy(l => l.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var lookup = await shifts.LookupAsync(ids, from, to, ct);
        audit.Record("hr.attendance.report", "attendance", null, Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["from"] = from.ToString("yyyy-MM-dd") });
        await db.SaveChangesAsync(ct);
        return [.. people.Select(p =>
        {
            var l = logs.GetValueOrDefault(p.Id) ?? [];
            var v = Views(l, (x, first) => IsLate(x, first, lookup));
            return new AttendanceReportRow(p.Id, p.EmployeeNo, p.FullName, l.Select(x => x.Date).Distinct().Count(), v.Count(x => x.Late), v.Sum(x => x.Hours ?? 0), l.Count(x => x.ClockOut == null),
                l.Where(x => x.WorkMode == WorkMode.Remote).Select(x => x.Date).Distinct().Count(), l.Where(x => x.WorkMode == WorkMode.Field).Select(x => x.Date).Distinct().Count(), l.Count(x => x.OutsideZone));
        })];
    }

    private (DateOnly From, DateOnly To) Month(string? month)
    {
        DateOnly first;
        if (string.IsNullOrWhiteSpace(month)) first = new DateOnly(Local(clock.UtcNow).Year, Local(clock.UtcNow).Month, 1);
        else if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out first)) throw DomainException.Validation("Unknown month.", new Dictionary<string, string> { ["month"] = "Use the form 2026-11." });
        return (first, first.AddMonths(1).AddDays(-1));
    }

    // ============================================================ work locations (geofence)
    private static WorkLocationView View(WorkLocation w) => new(w.Id, w.Name, w.Latitude, w.Longitude, w.RadiusMeters, w.ArchivedAt != null);

    public async Task<IReadOnlyList<WorkLocationView>> LocationsAsync(bool includeArchived, CancellationToken ct = default)
        => [.. (await db.WorkLocations.AsNoTracking().Where(w => includeArchived || w.ArchivedAt == null).OrderBy(w => w.Name).ToListAsync(ct)).Select(View)];

    public async Task<WorkLocationView> SaveLocationAsync(Guid? id, WorkLocationRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var w = id is null ? new WorkLocation { CreatedAt = clock.UtcNow } : await db.WorkLocations.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Work location");
        var errors = new Dictionary<string, string>();
        var name = (r.Name ?? w.Name).Trim(); if (name.Length is < 2 or > 100) errors["name"] = "Enter a name of 2–100 characters.";
        var lat = r.Latitude ?? (id is null ? null : w.Latitude); var lon = r.Longitude ?? (id is null ? null : w.Longitude); var radius = r.RadiusMeters ?? w.RadiusMeters;
        if (lat is null or < -90 or > 90) errors["latitude"] = "Enter a latitude between -90 and 90.";
        if (lon is null or < -180 or > 180) errors["longitude"] = "Enter a longitude between -180 and 180.";
        if (radius is < 20 or > 5000) errors["radiusMeters"] = "Enter a radius of 20–5,000 metres.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the work location.", errors);
        if (await db.WorkLocations.AnyAsync(x => x.Name == name && x.Id != w.Id, ct)) throw DomainException.Conflict($"A work location named '{name}' already exists.");
        w.Name = name; w.Latitude = Math.Round(lat!.Value, 6); w.Longitude = Math.Round(lon!.Value, 6); w.RadiusMeters = radius;
        if (r.Archived is { } a) w.ArchivedAt = a ? w.ArchivedAt ?? clock.UtcNow : null;
        if (id is null) db.WorkLocations.Add(w);
        audit.Record(id is null ? "hr.work_location.create" : "hr.work_location.update", "work_location", w.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name, ["radius"] = radius.ToString() });
        await db.SaveChangesAsync(ct);
        return View(w);
    }
}
