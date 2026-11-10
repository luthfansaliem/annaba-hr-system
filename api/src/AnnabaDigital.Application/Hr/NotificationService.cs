using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Hr;

public sealed record NotificationView(Guid Id, string Kind, string Title, string? Body, string? Link, DateTimeOffset CreatedAt, bool Read);
public sealed record NotificationList(int Unread, IReadOnlyList<NotificationView> Items);

/// <summary>
/// In-app notifications (ADM-006): a short message for one person, shown behind the bell. Producers call <see cref="Notify"/> inside their own unit of work (like the webhook queue),
/// so a message exists exactly when the change it announces does. Everyone reads only their own; read messages are purged after 90 days.
/// </summary>
public sealed class NotificationService(IAppDb db, IClock clock, ManagerScope managers)
{
    public const int KeepReadDays = 90; public const int PageSize = 30;

    /// <summary>Adds a message for <paramref name="userId"/> (nothing happens for people without a sign-in). The caller saves.</summary>
    public void Notify(Guid? userId, string kind, string title, string? body = null, string? link = null)
    {
        if (userId is not { } id) return;
        if (link is { Length: > 0 } && (!link.StartsWith('/') || link.StartsWith("//"))) link = null; // in-app paths only
        db.Notifications.Add(new UserNotification { UserId = id, Kind = kind, Title = Cut(title, 200), Body = body is null ? null : Cut(body, 500), Link = link, CreatedAt = clock.UtcNow });
    }

    /// <summary>Like <see cref="Notify"/>, but stays quiet while the same person still has an unread message of this kind for the same link (a recalculated pay run does not nag twice).</summary>
    public async Task NotifyOnceAsync(Guid userId, string kind, string title, string? body, string link, CancellationToken ct = default)
    {
        if (await db.Notifications.AnyAsync(n => n.UserId == userId && n.Kind == kind && n.Link == link && n.ReadAt == null, ct)) return;
        Notify(userId, kind, title, body, link);
    }

    /// <summary>The employee's manager and whoever the manager has delegated approvals to (only those with a sign-in).</summary>
    public async Task<IReadOnlyList<Guid>> ApproverUsersAsync(Guid employeeId, CancellationToken ct = default)
    {
        var managerId = await db.Employments.AsNoTracking().Where(m => m.EmployeeId == employeeId && m.EffectiveTo == null).Select(m => m.ManagerId).FirstOrDefaultAsync(ct);
        if (managerId is null) return [];
        var ids = new List<Guid> { managerId.Value }; ids.AddRange(await managers.DelegatesOfAsync(managerId, ct));
        return await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id) && e.UserId != null).Select(e => e.UserId!.Value).ToListAsync(ct);
    }

    /// <summary>Active users holding a permission, e.g. everyone who can approve a pay run.</summary>
    public async Task<IReadOnlyList<Guid>> UsersWithPermissionAsync(string permission, CancellationToken ct = default)
        => await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active && u.UserRoles.Any(r => r.Role!.ArchivedAt == null && r.Role.Permissions.Any(p => p.PermissionKey == permission))).Select(u => u.Id).ToListAsync(ct);

    public async Task<NotificationList> ListAsync(bool unreadOnly, SessionPrincipal actor, CancellationToken ct = default)
    {
        var mine = db.Notifications.AsNoTracking().Where(n => n.UserId == actor.UserId);
        var unread = await mine.CountAsync(n => n.ReadAt == null, ct);
        var rows = await mine.Where(n => !unreadOnly || n.ReadAt == null).OrderByDescending(n => n.CreatedAt).Take(PageSize).ToListAsync(ct);
        return new NotificationList(unread, [.. rows.Select(n => new NotificationView(n.Id, n.Kind, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt != null))]);
    }

    public async Task MarkReadAsync(Guid id, SessionPrincipal actor, CancellationToken ct = default)
        => await db.Notifications.Where(n => n.Id == id && n.UserId == actor.UserId && n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, clock.UtcNow), ct);

    public async Task<int> MarkAllReadAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var now = clock.UtcNow; var cutoff = now.AddDays(-KeepReadDays);
        var n = await db.Notifications.Where(x => x.UserId == actor.UserId && x.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, now), ct);
        await db.Notifications.Where(x => x.UserId == actor.UserId && x.ReadAt != null && x.ReadAt < cutoff).ExecuteDeleteAsync(ct);   // tidy up this person's old, read messages
        return n;
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];
}
