using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Hr;

/// <summary>
/// LVE-008. Tells people 30 and 7 days ahead that carried-over leave is about to expire (in the app and, unless they switched leave e-mails off, by e-mail).
/// Each person is told once per threshold and deadline: the claim is a row in hr_reminders, so several instances do not send twice.
/// </summary>
public sealed class LeaveExpiryReminderService(IAppDb db, IClock clock, IEmailQueue email, IAppLinks links, ITenantContext tenant, LeaveService leave, NotificationService notes)
{
    public static readonly int[] Thresholds = [30, 7];

    public async Task<int> RunAllTenantsAsync(CancellationToken ct = default)
    {
        var sent = 0;
        foreach (var id in await db.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct)) { using var _ = tenant.Switch(id); sent += await RunAsync(ct); }
        return sent;
    }

    /// <summary>One organisation. Returns the number of people told.</summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7)); var told = 0;
        foreach (var type in await db.LeaveTypes.AsNoTracking().Where(t => t.ArchivedAt == null && t.CarryOverExpiryMonth != null).ToListAsync(ct))
        {
            if (LeaveService.CarryDeadline(type, today.Year) is not { } deadline) continue;
            var left = deadline.DayNumber - today.DayNumber; if (left < 0 || left > Thresholds.Max()) continue;
            var crossed = Thresholds.Where(t => left <= t).ToList(); var kind = "leave_expiry:" + type.Code;
            var holders = await db.LeaveLedger.AsNoTracking().Where(l => l.LeaveTypeId == type.Id && l.Year == today.Year && l.Kind == LedgerKind.CarryOver && l.Days > 0).Select(l => l.EmployeeId).Distinct().ToListAsync(ct);
            foreach (var empId in holders)
            {
                var days = await leave.CarryLeftAsync(empId, type, today.Year, deadline, ct); if (days <= 0) continue;
                var already = (await db.HrReminders.AsNoTracking().Where(r => r.EmployeeId == empId && r.Kind == kind && r.DueOn == deadline).Select(r => r.ThresholdDays).ToListAsync(ct)).ToHashSet();
                var fresh = crossed.Where(t => !already.Contains(t)).ToList(); if (fresh.Count == 0) continue;
                foreach (var t in fresh) db.HrReminders.Add(new HrReminder { EmployeeId = empId, Kind = kind, DueOn = deadline, ThresholdDays = t, SentAt = clock.UtcNow });
                try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ResetTracking(); continue; } // another instance claimed it first
                var who = await db.Employees.AsNoTracking().Where(e => e.Id == empId && e.Status == EmployeeStatus.Active && e.UserId != null)
                    .Join(db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active), e => e.UserId, u => u.Id, (e, u) => new { u.Id, u.Email, e.FullName }).FirstOrDefaultAsync(ct);
                if (who is null) continue;
                var until = deadline.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
                notes.Notify(who.Id, "leave.expiring", $"{days:0.##} day(s) of {type.Name} expire on {until}", "Carried over from last year.", "/hr/my-leave");
                if (notes.EmailAllowed(who.Id, "leave.expiring")) email.Enqueue(EmailTemplates.LeaveExpiring(who.Email, who.FullName, type.Name, days.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), until, links.Origin + "/hr/my-leave"));
                await db.SaveChangesAsync(ct); told++;
            }
        }
        return told;
    }
}
