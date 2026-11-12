using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>Stage 21: the HR assistant. Everyone signed in may ask (about the policy and their own data); administrators turn it on and set the policy text.</summary>
public static class AssistantEndpoints
{
    public static void MapAssistant(this IEndpointRouteBuilder app)
    {
        var a = app.MapGroup("/api/hr/assistant"); a.RequireAuthorization();
        a.MapGet("/status", async (AssistantService svc, HttpContext ctx) => TypedResults.Ok(await svc.StatusAsync(ctx.RequestAborted)));
        a.MapPost("/ask", async (AskRequest body, AssistantService svc, HttpContext ctx) => TypedResults.Ok(await svc.AskAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        a.MapGet("/settings", async (AssistantService svc, HttpContext ctx) => TypedResults.Ok(await svc.SettingsAsync(ctx.RequestAborted))).RequirePermission(Permissions.SettingsManage);
        a.MapPut("/settings", async (AssistantSettingsRequest body, AssistantService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveSettingsAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.SettingsManage);
    }
}
