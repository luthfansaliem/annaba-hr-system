using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>Stage 18: courses, lessons and quizzes (HR), assignments and progress, and people taking their courses and downloading certificates.</summary>
public static class LearningEndpoints
{
    public static void MapLearning(this IEndpointRouteBuilder app)
    {
        const string Hr = Permissions.HrConfigManage;
        var l = app.MapGroup("/api/hr/learning"); l.RequireAuthorization();
        l.MapGet("/summary", async (LearningService svc, HttpContext ctx) => TypedResults.Ok(await svc.SummaryAsync(ctx.RequestAborted))).RequirePermission(Hr);
        l.MapGet("/courses", async (LearningService svc, HttpContext ctx) => TypedResults.Ok(await svc.CoursesAsync(ctx.RequestAborted))).RequirePermission(Hr);
        l.MapPost("/courses", async (CourseRequest body, LearningService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveCourseAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        l.MapGet("/courses/{id:guid}", async (Guid id, LearningService svc, HttpContext ctx) => TypedResults.Ok(await svc.EditorAsync(id, ctx.RequestAborted))).RequirePermission(Hr);
        l.MapPut("/courses/{id:guid}", async (Guid id, CourseRequest body, LearningService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveCourseAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        l.MapPost("/courses/{id:guid}/publish", async (Guid id, LearningService svc, HttpContext ctx) => TypedResults.Ok(await svc.SetStatusAsync(id, "publish", ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        l.MapPost("/courses/{id:guid}/archive", async (Guid id, LearningService svc, HttpContext ctx) => TypedResults.Ok(await svc.SetStatusAsync(id, "archive", ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        l.MapPost("/courses/{id:guid}/lessons", async (Guid id, LessonRequest body, LearningService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveLessonAsync(id, null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        l.MapPut("/courses/{id:guid}/lessons/{lessonId:guid}", async (Guid id, Guid lessonId, LessonRequest body, LearningService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveLessonAsync(id, lessonId, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        l.MapDelete("/courses/{id:guid}/lessons/{lessonId:guid}", async (Guid id, Guid lessonId, LearningService svc, HttpContext ctx) => TypedResults.Ok(await svc.DeleteLessonAsync(id, lessonId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);
        l.MapPost("/courses/{id:guid}/assign", async (Guid id, AssignRequest body, LearningService svc, HttpContext ctx) => TypedResults.Ok(new { added = await svc.AssignAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted) })).RequirePermission(Hr);
        l.MapGet("/courses/{id:guid}/enrollments", async (Guid id, LearningService svc, HttpContext ctx) => TypedResults.Ok(await svc.EnrollmentsAsync(id, ctx.RequestAborted))).RequirePermission(Hr);
        l.MapPost("/enrollments/{id:guid}/reset", async (Guid id, LearningService svc, HttpContext ctx) => TypedResults.Ok(await svc.ResetAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Hr);

        // people taking courses; a manager's view of the team. Ownership is checked in the service.
        l.MapGet("/mine", async (LearningService svc, HttpContext ctx) => TypedResults.Ok(await svc.MyCoursesAsync(ctx.RequirePrincipal(), ctx.RequestAborted)));
        l.MapGet("/team", async (LearningService svc, HttpContext ctx) => TypedResults.Ok(await svc.TeamAsync(ctx.RequirePrincipal(), ctx.RequestAborted)));
        l.MapGet("/take/{id:guid}", async (Guid id, LearningService svc, HttpContext ctx) => TypedResults.Ok(await svc.PlayerAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted)));
        l.MapPost("/take/{id:guid}/enroll", async (Guid id, LearningService svc, HttpContext ctx) => TypedResults.Ok(await svc.EnrollAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        l.MapPost("/take/{id:guid}/lessons/{lessonId:guid}/done", async (Guid id, Guid lessonId, LearningService svc, HttpContext ctx) => TypedResults.Ok(await svc.LessonDoneAsync(id, lessonId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        l.MapGet("/take/{id:guid}/lessons/{lessonId:guid}/file", async (Guid id, Guid lessonId, LearningService svc, HttpContext ctx) => ctx.Export(await svc.LessonFileAsync(id, lessonId, ctx.RequirePrincipal(), ctx.RequestAborted)));
        l.MapPost("/take/{id:guid}/quiz", async (Guid id, QuizSubmit body, LearningService svc, HttpContext ctx) => TypedResults.Ok(await svc.SubmitQuizAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        l.MapGet("/certificates/{id:guid}", async (Guid id, LearningService svc, HttpContext ctx) => ctx.Export(await svc.CertificateAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
    }
}
