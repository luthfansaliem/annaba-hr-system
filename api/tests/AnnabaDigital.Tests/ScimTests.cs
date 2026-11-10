using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class ScimTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private const string Base = "/api/scim/v2";

    /// <summary>What an identity provider sends: a bearer token, no cookie, no Origin, no CSRF token (the proxy key is added by the web application's proxy).</summary>
    private async Task<HttpResponseMessage> Idp(HttpMethod method, string path, string? token, object? body = null)
    {
        var http = fx.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var req = new HttpRequestMessage(method, path); req.Headers.Add("X-Internal-Proxy-Key", ApiFactory.ProxyKey);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/scim+json");
        return await http.SendAsync(req);
    }
    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    private async Task<(TestBrowser Admin, string Token, Guid TokenId)> NewToken()
    {
        var admin = TestBrowser.Create(fx.Factory); await admin.LoginAsync((await _seed.UserAsync(role: "super_admin")).Email, Seed.Password); await admin.Post("/api/auth/reauth", new { password = Seed.Password });
        var made = await (await admin.Post("/api/scim-tokens", new { name = "Entra ID " + Guid.NewGuid().ToString("N")[..5] })).Body();
        return (admin, made.GetProperty("secret").GetString()!, made.GetProperty("token").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Every_scim_route_demands_a_provisioning_token_and_tokens_are_made_by_administrators_shown_once_and_revocable()
    {
        var routes = fx.Factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().Where(e => e.RoutePattern.RawText!.StartsWith(Base))
            .SelectMany(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods.Select(m => (Method: m, Url: e.RoutePattern.RawText!.Replace("{id:guid}", Guid.NewGuid().ToString())))).ToList();
        Assert.Equal(13, routes.Count);
        foreach (var (method, url) in routes)
            foreach (var bad in new[] { null, "scim_this-token-does-not-exist-0123456789" })
            {
                var r = await Idp(new HttpMethod(method), url, bad, method is "POST" or "PUT" or "PATCH" ? new { } : null);
                Assert.True(r.StatusCode == HttpStatusCode.Unauthorized, $"{method} {url} answered {(int)r.StatusCode} without a valid token");
            }

        var hr = TestBrowser.Create(fx.Factory); await hr.LoginAsync((await _seed.UserAsync(role: "hr_admin")).Email, Seed.Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await hr.Post("/api/scim-tokens", new { name = "x" })).StatusCode);                 // not an HR matter
        var (admin, token, id) = await NewToken();
        Assert.StartsWith("scim_", token);
        Assert.DoesNotContain(token, await _seed.Db(db => db.ScimTokens.Where(t => t.Id == id).Select(t => t.TokenHash).SingleAsync()));   // only a hash is stored
        Assert.DoesNotContain("secret", await (await admin.Get("/api/scim-tokens")).Content.ReadAsStringAsync());                   // never shown again
        Assert.Equal(HttpStatusCode.OK, (await Idp(HttpMethod.Get, Base + "/ServiceProviderConfig", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(HttpMethod.Delete, $"/api/scim-tokens/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Idp(HttpMethod.Get, Base + "/ServiceProviderConfig", token)).StatusCode);  // revoked: dead at once
        Assert.True(await _seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "scim.token.revoke")));
    }

    [Fact]
    public async Task An_identity_provider_creates_finds_updates_and_deactivates_users()
    {
        var (_, token, _) = await NewToken(); var tag = Guid.NewGuid().ToString("N")[..8]; var mail = $"siti.{tag}@example.co.id";

        var created = await Idp(HttpMethod.Post, Base + "/Users", token, new { schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" }, userName = mail.ToUpperInvariant(), externalId = "ext-" + tag, name = new { givenName = "Siti", familyName = "Rahma" }, active = true, title = "ignored" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode); Assert.Equal("application/scim+json", created.Content.Headers.ContentType!.MediaType);
        var u = await Json(created); var id = u.GetProperty("id").GetGuid();
        Assert.Equal(mail, u.GetProperty("userName").GetString()); Assert.Equal("Siti Rahma", u.GetProperty("displayName").GetString()); Assert.True(u.GetProperty("active").GetBoolean());
        Assert.EndsWith($"/api/scim/v2/Users/{id}", u.GetProperty("meta").GetProperty("location").GetString());
        var row = await _seed.Db(db => db.Users.Include(x => x.UserRoles).ThenInclude(r => r.Role).SingleAsync(x => x.Id == id));
        Assert.Equal(UserStatus.Active, row.Status); Assert.NotNull(row.EmailVerifiedAt); Assert.Equal(["user"], row.UserRoles.Select(r => r.Role!.Key).ToList());   // no privileges come from outside

        Assert.Equal(HttpStatusCode.Conflict, (await Idp(HttpMethod.Post, Base + "/Users", token, new { userName = mail })).StatusCode);                     // same person twice
        Assert.Equal("uniqueness", (await Json(await Idp(HttpMethod.Post, Base + "/Users", token, new { userName = mail }))).GetProperty("scimType").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await Idp(HttpMethod.Post, Base + "/Users", token, new { userName = "not-an-address" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Idp(HttpMethod.Post, Base + "/Users", token, "{ not json")).StatusCode);

        var found = await Json(await Idp(HttpMethod.Get, Base + $"/Users?filter={Uri.EscapeDataString($"userName eq \"{mail}\"")}", token));
        Assert.Equal(1, found.GetProperty("totalResults").GetInt32()); Assert.Equal(id, found.GetProperty("Resources")[0].GetProperty("id").GetGuid());
        Assert.Equal(1, (await Json(await Idp(HttpMethod.Get, Base + $"/Users?filter={Uri.EscapeDataString($"externalId eq \"ext-{tag}\"")}", token))).GetProperty("totalResults").GetInt32());
        Assert.Equal(0, (await Json(await Idp(HttpMethod.Get, Base + $"/Users?filter={Uri.EscapeDataString("userName eq \"nobody@example.com\"")}", token))).GetProperty("totalResults").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await Idp(HttpMethod.Get, Base + $"/Users?filter={Uri.EscapeDataString("emails co \"example\"")}", token)).StatusCode);   // an unsupported filter is refused, never ignored (that would return everyone)
        var page = await Json(await Idp(HttpMethod.Get, Base + "/Users?startIndex=1&count=2", token)); Assert.Equal(2, page.GetProperty("itemsPerPage").GetInt32()); Assert.True(page.GetProperty("totalResults").GetInt32() >= 2);

        var renamed = await Json(await Idp(new HttpMethod("PATCH"), Base + $"/Users/{id}", token, new { schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:PatchOp" }, Operations = new object[] { new { op = "Replace", path = "name.familyName", value = "Rahmawati" } } }));
        Assert.Equal("Siti Rahmawati", renamed.GetProperty("displayName").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await Idp(HttpMethod.Put, Base + $"/Users/{id}", token, new { userName = "someone.else@example.co.id", active = true })).StatusCode);   // the sign-in address cannot be swapped

        // the person signs in, then the identity provider switches them off: the account is deactivated and the session dies (Entra sends "False" as a string)
        await _seed.Db(async db =>                                                                                               // (test set-up: give the account a password so a browser session exists; real provisioned users come in through single sign-on)
        {
            var hash = await db.Users.Where(q => q.Credential != null).Select(q => q.Credential!.PasswordHash).FirstAsync();
            db.Add(new UserCredential { UserId = id, PasswordHash = hash, PasswordChangedAt = fx.Clock.UtcNow }); return await db.SaveChangesAsync();
        });
        var person = TestBrowser.Create(fx.Factory); await person.LoginAsync(mail, Seed.Password); Assert.Equal(HttpStatusCode.OK, (await person.Get("/api/auth/me")).StatusCode);
        var off = await Json(await Idp(new HttpMethod("PATCH"), Base + $"/Users/{id}", token, new { Operations = new object[] { new { op = "replace", value = new { active = "False" } } } }));
        Assert.False(off.GetProperty("active").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await person.Get("/api/auth/me")).StatusCode);
        Assert.Equal(UserStatus.Deactivated, await _seed.Db(db => db.Users.Where(x => x.Id == id).Select(x => x.Status).SingleAsync()));
        var on = await Json(await Idp(HttpMethod.Put, Base + $"/Users/{id}", token, new { userName = mail, active = true, name = new { formatted = "Siti Rahmawati" } })); Assert.True(on.GetProperty("active").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await Idp(HttpMethod.Delete, Base + $"/Users/{id}", token)).StatusCode);            // DELETE deactivates; the row and its history stay
        Assert.False((await Json(await Idp(HttpMethod.Get, Base + $"/Users/{id}", token))).GetProperty("active").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await Idp(HttpMethod.Get, Base + $"/Users/{Guid.NewGuid()}", token)).StatusCode);
        Assert.True(await _seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "scim.user.update" && a.TargetId == id.ToString())));
    }

    [Fact]
    public async Task A_token_sees_only_its_own_organisation_and_cannot_switch_off_the_last_super_admin()
    {
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock);                                              // an organisation with exactly one super admin
        var boss = await seed.UserAsync(role: "super_admin"); var admin = TestBrowser.Create(f); await admin.LoginAsync(boss.Email, Seed.Password); await admin.Post("/api/auth/reauth", new { password = Seed.Password });
        var token = (await (await admin.Post("/api/scim-tokens", new { name = "Okta" })).Body()).GetProperty("secret").GetString()!;
        async Task<HttpResponseMessage> Call(HttpMethod m, string path, object? body = null)
        {
            var req = new HttpRequestMessage(m, path); req.Headers.Add("X-Internal-Proxy-Key", ApiFactory.ProxyKey); req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/scim+json");
            return await f.CreateClient().SendAsync(req);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Call(HttpMethod.Delete, Base + $"/Users/{boss.Id}")).StatusCode);
        Assert.Equal(UserStatus.Active, await seed.Db(db => db.Users.Where(x => x.Id == boss.Id).Select(x => x.Status).SingleAsync()));

        // a second organisation in the same database: this token neither lists nor reaches its users
        var otherTenant = Guid.CreateVersion7(); var stranger = Guid.CreateVersion7();
        await seed.Db(async db =>
        {
            db.Tenants.Add(new Tenant { Id = otherTenant, Slug = "other-org", Name = "Other Org", CreatedAt = clock.UtcNow });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO users (id, tenant_id, email, name, status, failed_login_count, created_at, updated_at, version) VALUES ({stranger}, {otherTenant}, 'stranger@other.example', 'Stranger Person', 'Active', 0, now(), now(), 1)");
            return 0;
        });
        Assert.Equal(HttpStatusCode.NotFound, (await Call(HttpMethod.Get, Base + $"/Users/{stranger}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Call(HttpMethod.Delete, Base + $"/Users/{stranger}")).StatusCode);
        Assert.Equal(0, JsonDocument.Parse(await (await Call(HttpMethod.Get, Base + $"/Users?filter={Uri.EscapeDataString("userName eq \"stranger@other.example\"")}")).Content.ReadAsStringAsync()).RootElement.GetProperty("totalResults").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await Call(HttpMethod.Post, Base + "/Users", new { userName = "stranger@other.example" })).StatusCode);   // an address is unique across organisations, and the answer does not say where it lives
        Assert.Equal(UserStatus.Active, await seed.Db(async db => (await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Id == stranger)).Status));
    }

    [Fact]
    public async Task Groups_are_the_roles_an_administrator_opened_up_and_an_identity_provider_can_only_change_who_is_in_them()
    {
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock);
        var boss = await seed.UserAsync(role: "super_admin"); var admin = TestBrowser.Create(f); await admin.LoginAsync(boss.Email, Seed.Password);
        var p1 = await seed.UserAsync(role: "user"); var p2 = await seed.UserAsync(role: "user");
        var roles = await (await admin.Get("/api/scim-tokens/roles")).Body();
        Guid Role(string key) => roles.EnumerateArray().Single(r => r.GetProperty("key").GetString() == key).GetProperty("id").GetGuid();
        var hrAdmin = Role("hr_admin"); var super = Role("super_admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.SendAsync(HttpMethod.Put, $"/api/scim-tokens/roles/{hrAdmin}", new { assignable = true })).StatusCode);     // needs a fresh password check
        await admin.Post("/api/auth/reauth", new { password = Seed.Password });
        var token = (await (await admin.Post("/api/scim-tokens", new { name = "Entra" })).Body()).GetProperty("secret").GetString()!;
        async Task<HttpResponseMessage> Call(HttpMethod m, string path, object? body = null)
        {
            var req = new HttpRequestMessage(m, path); req.Headers.Add("X-Internal-Proxy-Key", ApiFactory.ProxyKey); req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/scim+json");
            return await f.CreateClient().SendAsync(req);
        }
        async Task<JsonElement> J(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        async Task<List<Guid>> Holders(Guid role) => await seed.Db(db => db.UserRoles.Where(x => x.RoleId == role).Select(x => x.UserId).OrderBy(x => x).ToListAsync());

        // nothing is open by default: no groups, and a role's id answers 404 even though it exists
        Assert.Equal(0, (await J(await Call(HttpMethod.Get, Base + "/Groups"))).GetProperty("totalResults").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await Call(HttpMethod.Get, Base + $"/Groups/{hrAdmin}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Call(new HttpMethod("PATCH"), Base + $"/Groups/{hrAdmin}", new { Operations = new object[] { new { op = "add", path = "members", value = new[] { new { value = p1.Id } } } } })).StatusCode);
        Assert.Empty(await Holders(hrAdmin));

        // an administrator opens up HR Admin; the protected Super Admin role can never be opened
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(HttpMethod.Put, $"/api/scim-tokens/roles/{hrAdmin}", new { assignable = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.SendAsync(HttpMethod.Put, $"/api/scim-tokens/roles/{super}", new { assignable = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Call(new HttpMethod("PATCH"), Base + $"/Groups/{super}", new { Operations = new object[] { new { op = "add", path = "members", value = new[] { new { value = p1.Id } } } } })).StatusCode);
        Assert.Equal([boss.Id], await Holders(super));

        var list = await J(await Call(HttpMethod.Get, Base + "/Groups")); Assert.Equal(1, list.GetProperty("totalResults").GetInt32());
        var name = list.GetProperty("Resources")[0].GetProperty("displayName").GetString()!;
        Assert.Equal(1, (await J(await Call(HttpMethod.Get, Base + $"/Groups?filter={Uri.EscapeDataString($"displayName eq \"{name}\"")}"))).GetProperty("totalResults").GetInt32());
        Assert.Equal(0, (await J(await Call(HttpMethod.Get, Base + $"/Groups?filter={Uri.EscapeDataString("displayName eq \"Nobody\"")}"))).GetProperty("totalResults").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await Call(HttpMethod.Get, Base + $"/Groups?filter={Uri.EscapeDataString("members co \"x\"")}")).StatusCode);

        // add two people (Okta style), remove one (Entra style: the member is in the path), then replace the whole list
        var g = await J(await Call(new HttpMethod("PATCH"), Base + $"/Groups/{hrAdmin}", new { schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:PatchOp" }, Operations = new object[] { new { op = "Add", path = "members", value = new[] { new { value = p1.Id }, new { value = p2.Id } } } } }));
        Assert.Equal(2, g.GetProperty("members").GetArrayLength()); Assert.Equal(new[] { p1.Id, p2.Id }.Order().ToList(), await Holders(hrAdmin));
        var asHr = TestBrowser.Create(f); await asHr.LoginAsync(p1.Email, Seed.Password); Assert.Equal(HttpStatusCode.OK, (await asHr.Get("/api/hr/employees")).StatusCode);   // the role works at once
        await Call(new HttpMethod("PATCH"), Base + $"/Groups/{hrAdmin}", new { Operations = new object[] { new { op = "remove", path = $"members[value eq \"{p1.Id}\"]" } } });
        Assert.Equal([p2.Id], await Holders(hrAdmin)); Assert.Equal(HttpStatusCode.Forbidden, (await asHr.Get("/api/hr/employees")).StatusCode);
        g = await J(await Call(HttpMethod.Put, Base + $"/Groups/{hrAdmin}", new { displayName = "Renamed from outside", members = new[] { new { value = p1.Id } } }));
        Assert.Equal([p1.Id], await Holders(hrAdmin)); Assert.Equal(name, g.GetProperty("displayName").GetString());            // the name is the role's name; it is not changed from outside
        Assert.Equal(HttpStatusCode.BadRequest, (await Call(new HttpMethod("PATCH"), Base + $"/Groups/{hrAdmin}", new { Operations = new object[] { new { op = "add", path = "members", value = new[] { new { value = Guid.NewGuid() } } } } })).StatusCode);   // a stranger
        Assert.False((await Call(HttpMethod.Post, Base + "/Groups", new { displayName = "Made outside" })).IsSuccessStatusCode); Assert.False(await seed.Db(db => db.Roles.AnyAsync(r => r.Name == "Made outside")));          // groups are not created or deleted from outside
        Assert.True(await seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "scim.group.members" && a.TargetId == hrAdmin.ToString())));
        Assert.True(await seed.Db(db => db.SecurityEvents.AnyAsync(e => e.Type == "privilege.change" && e.UserId == p1.Id)));

        // closing the role again hides the group at once; the people keep what they have until an administrator decides
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(HttpMethod.Put, $"/api/scim-tokens/roles/{hrAdmin}", new { assignable = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Call(HttpMethod.Get, Base + $"/Groups/{hrAdmin}")).StatusCode);
        Assert.Equal([p1.Id], await Holders(hrAdmin));
    }
}
