using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>Stage 11: pay grades and salary reviews, claim policies and balances, cash advances, early wage withdrawals. Every rule is checked again in the services.</summary>
public static class BenefitEndpoints
{
    public static void MapBenefits(this IEndpointRouteBuilder app)
    {
        // ---- pay grades and salary review rounds (payroll data: recent password check inside)
        var pay = app.MapGroup("/api/hr/payroll"); pay.RequireAuthorization();
        pay.MapGet("/grades", async (CompensationPlanningService svc, HttpContext ctx) => TypedResults.Ok(await svc.GradesAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollRead);
        pay.MapPost("/grades", async (SalaryGradeRequest body, CompensationPlanningService svc, HttpContext ctx) => { var v = await svc.SaveGradeAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/payroll/grades/{v.Id}", v); }).RequirePermission(Permissions.PayrollConfigure);
        pay.MapPut("/grades/{id:guid}", async (Guid id, SalaryGradeRequest body, CompensationPlanningService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveGradeAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollConfigure);
        pay.MapGet("/reviews", async (CompensationPlanningService svc, HttpContext ctx) => TypedResults.Ok(await svc.ReviewsAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollRead);
        pay.MapPost("/reviews", async (CreateSalaryReviewRequest body, CompensationPlanningService svc, HttpContext ctx) => { var v = await svc.CreateReviewAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/payroll/reviews/{v.Id}", v); }).RequirePermission(Permissions.PayrollConfigure);
        pay.MapGet("/reviews/{id:guid}", async (Guid id, CompensationPlanningService svc, HttpContext ctx) => TypedResults.Ok(await svc.ReviewAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollRead);
        pay.MapPost("/reviews/{id:guid}/employees", async (Guid id, AddReviewEmployeesRequest body, CompensationPlanningService svc, HttpContext ctx) => TypedResults.Ok(new { added = await svc.AddEmployeesAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted) })).RequirePermission(Permissions.PayrollConfigure);
        pay.MapPut("/reviews/{id:guid}/lines/{lineId:guid}", async (Guid id, Guid lineId, SetReviewLineRequest body, CompensationPlanningService svc, HttpContext ctx) => TypedResults.Ok(await svc.SetLineAsync(id, lineId, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollConfigure);
        pay.MapDelete("/reviews/{id:guid}/lines/{lineId:guid}", async (Guid id, Guid lineId, CompensationPlanningService svc, HttpContext ctx) => { await svc.RemoveLineAsync(id, lineId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); }).RequirePermission(Permissions.PayrollConfigure);
        // submit / apply / cancel need payroll.configure, approve / return need payroll.approve: both are checked in the service
        foreach (var (action, perm) in new[] { ("submit", Permissions.PayrollConfigure), ("apply", Permissions.PayrollConfigure), ("cancel", Permissions.PayrollConfigure), ("approve", Permissions.PayrollApprove), ("return", Permissions.PayrollApprove) })
            pay.MapPost($"/reviews/{{id:guid}}/{action}", async (Guid id, AdvanceDecisionRequest? body, CompensationPlanningService svc, HttpContext ctx) =>
                TypedResults.Ok(await svc.ActAsync(id, action, body?.Note, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(perm);

        // ---- early wage withdrawal
        pay.MapGet("/wage-advances", async (string? scope, BenefitService svc, HttpContext ctx) => TypedResults.Ok(await svc.WageAdvancesAsync(scope, ctx.RequirePrincipal(), ctx.RequestAborted)));
        pay.MapGet("/wage-advances/available", async (BenefitService svc, HttpContext ctx) => TypedResults.Ok(await svc.AvailabilityAsync(ctx.RequirePrincipal(), ctx.RequestAborted)));
        pay.MapPost("/wage-advances", async (WageAdvanceRequest body, BenefitService svc, HttpContext ctx) => { var v = await svc.RequestWageAdvanceAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/payroll/wage-advances/{v.Id}", v); });
        pay.MapPost("/wage-advances/{id:guid}/cancel", async (Guid id, BenefitService svc, HttpContext ctx) => TypedResults.Ok(await svc.WageAdvanceActAsync(id, "cancel", new(null, null), null, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        pay.MapPost("/wage-advances/{id:guid}/decide", async (Guid id, AdvanceDecisionRequest body, BenefitService svc, HttpContext ctx) => TypedResults.Ok(await svc.WageAdvanceActAsync(id, "decide", body, null, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollRun);
        pay.MapPost("/wage-advances/{id:guid}/disburse", async (Guid id, DisburseRequest? body, BenefitService svc, HttpContext ctx) => TypedResults.Ok(await svc.WageAdvanceActAsync(id, "disburse", new(null, null), body?.On, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollRun);

        // ---- claim policies and balances
        var cl = app.MapGroup("/api/hr/claims"); cl.RequireAuthorization();
        cl.MapGet("/balances", async (BenefitService svc, HttpContext ctx) => TypedResults.Ok(await svc.MyBalancesAsync(ctx.RequirePrincipal(), ctx.RequestAborted)));
        cl.MapGet("/policies", async (BenefitService svc, HttpContext ctx) => TypedResults.Ok(await svc.PoliciesAsync(ctx.RequestAborted))).RequirePermission(Permissions.ClaimsManage);
        cl.MapPost("/policies", async (ClaimPolicyRequest body, BenefitService svc, HttpContext ctx) => { var v = await svc.SavePolicyAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/claims/policies/{v.Id}", v); }).RequirePermission(Permissions.ClaimsManage);
        cl.MapPut("/policies/{id:guid}", async (Guid id, ClaimPolicyRequest body, BenefitService svc, HttpContext ctx) => TypedResults.Ok(await svc.SavePolicyAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.ClaimsManage);

        // ---- cash advances: everyone asks for and settles their own; deciding needs claims.approve (own reports) or claims.manage (anyone, and paying out)
        var ad = app.MapGroup("/api/hr/advances"); ad.RequireAuthorization();
        ad.MapGet("/", async (string? scope, BenefitService svc, HttpContext ctx) => TypedResults.Ok(await svc.AdvancesAsync(scope, ctx.RequirePrincipal(), ctx.RequestAborted)));
        ad.MapPost("/", async (CashAdvanceRequest body, BenefitService svc, HttpContext ctx) => { var v = await svc.RequestAdvanceAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/advances/{v.Id}", v); });
        ad.MapPost("/{id:guid}/cancel", async (Guid id, BenefitService svc, HttpContext ctx) => TypedResults.Ok(await svc.CancelAdvanceAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        ad.MapPost("/{id:guid}/settle", async (Guid id, SettleAdvanceRequest body, BenefitService svc, HttpContext ctx) => TypedResults.Ok(await svc.SettleAdvanceAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        ad.MapGet("/{id:guid}/receipt", async (Guid id, BenefitService svc, HttpContext ctx) => { var f = await svc.AdvanceReceiptAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.File(f.Bytes, f.ContentType, f.FileName); });
        ad.MapPost("/{id:guid}/decide", async (Guid id, AdvanceDecisionRequest body, BenefitService svc, HttpContext ctx) => TypedResults.Ok(await svc.DecideAdvanceAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.ClaimsApprove);
        ad.MapPost("/{id:guid}/settlement", async (Guid id, AdvanceDecisionRequest body, BenefitService svc, HttpContext ctx) => TypedResults.Ok(await svc.ApproveSettlementAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.ClaimsApprove);
        ad.MapPost("/{id:guid}/disburse", async (Guid id, DisburseRequest? body, BenefitService svc, HttpContext ctx) => TypedResults.Ok(await svc.DisburseAdvanceAsync(id, body ?? new(null), ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.ClaimsManage);
    }
}
