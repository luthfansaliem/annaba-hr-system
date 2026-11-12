using System.Net;
using System.Text.Json;
using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Stage 20: read-only API keys (scopes, IP list, expiry, revocation) and the bank file layout renderer.</summary>
[Collection("db")]
public sealed class IntegrationTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);

    private static async Task<HttpResponseMessage> Call(TestBrowser from, string path, string? key)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Add("X-Internal-Proxy-Key", ApiFactory.ProxyKey); req.Headers.Add("X-Forwarded-For", from.Ip);
        if (key is not null) req.Headers.Add("Authorization", $"Bearer {key}");
        return await from.Http.SendAsync(req);
    }

    [Fact]
    public async Task An_api_key_reads_only_its_scopes_from_allowed_addresses_until_it_expires_or_is_revoked()
    {
        var admin = TestBrowser.Create(fx.Factory); await admin.LoginAsync((await _seed.UserAsync(role: "super_admin")).Email, Seed.Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Post("/api/integrations/api-keys", new { name = "Lark", scopes = new[] { "employees.read" } })).StatusCode);   // needs a fresh password check
        await admin.Post("/api/auth/reauth", new { password = Seed.Password });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.Post("/api/integrations/api-keys", new { name = "Payroll sync", scopes = new[] { "payroll.read" } })).StatusCode);   // pay data never leaves by API
        var server = TestBrowser.Create(fx.Factory); var elsewhere = TestBrowser.Create(fx.Factory);
        var created = await (await admin.Post("/api/integrations/api-keys", new { name = "Access control system", scopes = new[] { "employees.read" }, allowedIps = new[] { server.Ip }, expiresInDays = 30 })).Body();
        var key = created.GetProperty("secret").GetString()!; Assert.StartsWith("ak_", key); Assert.StartsWith(created.GetProperty("key").GetProperty("prefix").GetString()!, key);
        Assert.DoesNotContain(key, (await (await admin.Get("/api/integrations/api-keys")).Content.ReadAsStringAsync()));   // shown once only

        Assert.Equal(HttpStatusCode.Unauthorized, (await Call(server, "/api/public/v1/employees", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Call(server, "/api/public/v1/employees", "ak_not-a-real-key-0123456789")).StatusCode);
        var page = await (await Call(server, "/api/public/v1/employees?pageSize=5", key)).Body();
        Assert.True(page.GetProperty("total").GetInt32() >= 0); Assert.Equal(5, page.GetProperty("pageSize").GetInt32());
        var raw = await (await Call(server, "/api/public/v1/employees?pageSize=1", key)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("nationalId", raw, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("bankAccount", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await Call(server, "/api/public/v1/org-units", key)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Call(server, "/api/public/v1/leave-requests", key)).StatusCode);   // not in its scopes
        Assert.Equal(HttpStatusCode.Unauthorized, (await Call(elsewhere, "/api/public/v1/employees", key)).StatusCode);   // another address

        fx.Clock.Advance(TimeSpan.FromDays(31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Call(server, "/api/public/v1/employees", key)).StatusCode);   // expired

        var admin2 = TestBrowser.Create(fx.Factory); await admin2.LoginAsync((await _seed.UserAsync(role: "super_admin")).Email, Seed.Password); await admin2.Post("/api/auth/reauth", new { password = Seed.Password });
        var other = await (await admin2.Post("/api/integrations/api-keys", new { name = "BI dashboard", scopes = new[] { "leave.read_all", "attendance.read_all" } })).Body();
        var k2 = other.GetProperty("secret").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await Call(elsewhere, "/api/public/v1/leave-requests", k2)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Call(elsewhere, "/api/public/v1/attendance?from=2026-01-01&to=2026-12-31", k2)).StatusCode);   // at most 92 days per call
        Assert.Equal(HttpStatusCode.NoContent, (await admin2.SendAsync(HttpMethod.Delete, $"/api/integrations/api-keys/{other.GetProperty("key").GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Call(elsewhere, "/api/public/v1/leave-requests", k2)).StatusCode);   // revoked
    }

    [Fact]
    public void Bank_files_follow_the_layout_and_never_start_a_cell_with_a_formula()
    {
        var rows = new[] { new BankTransfer("E-001", "=HYPERLINK(\"x\")", "BCA", "123-456-789", 7_500_000.5m, "Gaji 2027-01", null), new BankTransfer("E-002", "Siti; Rahma", "Mandiri", "000111", 4_000_000m, "Gaji 2027-01", "s@x.id") };
        var tsv = System.Text.Encoding.UTF8.GetString(IntegrationService.Render(new BankFileLayout { Delimiter = ";", Header = true, Columns = "account_no,name,amount,transfer_type,date", AmountDecimals = 2, DateFormat = "dd/MM/yyyy" }, rows, new DateOnly(2027, 1, 31)));
        var lines = tsv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("account_no;name;amount;transfer_type;date", lines[0]);
        Assert.Equal("123456789;\"'=HYPERLINK(\"\"x\"\")\";7500000.50;BCA;31/01/2027", lines[1]);
        Assert.Equal("000111;\"Siti; Rahma\";4000000.00;LLG;31/01/2027", lines[2]);
    }
}
