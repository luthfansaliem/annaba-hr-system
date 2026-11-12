using System.Net;
using System.Security.Cryptography;
using System.Text;
using AnnabaDigital.Infrastructure.Push;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Stage 14: Web Push message encryption (RFC 8291) and VAPID (RFC 8292), device subscriptions, and the payslip lock.</summary>
[Collection("db")]
public sealed class PushTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private static byte[] B(string s) => WebPushCrypto.FromB64Url(s);

    /// <summary>What a browser does with a push message: undo the RFC 8291 encryption with its own private key and auth secret.</summary>
    private static byte[] Decrypt(byte[] body, ECDiffieHellman ua, byte[] authSecret)
    {
        var salt = body[..16]; var idlen = body[20]; var asPublic = body[21..(21 + idlen)]; var cipher = body[(21 + idlen)..];
        var uaPublic = WebPushCrypto.PublicPoint(ua.ExportParameters(false));
        using var server = ECDiffieHellman.Create(WebPushCrypto.FromPoint(asPublic));
        var ecdh = ua.DeriveRawSecretAgreement(server.PublicKey);
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdh, 32, authSecret, Encoding.ASCII.GetBytes("WebPush: info\0").Concat(uaPublic).Concat(asPublic).ToArray());
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));
        var plain = new byte[cipher.Length - 16];
        using (var gcm = new AesGcm(cek, 16)) gcm.Decrypt(nonce, cipher[..^16], cipher[^16..], plain);
        Assert.Equal(0x02, plain[^1]);                                                                 // the last-record delimiter
        return plain[..^1];
    }

    [Fact]
    public void A_push_message_decrypts_on_the_receiving_device_and_matches_the_RFC_8291_example()
    {
        // round trip with a fresh "browser" key pair
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256); var auth = RandomNumberGenerator.GetBytes(16);
        var body = WebPushCrypto.Encrypt("{\"title\":\"Payslip ready\"}"u8.ToArray(), WebPushCrypto.PublicPoint(ua.ExportParameters(false)), auth);
        Assert.Equal(4096u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(16)));   // record size
        Assert.Equal(65, body[20]);                                                                     // the sender's public key follows in the header
        Assert.Equal("{\"title\":\"Payslip ready\"}", Encoding.UTF8.GetString(Decrypt(body, ua, auth)));

        // RFC 8291 appendix A: the same inputs give the same message
        var asPublic = B("BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8");
        var asParams = WebPushCrypto.FromPoint(asPublic); asParams.D = B("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw");
        var expected = "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN";
        var rfc = WebPushCrypto.Encrypt("When I grow up, I want to be a watermelon"u8.ToArray(), B("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4"),
            B("BTBZMqHH6r4Tts7J_aSIgg"), ECDiffieHellman.Create(asParams), B("DGv6ra1nlYgDCS1FRnbzlw"));
        Assert.Equal(expected, WebPushCrypto.B64Url(rfc));
    }

    [Fact]
    public void The_VAPID_header_carries_a_valid_ES256_token_for_the_push_service()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var pub = WebPushCrypto.PublicPoint(key.ExportParameters(false));
        var header = WebPushCrypto.Vapid("https://fcm.googleapis.com/fcm/send/abc", key, pub, "https://hris.example.com", DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
        Assert.StartsWith("vapid t=", header); Assert.EndsWith($", k={WebPushCrypto.B64Url(pub)}", header);
        var jwt = header["vapid t=".Length..header.IndexOf(',')]; var parts = jwt.Split('.');
        var claims = Encoding.UTF8.GetString(B(parts[1]));
        Assert.Contains("\"aud\":\"https://fcm.googleapis.com\"", claims); Assert.Contains("\"exp\":1800043200", claims);   // 12 hours ahead
        using var verify = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = WebPushCrypto.FromPoint(pub).Q });
        Assert.True(verify.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), B(parts[2]), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public async Task Devices_subscribe_only_with_a_known_push_service_and_valid_keys()
    {
        var u = await _seed.UserAsync(role: "user"); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password);
        var key = (await (await b.Get("/api/me/push/key")).Body()).GetProperty("publicKey").GetString()!;
        Assert.Equal(65, B(key).Length);
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p256 = WebPushCrypto.B64Url(WebPushCrypto.PublicPoint(ua.ExportParameters(false))); var auth = WebPushCrypto.B64Url(RandomNumberGenerator.GetBytes(16));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await b.Post("/api/me/push/subscribe", new { endpoint = "https://attacker.example/collect", p256dh = p256, auth })).StatusCode);   // not a push service
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await b.Post("/api/me/push/subscribe", new { endpoint = "http://fcm.googleapis.com/fcm/send/x", p256dh = p256, auth })).StatusCode);   // not https
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await b.Post("/api/me/push/subscribe", new { endpoint = "https://fcm.googleapis.com/fcm/send/x", p256dh = "short", auth })).StatusCode);
        var endpoint = "https://fcm.googleapis.com/fcm/send/" + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.OK, (await b.Post("/api/me/push/subscribe", new { endpoint, p256dh = p256, auth, device = "Chrome on Android" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Post("/api/me/push/subscribe", new { endpoint, p256dh = p256, auth })).StatusCode);   // the same device again: still one
        Assert.Equal(1, (await (await b.Get("/api/me/push/devices")).Body()).GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await b.Post("/api/me/push/unsubscribe", new { endpoint })).StatusCode);
        Assert.Equal(0, (await (await b.Get("/api/me/push/devices")).Body()).GetArrayLength());
    }

    [Fact]
    public async Task A_locked_payslip_list_asks_for_the_password_again()
    {
        var hr = TestBrowser.Create(fx.Factory); await hr.LoginAsync((await _seed.UserAsync(role: "hr_admin")).Email, Seed.Password);
        var u = await _seed.UserAsync(role: "user");
        Assert.Equal(HttpStatusCode.Created, (await hr.Post("/api/hr/employees", new { fullName = "Locked Slip", hireDate = DateTime.UtcNow.AddYears(-1).ToString("yyyy-MM-dd"), jobTitle = "Analyst", userId = u.Id })).StatusCode);
        var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password);
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/hr/payroll/payslips")).StatusCode);
        Assert.True((await (await b.SendAsync(HttpMethod.Put, "/api/hr/payroll/payslips/lock", new { locked = true })).Body()).GetProperty("locked").GetBoolean());
        for (var i = 0; i < 4; i++) { fx.Clock.Advance(TimeSpan.FromMinutes(4)); await b.Get("/api/hr/me/notifications"); }   // past the recent-password window, session kept alive
        var locked = await b.Get("/api/hr/payroll/payslips");
        Assert.Contains(locked.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.Contains("reauth", await locked.Content.ReadAsStringAsync());
        await b.Post("/api/auth/reauth", new { password = Seed.Password });
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/hr/payroll/payslips")).StatusCode);
    }
}
