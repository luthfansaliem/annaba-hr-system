using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Hr;

public sealed record PushSubscribeRequest(string? Endpoint, string? P256dh, string? Auth, string? Device);
public sealed record PushDeviceView(Guid Id, string? Device, DateTimeOffset CreatedAt, DateTimeOffset? LastSuccessAt);

/// <summary>
/// Stage 14: push notifications to the installed app or browser. A device subscribes with the endpoint its push service gave it; only the big browser push services are accepted,
/// so the server never posts to an address someone made up (no server-side request forgery). What is pushed is exactly what the bell shows.
/// </summary>
public sealed class PushService(IAppDb db, IClock clock, IPushKeys keys, AuditWriter audit)
{
    /// <summary>Hosts of the push services of Chrome/Edge/Android (FCM), Firefox, Safari/iOS and Windows.</summary>
    private static readonly string[] Hosts = ["fcm.googleapis.com", "updates.push.services.mozilla.com", "web.push.apple.com", ".push.apple.com", ".notify.windows.com", "android.googleapis.com"];
    public static bool AllowedEndpoint(string endpoint) => Uri.TryCreate(endpoint, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.IsDefaultPort && Hosts.Any(h => h.StartsWith('.') ? u.Host.EndsWith(h, StringComparison.OrdinalIgnoreCase) : u.Host.Equals(h, StringComparison.OrdinalIgnoreCase));

    private static byte[]? B64Url(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { var t = s.Trim().Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(t.PadRight(t.Length + (4 - t.Length % 4) % 4, '=')); } catch (FormatException) { return null; }
    }

    public Task<string> PublicKeyAsync(CancellationToken ct = default) => keys.PublicKeyAsync(ct);

    public async Task<IReadOnlyList<PushDeviceView>> DevicesAsync(SessionPrincipal actor, CancellationToken ct = default)
        => [.. (await db.PushSubscriptions.AsNoTracking().Where(s => s.UserId == actor.UserId).OrderByDescending(s => s.CreatedAt).ToListAsync(ct)).Select(s => new PushDeviceView(s.Id, s.Device, s.CreatedAt, s.LastSuccessAt))];

    public async Task<PushDeviceView> SubscribeAsync(PushSubscribeRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        var endpoint = r.Endpoint?.Trim() ?? "";
        if (endpoint.Length is 0 or > 1000 || !AllowedEndpoint(endpoint)) errors["endpoint"] = "This browser's push service is not supported.";
        if (B64Url(r.P256dh) is not { Length: 65 } key || key[0] != 0x04) errors["p256dh"] = "The device key is not valid.";
        if (B64Url(r.Auth) is not { Length: 16 }) errors["auth"] = "The device secret is not valid.";
        if (errors.Count > 0) throw DomainException.Validation("Push notifications cannot be turned on for this device.", errors);
        var sub = await db.PushSubscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);
        if (sub is not null && sub.UserId != actor.UserId) { db.PushSubscriptions.Remove(sub); await db.SaveChangesAsync(ct); sub = null; }   // a shared device that someone else used before
        if (sub is null) { sub = new PushSubscription { UserId = actor.UserId, Endpoint = endpoint, CreatedAt = clock.UtcNow }; db.PushSubscriptions.Add(sub); }
        sub.P256dh = r.P256dh!.Trim(); sub.Auth = r.Auth!.Trim(); sub.Device = r.Device?.Trim() is { Length: > 0 } d ? d[..Math.Min(d.Length, 200)] : null; sub.Failures = 0;
        if (await db.PushSubscriptions.CountAsync(s => s.UserId == actor.UserId, ct) >= 10 && sub.Id == default) throw DomainException.Invariant("Push is already on for 10 devices. Turn it off on one first.");
        audit.Record("account.push_subscribe", "user", actor.UserId.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return new PushDeviceView(sub.Id, sub.Device, sub.CreatedAt, sub.LastSuccessAt);
    }

    public async Task UnsubscribeAsync(Guid? id, string? endpoint, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var q = db.PushSubscriptions.Where(s => s.UserId == actor.UserId);
        var rows = id is { } i ? await q.Where(s => s.Id == i).ToListAsync(ct) : endpoint is { } e ? await q.Where(s => s.Endpoint == e).ToListAsync(ct) : [];
        db.PushSubscriptions.RemoveRange(rows);
        audit.Record("account.push_unsubscribe", "user", actor.UserId.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
    }
}
