using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

/// <summary>
/// ONB-005. Runs daily for every organisation. When a fixed-term contract, probation or internship comes within 30, 14 or 7 days of its end, the employee's manager and the people who maintain
/// employee records get one e-mail. "Within" rather than "exactly" so a day when the job did not run is caught up; a unique row per (employee, end date, threshold) means nothing is mailed twice,
/// and when several thresholds were crossed at once only one mail goes out.
/// </summary>
public sealed class ContractReminderService(IAppDb db, IClock clock, IEmailQueue email, IAppLinks links, ITenantContext tenant)
{
    public static readonly int[] Thresholds = [30, 14, 7];

    public async Task<int> RunAllTenantsAsync(CancellationToken ct = default)
    {
        var sent = 0;
        foreach (var id in await db.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct))
        {
            using var _ = tenant.Switch(id);
            sent += await RunAsync(ct);
        }
        return sent;
    }

    /// <summary>One organisation (the current tenant scope). Returns the number of e-mails queued.</summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7)); var horizon = today.AddDays(Thresholds.Max());
        var due = await db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active)
            .SelectMany(e => e.Employments.Where(m => m.EffectiveTo == null && m.Type != EmploymentType.Permanent && m.ContractEnd != null && m.ContractEnd >= today && m.ContractEnd <= horizon)
                .Select(m => new { e.Id, e.FullName, e.EmployeeNo, End = m.ContractEnd!.Value, m.ManagerId })).ToListAsync(ct);
        if (due.Count == 0) return 0;

        var hrUsers = await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active && u.UserRoles.Any(r => r.Role!.ArchivedAt == null && r.Role.Permissions.Any(p => p.PermissionKey == Permissions.EmployeesUpdate)))
            .Select(u => new { u.Email, u.Name }).ToListAsync(ct);
        var mgrIds = due.Where(d => d.ManagerId != null).Select(d => d.ManagerId!.Value).Distinct().ToList();
        var managers = await db.Employees.AsNoTracking().Where(e => mgrIds.Contains(e.Id) && e.UserId != null)
            .Join(db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active), e => e.UserId, u => u.Id, (e, u) => new { e.Id, u.Email, e.FullName }).ToDictionaryAsync(x => x.Id, ct);

        var sentCount = 0;
        foreach (var d in due)
        {
            var left = d.End.DayNumber - today.DayNumber;
            var crossed = Thresholds.Where(t => left <= t).ToList();
            var already = (await db.HrReminders.AsNoTracking().Where(r => r.EmployeeId == d.Id && r.Kind == "contract_end" && r.DueOn == d.End).Select(r => r.ThresholdDays).ToListAsync(ct)).ToHashSet();
            var fresh = crossed.Where(t => !already.Contains(t)).ToList();
            if (fresh.Count == 0) continue;
            foreach (var t in fresh) db.HrReminders.Add(new HrReminder { EmployeeId = d.Id, DueOn = d.End, ThresholdDays = t, SentAt = clock.UtcNow });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { db.ResetTracking(); continue; } // another instance claimed it first
            var recipients = hrUsers.Select(u => (u.Email, u.Name)).ToList();
            if (d.ManagerId is { } m && managers.TryGetValue(m, out var mgr)) recipients.Add((mgr.Email, mgr.FullName));
            foreach (var (to, name) in recipients.DistinctBy(r => r.Item1.ToLowerInvariant()))
            {
                email.Enqueue(EmailTemplates.ContractEnding(to, name, d.FullName, d.End.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture), left, links.Origin + $"/hr/employees/{d.Id}"));
                sentCount++;
            }
            await db.SaveChangesAsync(ct); // the e-mails go to the outbox in the same context
        }
        return sentCount;
    }
}
