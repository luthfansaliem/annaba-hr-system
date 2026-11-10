using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

public static class HrEndpoints
{
    public static void MapHr(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/hr");

        g.MapGet("/org-units", async (bool? includeArchived, OrgUnitService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(includeArchived ?? false, ctx.RequestAborted))).RequirePermission(Permissions.EmployeesRead);
        g.MapPost("/org-units", async (OrgUnitRequest body, OrgUnitService svc, HttpContext ctx) =>
        {
            var u = await svc.CreateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/hr/org-units/{u.Id}", u);
        }).RequirePermission(Permissions.HrConfigManage);
        g.MapPatch("/org-units/{id:guid}", async (Guid id, OrgUnitRequest body, OrgUnitService svc, HttpContext ctx)
            => Results.Ok(await svc.UpdateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.HrConfigManage);

        var e = g.MapGroup("/employees");
        e.MapGet("/", async (string? q, string? status, Guid? orgUnitId, string? type, string? sort, string? dir, int? page, int? pageSize, EmployeeService svc, HttpContext ctx)
            => Results.Ok(await svc.ListAsync(new EmployeeQuery(q, status, orgUnitId, type, sort, dir, page ?? 1, pageSize ?? 10), ctx.RequestAborted))).RequirePermission(Permissions.EmployeesRead);

        // Export is a separate permission (it moves data out of the system) and is audited.
        e.MapGet("/org-chart", async (EmployeeService svc, HttpContext ctx) => Results.Ok(await svc.OrgChartAsync(ctx.RequestAborted))).RequirePermission(Permissions.EmployeesRead);
        e.MapGet("/export", async (string? q, string? status, Guid? orgUnitId, string? type, EmployeeService svc, HttpContext ctx) =>
        {
            var f = await svc.ExportAsync(new EmployeeQuery(q, status, orgUnitId, type, null, null), ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.File(f.Bytes, f.ContentType, f.FileName);
        }).RequirePermission(Permissions.EmployeesExport);

        // All-or-nothing CSV load; a dry run only reports. Protected columns inside need employees.read_sensitive and a recent password check (the service enforces both).
        e.MapPost("/import", async (ImportEmployeesRequest body, EmployeeImportService svc, HttpContext ctx)
            => Results.Ok(await svc.ImportAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.EmployeesCreate);

        e.MapPost("/", async (CreateEmployeeRequest body, EmployeeService svc, HttpContext ctx) =>
        {
            var d = await svc.CreateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/hr/employees/{d.Id}", d);
        }).RequirePermission(Permissions.EmployeesCreate);

        e.MapGet("/{id:guid}", async (Guid id, EmployeeService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.EmployeesRead);
        e.MapPatch("/{id:guid}", async (Guid id, UpdateEmployeeRequest body, EmployeeService svc, HttpContext ctx)
            => Results.Ok(await svc.UpdateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.EmployeesUpdate);
        e.MapPost("/{id:guid}/employment", async (Guid id, ChangeEmploymentRequest body, EmployeeService svc, HttpContext ctx)
            => Results.Ok(await svc.ChangeEmploymentAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.EmployeesUpdate);
        e.MapPost("/{id:guid}/terminate", async (Guid id, TerminateRequest body, EmployeeService svc, HttpContext ctx)
            => Results.Ok(await svc.TerminateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.EmployeesUpdate);
    }
}
