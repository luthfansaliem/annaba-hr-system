using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Hr;

public sealed record WebhookView(Guid Id, string Url, string[] Events, bool Active, string? Description, DateTimeOffset CreatedAt, DateTimeOffset? LastDeliveredAt, int Pending, int Dead);
public sealed record CreateWebhookResult(WebhookView Webhook, string Secret);
public sealed record WebhookRequest(string? Url, string[]? Events, bool? Active, string? Description);
public sealed record DeliveryView(Guid Id, string Event, string Status, int Attempts, int? LastHttpStatus, string? LastError, DateTimeOffset CreatedAt, DateTimeOffset? DeliveredAt, DateTimeOffset NextAttemptAt);

/// <summary>What leaves the building. The transport is a port so tests (and any other host) can swap it; the real one connects only to public addresses.</summary>
public interface IWebhookTransport { Task<(int Status, string? Error)> SendAsync(string url, string body, IReadOnlyDictionary<string, string> headers, CancellationToken ct); }

public static class WebhookEvents
{
    public static readonly string[] All = ["employee.created", "employee.terminated", "leave.approved", "leave.rejected", "payroll.locked", "payroll.paid"];
}

/// <summary>Decides whether an address may be called. Used when saving a URL (cheap, no DNS) and again by the transport on the address actually connected to (the one that matters against DNS tricks).</summary>
public static class WebhookUrlPolicy
{
    public static string? Validate(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 500 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return "Enter a full https:// address.";
        if (u.Scheme != Uri.UriSchemeHttps) return "Only https:// addresses are allowed.";
        if (!string.IsNullOrEmpty(u.UserInfo)) return "The address cannot contain a username or password.";
        var host = u.IdnHost.ToLowerInvariant();
        if (host is "localhost" || host.EndsWith(".localhost") || host.EndsWith(".local") || host.EndsWith(".internal") || host.EndsWith(".lan") || !host.Contains('.') && !IPAddress.TryParse(host.Trim('[', ']'), out _)) return "That address is not a public internet address.";
        if (IPAddress.TryParse(host.Trim('[', ']'), out var ip) && !IsPublic(ip)) return "That address is not a public internet address.";
        return null;
    }

    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6) { var b6 = ip.GetAddressBytes(); return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || (b6[0] & 0xFE) == 0xFC); } // fc00::/7 unique-local
        var b = ip.GetAddressBytes();
        return !(b[0] == 10 || b[0] == 127 || b[0] == 0 || b[0] >= 224                                // private, loopback, "this network", multicast/reserved
            || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168                    // private
            || b[0] == 169 && b[1] == 254                                                               // link-local, incl. the cloud metadata address
            || b[0] == 100 && b[1] is >= 64 and <= 127                                                  // carrier-grade NAT
            || b[0] == 192 && b[1] == 0 && b[2] == 0 || b[0] == 198 && b[1] is 18 or 19);              // protocol assignments, benchmarking
    }
}

/// <summary>Management of webhook endpoints (HR configuration permission and a recent password check: it decides where HR events are sent) and the queue that hands events to them.</summary>
public sealed class WebhookService(IAppDb db, IClock clock, ISecretProtector protector, SettingsProvider settings, AuditWriter audit)
{
    private async Task GuardAsync(SessionPrincipal actor, CancellationToken ct) => Guards.RequireRecentReauthForCritical(actor, (await settings.GetAsync(ct)) with { RequireReauthForCritical = true }, clock);

    private static string NewSecret() => "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    private string Seal(Guid id, string secret) { var s = protector.Protect(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"webhook:{id}")); return $"{s.KeyVersion}.{Convert.ToBase64String(s.Nonce)}.{Convert.ToBase64String(s.Ciphertext)}"; }
    public string Open(WebhookEndpoint e) { var p = e.SecretEnc.Split('.'); return Encoding.UTF8.GetString(protector.Unprotect(new ProtectedSecret(Convert.FromBase64String(p[2]), Convert.FromBase64String(p[1]), int.Parse(p[0])), Encoding.UTF8.GetBytes($"webhook:{e.Id}"))); }

    private static string[] Normalise(string[]? events, Dictionary<string, string> errors)
    {
        var list = (events ?? []).Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0).Distinct().ToArray();
        if (list.Length == 0) errors["events"] = "Choose at least one event."; else if (list.Except(WebhookEvents.All).Any()) errors["events"] = "Unknown event. Use: " + string.Join(", ", WebhookEvents.All);
        return list;
    }

    private async Task<WebhookView> ViewAsync(WebhookEndpoint e, CancellationToken ct) => new(e.Id, e.Url, e.Events.Split(',', StringSplitOptions.RemoveEmptyEntries), e.Active, e.Description, e.CreatedAt, e.LastDeliveredAt,
        await db.WebhookDeliveries.CountAsync(d => d.EndpointId == e.Id && d.Status == DeliveryStatus.Pending, ct), await db.WebhookDeliveries.CountAsync(d => d.EndpointId == e.Id && d.Status == DeliveryStatus.Dead, ct));

    public async Task<IReadOnlyList<WebhookView>> ListAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct); var list = new List<WebhookView>();
        foreach (var e in await db.WebhookEndpoints.AsNoTracking().OrderBy(x => x.CreatedAt).ToListAsync(ct)) list.Add(await ViewAsync(e, ct));
        return list;
    }

    public async Task<CreateWebhookResult> CreateAsync(WebhookRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct); var errors = new Dictionary<string, string>();
        if (WebhookUrlPolicy.Validate(r.Url) is { } why) errors["url"] = why;
        var events = Normalise(r.Events, errors); var desc = r.Description?.Trim(); if (desc is { Length: > 200 }) errors["description"] = "Keep it under 200 characters.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the webhook.", errors);
        if (await db.WebhookEndpoints.CountAsync(ct) >= 10) throw DomainException.Invariant("At most 10 webhook endpoints are allowed.");
        var e = new WebhookEndpoint { Url = r.Url!.Trim(), Events = string.Join(',', events), Description = string.IsNullOrEmpty(desc) ? null : desc, Active = r.Active ?? true, CreatedAt = clock.UtcNow };
        var secret = NewSecret(); e.SecretEnc = Seal(e.Id, secret);
        db.WebhookEndpoints.Add(e);
        audit.Record("hr.webhook.create", "webhook", e.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["url"] = e.Url, ["events"] = e.Events });
        await db.SaveChangesAsync(ct);
        return new CreateWebhookResult(await ViewAsync(e, ct), secret);
    }

    public async Task<WebhookView> UpdateAsync(Guid id, WebhookRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct); var e = await db.WebhookEndpoints.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Webhook"); var errors = new Dictionary<string, string>();
        if (r.Url is not null) { if (WebhookUrlPolicy.Validate(r.Url) is { } why) errors["url"] = why; else e.Url = r.Url.Trim(); }
        if (r.Events is not null) { var ev = Normalise(r.Events, errors); if (!errors.ContainsKey("events")) e.Events = string.Join(',', ev); }
        if (r.Description is not null) e.Description = r.Description.Trim() is { Length: > 0 } d ? d[..Math.Min(d.Length, 200)] : null;
        if (errors.Count > 0) throw DomainException.Validation("Please check the webhook.", errors);
        if (r.Active is { } a) e.Active = a;
        audit.Record("hr.webhook.update", "webhook", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["url"] = e.Url, ["active"] = e.Active.ToString() });
        await db.SaveChangesAsync(ct); return await ViewAsync(e, ct);
    }

    public async Task DeleteAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct); var e = await db.WebhookEndpoints.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Webhook");
        db.WebhookEndpoints.Remove(e); audit.Record("hr.webhook.delete", "webhook", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["url"] = e.Url });
        await db.SaveChangesAsync(ct);
    }

    public async Task<CreateWebhookResult> RotateSecretAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct); var e = await db.WebhookEndpoints.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Webhook");
        var secret = NewSecret(); e.SecretEnc = Seal(e.Id, secret);
        audit.Record("hr.webhook.rotate_secret", "webhook", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct); return new CreateWebhookResult(await ViewAsync(e, ct), secret);
    }

    public async Task<IReadOnlyList<DeliveryView>> DeliveriesAsync(Guid id, SessionPrincipal actor, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct);
        if (!await db.WebhookEndpoints.AnyAsync(x => x.Id == id, ct)) throw DomainException.NotFound("Webhook");
        return [.. (await db.WebhookDeliveries.AsNoTracking().Where(d => d.EndpointId == id).OrderByDescending(d => d.CreatedAt).Take(50).ToListAsync(ct))
            .Select(d => new DeliveryView(d.Id, d.Event, d.Status.ToString().ToLowerInvariant(), d.Attempts, d.LastHttpStatus, d.LastError, d.CreatedAt, d.DeliveredAt, d.NextAttemptAt))];
    }

    /// <summary>Sends a "ping" so the receiver can check its signature code. It goes through the same queue and retries as real events.</summary>
    public async Task PingAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await GuardAsync(actor, ct); var e = await db.WebhookEndpoints.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Webhook");
        db.WebhookDeliveries.Add(NewDelivery(e.Id, "ping", new { message = "Test event from Annaba HR" }, clock.UtcNow));
        audit.Record("hr.webhook.ping", "webhook", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
    }

    private static WebhookDelivery NewDelivery(Guid endpointId, string evt, object data, DateTimeOffset now)
    {
        var id = Guid.CreateVersion7();
        return new WebhookDelivery { Id = id, EndpointId = endpointId, Event = evt, CreatedAt = now, NextAttemptAt = now, PayloadJson = JsonSerializer.Serialize(new { id, type = evt, createdAt = now, data }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) };
    }

    /// <summary>Queues <paramref name="evt"/> for every active endpoint subscribed to it. Adds rows to the current unit of work: the caller saves them together with the change itself, so an event exists exactly when the change does.</summary>
    public async Task EnqueueAsync(string evt, object data, CancellationToken ct = default)
    {
        var targets = await db.WebhookEndpoints.AsNoTracking().Where(e => e.Active).Select(e => new { e.Id, e.Events }).ToListAsync(ct);
        foreach (var t in targets.Where(t => t.Events.Split(',').Contains(evt))) db.WebhookDeliveries.Add(NewDelivery(t.Id, evt, data, clock.UtcNow));
    }
}

/// <summary>Delivers due events: signs the body, posts it, backs off 1 min → 5 min → 30 min → 2 h → 6 h, then gives up (dead). Safe with several instances: a delivery is leased with one conditional UPDATE.</summary>
public sealed class WebhookDispatcher(IAppDb db, IClock clock, WebhookService webhooks, IWebhookTransport transport)
{
    public static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(6)];
    public const int MaxAttempts = 6;

    public static string Sign(string secret, long timestamp, string body)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();
    }

    public async Task<int> RunDueAsync(int batch = 20, CancellationToken ct = default)
    {
        var now = clock.UtcNow; var due = await db.WebhookDeliveries.AsNoTracking().Where(d => d.Status == DeliveryStatus.Pending && d.NextAttemptAt <= now).OrderBy(d => d.NextAttemptAt).Take(batch).Select(d => new { d.Id, d.NextAttemptAt }).ToListAsync(ct);
        var done = 0;
        foreach (var d in due)
        {
            var lease = now.AddMinutes(2); var old = d.NextAttemptAt;
            if (await db.WebhookDeliveries.Where(x => x.Id == d.Id && x.Status == DeliveryStatus.Pending && x.NextAttemptAt == old).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAt, lease), ct) == 0) continue; // another instance has it
            db.ResetTracking();
            var del = await db.WebhookDeliveries.FirstAsync(x => x.Id == d.Id, ct); var ep = await db.WebhookEndpoints.AsNoTracking().FirstOrDefaultAsync(x => x.Id == del.EndpointId, ct);
            if (ep is null || !ep.Active) { del.Status = DeliveryStatus.Dead; del.LastError = "The endpoint was removed or switched off."; await db.SaveChangesAsync(ct); continue; }
            var ts = clock.UtcNow.ToUnixTimeSeconds(); var body = del.PayloadJson;
            var headers = new Dictionary<string, string> { ["X-Annaba-Event"] = del.Event, ["X-Annaba-Delivery"] = del.Id.ToString(), ["X-Annaba-Timestamp"] = ts.ToString(), ["X-Annaba-Signature"] = $"v1={Sign(webhooks.Open(ep), ts, body)}" };
            (int Status, string? Error) result;
            try { result = await transport.SendAsync(ep.Url, body, headers, ct); } catch (Exception e) when (e is not OperationCanceledException) { result = (0, e.Message); }
            del.Attempts++; del.LastHttpStatus = result.Status == 0 ? null : result.Status;
            if (result.Status is >= 200 and < 300) { del.Status = DeliveryStatus.Delivered; del.DeliveredAt = clock.UtcNow; del.LastError = null; await db.WebhookEndpoints.Where(x => x.Id == ep.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastDeliveredAt, clock.UtcNow), ct); }
            else
            {
                del.LastError = (result.Error ?? $"The receiver answered {result.Status}.")[..Math.Min((result.Error ?? $"The receiver answered {result.Status}.").Length, 300)];
                if (del.Attempts >= MaxAttempts) del.Status = DeliveryStatus.Dead; else del.NextAttemptAt = clock.UtcNow + Backoff[Math.Min(del.Attempts - 1, Backoff.Length - 1)];
            }
            await db.SaveChangesAsync(ct); done++;
        }
        return done;
    }
}
