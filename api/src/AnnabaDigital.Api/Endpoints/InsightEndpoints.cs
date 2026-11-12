using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>Stage 19: turnover, overtime and performance analytics, employees at risk, and the report builder (each data set checks its own permission in the service).</summary>
public static class InsightEndpoints
{
    public static void MapInsights(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/hr/analytics/turnover", async (InsightService svc, HttpContext ctx) => TypedResults.Ok(await svc.TurnoverAsync(ctx.RequestAborted))).RequireAuthorization().RequirePermission(Permissions.EmployeesRead);
        app.MapGet("/api/hr/analytics/overtime", async (InsightService svc, HttpContext ctx) => TypedResults.Ok(await svc.OvertimeAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization().RequirePermission(Permissions.AttendanceReadAll);
        app.MapGet("/api/hr/analytics/performance", async (InsightService svc, HttpContext ctx) => TypedResults.Ok(await svc.PerformanceAsync(ctx.RequestAborted))).RequireAuthorization().RequirePermission(Permissions.HrConfigManage);
        app.MapGet("/api/hr/analytics/at-risk", async (InsightService svc, HttpContext ctx) => TypedResults.Ok(await svc.AtRiskAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization().RequirePermission(Permissions.HrConfigManage);

        var r = app.MapGroup("/api/hr/reports"); r.RequireAuthorization();
        r.MapGet("/datasets", async (InsightService svc, HttpContext ctx) => TypedResults.Ok(await svc.DatasetsAsync(ctx.RequirePrincipal(), ctx.RequestAborted)));
        r.MapPost("/run", async (ReportConfig body, InsightService svc, HttpContext ctx) => TypedResults.Ok(await svc.RunAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        r.MapPost("/export", async (ReportConfig body, InsightService svc, HttpContext ctx) => ctx.Export(await svc.ExportAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        r.MapGet("/saved", async (InsightService svc, HttpContext ctx) => TypedResults.Ok(await svc.SavedAsync(ctx.RequirePrincipal(), ctx.RequestAborted)));
        r.MapPost("/saved", async (SaveReportRequest body, InsightService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        r.MapPut("/saved/{id:guid}", async (Guid id, SaveReportRequest body, InsightService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        r.MapDelete("/saved/{id:guid}", async (Guid id, InsightService svc, HttpContext ctx) => { await svc.DeleteAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); });
    }
}
