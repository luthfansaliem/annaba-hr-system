using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Users;

public sealed record ScimTokenView(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, bool Revoked);
public sealed record CreateScimTokenRequest(string? Name);
public sealed record CreatedScimToken(ScimTokenView Token, string Secret);

/// <summary>An error in the shape SCIM clients expect (RFC 7644 §3.12).</summary>
public sealed class ScimException(int status, string detail, string? scimType = null) : Exception(detail)
{
    public int Status { get; } = status; public string? ScimType { get; } = scimType;
}

/// <summary>
/// ADM-005. SCIM 2.0 (RFC 7643/7644) for user provisioning from an identity provider (Entra ID, Okta, Google): create, read, find by userName or externalId, update, deactivate.
/// A provisioning token belongs to one organisation; whoever presents it acts inside that organisation only. Users created here are active and e-mail-verified (the identity provider
/// vouches for them), get the plain "user" role and no password: they sign in through single sign-on. DELETE and active=false deactivate the account and end its sessions;
/// nothing is ever hard-deleted, and the last active super admin cannot be switched off from outside. Groups are not provisioned: roles stay an explicit decision inside the application.
/// </summary>
public sealed partial class ScimService(IAppDb db, IClock clock, ITokenService tokens, ITenantContext tenant, SessionService sessions, SettingsProvider settings, AuditWriter audit, UserAdminService users)
{
    public const string UserSchema = "urn:ietf:params:scim:schemas:core:2.0:User";
    public const string ListSchema = "urn:ietf:params:scim:api:messages:2.0:ListResponse";
    public const string ErrorSchema = "urn:ietf:params:scim:api:messages:2.0:Error";
    public const int MaxPage = 200;
    private string? _tokenName;

    // ============================================================ provisioning tokens (managed by an administrator)
    public async Task<IReadOnlyList<ScimTokenView>> TokensAsync(CancellationToken ct = default)
        => [.. (await db.ScimTokens.AsNoTracking().OrderByDescending(t => t.CreatedAt).ToListAsync(ct)).Select(View)];
    private static ScimTokenView View(ScimToken t) => new(t.Id, t.Name, t.CreatedAt, t.LastUsedAt, t.RevokedAt != null);

    public async Task<CreatedScimToken> CreateTokenAsync(CreateScimTokenRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, (await settings.GetAsync(ct)) with { RequireReauthForCritical = true }, clock);
        var name = r.Name?.Trim(); if (string.IsNullOrEmpty(name) || name.Length > 80) throw DomainException.Validation("Please check the token.", new Dictionary<string, string> { ["name"] = "Name the identity provider (up to 80 characters)." });
        if (await db.ScimTokens.CountAsync(t => t.RevokedAt == null, ct) >= 5) throw DomainException.Invariant("At most 5 active provisioning tokens. Revoke one first.");
        var secret = "scim_" + tokens.NewToken();
        var t = new ScimToken { Name = name, TokenHash = tokens.Hash(secret), CreatedBy = actor.UserId, CreatedAt = clock.UtcNow };
        db.ScimTokens.Add(t);
        audit.Record("scim.token.create", "scim_token", t.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name });
        await db.SaveChangesAsync(ct);
        return new CreatedScimToken(View(t), secret);
    }

    public async Task RevokeTokenAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, (await settings.GetAsync(ct)) with { RequireReauthForCritical = true }, clock);
        var t = await db.ScimTokens.FirstOrDefaultAsync(x => x.Id == id && x.RevokedAt == null, ct) ?? throw DomainException.NotFound("Token");
        t.RevokedAt = clock.UtcNow;
        audit.Record("scim.token.revoke", "scim_token", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = t.Name });
        await db.SaveChangesAsync(ct);
    }

    // ============================================================ the caller
    /// <summary>Checks the bearer token and switches into its organisation. Returns false when the token is missing, unknown or revoked.</summary>
    public async Task<bool> AuthenticateAsync(string? authorization, CancellationToken ct = default)
    {
        if (authorization is null || !authorization.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        var presented = authorization[7..].Trim(); if (presented.Length is < 20 or > 200) return false;
        var hash = tokens.Hash(presented);
        var t = await db.ScimTokens.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TokenHash == hash && x.RevokedAt == null, ct);
        if (t is null) return false;
        if (!await db.Tenants.AsNoTracking().AnyAsync(x => x.Id == t.TenantId && x.Status == TenantStatus.Active, ct)) return false;
        tenant.Use(t.TenantId, false); _tokenName = t.Name;
        if (t.LastUsedAt is null || clock.UtcNow - t.LastUsedAt > TimeSpan.FromMinutes(1)) { t.LastUsedAt = clock.UtcNow; await db.SaveChangesAsync(ct); }
        return true;
    }

    // ============================================================ users
    private static JsonObject Resource(User u, string baseUrl)
    {
        var parts = u.Name.Trim().Split(' ', 2);
        return new JsonObject
        {
            ["schemas"] = new JsonArray(UserSchema), ["id"] = u.Id.ToString(), ["externalId"] = u.ExternalId, ["userName"] = u.Email,
            ["name"] = new JsonObject { ["formatted"] = u.Name, ["givenName"] = parts[0], ["familyName"] = parts.Length > 1 ? parts[1] : "" },
            ["displayName"] = u.Name, ["active"] = u.Status == UserStatus.Active,
            ["emails"] = new JsonArray(new JsonObject { ["value"] = u.Email, ["type"] = "work", ["primary"] = true }),
            ["meta"] = new JsonObject { ["resourceType"] = "User", ["created"] = u.CreatedAt.ToString("O"), ["lastModified"] = u.UpdatedAt.ToString("O"), ["location"] = $"{baseUrl}/Users/{u.Id}" },
        };
    }

    public async Task<JsonObject> GetAsync(Guid id, string baseUrl, CancellationToken ct = default)
        => Resource(await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new ScimException(404, $"User {id} not found."), baseUrl);

    [GeneratedRegex("^\\s*(userName|externalId)\\s+eq\\s+\"((?:[^\"\\\\]|\\\\.)*)\"\\s*$", RegexOptions.IgnoreCase)] private static partial Regex Filter();

    public async Task<JsonObject> ListAsync(string? filter, int? startIndex, int? count, string baseUrl, CancellationToken ct = default)
    {
        var q = db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var m = Filter().Match(filter); if (!m.Success) throw new ScimException(400, "Only 'userName eq \"…\"' and 'externalId eq \"…\"' filters are supported.", "invalidFilter");
            var value = m.Groups[2].Value.Replace("\\\"", "\"");
            q = m.Groups[1].Value.Equals("userName", StringComparison.OrdinalIgnoreCase) ? q.Where(u => u.Email == value.ToLowerInvariant()) : q.Where(u => u.ExternalId == value);
        }
        var start = Math.Max(1, startIndex ?? 1); var size = Math.Clamp(count ?? 100, 0, MaxPage); var total = await q.CountAsync(ct);
        var rows = size == 0 ? [] : await q.OrderBy(u => u.CreatedAt).ThenBy(u => u.Id).Skip(start - 1).Take(size).ToListAsync(ct);
        return new JsonObject { ["schemas"] = new JsonArray(ListSchema), ["totalResults"] = total, ["startIndex"] = start, ["itemsPerPage"] = rows.Count, ["Resources"] = new JsonArray([.. rows.Select(u => (JsonNode)Resource(u, baseUrl))]) };
    }

    private sealed record Incoming(string? UserName, string? Name, bool? Active, string? ExternalId, bool ExternalIdGiven);

    private static bool? Bool(JsonElement v) => v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.String when bool.TryParse(v.GetString(), out var b) => b, _ => null }; // Entra sends "False" as a string
    private static string? Str(JsonElement o, string name) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static Incoming Read(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) throw new ScimException(400, "The request body must be a JSON object.", "invalidSyntax");
        string? name = Str(body, "displayName");
        if (body.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.Object)
            name = Str(n, "formatted") ?? (string.Join(' ', new[] { Str(n, "givenName"), Str(n, "familyName") }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } joined ? joined : name);
        return new Incoming(Str(body, "userName"), name, body.TryGetProperty("active", out var a) ? Bool(a) : null, Str(body, "externalId"), body.TryGetProperty("externalId", out _));
    }

    private static string Email(string? userName)
    {
        var v = (userName ?? "").Trim().ToLowerInvariant();
        if (v.Length is 0 or > 254 || !MailAddress.TryCreate(v, out var parsed) || parsed.Address != v || !v[(v.IndexOf('@') + 1)..].Contains('.')) throw new ScimException(400, "userName must be the person's e-mail address.", "invalidValue");
        return v;
    }
    private static string PersonName(string? name, string email)
    {
        var v = (name ?? "").Trim(); if (v.Length < 2) v = email[..email.IndexOf('@')]; if (v.Length < 2) v += " user";
        return v.Length > 200 ? v[..200] : v;
    }

    public async Task<JsonObject> CreateAsync(JsonElement body, string baseUrl, ClientInfo client, CancellationToken ct = default)
    {
        var r = Read(body); var email = Email(r.UserName); var now = clock.UtcNow;
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct)) throw new ScimException(409, "A user with this userName already exists.", "uniqueness"); // e-mail is unique across all organisations
        if (r.ExternalId is { Length: > 0 } ext && await db.Users.AnyAsync(u => u.ExternalId == ext, ct)) throw new ScimException(409, "A user with this externalId already exists.", "uniqueness");
        var active = r.Active ?? true;
        var u = new User { Email = email, Name = PersonName(r.Name, email), ExternalId = string.IsNullOrEmpty(r.ExternalId) ? null : r.ExternalId[..Math.Min(r.ExternalId.Length, 200)], Status = active ? UserStatus.Active : UserStatus.Deactivated,
            EmailVerifiedAt = now, DeactivatedAt = active ? null : now, CreatedAt = now, UpdatedAt = now, Profile = new UserProfile() };
        var basic = await db.Roles.FirstOrDefaultAsync(x => x.Key == SystemRoles.User && x.ArchivedAt == null, ct);
        if (basic is not null) u.UserRoles.Add(new UserRole { RoleId = basic.Id, AssignedAt = now });
        db.Users.Add(u);
        audit.Record("scim.user.create", "user", u.Id.ToString(), Outcome.Success, client, null, $"scim:{_tokenName}", new Dictionary<string, string> { ["email"] = email, ["active"] = active.ToString() });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ResetTracking(); throw new ScimException(409, "A user with this userName already exists.", "uniqueness"); }
        return Resource(u, baseUrl);
    }

    /// <summary>PUT: the whole resource as the identity provider sees it. userName cannot change (the e-mail address is the sign-in identity here).</summary>
    public async Task<JsonObject> ReplaceAsync(Guid id, JsonElement body, string baseUrl, ClientInfo client, CancellationToken ct = default)
        => await ApplyAsync(id, Read(body), baseUrl, client, ct);

    /// <summary>PATCH with "replace"/"add" operations on active, userName, externalId, displayName and name.*, with or without a path.</summary>
    public async Task<JsonObject> PatchAsync(Guid id, JsonElement body, string baseUrl, ClientInfo client, CancellationToken ct = default)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("Operations", out var ops) || ops.ValueKind != JsonValueKind.Array) throw new ScimException(400, "A PATCH needs an Operations array.", "invalidSyntax");
        string? userName = null, name = null, given = null, family = null, externalId = null; bool? active = null; var extGiven = false;
        foreach (var op in ops.EnumerateArray())
        {
            var kind = (Str(op, "op") ?? "").ToLowerInvariant(); if (kind is not ("replace" or "add")) throw new ScimException(400, $"Operation '{kind}' is not supported.", "invalidSyntax");
            if (!op.TryGetProperty("value", out var value)) throw new ScimException(400, "An operation needs a value.", "invalidValue");
            void Set(string path, JsonElement v)
            {
                switch (path.ToLowerInvariant())
                {
                    case "active": active = Bool(v) ?? throw new ScimException(400, "active must be true or false.", "invalidValue"); break;
                    case "username": userName = v.GetString(); break;
                    case "externalid": externalId = v.ValueKind == JsonValueKind.String ? v.GetString() : null; extGiven = true; break;
                    case "displayname" or "name.formatted": name = v.GetString(); break;
                    case "name.givenname": given = v.GetString(); break;
                    case "name.familyname": family = v.GetString(); break;
                    case "name" when v.ValueKind == JsonValueKind.Object: name = Str(v, "formatted") ?? name; given = Str(v, "givenName") ?? given; family = Str(v, "familyName") ?? family; break;
                    default: break; // attributes this application does not keep (phone numbers, titles, enterprise extension…) are accepted and ignored
                }
            }
            if (Str(op, "path") is { Length: > 0 } p) Set(p, value);
            else if (value.ValueKind == JsonValueKind.Object) foreach (var prop in value.EnumerateObject()) Set(prop.Name, prop.Value);
            else throw new ScimException(400, "An operation without a path needs an object value.", "invalidValue");
        }
        if (name is null && (given is not null || family is not null))
        {
            var current = (await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.Name).FirstOrDefaultAsync(ct) ?? "").Split(' ', 2);
            name = string.Join(' ', new[] { given ?? current[0], family ?? (current.Length > 1 ? current[1] : "") }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        return await ApplyAsync(id, new Incoming(userName, name, active, externalId, extGiven), baseUrl, client, ct);
    }

    public async Task DeleteAsync(Guid id, ClientInfo client, CancellationToken ct = default) => await ApplyAsync(id, new Incoming(null, null, false, null, false), "", client, ct);

    private async Task<JsonObject> ApplyAsync(Guid id, Incoming r, string baseUrl, ClientInfo client, CancellationToken ct)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ScimException(404, $"User {id} not found.");
        var diff = new List<AuditDiff>(); var now = clock.UtcNow;
        if (r.UserName is not null && Email(r.UserName) != u.Email) throw new ScimException(400, "userName cannot be changed: it is the address the person signs in with. Deactivate this user and create a new one.", "mutability");
        if (r.Name is not null && PersonName(r.Name, u.Email) is var n && n != u.Name) { diff.Add(new("name", u.Name, n)); u.Name = n; }
        if (r.ExternalIdGiven && r.ExternalId != u.ExternalId)
        {
            if (r.ExternalId is { Length: > 0 } ext && await db.Users.AnyAsync(x => x.ExternalId == ext && x.Id != id, ct)) throw new ScimException(409, "A user with this externalId already exists.", "uniqueness");
            u.ExternalId = string.IsNullOrEmpty(r.ExternalId) ? null : r.ExternalId[..Math.Min(r.ExternalId.Length, 200)]; diff.Add(new("externalId", "[previous]", "[changed]"));
        }
        var revoked = 0;
        if (r.Active is { } active && active != (u.Status == UserStatus.Active))
        {
            if (!active)
            {
                if (await users.IsLastActiveSuperAdminAsync(id, ct)) throw new ScimException(409, "This is the last active super admin of the organisation. Assign another one inside the application first.");
                diff.Add(new("status", u.Status.ToString().ToLowerInvariant(), "deactivated")); u.Status = UserStatus.Deactivated; u.DeactivatedAt = now;
                revoked = await sessions.RevokeAllForUserAsync(id, RevokeReason.Admin, null, ct);
                await db.Invitations.Where(i => i.Email == u.Email && i.Status == InvitationStatus.Pending).ExecuteUpdateAsync(x => x.SetProperty(i => i.Status, InvitationStatus.Revoked).SetProperty(i => i.RevokedAt, now), ct);
            }
            else { diff.Add(new("status", u.Status.ToString().ToLowerInvariant(), "active")); u.Status = UserStatus.Active; u.DeactivatedAt = null; u.EmailVerifiedAt ??= now; }
        }
        if (diff.Count > 0)
        {
            u.UpdatedAt = now; u.Version++;
            audit.Record("scim.user.update", "user", id.ToString(), Outcome.Success, client, null, $"scim:{_tokenName}", new Dictionary<string, string> { ["sessionsRevoked"] = revoked.ToString() }, diff);
            if (revoked > 0) audit.Security("session.revoked", Severity.Low, $"{revoked} session(s) revoked because the account was deactivated by the identity provider", id);
            await db.SaveChangesAsync(ct);
        }
        return Resource(u, baseUrl);
    }
}
