using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>
/// SCIM 2.0 under /api/scim/v2 for identity providers, and the administrator's token management under /api/scim-tokens.
/// The SCIM routes are "anonymous" to the session system on purpose: the caller is a machine with a bearer token (checked on every call by the filter below), not a signed-in person.
/// They carry no cookie, so the CSRF check does not apply to them (see CsrfMiddleware).
/// </summary>
public sealed record SetAssignable(bool Assignable);
public sealed record VerifyDocument(string? Code, decimal? Amount);

public static class ScimEndpoints
{
    private const string Media = "application/scim+json";
    private static IResult Scim(JsonNode body, int status = 200) => Results.Text(body.ToJsonString(), Media, System.Text.Encoding.UTF8, status);
    private static IResult Error(int status, string detail, string? scimType = null)
    {
        var o = new JsonObject { ["schemas"] = new JsonArray(ScimService.ErrorSchema), ["status"] = status.ToString(), ["detail"] = detail }; if (scimType is not null) o["scimType"] = scimType;
        return Scim(o, status);
    }

    public static void MapScim(this IEndpointRouteBuilder app)
    {
        var t = app.MapGroup("/api/scim-tokens");
        t.MapGet("/", async (ScimService svc, HttpContext ctx) => TypedResults.Ok(await svc.TokensAsync(ctx.RequestAborted))).RequirePermission(Permissions.SettingsManage);
        t.MapPost("/", async (CreateScimTokenRequest body, ScimService svc, HttpContext ctx) => TypedResults.Ok(await svc.CreateTokenAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.SettingsManage);
        t.MapDelete("/{id:guid}", async (Guid id, ScimService svc, HttpContext ctx) => { await svc.RevokeTokenAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); }).RequirePermission(Permissions.SettingsManage);

        t.MapGet("/roles", async (ScimService svc, HttpContext ctx) => TypedResults.Ok(await svc.RolesAsync(ctx.RequestAborted))).RequirePermission(Permissions.SettingsManage);
        t.MapPut("/roles/{roleId:guid}", async (Guid roleId, SetAssignable body, ScimService svc, HttpContext ctx) => { await svc.SetRoleAssignableAsync(roleId, body.Assignable, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); }).RequirePermission(Permissions.SettingsManage);
        // Anyone holding a printed letter or payslip can check its verification code; the answer carries no amounts.
        // The code travels in the body, not the address, so that it stays out of access logs and browser history.
        app.MapPost("/api/verify", async (VerifyDocument body, AnnabaDigital.Application.Hr.DocumentSealService svc, HttpContext ctx) =>
            TypedResults.Ok(await svc.VerifyAsync(body.Code, body.Amount, ctx.Client(), ctx.RequestAborted))).AllowAnonymous();

        var g = app.MapGroup("/api/scim/v2").AllowAnonymous().ExcludeFromDescription();
        g.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext; var svc = http.RequestServices.GetRequiredService<ScimService>();
            if (!await svc.AuthenticateAsync(http.Request.Headers.Authorization.ToString(), http.RequestAborted)) { http.Response.Headers.WWWAuthenticate = "Bearer"; return Error(401, "A valid provisioning token is required."); }
            try { return await next(ctx); }
            catch (ScimException e) { return Error(e.Status, e.Message, e.ScimType); }
            catch (DomainException e) { return Error(e.Status is >= 400 and < 600 ? e.Status : 400, e.Message); }
            catch (BadHttpRequestException) { return Error(400, "The request body is not valid JSON.", "invalidSyntax"); }
        });
        string Base(HttpContext ctx) => ctx.RequestServices.GetRequiredService<IOptions<ApiOptions>>().Value.Origin.TrimEnd('/') + "/api/scim/v2";

        g.MapGet("/ServiceProviderConfig", () => Scim(new JsonObject
        {
            ["schemas"] = new JsonArray("urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig"),
            ["patch"] = new JsonObject { ["supported"] = true }, ["bulk"] = new JsonObject { ["supported"] = false, ["maxOperations"] = 0, ["maxPayloadSize"] = 0 },
            ["filter"] = new JsonObject { ["supported"] = true, ["maxResults"] = ScimService.MaxPage }, ["changePassword"] = new JsonObject { ["supported"] = false },
            ["sort"] = new JsonObject { ["supported"] = false }, ["etag"] = new JsonObject { ["supported"] = false },
            ["authenticationSchemes"] = new JsonArray(new JsonObject { ["type"] = "oauthbearertoken", ["name"] = "Bearer token", ["description"] = "A provisioning token created by an administrator of the organisation." }),
        }));
        g.MapGet("/ResourceTypes", (HttpContext ctx) => Scim(new JsonObject { ["schemas"] = new JsonArray(ScimService.ListSchema), ["totalResults"] = 2, ["startIndex"] = 1, ["itemsPerPage"] = 2,
            ["Resources"] = new JsonArray(
                new JsonObject { ["schemas"] = new JsonArray("urn:ietf:params:scim:schemas:core:2.0:ResourceType"), ["id"] = "User", ["name"] = "User", ["endpoint"] = "/Users", ["schema"] = ScimService.UserSchema, ["meta"] = new JsonObject { ["resourceType"] = "ResourceType", ["location"] = Base(ctx) + "/ResourceTypes/User" } },
                new JsonObject { ["schemas"] = new JsonArray("urn:ietf:params:scim:schemas:core:2.0:ResourceType"), ["id"] = "Group", ["name"] = "Group", ["endpoint"] = "/Groups", ["schema"] = ScimService.GroupSchema, ["meta"] = new JsonObject { ["resourceType"] = "ResourceType", ["location"] = Base(ctx) + "/ResourceTypes/Group" } }) }));
        g.MapGet("/Schemas", () => Scim(new JsonObject { ["schemas"] = new JsonArray(ScimService.ListSchema), ["totalResults"] = 1, ["startIndex"] = 1, ["itemsPerPage"] = 1,
            ["Resources"] = new JsonArray(new JsonObject { ["id"] = ScimService.UserSchema, ["name"] = "User", ["description"] = "A user account. Kept: userName (the e-mail address, immutable), name, displayName, active, externalId.",
                ["attributes"] = new JsonArray([.. new[] { ("userName", "string", true, "immutable"), ("displayName", "string", false, "readWrite"), ("active", "boolean", false, "readWrite"), ("externalId", "string", false, "readWrite") }
                    .Select(a => (JsonNode)new JsonObject { ["name"] = a.Item1, ["type"] = a.Item2, ["multiValued"] = false, ["required"] = a.Item3, ["mutability"] = a.Item4, ["returned"] = "default" })]) }) }));
        // Groups are the roles an administrator opened up for provisioning (none by default): membership can be read and changed, groups cannot be created or deleted from outside.
        g.MapGet("/Groups", async (string? filter, ScimService svc, HttpContext ctx) => Scim(await svc.ListGroupsAsync(filter, Base(ctx), ctx.RequestAborted)));
        g.MapGet("/Groups/{id:guid}", async (Guid id, ScimService svc, HttpContext ctx) => Scim(await svc.GetGroupAsync(id, Base(ctx), ctx.RequestAborted)));
        g.MapPatch("/Groups/{id:guid}", async (Guid id, JsonElement body, ScimService svc, HttpContext ctx) => Scim(await svc.ChangeGroupMembersAsync(id, body, false, Base(ctx), ctx.Client(), ctx.RequestAborted)));
        g.MapPut("/Groups/{id:guid}", async (Guid id, JsonElement body, ScimService svc, HttpContext ctx) => Scim(await svc.ChangeGroupMembersAsync(id, body, true, Base(ctx), ctx.Client(), ctx.RequestAborted)));

        g.MapGet("/Users", async (string? filter, int? startIndex, int? count, ScimService svc, HttpContext ctx) => Scim(await svc.ListAsync(filter, startIndex, count, Base(ctx), ctx.RequestAborted)));
        g.MapGet("/Users/{id:guid}", async (Guid id, ScimService svc, HttpContext ctx) => Scim(await svc.GetAsync(id, Base(ctx), ctx.RequestAborted)));
        g.MapPost("/Users", async (JsonElement body, ScimService svc, HttpContext ctx) =>
        {
            var u = await svc.CreateAsync(body, Base(ctx), ctx.Client(), ctx.RequestAborted); ctx.Response.Headers.Location = u["meta"]!["location"]!.GetValue<string>(); return Scim(u, 201);
        });
        g.MapPut("/Users/{id:guid}", async (Guid id, JsonElement body, ScimService svc, HttpContext ctx) => Scim(await svc.ReplaceAsync(id, body, Base(ctx), ctx.Client(), ctx.RequestAborted)));
        g.MapPatch("/Users/{id:guid}", async (Guid id, JsonElement body, ScimService svc, HttpContext ctx) => Scim(await svc.PatchAsync(id, body, Base(ctx), ctx.Client(), ctx.RequestAborted)));
        g.MapDelete("/Users/{id:guid}", async (Guid id, ScimService svc, HttpContext ctx) => { await svc.DeleteAsync(id, ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); });
    }
}
