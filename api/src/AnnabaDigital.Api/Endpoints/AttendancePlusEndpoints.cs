using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>Stage 13: breaks, hourly leave, roster, on-call, projects and timesheets, kiosk, field visits, block leave. Rules are checked again in the services.</summary>
public static class AttendancePlusEndpoints
{
    public static void MapAttendancePlus(this IEndpointRouteBuilder app)
    {
        var a = app.MapGroup("/api/hr/attendance"); a.RequireAuthorization();
        a.MapPost("/break-start", async (AttendanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.BreakAsync(true, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        a.MapPost("/break-end", async (AttendanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.BreakAsync(false, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        a.MapGet("/hourly-leave", async (string? scope, AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(await svc.HourlyAsync(scope, ctx.RequirePrincipal(), ctx.RequestAborted)));
        a.MapPost("/hourly-leave", async (HourlyLeaveRequest body, AttendancePlusService svc, HttpContext ctx) => { var v = await svc.AskHourlyAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/attendance/hourly-leave/{v.Id}", v); });
        a.MapPost("/hourly-leave/{id:guid}/cancel", async (Guid id, AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(await svc.CancelHourlyAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        a.MapPost("/hourly-leave/{id:guid}/decide", async (Guid id, AdvanceDecisionRequest body, AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(await svc.DecideHourlyAsync(id, body.Approve, body.Note, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AttendanceApprove);
        a.MapGet("/roster", async (DateOnly from, DateOnly to, Guid? orgUnitId, AttendancePlusService svc, ShiftService shifts, HttpContext ctx) => TypedResults.Ok(await svc.RosterAsync(from, to, orgUnitId, null, shifts, ctx.RequestAborted))).RequirePermission(Permissions.AttendanceReadAll);
        a.MapGet("/roster/mine", async (DateOnly from, DateOnly to, AttendancePlusService svc, ShiftService shifts, HttpContext ctx) =>
            TypedResults.Ok(await svc.RosterAsync(from, to, null, await svc.MyEmployeeIdAsync(ctx.RequirePrincipal(), ctx.RequestAborted), shifts, ctx.RequestAborted)));
        a.MapPut("/roster", async (RosterSetRequest body, AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(new { changed = await svc.SetRosterAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted) })).RequirePermission(Permissions.AttendanceManage);
        a.MapPost("/roster/pattern", async (RosterPatternRequest body, AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(new { changed = await svc.ApplyPatternAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted) })).RequirePermission(Permissions.AttendanceManage);
        a.MapGet("/on-call", async (int year, int month, AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(await svc.OnCallAsync(year, month, ctx.RequestAborted))).RequirePermission(Permissions.AttendanceReadAll);
        a.MapPost("/on-call", async (OnCallRequest body, AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(new { added = await svc.AddOnCallAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted) })).RequirePermission(Permissions.AttendanceManage);
        a.MapDelete("/on-call/{id:guid}", async (Guid id, AttendancePlusService svc, HttpContext ctx) => { await svc.DeleteOnCallAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); }).RequirePermission(Permissions.AttendanceManage);
        a.MapGet("/kiosk-pin", async (AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(new { hasPin = await svc.HasPinAsync(ctx.RequirePrincipal(), ctx.RequestAborted) }));
        a.MapPut("/kiosk-pin", async (KioskPinRequest body, AttendancePlusService svc, HttpContext ctx) => { await svc.SetMyPinAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); });
        a.MapGet("/kiosks", async (AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(await svc.KiosksAsync(ctx.RequestAborted))).RequirePermission(Permissions.AttendanceManage);
        a.MapPost("/kiosks", async (KioskDeviceRequest body, AttendancePlusService svc, HttpContext ctx) => { var v = await svc.CreateKioskAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/attendance/kiosks/{v.Id}", v); }).RequirePermission(Permissions.AttendanceManage);
        a.MapDelete("/kiosks/{id:guid}", async (Guid id, AttendancePlusService svc, HttpContext ctx) => { await svc.DeactivateKioskAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); }).RequirePermission(Permissions.AttendanceManage);
        a.MapPost("/visits", async (FieldVisitRequest body, AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(await svc.VisitAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        a.MapGet("/visits", async (DateOnly? date, Guid? employeeId, string? scope, AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(await svc.VisitsAsync(date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)), employeeId, scope != "all", ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        a.MapGet("/block-leave", async (int? year, AttendancePlusService svc, ShiftService shifts, HttpContext ctx) => TypedResults.Ok(await svc.BlockLeaveAsync(year ?? DateTime.UtcNow.Year, shifts, ctx.RequestAborted))).RequirePermission(Permissions.LeaveReadAll);

        var p = app.MapGroup("/api/hr/projects"); p.RequireAuthorization();
        p.MapGet("/", async (bool? includeArchived, AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(await svc.ProjectsAsync(includeArchived ?? false, ctx.RequestAborted)));
        p.MapPost("/", async (ProjectRequest body, AttendancePlusService svc, HttpContext ctx) => { var v = await svc.SaveProjectAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/projects/{v.Id}", v); }).RequirePermission(Permissions.AttendanceManage);
        p.MapPut("/{id:guid}", async (Guid id, ProjectRequest body, AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveProjectAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AttendanceManage);

        var ts = app.MapGroup("/api/hr/timesheets"); ts.RequireAuthorization();
        ts.MapGet("/week", async (DateOnly weekStart, Guid? employeeId, AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(await svc.WeekAsync(employeeId, weekStart, ctx.RequirePrincipal(), ctx.RequestAborted)));
        ts.MapPut("/week", async (DateOnly weekStart, bool? submit, TimesheetSaveRequest body, AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveWeekAsync(weekStart, body, submit ?? false, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        ts.MapGet("/pending", async (AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(await svc.PendingWeeksAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.AttendanceApprove);
        ts.MapPost("/decide", async (TimesheetDecision body, AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(await svc.DecideWeekAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AttendanceApprove);
        ts.MapGet("/report", async (int year, int month, AttendancePlusService svc, HttpContext ctx) => ctx.Export(await svc.TimesheetReportAsync(year, month, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AttendanceReadAll);

        // the kiosk itself: no session, the device token in a header proves the device; employee number + PIN prove the person
        var k = app.MapGroup("/api/kiosk").AllowAnonymous();
        k.MapGet("/device", async (AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(new { name = await svc.KioskNameAsync(ctx.Request.Headers["X-Kiosk-Token"].ToString(), ctx.RequestAborted) }));
        k.MapPost("/clock", async (KioskClockRequest body, AttendancePlusService svc, HttpContext ctx) => TypedResults.Ok(await svc.KioskClockAsync(ctx.Request.Headers["X-Kiosk-Token"].ToString(), body, ctx.Client(), ctx.RequestAborted)));
    }
}
