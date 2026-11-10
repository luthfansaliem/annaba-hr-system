using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Hr;

public sealed record AnnouncementView(Guid Id, string Title, string Body, Guid? OrgUnitId, string? OrgUnit, bool RequiresAck, DateOnly? ExpiresOn, DateTimeOffset PublishedAt, bool Read, bool Acked, bool Archived);
public sealed record AnnouncementRequest(string? Title, string? Body, Guid? OrgUnitId, bool? RequiresAck, DateOnly? ExpiresOn, bool? Archived);
public sealed record AckReportRow(Guid EmployeeId, string EmployeeNo, string EmployeeName, bool Read, DateTimeOffset? AckedAt);
public sealed record AckReport(Guid AnnouncementId, string Title, int Audience, int Read, int Acked, IReadOnlyList<AckReportRow> People);

/// <summary>COM-001/003. People see announcements meant for them (everyone, or their department), mark them read and — for policies — acknowledge them; HR sees who has not.</summary>
public sealed class AnnouncementService(IAppDb db, IClock clock, AuditWriter audit)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    private async Task<(Employee Emp, Guid? Unit)> MeAsync(SessionPrincipal actor, CancellationToken ct)
    {
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == actor.UserId && x.Status == EmployeeStatus.Active, ct) ?? throw DomainException.Invariant("Your account is not linked to an active employee record. Ask HR to link it.");
        var unit = await db.Employments.AsNoTracking().Where(m => m.EmployeeId == e.Id && m.EffectiveTo == null).Select(m => m.OrgUnitId).FirstOrDefaultAsync(ct);
        return (e, unit);
    }

    public async Task<IReadOnlyList<AnnouncementView>> ForMeAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var (e, unit) = await MeAsync(actor, ct); var today = Today;
        var rows = await db.Announcements.AsNoTracking().Where(a => a.ArchivedAt == null && (a.ExpiresOn == null || a.ExpiresOn >= today) && (a.OrgUnitId == null || a.OrgUnitId == unit)).OrderByDescending(a => a.PublishedAt).Take(100).ToListAsync(ct);
        var ids = rows.Select(a => a.Id).ToList(); var rec = await db.AnnouncementReceipts.AsNoTracking().Where(r => r.EmployeeId == e.Id && ids.Contains(r.AnnouncementId)).ToDictionaryAsync(r => r.AnnouncementId, ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        return [.. rows.Select(a => new AnnouncementView(a.Id, a.Title, a.Body, a.OrgUnitId, a.OrgUnitId is { } o ? units.GetValueOrDefault(o) : null, a.RequiresAck, a.ExpiresOn, a.PublishedAt, rec.ContainsKey(a.Id), rec.TryGetValue(a.Id, out var r) && r.AckedAt != null, false))];
    }

    private async Task<(Announcement A, Employee E, AnnouncementReceipt? R)> LoadAsync(Guid id, SessionPrincipal actor, CancellationToken ct)
    {
        var (e, unit) = await MeAsync(actor, ct); var today = Today;
        var a = await db.Announcements.FirstOrDefaultAsync(x => x.Id == id && x.ArchivedAt == null && (x.ExpiresOn == null || x.ExpiresOn >= today) && (x.OrgUnitId == null || x.OrgUnitId == unit), ct) ?? throw DomainException.NotFound("Announcement");
        return (a, e, await db.AnnouncementReceipts.FirstOrDefaultAsync(r => r.AnnouncementId == id && r.EmployeeId == e.Id, ct));
    }

    public async Task MarkReadAsync(Guid id, SessionPrincipal actor, CancellationToken ct = default)
    {
        var (a, e, r) = await LoadAsync(id, actor, ct); if (r is not null) return;
        db.AnnouncementReceipts.Add(new AnnouncementReceipt { AnnouncementId = a.Id, EmployeeId = e.Id, ReadAt = clock.UtcNow });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ResetTracking(); } // opened in two tabs at once
    }

    public async Task AckAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var (a, e, r) = await LoadAsync(id, actor, ct);
        if (!a.RequiresAck) throw DomainException.Invariant("This announcement does not need an acknowledgement.");
        if (r?.AckedAt != null) return;
        if (r is null) { r = new AnnouncementReceipt { AnnouncementId = a.Id, EmployeeId = e.Id, ReadAt = clock.UtcNow }; db.AnnouncementReceipts.Add(r); }
        r.AckedAt = clock.UtcNow;
        audit.Record("hr.announcement.ack", "announcement", a.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["title"] = a.Title });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ResetTracking(); }
    }

    // ---- management
    public async Task<IReadOnlyList<AnnouncementView>> ManageListAsync(CancellationToken ct = default)
    {
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        return [.. (await db.Announcements.AsNoTracking().OrderByDescending(a => a.PublishedAt).Take(200).ToListAsync(ct)).Select(a => new AnnouncementView(a.Id, a.Title, a.Body, a.OrgUnitId, a.OrgUnitId is { } o ? units.GetValueOrDefault(o) : null, a.RequiresAck, a.ExpiresOn, a.PublishedAt, false, false, a.ArchivedAt != null))];
    }

    public async Task<AnnouncementView> SaveAsync(Guid? id, AnnouncementRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var a = id is null ? new Announcement { PublishedAt = clock.UtcNow, CreatedBy = actor.UserId } : await db.Announcements.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Announcement");
        var errors = new Dictionary<string, string>(); var title = (r.Title ?? a.Title).Trim(); var body = (r.Body ?? a.Body).Trim();
        if (title.Length is < 3 or > 200) errors["title"] = "Enter a title of 3–200 characters.";
        if (body.Length is < 1 or > 4000) errors["body"] = "Enter the text (up to 4,000 characters).";
        if (r.ExpiresOn is { } ex && ex < Today) errors["expiresOn"] = "The expiry date is in the past.";
        if (r.OrgUnitId is { } u && !await db.OrgUnits.AnyAsync(o => o.Id == u, ct)) errors["orgUnitId"] = "Choose an existing department.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the announcement.", errors);
        a.Title = title; a.Body = body; if (id is null || r.OrgUnitId is not null) a.OrgUnitId = r.OrgUnitId ?? a.OrgUnitId; a.RequiresAck = r.RequiresAck ?? a.RequiresAck; if (r.ExpiresOn is not null) a.ExpiresOn = r.ExpiresOn;
        if (r.Archived is { } ar) a.ArchivedAt = ar ? a.ArchivedAt ?? clock.UtcNow : null;
        if (id is null) db.Announcements.Add(a);
        audit.Record(id is null ? "hr.announcement.create" : "hr.announcement.update", "announcement", a.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["title"] = title, ["requiresAck"] = a.RequiresAck.ToString() });
        await db.SaveChangesAsync(ct);
        return (await ManageListAsync(ct)).First(x => x.Id == a.Id);
    }

    public async Task<AckReport> ReportAsync(Guid id, CancellationToken ct = default)
    {
        var a = await db.Announcements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Announcement");
        var people = await db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active && (a.OrgUnitId == null || e.Employments.Any(m => m.EffectiveTo == null && m.OrgUnitId == a.OrgUnitId))).OrderBy(e => e.FullName).Select(e => new { e.Id, e.EmployeeNo, e.FullName }).ToListAsync(ct);
        var rec = await db.AnnouncementReceipts.AsNoTracking().Where(r => r.AnnouncementId == id).ToDictionaryAsync(r => r.EmployeeId, ct);
        var rows = people.Select(p => new AckReportRow(p.Id, p.EmployeeNo, p.FullName, rec.ContainsKey(p.Id), rec.TryGetValue(p.Id, out var r) ? r.AckedAt : null)).ToList();
        return new AckReport(id, a.Title, rows.Count, rows.Count(x => x.Read), rows.Count(x => x.AckedAt != null), rows);
    }
}
