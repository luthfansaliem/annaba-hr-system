using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record ReportOption(string Key, string Name, bool Allowed);
public sealed record ReportScheduleView(Guid Id, string Report, string Name, string Frequency, int Day, DateOnly? LastSentOn);
public sealed record ReportSchedulesOverview(IReadOnlyList<ReportOption> Reports, IReadOnlyList<ReportScheduleView> Schedules);
public sealed record ReportScheduleRequest(string? Report, string? Frequency, int? Day);

/// <summary>
/// RPT-004. A person asks to be reminded of a report every week or month. The e-mail carries a link to the report inside the application and nothing else: no figures, no attachment,
/// so the data never leaves the sign-in. People can only schedule reports they are allowed to open, and a schedule stops by itself when that permission (or the account) is gone.
/// </summary>
public sealed class ScheduledReportService(IAppDb db, IClock clock, AuthorizationService authz, IEmailQueue email, IAppLinks links)
{
    private sealed record Def(string Key, string Name, string NameId, string Path, string Permission);
    private static readonly Def[] Catalogue =
    [
        new("hr_dashboard", "HR dashboard", "Dasbor HR", "/hr/dashboard", Permissions.EmployeesRead),
        new("attendance", "Attendance report", "Laporan absensi", "/hr/attendance-report", Permissions.AttendanceReadAll),
        new("leave", "Leave overview", "Ringkasan cuti", "/hr/leave-all", Permissions.LeaveReadAll),
        new("payroll", "Pay runs and payroll reports", "Penggajian dan laporan payroll", "/hr/payroll", Permissions.PayrollRead),
        new("approvals", "Requests waiting for my decision", "Pengajuan yang menunggu keputusan saya", "/hr/approvals", Permissions.LeaveApprove),
        new("team", "My team", "Tim saya", "/hr/team", Permissions.LeaveApprove),
    ];
    public const int MaxPerUser = 10;
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    private static ReportScheduleView View(ReportSchedule s) => new(s.Id, s.Report, Catalogue.FirstOrDefault(c => c.Key == s.Report)?.Name ?? s.Report, s.Frequency, s.Day, s.LastSentOn);

    public async Task<ReportSchedulesOverview> MineAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var perms = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        var rows = await db.ReportSchedules.AsNoTracking().Where(s => s.UserId == actor.UserId).OrderBy(s => s.CreatedAt).ToListAsync(ct);
        return new ReportSchedulesOverview([.. Catalogue.Select(c => new ReportOption(c.Key, c.Name, perms.Contains(c.Permission)))], [.. rows.Select(View)]);
    }

    public async Task<ReportScheduleView> CreateAsync(ReportScheduleRequest r, SessionPrincipal actor, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>(); var def = Catalogue.FirstOrDefault(c => c.Key == r.Report);
        if (def is null) errors["report"] = "Choose a report.";
        var freq = (r.Frequency ?? "").ToLowerInvariant(); if (freq is not ("weekly" or "monthly")) errors["frequency"] = "Choose weekly or monthly.";
        else if (freq == "weekly" && r.Day is not (>= 1 and <= 7)) errors["day"] = "Choose a day of the week (1 = Monday … 7 = Sunday)."; else if (freq == "monthly" && r.Day is not (>= 1 and <= 28)) errors["day"] = "Choose a day of the month (1–28).";
        if (errors.Count > 0) throw DomainException.Validation("Please check the schedule.", errors);
        if (!(await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(def!.Permission)) throw DomainException.Denied(def.Permission);
        if (await db.ReportSchedules.CountAsync(s => s.UserId == actor.UserId, ct) >= MaxPerUser) throw DomainException.Invariant($"At most {MaxPerUser} scheduled reports per person.");
        if (await db.ReportSchedules.AnyAsync(s => s.UserId == actor.UserId && s.Report == def.Key && s.Frequency == freq && s.Day == r.Day, ct)) throw DomainException.Conflict("You already have this schedule.");
        var s = new ReportSchedule { UserId = actor.UserId, Report = def.Key, Frequency = freq, Day = r.Day!.Value, CreatedAt = clock.UtcNow };
        db.ReportSchedules.Add(s); await db.SaveChangesAsync(ct);
        return View(s);
    }

    public async Task DeleteAsync(Guid id, SessionPrincipal actor, CancellationToken ct = default)
    {
        if (await db.ReportSchedules.Where(s => s.Id == id && s.UserId == actor.UserId).ExecuteDeleteAsync(ct) == 0) throw DomainException.NotFound("Schedule");
    }

    /// <summary>Sends today's reminders for the current organisation, once per schedule and day (the row is claimed by a conditional update, so two instances never both send).</summary>
    public async Task<int> RunDueAsync(CancellationToken ct = default)
    {
        var today = Today; var dow = ((int)today.DayOfWeek + 6) % 7 + 1; var sent = 0;
        var due = await db.ReportSchedules.AsNoTracking().Where(s => (s.LastSentOn == null || s.LastSentOn < today) && (s.Frequency == "weekly" && s.Day == dow || s.Frequency == "monthly" && s.Day == today.Day)).ToListAsync(ct);
        foreach (var s in due)
        {
            if (await db.ReportSchedules.Where(x => x.Id == s.Id && (x.LastSentOn == null || x.LastSentOn < today)).ExecuteUpdateAsync(u => u.SetProperty(x => x.LastSentOn, today), ct) == 0) continue;
            var def = Catalogue.FirstOrDefault(c => c.Key == s.Report);
            var user = await db.Users.AsNoTracking().Where(u => u.Id == s.UserId && u.Status == UserStatus.Active).Select(u => new { u.Email, u.Name }).FirstOrDefaultAsync(ct);
            if (def is null || user is null || !(await authz.GetEffectivePermissionsAsync(s.UserId, ct)).Contains(def.Permission))
            {
                await db.ReportSchedules.Where(x => x.Id == s.Id).ExecuteDeleteAsync(ct); continue;      // the person may no longer open it: stop reminding
            }
            email.Enqueue(EmailTemplates.ScheduledReport(user.Email, user.Name, def.Name, def.NameId, links.Origin + def.Path));
            await db.SaveChangesAsync(ct); sent++;
        }
        return sent;
    }
}
