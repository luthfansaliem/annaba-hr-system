using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>Stage 12: custom fields, letter templates, forms and surveys, HR helpdesk, company calendar, employee directory, branches.</summary>
public static class HrAdminEndpoints
{
    public static void MapHrAdmin(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/hr"); g.RequireAuthorization();

        // ---- custom fields
        g.MapGet("/custom-fields", async (HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.FieldsAsync(ctx.RequestAborted))).RequirePermission(Permissions.EmployeesRead);
        g.MapPost("/custom-fields", async (CustomFieldRequest body, HrAdminService svc, HttpContext ctx) => { var v = await svc.SaveFieldAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/custom-fields/{v.Id}", v); }).RequirePermission(Permissions.HrConfigManage);
        g.MapPut("/custom-fields/{id:guid}", async (Guid id, CustomFieldRequest body, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveFieldAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
        g.MapGet("/employees/{id:guid}/custom-fields", async (Guid id, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.EmployeeFieldsAsync(id, ctx.RequestAborted))).RequirePermission(Permissions.EmployeesRead);
        g.MapPut("/employees/{id:guid}/custom-fields", async (Guid id, Dictionary<string, string?> body, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.SetEmployeeFieldsAsync(id, body, false, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.EmployeesUpdate);
        g.MapGet("/employees/{id:guid}/branch", async (Guid id, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.EmployeeBranchAsync(id, ctx.RequestAborted))).RequirePermission(Permissions.EmployeesRead);
        g.MapPut("/employees/{id:guid}/branch", async (Guid id, SetBranchRequest body, HrAdminService svc, HttpContext ctx) => { await svc.SetEmployeeBranchAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); }).RequirePermission(Permissions.EmployeesUpdate);
        g.MapGet("/me/custom-fields", async (HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.MyFieldsAsync(ctx.RequirePrincipal(), ctx.RequestAborted)));
        g.MapPut("/me/custom-fields", async (Dictionary<string, string?> body, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.SetMyFieldsAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));

        // ---- letter templates and issued letters
        g.MapGet("/letter-templates", async (HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.TemplatesAsync(ctx.RequestAborted))).RequirePermission(Permissions.EmployeesRead);
        g.MapPost("/letter-templates", async (LetterTemplateRequest body, HrAdminService svc, HttpContext ctx) => { var v = await svc.SaveTemplateAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/letter-templates/{v.Id}", v); }).RequirePermission(Permissions.HrConfigManage);
        g.MapPut("/letter-templates/{id:guid}", async (Guid id, LetterTemplateRequest body, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveTemplateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
        g.MapPost("/issued-letters/preview", async (IssueLetterRequest body, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.PreviewAsync(body, ctx.RequestAborted))).RequirePermission(Permissions.EmployeesUpdate);
        g.MapPost("/issued-letters", async (IssueLetterRequest body, HrAdminService svc, HttpContext ctx) => { var v = await svc.IssueAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/issued-letters/{v.Id}", v); }).RequirePermission(Permissions.EmployeesUpdate);
        g.MapGet("/issued-letters", async (Guid? employeeId, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.IssuedAsync(employeeId, false, ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.EmployeesRead);
        g.MapGet("/me/issued-letters", async (HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.IssuedAsync(null, true, ctx.RequirePrincipal(), ctx.RequestAborted)));
        g.MapGet("/issued-letters/{id:guid}/pdf", async (Guid id, HrAdminService svc, HttpContext ctx) => ctx.Export(await svc.LetterPdfAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));

        // ---- forms and surveys
        g.MapGet("/forms", async (HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.FormsAsync(ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
        g.MapPost("/forms", async (HrFormRequest body, HrAdminService svc, HttpContext ctx) => { var v = await svc.SaveFormAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/forms/{v.Id}", v); }).RequirePermission(Permissions.HrConfigManage);
        g.MapPut("/forms/{id:guid}", async (Guid id, HrFormRequest body, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveFormAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
        g.MapPost("/forms/{id:guid}/open", async (Guid id, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.SetFormStatusAsync(id, true, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
        g.MapPost("/forms/{id:guid}/close", async (Guid id, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.SetFormStatusAsync(id, false, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
        g.MapGet("/forms/{id:guid}/results", async (Guid id, HrAdminService svc, HttpContext ctx) =>
        {
            var r = await svc.ResultsAsync(id, ctx.RequestAborted);
            return TypedResults.Ok(new { r.Form, Questions = r.Questions.Select(q => new { q.Id, q.Label, q.Type, q.Answered, Counts = q.Counts.Select(c => new { option = c.Option, count = c.Count }), q.Average, q.Texts }) });
        }).RequirePermission(Permissions.HrConfigManage);
        g.MapGet("/me/forms", async (HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.MyFormsAsync(ctx.RequirePrincipal(), ctx.RequestAborted)));
        g.MapPost("/me/forms/{id:guid}", async (Guid id, FormAnswerRequest body, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.AnswerAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));

        // ---- HR helpdesk: employees see their own questions, HR (hrconfig.manage) sees all; checked in the service
        g.MapGet("/helpdesk", async (string? scope, string? status, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.TicketsAsync(scope != "all", status, ctx.RequirePrincipal(), ctx.RequestAborted)));
        g.MapPost("/helpdesk", async (NewTicketRequest body, HrAdminService svc, HttpContext ctx) => { var v = await svc.NewTicketAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/helpdesk/{v.Id}", v); });
        g.MapGet("/helpdesk/{id:guid}", async (Guid id, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.TicketAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted)));
        g.MapPost("/helpdesk/{id:guid}/messages", async (Guid id, TicketReplyRequest body, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.ReplyAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        g.MapPost("/helpdesk/{id:guid}/status", async (Guid id, TicketStatusRequest body, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.SetTicketStatusAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));

        // ---- company calendar and directory (every linked employee)
        g.MapGet("/calendar", async (int year, int month, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.CalendarAsync(year, month, ctx.RequirePrincipal(), ctx.RequestAborted)));
        g.MapPost("/calendar/events", async (CompanyEventRequest body, HrAdminService svc, HttpContext ctx) => { var v = await svc.SaveEventAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/calendar/events/{v.Id}", v); }).RequirePermission(Permissions.HrConfigManage);
        g.MapPut("/calendar/events/{id:guid}", async (Guid id, CompanyEventRequest body, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveEventAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
        g.MapDelete("/calendar/events/{id:guid}", async (Guid id, HrAdminService svc, HttpContext ctx) => { await svc.DeleteEventAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.NoContent(); }).RequirePermission(Permissions.HrConfigManage);
        g.MapGet("/directory", async (string? q, Guid? orgUnitId, Guid? branchId, int? page, int? pageSize, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.DirectoryAsync(q, orgUnitId, branchId, page ?? 1, pageSize ?? 50, ctx.RequirePrincipal(), ctx.RequestAborted)));

        // ---- branches and business units
        g.MapGet("/branches", async (HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.BranchesAsync(ctx.RequestAborted))).RequirePermission(Permissions.EmployeesRead);
        g.MapPost("/branches", async (BranchRequest body, HrAdminService svc, HttpContext ctx) => { var v = await svc.SaveBranchAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return TypedResults.Created($"/api/hr/branches/{v.Id}", v); }).RequirePermission(Permissions.HrConfigManage);
        g.MapPut("/branches/{id:guid}", async (Guid id, BranchRequest body, HrAdminService svc, HttpContext ctx) => TypedResults.Ok(await svc.SaveBranchAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);
    }
}
