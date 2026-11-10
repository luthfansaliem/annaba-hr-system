using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class HrWebhookTests(PostgresFixture fx)
{
    [Theory]
    [InlineData("https://hooks.example.com/annaba", true)]
    [InlineData("https://hooks.example.com:8443/a?b=c", true)]
    [InlineData("http://hooks.example.com/annaba", false)]          // not encrypted
    [InlineData("ftp://hooks.example.com/", false)]
    [InlineData("not a url", false)]
    [InlineData("https://localhost/hook", false)]
    [InlineData("https://127.0.0.1/hook", false)]                   // loopback
    [InlineData("https://10.1.2.3/hook", false)]                    // private ranges
    [InlineData("https://172.16.5.5/hook", false)]
    [InlineData("https://192.168.0.5/hook", false)]
    [InlineData("https://169.254.169.254/latest/meta-data", false)] // the cloud metadata address
    [InlineData("https://100.64.0.1/hook", false)]                  // carrier-grade NAT
    [InlineData("https://[::1]/hook", false)]
    [InlineData("https://[fd00::1]/hook", false)]
    [InlineData("https://intranet/hook", false)]                    // single-label internal name
    [InlineData("https://printer.local/hook", false)]
    [InlineData("https://user:secret@hooks.example.com/hook", false)]
    public void The_url_policy_only_lets_public_https_addresses_through(string url, bool allowed) => Assert.Equal(allowed, WebhookUrlPolicy.Validate(url) is null);

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("10.0.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::ffff:10.0.0.1", false)]                          // an IPv4 private address dressed as IPv6
    [InlineData("fe80::1", false)]
    [InlineData("2606:4700:4700::1111", true)]
    public void The_address_check_used_when_connecting_rejects_non_public_addresses(string ip, bool pub) => Assert.Equal(pub, WebhookUrlPolicy.IsPublic(IPAddress.Parse(ip)));

    private static async Task<TestBrowser> As(ApiFactory f, Seed seed, string role, bool reauth = false)
    {
        var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return b;
    }

    [Fact]
    public async Task Webhook_endpoints_are_managed_by_hr_configuration_with_a_password_check_and_the_secret_is_shown_once()
    {
        var seed = new Seed(fx.Factory, fx.Clock); var hr = await As(fx.Factory, seed, "hr_admin", reauth: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As(fx.Factory, seed, "hr_head", reauth: true)).Post("/api/hr/webhooks", new { url = "https://hooks.example.com/x", events = new[] { "employee.created" } })).StatusCode);
        Assert.Equal("reauth_required", await (await (await As(fx.Factory, seed, "hr_admin")).Get("/api/hr/webhooks")).ErrorCode());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/webhooks", new { url = "http://hooks.example.com/x", events = new[] { "employee.created" } })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/webhooks", new { url = "https://169.254.169.254/x", events = new[] { "employee.created" } })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/webhooks", new { url = "https://hooks.example.com/x", events = new[] { "salary.leaked" } })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/webhooks", new { url = "https://hooks.example.com/x", events = Array.Empty<string>() })).StatusCode);

        var made = await hr.Post("/api/hr/webhooks", new { url = "https://hooks.example.com/x", events = new[] { "employee.created", "payroll.locked" }, description = "ERP" });
        Assert.Equal(HttpStatusCode.Created, made.StatusCode); var body = await made.Body(); var id = body.GetProperty("webhook").GetProperty("id").GetGuid(); var secret = body.GetProperty("secret").GetString()!;
        Assert.StartsWith("whsec_", secret); Assert.True(secret.Length > 30);
        var list = await (await hr.Get("/api/hr/webhooks")).Content.ReadAsStringAsync(); Assert.DoesNotContain(secret, list); Assert.DoesNotContain("secret", list.ToLowerInvariant());   // never listed again
        Assert.DoesNotContain(secret, await seed.Db(db => db.WebhookEndpoints.Where(e => e.Id == id).Select(e => e.SecretEnc).SingleAsync()));                                            // encrypted at rest

        var rotated = (await (await hr.Post($"/api/hr/webhooks/{id}/rotate-secret")).Body()).GetProperty("secret").GetString(); Assert.NotEqual(secret, rotated);
        Assert.Equal(HttpStatusCode.OK, (await hr.SendAsync(HttpMethod.Patch, $"/api/hr/webhooks/{id}", new { active = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await hr.SendAsync(HttpMethod.Delete, $"/api/hr/webhooks/{id}")).StatusCode);
        foreach (var a in new[] { "hr.webhook.create", "hr.webhook.rotate_secret", "hr.webhook.update", "hr.webhook.delete" }) Assert.True(await seed.Db(db => db.AuditEvents.AnyAsync(x => x.Action == a)), a);
        Assert.DoesNotContain((await seed.Db(db => db.AuditEvents.Select(x => new { x.MetaJson, x.DiffJson }).ToListAsync())).Select(x => x.MetaJson + x.DiffJson), t => t.Contains(secret) || t.Contains(rotated!));
    }

    [Fact]
    public async Task Events_are_queued_with_the_change_signed_delivered_once_retried_with_backoff_and_given_up_on()
    {
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock); var hr = await As(f, seed, "hr_admin", reauth: true); var transport = f.Services.GetRequiredService<FakeWebhookTransport>();
        var created = await (await hr.Post("/api/hr/webhooks", new { url = "https://hooks.example.com/annaba", events = new[] { "employee.created", "employee.terminated" } })).Body();
        var secret = created.GetProperty("secret").GetString()!; var endpoint = created.GetProperty("webhook").GetProperty("id").GetGuid();
        var other = await (await hr.Post("/api/hr/webhooks", new { url = "https://elsewhere.example.com/x", events = new[] { "payroll.locked" }, active = true })).Body();   // not subscribed to what happens below
        var off = await (await hr.Post("/api/hr/webhooks", new { url = "https://off.example.com/x", events = new[] { "employee.created" }, active = false })).Body();          // switched off

        var emp = await hr.Post("/api/hr/employees", new { fullName = "Webhook Person", hireDate = "2023-01-02", jobTitle = "Analyst" }); var empId = (await emp.Body()).GetProperty("id").GetGuid();
        var rows = await seed.Db(db => db.WebhookDeliveries.ToListAsync());
        Assert.Single(rows); Assert.Equal(endpoint, rows[0].EndpointId); Assert.Equal("employee.created", rows[0].Event);
        Assert.Contains("EMP-", rows[0].PayloadJson); Assert.DoesNotContain("Webhook Person", rows[0].PayloadJson); Assert.DoesNotContain("Analyst", rows[0].PayloadJson);            // ids and codes only

        async Task<int> Run() { using var scope = f.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<WebhookDispatcher>().RunDueAsync(); }
        Assert.Equal(1, await Run());
        var call = Assert.Single(transport.Calls); Assert.Equal("https://hooks.example.com/annaba", call.Url); Assert.Equal("employee.created", call.Headers["X-Annaba-Event"]);
        var ts = long.Parse(call.Headers["X-Annaba-Timestamp"]); Assert.Equal("v1=" + WebhookDispatcher.Sign(secret, ts, call.Body), call.Headers["X-Annaba-Signature"]);   // the receiver can verify it
        Assert.NotEqual("v1=" + WebhookDispatcher.Sign("whsec_wrong", ts, call.Body), call.Headers["X-Annaba-Signature"]);
        Assert.Equal(DeliveryStatus.Delivered, (await seed.Db(db => db.WebhookDeliveries.SingleAsync())).Status); Assert.Equal(0, await Run());                                      // never twice

        // two instances racing for the same delivery: only one wins the lease
        await hr.Post($"/api/hr/employees/{empId}/terminate", new { reason = "Resigned" });
        var before = transport.Calls.Count; var both = await Task.WhenAll(Run(), Run()); Assert.Equal(1, transport.Calls.Count - before); Assert.Equal(1, both.Sum());

        // a failing receiver: backoff 1 min, 5 min, 30 min, 2 h, 6 h, then dead
        transport.Status = 500; await hr.Post($"/api/hr/webhooks/{endpoint}/test");
        var delays = new[] { 1, 5, 30, 120, 360 }; var attempts = 0;
        Assert.Equal(1, await Run()); attempts++; Assert.Equal(0, await Run());                                                                                                          // not due again yet
        foreach (var minutes in delays) { clock.Advance(TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(1)); Assert.Equal(1, await Run()); attempts++; if (attempts >= 6) break; }
        var dead = await seed.Db(db => db.WebhookDeliveries.SingleAsync(d => d.Event == "ping")); Assert.Equal(DeliveryStatus.Dead, dead.Status); Assert.Equal(6, dead.Attempts); Assert.Equal(500, dead.LastHttpStatus);
        clock.Advance(TimeSpan.FromDays(1)); Assert.Equal(0, await Run());
        Assert.Equal(2, await seed.Db(db => db.WebhookDeliveries.CountAsync(d => d.EndpointId == endpoint && d.Status == DeliveryStatus.Delivered)));                                 // created and terminated; the ping died
        Assert.Equal(0, await seed.Db(db => db.WebhookDeliveries.CountAsync(d => d.EndpointId == Guid.Parse(other.GetProperty("webhook").GetProperty("id").GetString()!)))); Assert.Equal(0, await seed.Db(db => db.WebhookDeliveries.CountAsync(d => d.EndpointId == Guid.Parse(off.GetProperty("webhook").GetProperty("id").GetString()!))));
    }
}
