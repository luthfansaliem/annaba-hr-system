using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

public static class LeaveEndpoints
{
    public static void MapLeave(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/hr/leave");
        g.RequireAuthorization(); // signed in at minimum; permission-gated routes add their own policy

        // Anyone signed in may read types, holidays and their own balances/requests (self-service); the service checks that the account is linked to an employee.
        g.MapGet("/types", async (bool? includeArchived, LeaveService svc, HttpContext ctx) => TypedResults.Ok(await svc.TypesAsync(includeArchived ?? false, ctx.RequestAborted)));
        g.MapPost("/types", async (LeaveTypeRequest body, LeaveService svc, HttpContext ctx) =>
        {
            var t = await svc.SaveTypeAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return TypedResults.Created($"/api/hr/leave/types/{t.Id}", t);
        }).RequirePermission(Permissions.LeaveManage);
        g.MapPatch("/types/{id:guid}", async (Guid id, LeaveTypeRequest body, LeaveService svc, HttpContext ctx)
            => TypedResults.Ok(await svc.SaveTypeAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.LeaveManage);

        g.MapGet("/holidays", async (int? year, LeaveService svc, HttpContext ctx) => TypedResults.Ok(await svc.HolidaysAsync(year ?? DateTime.UtcNow.Year, ctx.RequestAborted)));
        g.MapPost("/holidays", async (HolidayRequest body, LeaveService svc, HttpContext ctx) =>
        {
            var h = await svc.AddHolidayAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return TypedResults.Created($"/api/hr/leave/holidays/{h.Id}", h);
        }).RequirePermission(Permissions.LeaveManage);
        g.MapDelete("/holidays/{id:guid}", async (Guid id, LeaveService svc, HttpContext ctx) =>
        {
            await svc.DeleteHolidayAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent();
        }).RequirePermission(Permissions.LeaveManage);

        g.MapGet("/balances", async (Guid? employeeId, LeaveService svc, HttpContext ctx) => TypedResults.Ok(await svc.BalancesAsync(employeeId, ctx.RequirePrincipal(), ctx.RequestAborted)));
        g.MapPost("/balances/adjust", async (AdjustBalanceRequest body, LeaveService svc, HttpContext ctx)
            => TypedResults.Ok(await svc.AdjustAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.LeaveManage);

        g.MapGet("/requests", async (string? scope, string? status, DateOnly? from, DateOnly? to, int? page, int? pageSize, LeaveService svc, HttpContext ctx)
            => TypedResults.Ok(await svc.ListAsync(new LeaveQuery(scope, status, from, to, page ?? 1, pageSize ?? 20), ctx.RequirePrincipal(), ctx.RequestAborted)));
        g.MapPost("/requests", async (SubmitLeaveRequest body, LeaveService svc, HttpContext ctx) =>
        {
            var r = await svc.SubmitAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return TypedResults.Created($"/api/hr/leave/requests/{r.Id}", r);
        });
        g.MapPost("/requests/{id:guid}/approve", async (Guid id, DecisionRequest? body, LeaveService svc, HttpContext ctx)
            => TypedResults.Ok(await svc.DecideAsync(id, true, body?.Note, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.LeaveApprove);
        g.MapPost("/requests/{id:guid}/reject", async (Guid id, DecisionRequest? body, LeaveService svc, HttpContext ctx)
            => TypedResults.Ok(await svc.DecideAsync(id, false, body?.Note, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.LeaveApprove);
        g.MapPost("/requests/{id:guid}/cancel", async (Guid id, LeaveService svc, HttpContext ctx)
            => TypedResults.Ok(await svc.CancelAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
    }
}
