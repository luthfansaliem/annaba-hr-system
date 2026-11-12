using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Infrastructure.Persistence;

namespace AnnabaDigital.Infrastructure.Push;

/// <summary>
/// Web Push without a third-party library: RFC 8291 message encryption (aes128gcm with an ephemeral P-256 key, HKDF and AES-GCM) and RFC 8292 VAPID (an ES256 JWT signed with the
/// server's key). The VAPID key pair is made once and stored in the system settings, the private half encrypted with the application's secret protector.
/// </summary>
public static class WebPushCrypto
{
    public static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] FromB64Url(string s) { var t = s.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(t.PadRight(t.Length + (4 - t.Length % 4) % 4, '=')); }

    /// <summary>Uncompressed P-256 point: 0x04 || X || Y.</summary>
    public static byte[] PublicPoint(ECParameters p) => [0x04, .. p.Q.X!, .. p.Q.Y!];
    public static ECParameters FromPoint(byte[] point) => new() { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = point[1..33], Y = point[33..65] } };

    /// <summary>Encrypts <paramref name="plaintext"/> for one subscription (single record, as browsers expect). Returns the whole request body.</summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] uaPublic, byte[] authSecret, ECDiffieHellman? ephemeral = null, byte[]? salt = null)
    {
        using var server = ephemeral ?? ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var asPublic = PublicPoint(server.ExportParameters(false));
        using var ua = ECDiffieHellman.Create(FromPoint(uaPublic));
        var ecdh = server.DeriveRawSecretAgreement(ua.PublicKey);
        var keyInfo = Encoding.ASCII.GetBytes("WebPush: info\0").Concat(uaPublic).Concat(asPublic).ToArray();
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdh, 32, authSecret, keyInfo);
        salt ??= RandomNumberGenerator.GetBytes(16);
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));
        var padded = plaintext.Append((byte)0x02).ToArray();   // last (and only) record delimiter
        var cipher = new byte[padded.Length]; var tag = new byte[16];
        using (var gcm = new AesGcm(cek, 16)) gcm.Encrypt(nonce, padded, cipher, tag);
        var header = new byte[16 + 4 + 1 + asPublic.Length];
        salt.CopyTo(header, 0); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), 4096); header[20] = (byte)asPublic.Length; asPublic.CopyTo(header, 21);
        return [.. header, .. cipher, .. tag];
    }

    /// <summary>The VAPID Authorization header value for a push service origin.</summary>
    public static string Vapid(string endpoint, ECDsa key, byte[] publicPoint, string subject, DateTimeOffset now)
    {
        var aud = new Uri(endpoint).GetLeftPart(UriPartial.Authority);
        var head = B64Url(Encoding.UTF8.GetBytes("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"));
        var body = B64Url(JsonSerializer.SerializeToUtf8Bytes(new { aud, exp = now.AddHours(12).ToUnixTimeSeconds(), sub = subject }));
        var sig = key.SignData(Encoding.ASCII.GetBytes($"{head}.{body}"), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"vapid t={head}.{body}.{B64Url(sig)}, k={B64Url(publicPoint)}";
    }
}

/// <summary>Creates the VAPID key pair on first use and keeps it in the system settings (private key encrypted).</summary>
public sealed class VapidKeys(IServiceScopeFactory scopes, ISecretProtector protector) : IPushKeys
{
    private const string Key = "push.vapid";
    private static readonly byte[] Aad = Encoding.UTF8.GetBytes("system:push.vapid");
    private (byte[] Public, byte[] Private)? _cache;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<(byte[] Public, byte[] Private)> GetAsync(CancellationToken ct = default)
    {
        if (_cache is { } c) return c;
        await _gate.WaitAsync(ct);
        try
        {
            if (_cache is { } c2) return c2;
            using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AnnabaDigitalDbContext>();
            var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == Key, ct);
            if (row?.ValueCiphertext is { } cipher && row.ValueJson is { } json)
            {
                var meta = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
                var priv = protector.Unprotect(new ProtectedSecret(cipher, Convert.FromBase64String(meta["nonce"]), row.KeyVersion), Aad);
                _cache = (WebPushCrypto.FromB64Url(meta["public"]), priv);
            }
            else
            {
                using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256); var p = ec.ExportParameters(true);
                var pub = WebPushCrypto.PublicPoint(p); var sealedKey = protector.Protect(p.D!, Aad);
                db.Settings.Add(new SystemSetting { Key = Key, IsSecret = true, ValueCiphertext = sealedKey.Ciphertext, KeyVersion = sealedKey.KeyVersion,
                    ValueJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["public"] = WebPushCrypto.B64Url(pub), ["nonce"] = Convert.ToBase64String(sealedKey.Nonce) }), UpdatedAt = DateTimeOffset.UtcNow });
                try { await db.SaveChangesAsync(ct); _cache = (pub, p.D!); }
                catch (DbUpdateException) { _cache = null; return await GetAsync(ct); }   // another instance made it at the same moment: use theirs
            }
            return _cache!.Value;
        }
        finally { _gate.Release(); }
    }

    public async Task<string> PublicKeyAsync(CancellationToken ct = default) => WebPushCrypto.B64Url((await GetAsync(ct)).Public);
}

/// <summary>
/// Sends new in-app notifications as push messages to every device the person subscribed. Runs every few seconds; a notification older than an hour is no longer pushed
/// (it is still in the bell). Payloads hold the title, a short body and the link — never amounts or personal data beyond what the bell shows.
/// </summary>
public sealed class PushWorker(IServiceScopeFactory scopes, VapidKeys keys, IHttpClientFactory http, IAppLinks links, ILogger<PushWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { if (await SendBatchAsync(stoppingToken) == 0) await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { log.LogError("Push worker iteration failed ({Error}).", e.GetType().Name); try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } catch (OperationCanceledException) { return; } }
        }
    }

    public async Task<int> SendBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AnnabaDigitalDbContext>();
        var since = DateTimeOffset.UtcNow.AddHours(-1);
        var batch = await db.Notifications.IgnoreQueryFilters().Where(n => n.PushedAt == null && n.CreatedAt > since).OrderBy(n => n.CreatedAt).Take(100).ToListAsync(ct);
        if (batch.Count == 0) return 0;
        var users = batch.Select(n => n.UserId).Distinct().ToList();
        var subs = await db.PushSubscriptions.IgnoreQueryFilters().Where(s => users.Contains(s.UserId)).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        if (subs.Count > 0)
        {
            var (pub, priv) = await keys.GetAsync(ct);
            using var signer = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = priv, Q = WebPushCrypto.FromPoint(pub).Q });
            var client = http.CreateClient("webpush");
            foreach (var n in batch)
                foreach (var s in subs.Where(s => s.UserId == n.UserId))
                {
                    try
                    {
                        var payload = JsonSerializer.SerializeToUtf8Bytes(new { title = n.Title, body = n.Body, link = n.Link ?? "/" });
                        using var req = new HttpRequestMessage(HttpMethod.Post, s.Endpoint) { Content = new ByteArrayContent(WebPushCrypto.Encrypt(payload, WebPushCrypto.FromB64Url(s.P256dh), WebPushCrypto.FromB64Url(s.Auth))) };
                        req.Content.Headers.ContentType = new("application/octet-stream"); req.Content.Headers.ContentEncoding.Add("aes128gcm");
                        req.Headers.Add("TTL", "86400"); req.Headers.Add("Urgency", "normal"); req.Headers.TryAddWithoutValidation("Authorization", WebPushCrypto.Vapid(s.Endpoint, signer, pub, links.Origin, now));
                        using var res = await client.SendAsync(req, ct);
                        if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) db.PushSubscriptions.Remove(s);   // the device unsubscribed
                        else if (res.IsSuccessStatusCode) { s.LastSuccessAt = now; s.Failures = 0; }
                        else if (++s.Failures >= 20) db.PushSubscriptions.Remove(s);
                    }
                    catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { if (++s.Failures >= 20) db.PushSubscriptions.Remove(s); }
                }
        }
        foreach (var n in batch) n.PushedAt = now;
        await db.SaveChangesAsync(ct);
        return batch.Count;
    }
}
