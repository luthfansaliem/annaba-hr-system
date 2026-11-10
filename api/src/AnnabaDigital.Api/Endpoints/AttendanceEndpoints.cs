using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

public static class AttendanceEndpoints
{
    public static void MapAttendance(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/hr/attendance");
        g.RequireAuthorization(); // signed in at minimum; permission-gated routes add their own policy
        g.MapGet("/me", async (string? month, AttendanceService svc, HttpContext ctx) => Results.Ok(await svc.MineAsync(month, ctx.RequirePrincipal(), ctx.RequestAborted)));
        g.MapPost("/clock-in", async (AttendanceService svc, HttpContext ctx) => Results.Ok(await svc.ClockInAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        g.MapPost("/clock-out", async (AttendanceService svc, HttpContext ctx) => Results.Ok(await svc.ClockOutAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        g.MapGet("/reports/overtime", async (string? month, TimeRequestService svc, HttpContext ctx) =>
        {
            var f = await svc.OvertimeReportAsync(month, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return ctx.Export(f);
        }).RequirePermission(Permissions.AttendanceReadAll);
        g.MapGet("/report", async (string? month, Guid? orgUnitId, AttendanceService svc, HttpContext ctx)
            => Results.Ok(await svc.ReportAsync(month, orgUnitId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AttendanceReadAll);
    }
}
