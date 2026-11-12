using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>Stages 15–16: goals (KPI/OKR), competencies, review cycles, calibration and 9-box, development plans, succession. Who may act on a person's goals and plans is checked in the service.</summary>
public static class PerformanceEndpoints
{
    public static void MapPerformance(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/hr/goals"); g.RequireAuthorization();
        g.MapGet("/", async (string? scope, int? year, Guid? employeeId, PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.GoalsAsync(scope, year, employeeId, ctx.RequirePrincipal(), ctx.RequestAborted)));
        g.MapPost("/", async (GoalRequest body, PerformanceService svc, HttpContext ctx) => { var v = await svc.SaveGoalAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/goals/{v.Id}", v); });
        g.MapPut("/{id:guid}", async (Guid id, GoalRequest body, PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveGoalAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        g.MapPost("/{id:guid}/check-in", async (Guid id, CheckInRequest body, PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.CheckInAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        g.MapGet("/{id:guid}/check-ins", async (Guid id, PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.CheckInsAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted)));

        var c = app.MapGroup("/api/hr/competencies"); c.RequireAuthorization();
        c.MapGet("/", async (PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.CompetenciesAsync(ctx.RequestAborted)));
        c.MapPost("/", async (CompetencyRequest body, PerformanceService svc, HttpContext ctx) => { var v = await svc.SaveCompetencyAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/competencies/{v.Id}", v); }).RequirePermission(Permissions.HrConfigManage);
        c.MapPut("/{id:guid}", async (Guid id, CompetencyRequest body, PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveCompetencyAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);

        var r = app.MapGroup("/api/hr/review-cycles"); r.RequireAuthorization();
        r.MapGet("/", async (PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.CyclesAsync(ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
        r.MapPost("/", async (CycleRequest body, PerformanceService svc, HttpContext ctx) => { var v = await svc.SaveCycleAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/review-cycles/{v.Id}", v); }).RequirePermission(Permissions.HrConfigManage);
        r.MapPut("/{id:guid}", async (Guid id, CycleRequest body, PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveCycleAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
        r.MapPost("/{id:guid}/launch", async (Guid id, PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.LaunchAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
        r.MapPost("/{id:guid}/calibrate", async (Guid id, PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.SetCycleStatusAsync(id, "calibrate", ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
        r.MapPost("/{id:guid}/close", async (Guid id, PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.SetCycleStatusAsync(id, "close", ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
        r.MapGet("/{id:guid}/results", async (Guid id, PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.ResultsAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
        r.MapPost("/{id:guid}/results", async (Guid id, CalibrateRequest body, PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.CalibrateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
        // managers pick peers for their direct reports (checked in the service), HR for anyone
        r.MapPost("/{id:guid}/peers", async (Guid id, PeerRequest body, PerformanceService svc, HttpContext ctx) => { await svc.AddPeerAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); });

        var m = app.MapGroup("/api/hr/reviews"); m.RequireAuthorization();
        m.MapGet("/mine", async (PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.MyTasksAsync(ctx.RequirePrincipal(), ctx.RequestAborted)));
        m.MapGet("/results", async (PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.MyResultsAsync(ctx.RequirePrincipal(), ctx.RequestAborted)));
        m.MapGet("/{id:guid}", async (Guid id, PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.FormAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted)));
        m.MapPut("/{id:guid}", async (Guid id, ReviewAnswers body, PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.AnswerAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));

        var t = app.MapGroup("/api/hr/talent"); t.RequireAuthorization();
        t.MapGet("/pool", async (PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.TalentPoolAsync(ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
        t.MapGet("/succession", async (PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.SuccessionAsync(ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
        t.MapPost("/succession", async (SuccessionRequest body, PerformanceService svc, HttpContext ctx) => { var v = await svc.SaveSuccessionAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/talent/succession/{v.Id}", v); }).RequirePermission(Permissions.HrConfigManage);
        t.MapPut("/succession/{id:guid}", async (Guid id, SuccessionRequest body, PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveSuccessionAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
        t.MapPost("/succession/{id:guid}/candidates", async (Guid id, CandidateRequest body, PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.AddCandidateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
        t.MapDelete("/succession/{id:guid}/candidates/{candidateId:guid}", async (Guid id, Guid candidateId, PerformanceService svc, HttpContext ctx) => { await svc.RemoveCandidateAsync(id, candidateId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); }).RequirePermission(Permissions.HrConfigManage);
        t.MapGet("/plans", async (Guid? employeeId, PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.PlansAsync(employeeId, ctx.RequirePrincipal(), ctx.RequestAborted)));
        t.MapPost("/plans", async (IdpRequest body, PerformanceService svc, HttpContext ctx) => { var v = await svc.SavePlanAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/talent/plans/{v.Id}", v); });
        t.MapPut("/plans/{id:guid}", async (Guid id, IdpRequest body, PerformanceService svc, HttpContext ctx) => TypedResults.Ok(await svc.SavePlanAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
    }
}
