using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;

namespace AnnabaDigital.Application.Hr;

/// <summary>Housekeeping the background worker runs for every organisation in turn: job changes whose day has come, report e-mails that are due, and clock-in pictures past their 90 days.</summary>
public sealed class HrDailyJobs(IAppDb db, ITenantContext tenant, EmployeeService employees, ScheduledReportService reports, PhotoService photos)
{
    public async Task<int> RunAllTenantsAsync(CancellationToken ct = default)
    {
        var done = 0;
        foreach (var id in await db.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct))
        {
            using var _ = tenant.Switch(id);
            done += await employees.ApplyScheduledAsync(ct) + await reports.RunDueAsync(ct) + await photos.PurgeOldClockInPhotosAsync(ct);
        }
        return done;
    }
}
