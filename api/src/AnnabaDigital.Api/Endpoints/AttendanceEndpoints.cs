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
        g.MapPost("/clock-in", async (ClockInRequest? body, AttendanceService svc, HttpContext ctx) => Results.Ok(await svc.ClockInAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted, body)));
        g.MapGet("/work-locations", async (bool? includeArchived, AttendanceService svc, HttpContext ctx) => Results.Ok(await svc.LocationsAsync(includeArchived ?? false, ctx.RequestAborted))).RequirePermission(Permissions.AttendanceReadAll);
        g.MapPost("/work-locations", async (WorkLocationRequest body, AttendanceService svc, HttpContext ctx) =>
        {
            var w = await svc.SaveLocationAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.Created($"/api/hr/attendance/work-locations/{w.Id}", w);
        }).RequirePermission(Permissions.AttendanceManage);
        g.MapPatch("/work-locations/{id:guid}", async (Guid id, WorkLocationRequest body, AttendanceService svc, HttpContext ctx)
            => Results.Ok(await svc.SaveLocationAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AttendanceManage);
        g.MapPost("/clock-out", async (AttendanceService svc, HttpContext ctx) => Results.Ok(await svc.ClockOutAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        g.MapGet("/reports/overtime", async (string? month, TimeRequestService svc, HttpContext ctx) =>
        {
            var f = await svc.OvertimeReportAsync(month, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return ctx.Export(f);
        }).RequirePermission(Permissions.AttendanceReadAll);
        g.MapGet("/report", async (string? month, Guid? orgUnitId, AttendanceService svc, HttpContext ctx)
            => Results.Ok(await svc.ReportAsync(month, orgUnitId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AttendanceReadAll);
    }
}
