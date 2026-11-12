using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Hr;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>Stage 14: push notifications for the signed-in person's devices, and the payslip lock.</summary>
public static class PushEndpoints
{
    public sealed record PayslipLockRequest(bool? Locked);
    public sealed record UnsubscribeRequest(Guid? Id, string? Endpoint);

    public static void MapPush(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/me/push"); g.RequireAuthorization();
        g.MapGet("/key", async (PushService svc, HttpContext ctx) => TypedResults.Ok(new { publicKey = await svc.PublicKeyAsync(ctx.RequestAborted) }));
        g.MapGet("/devices", async (PushService svc, HttpContext ctx) => TypedResults.Ok(await svc.DevicesAsync(ctx.RequirePrincipal(), ctx.RequestAborted)));
        g.MapPost("/subscribe", async (PushSubscribeRequest body, PushService svc, HttpContext ctx) => TypedResults.Ok(await svc.SubscribeAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        g.MapPost("/unsubscribe", async (UnsubscribeRequest body, PushService svc, HttpContext ctx) => { await svc.UnsubscribeAsync(body.Id, body.Endpoint, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); });

        var p = app.MapGroup("/api/hr/payroll/payslips"); p.RequireAuthorization();
        p.MapGet("/lock", async (PayrollService svc, HttpContext ctx) => TypedResults.Ok(new { locked = await svc.PayslipLockedAsync(ctx.RequirePrincipal(), ctx.RequestAborted) }));
        p.MapPut("/lock", async (PayslipLockRequest body, PayrollService svc, HttpContext ctx) => TypedResults.Ok(new { locked = await svc.SetPayslipLockAsync(body.Locked ?? true, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted) }));
    }
}
