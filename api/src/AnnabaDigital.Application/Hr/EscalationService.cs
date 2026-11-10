using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

/// <summary>
/// LVE-004. A leave, correction or overtime request that nobody decided within 48 hours is reported to HR once (a digest per recipient). The unique reminder row claims each request before
/// anything is mailed, so the daily job never repeats itself. HR can already decide any request, so escalation only has to make sure they know.
/// </summary>
public sealed class EscalationService(IAppDb db, IClock clock, IEmailQueue email, IAppLinks links, ITenantContext tenant)
{
    public static readonly TimeSpan After = TimeSpan.FromHours(48);
    private const int Marker = 48;

    public async Task<int> RunAllTenantsAsync(CancellationToken ct = default)
    {
        var sent = 0;
        foreach (var id in await db.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct)) { using var _ = tenant.Switch(id); sent += await RunAsync(ct); }
        return sent;
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var cutoff = clock.UtcNow - After;
        var leave = await db.LeaveRequests.AsNoTracking().Where(l => l.Status == LeaveStatus.Pending && l.CreatedAt < cutoff).Select(l => new { l.Id, l.EmployeeId, Due = l.StartDate }).ToListAsync(ct);
        var corr = await db.AttendanceCorrections.AsNoTracking().Where(c => c.Status == RequestStatus.Pending && c.CreatedAt < cutoff).Select(c => new { c.Id, c.EmployeeId, Due = c.Date }).ToListAsync(ct);
        var over = await db.OvertimeRequests.AsNoTracking().Where(o => o.Status == RequestStatus.Pending && o.CreatedAt < cutoff).Select(o => new { o.Id, o.EmployeeId, Due = o.Date }).ToListAsync(ct);
        if (leave.Count + corr.Count + over.Count == 0) return 0;

        async Task<int> Claim(string kind, IEnumerable<(Guid Employee, DateOnly Due)> items)
        {
            var n = 0;
            foreach (var (emp, due) in items.DistinctBy(i => (i.Employee, i.Due)))
            {
                if (await db.HrReminders.AnyAsync(r => r.EmployeeId == emp && r.Kind == kind && r.DueOn == due && r.ThresholdDays == Marker, ct)) continue;
                db.HrReminders.Add(new HrReminder { EmployeeId = emp, Kind = kind, DueOn = due, ThresholdDays = Marker, SentAt = clock.UtcNow });
                try { await db.SaveChangesAsync(ct); n++; } catch (DbUpdateException) { db.ResetTracking(); }
            }
            return n;
        }
        var nl = await Claim("leave_escalation", leave.Select(l => (l.EmployeeId, l.Due)));
        var nt = await Claim("time_escalation", corr.Select(c => (c.EmployeeId, c.Due)).Concat(over.Select(o => (o.EmployeeId, o.Due))));
        if (nl + nt == 0) return 0;

        var hr = await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active && u.UserRoles.Any(r => r.Role!.ArchivedAt == null && r.Role.Permissions.Any(p => p.PermissionKey == Permissions.LeaveManage || p.PermissionKey == Permissions.AttendanceManage)))
            .Select(u => new { u.Email, u.Name }).ToListAsync(ct);
        foreach (var u in hr)
            email.Enqueue(EmailTemplates.Escalation(u.Email, u.Name, nl, nt, links.Origin + (nl > 0 ? "/hr/leave-all" : "/hr/time-approvals")));
        await db.SaveChangesAsync(ct);
        return hr.Count;
    }
}
