using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>Checklists, documents, attendance corrections, overtime and month close.</summary>
public static class HrOpsEndpoints
{
    public static void MapHrOps(this IEndpointRouteBuilder app)
    {
        var e = app.MapGroup("/api/hr/employees"); e.RequireAuthorization();
        e.MapGet("/expiring", async (int? days, bool? documents, ChecklistService svc, HttpContext ctx) => Results.Ok(await svc.ExpiringAsync(days ?? 60, documents ?? false, ctx.RequestAborted))).RequirePermission(Permissions.EmployeesRead);
        e.MapGet("/{id:guid}/checklist", async (Guid id, ChecklistService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(id, ctx.RequestAborted))).RequirePermission(Permissions.EmployeesRead);
        e.MapPost("/{id:guid}/checklist", async (Guid id, AddTaskRequest body, ChecklistService svc, HttpContext ctx) =>
        {
            var t = await svc.AddAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.Created($"/api/hr/employees/{id}/checklist/{t.Id}", t);
        }).RequirePermission(Permissions.EmployeesUpdate);
        e.MapPost("/checklist/{taskId:guid}/done", async (Guid taskId, ChecklistService svc, HttpContext ctx) => Results.Ok(await svc.SetDoneAsync(taskId, true, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.EmployeesUpdate);
        e.MapPost("/checklist/{taskId:guid}/reopen", async (Guid taskId, ChecklistService svc, HttpContext ctx) => Results.Ok(await svc.SetDoneAsync(taskId, false, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.EmployeesUpdate);
        e.MapDelete("/checklist/{taskId:guid}", async (Guid taskId, ChecklistService svc, HttpContext ctx) =>
        {
            await svc.DeleteAsync(taskId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.EmployeesUpdate);

        // Documents: every route asks for a recent password check inside the service.
        e.MapGet("/{id:guid}/documents", async (Guid id, DocumentService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.DocumentsRead);
        e.MapPost("/{id:guid}/documents", async (Guid id, UploadDocumentRequest body, DocumentService svc, HttpContext ctx) =>
        {
            var d = await svc.UploadAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.Created($"/api/hr/employees/documents/{d.Id}", d);
        }).RequirePermission(Permissions.DocumentsManage);
        e.MapGet("/documents/{docId:guid}/download", async (Guid docId, DocumentService svc, HttpContext ctx) =>
        {
            var f = await svc.DownloadAsync(docId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.File(f.Bytes, f.ContentType, f.FileName);
        }).RequirePermission(Permissions.DocumentsRead);
        e.MapDelete("/documents/{docId:guid}", async (Guid docId, DocumentService svc, HttpContext ctx) =>
        {
            await svc.DeleteAsync(docId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.DocumentsManage);

        var dg = app.MapGroup("/api/hr/delegations"); dg.RequireAuthorization();
        dg.MapGet("/", async (bool? all, DelegationService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(all ?? false, ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.LeaveApprove);
        dg.MapGet("/candidates", async (DelegationService svc, HttpContext ctx) => Results.Ok(await svc.CandidatesAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.LeaveApprove);
        dg.MapPost("/", async (CreateDelegationRequest body, DelegationService svc, HttpContext ctx) =>
        {
            var d = await svc.CreateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.Created($"/api/hr/delegations/{d.Id}", d);
        }).RequirePermission(Permissions.LeaveApprove);
        dg.MapDelete("/{id:guid}", async (Guid id, DelegationService svc, HttpContext ctx) =>
        {
            await svc.RevokeAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.LeaveApprove);

        var sh = app.MapGroup("/api/hr/shifts"); sh.RequireAuthorization();
        sh.MapGet("/", async (bool? includeArchived, ShiftService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(includeArchived ?? false, ctx.RequestAborted))).RequirePermission(Permissions.AttendanceReadAll);
        sh.MapPost("/", async (ShiftRequest body, ShiftService svc, HttpContext ctx) =>
        {
            var v = await svc.SaveAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.Created($"/api/hr/shifts/{v.Id}", v);
        }).RequirePermission(Permissions.AttendanceManage);
        sh.MapPatch("/{id:guid}", async (Guid id, ShiftRequest body, ShiftService svc, HttpContext ctx) => Results.Ok(await svc.SaveAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AttendanceManage);
        sh.MapGet("/assignments", async (ShiftService svc, HttpContext ctx) => Results.Ok(await svc.AssignmentsAsync(ctx.RequestAborted))).RequirePermission(Permissions.AttendanceReadAll);
        sh.MapPut("/assignments/{employeeId:guid}", async (Guid employeeId, AssignShiftRequest body, ShiftService svc, HttpContext ctx)
            => Results.Ok(await svc.AssignAsync(employeeId, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AttendanceManage);

        var a = app.MapGroup("/api/hr/attendance"); a.RequireAuthorization();
        foreach (var kind in new[] { "corrections", "overtime" })
        {
            a.MapGet($"/{kind}", async (string? scope, string? status, int? page, int? pageSize, TimeRequestService svc, HttpContext ctx)
                => Results.Ok(await svc.ListAsync(kind, new TimeQuery(scope, status, page ?? 1, pageSize ?? 20), ctx.RequirePrincipal(), ctx.RequestAborted)));
            a.MapPost($"/{kind}/{{id:guid}}/approve", async (Guid id, DecisionRequest? body, TimeRequestService svc, HttpContext ctx)
                => Results.Ok(await svc.DecideAsync(kind, id, true, body?.Note, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AttendanceApprove);
            a.MapPost($"/{kind}/{{id:guid}}/reject", async (Guid id, DecisionRequest? body, TimeRequestService svc, HttpContext ctx)
                => Results.Ok(await svc.DecideAsync(kind, id, false, body?.Note, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AttendanceApprove);
            a.MapPost($"/{kind}/{{id:guid}}/cancel", async (Guid id, TimeRequestService svc, HttpContext ctx)
                => Results.Ok(await svc.CancelAsync(kind, id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        }
        a.MapPost("/corrections", async (SubmitCorrectionRequest body, TimeRequestService svc, HttpContext ctx) =>
        {
            var r = await svc.SubmitCorrectionAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.Created($"/api/hr/attendance/corrections/{r.Id}", r);
        });
        a.MapPost("/overtime", async (SubmitOvertimeRequest body, TimeRequestService svc, HttpContext ctx) =>
        {
            var r = await svc.SubmitOvertimeAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.Created($"/api/hr/attendance/overtime/{r.Id}", r);
        });
        a.MapPost("/import", async (ImportAttendanceRequest body, AttendanceImportService svc, HttpContext ctx) => Results.Ok(await svc.ImportAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AttendanceManage);
        a.MapGet("/periods", async (TimeRequestService svc, HttpContext ctx) => Results.Ok(await svc.PeriodsAsync(ctx.RequestAborted))).RequirePermission(Permissions.AttendanceReadAll);
        a.MapPost("/periods/{month}/close", async (string month, TimeRequestService svc, HttpContext ctx) => Results.Ok(await svc.CloseAsync(month, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AttendanceManage);
        a.MapPost("/periods/{month}/reopen", async (string month, ReopenRequest body, TimeRequestService svc, HttpContext ctx) =>
        {
            await svc.ReopenAsync(month, body.Reason, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.AttendanceManage);
    }
}
