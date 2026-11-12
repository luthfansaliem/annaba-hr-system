using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>Stage 17: manpower plans, hiring requests, postings and the applicant pipeline, interviews, tests, offers, hiring; plus the public career page and candidate links.</summary>
public static class RecruitmentEndpoints
{
    public static void MapRecruitment(this IEndpointRouteBuilder app)
    {
        const string Hr = Permissions.HrConfigManage;
        var r = app.MapGroup("/api/hr/recruitment"); r.RequireAuthorization();
        r.MapGet("/career-link", async (RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.CareerLinkAsync(ctx.RequestAborted))).RequirePermission(Hr);
        r.MapGet("/summary", async (RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.SummaryAsync(ctx.RequestAborted))).RequirePermission(Hr);
        r.MapGet("/manpower", async (int? year, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.ManpowerAsync(year ?? DateTime.UtcNow.Year, ctx.RequestAborted))).RequirePermission(Hr);
        r.MapPost("/manpower", async (ManpowerRequest body, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveManpowerAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        r.MapPut("/manpower/{id:guid}", async (Guid id, ManpowerRequest body, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveManpowerAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        r.MapDelete("/manpower/{id:guid}", async (Guid id, RecruitmentService svc, HttpContext ctx) => { await svc.DeleteManpowerAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); }).RequirePermission(Hr);

        // managers raise hiring requests (checked in the service); HR decides
        r.MapGet("/requisitions", async (string? scope, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.RequisitionsAsync(scope, ctx.RequirePrincipal(), ctx.RequestAborted)));
        r.MapPost("/requisitions", async (RequisitionRequest body, RecruitmentService svc, HttpContext ctx) => { var v = await svc.RequestAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/recruitment/requisitions/{v.Id}", v); });
        r.MapPost("/requisitions/{id:guid}/cancel", async (Guid id, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.CancelRequisitionAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        r.MapPost("/requisitions/{id:guid}/decide", async (Guid id, DecideRequest body, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.DecideRequisitionAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);

        r.MapGet("/postings", async (RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.PostingsAsync(ctx.RequestAborted))).RequirePermission(Hr);
        r.MapPost("/postings", async (PostingRequest body, RecruitmentService svc, HttpContext ctx) => { var v = await svc.SavePostingAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/recruitment/postings/{v.Id}", v); }).RequirePermission(Hr);
        r.MapPut("/postings/{id:guid}", async (Guid id, PostingRequest body, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.SavePostingAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        r.MapPost("/postings/{id:guid}/open", async (Guid id, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.SetPostingStatusAsync(id, true, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        r.MapPost("/postings/{id:guid}/close", async (Guid id, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.SetPostingStatusAsync(id, false, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        r.MapGet("/postings/{id:guid}/applications", async (Guid id, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.ApplicationsAsync(id, ctx.RequestAborted))).RequirePermission(Hr);
        r.MapPost("/postings/{id:guid}/applications", async (Guid id, ApplyRequest body, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.AddCandidateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);

        r.MapGet("/applications/{id:guid}", async (Guid id, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.ApplicationAsync(id, ctx.RequestAborted))).RequirePermission(Hr);
        r.MapPatch("/applications/{id:guid}", async (Guid id, ApplicationUpdate body, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.UpdateApplicationAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        // HR, or an interviewer of this applicant (checked in the service)
        r.MapGet("/applications/{id:guid}/cv", async (Guid id, RecruitmentService svc, HttpContext ctx) => ctx.Export(await svc.CvAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        r.MapPost("/applications/{id:guid}/interviews", async (Guid id, InterviewRequest body, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.ScheduleAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        r.MapPost("/applications/{id:guid}/tests", async (Guid id, SendTestRequest body, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.SendTestAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        r.MapPost("/applications/{id:guid}/offers", async (Guid id, OfferRequest body, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.CreateOfferAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        r.MapPost("/applications/{id:guid}/hire", async (Guid id, HireRequest body, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.HireAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.EmployeesCreate);
        r.MapDelete("/candidates/{id:guid}", async (Guid id, RecruitmentService svc, HttpContext ctx) => { await svc.EraseCandidateAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); }).RequirePermission(Hr);
        r.MapPost("/offers/{id:guid}/send", async (Guid id, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.SendOfferAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        r.MapPost("/offers/{id:guid}/withdraw", async (Guid id, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.WithdrawOfferAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        r.MapGet("/offers/{id:guid}/pdf", async (Guid id, RecruitmentService svc, HttpContext ctx) => ctx.Export(await svc.OfferPdfAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);

        r.MapGet("/interviews/mine", async (RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.MyInterviewsAsync(ctx.RequirePrincipal(), ctx.RequestAborted)));
        r.MapPost("/interviews/{id:guid}/feedback", async (Guid id, FeedbackRequest body, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.FeedbackAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        r.MapPost("/interviews/{id:guid}/cancel", async (Guid id, RecruitmentService svc, HttpContext ctx) => { await svc.CancelInterviewAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); }).RequirePermission(Hr);

        r.MapGet("/assessments", async (RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.TemplatesAsync(ctx.RequestAborted))).RequirePermission(Hr);
        r.MapPost("/assessments", async (TemplateRequest body, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveTemplateAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        r.MapPost("/assessments/sample-profile", async (RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.AddSampleProfileAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        r.MapPut("/assessments/{id:guid}", async (Guid id, TemplateRequest body, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveTemplateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);

        // public: the career page, and the candidate's private links (the token proves who they are)
        var c = app.MapGroup("/api/careers").AllowAnonymous();
        c.MapGet("/test", async (HttpContext ctx, RecruitmentService svc) => TypedResults.Ok(await svc.PublicTestAsync(ctx.Request.Headers["X-Candidate-Token"].ToString(), false, ctx.Client(), ctx.RequestAborted)));
        c.MapPost("/test/start", async (HttpContext ctx, RecruitmentService svc) => TypedResults.Ok(await svc.PublicTestAsync(ctx.Request.Headers["X-Candidate-Token"].ToString(), true, ctx.Client(), ctx.RequestAborted)));
        c.MapPost("/test/submit", async (TestAnswers body, HttpContext ctx, RecruitmentService svc) => { await svc.SubmitTestAsync(ctx.Request.Headers["X-Candidate-Token"].ToString(), body, ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); });
        c.MapGet("/offer", async (HttpContext ctx, RecruitmentService svc) => TypedResults.Ok(await svc.PublicOfferAsync(ctx.Request.Headers["X-Candidate-Token"].ToString(), ctx.Client(), ctx.RequestAborted)));
        c.MapPost("/offer/respond", async (OfferResponse body, HttpContext ctx, RecruitmentService svc) => TypedResults.Ok(await svc.RespondOfferAsync(ctx.Request.Headers["X-Candidate-Token"].ToString(), body, ctx.Client(), ctx.RequestAborted)));
        c.MapGet("/{tenant}", async (string tenant, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.CareersAsync(tenant, ctx.RequestAborted)));
        c.MapGet("/{tenant}/{job}", async (string tenant, string job, RecruitmentService svc, HttpContext ctx) => TypedResults.Ok(await svc.CareerJobAsync(tenant, job, ctx.RequestAborted)));
        c.MapPost("/{tenant}/{job}/apply", async (string tenant, string job, ApplyRequest body, RecruitmentService svc, HttpContext ctx) => { await svc.ApplyAsync(tenant, job, body, ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); });
    }
}
