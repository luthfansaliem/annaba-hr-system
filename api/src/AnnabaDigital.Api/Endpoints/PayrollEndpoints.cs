using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

public static class PayrollEndpoints
{
    public static void MapPayroll(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/hr/payroll");
        g.RequireAuthorization();

        // ---- self-service: your own payslips (only once a run is locked)
        g.MapGet("/payslips", async (PayrollService svc, HttpContext ctx) => Results.Ok(await svc.MyPayslipsAsync(ctx.RequirePrincipal(), ctx.RequestAborted)));
        g.MapGet("/payslips/{itemId:guid}/pdf", async (Guid itemId, PayrollExtrasService svc, HttpContext ctx) =>
        {
            var f = await svc.PayslipPdfAsync(itemId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.File(f.Bytes, f.ContentType, f.FileName);
        });
        g.MapGet("/payslips/{itemId:guid}", async (Guid itemId, PayrollService svc, HttpContext ctx) => Results.Ok(await svc.MyPayslipAsync(itemId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));

        // ---- compensation
        g.MapGet("/compensation", async (PayrollService svc, HttpContext ctx) => Results.Ok(await svc.CompensationListAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollRead);
        g.MapGet("/rules", async (int? year, int? month, PayrollRulesService svc, HttpContext ctx) => Results.Ok(await svc.OverviewAsync(year, month, ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollRead);
        g.MapPost("/rules", async (RulesUpdateRequest body, PayrollRulesService svc, HttpContext ctx) => Results.Ok(await svc.CreateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollConfigure);
        g.MapGet("/retro", async (PayrollService svc, HttpContext ctx) => Results.Ok(await svc.PendingRetroAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollRead);
        g.MapGet("/compensation/{employeeId:guid}", async (Guid employeeId, PayrollService svc, HttpContext ctx)
            => Results.Ok(await svc.CompensationAsync(employeeId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollRead);
        g.MapPut("/compensation/{employeeId:guid}", async (Guid employeeId, SetCompensationRequest body, PayrollService svc, HttpContext ctx)
            => Results.Ok(await svc.SetCompensationAsync(employeeId, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollConfigure);

        // ---- pay runs
        g.MapGet("/runs", async (PayrollService svc, HttpContext ctx) => Results.Ok(await svc.RunsAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollRead);
        g.MapPost("/runs", async (CreateRunRequest body, PayrollService svc, HttpContext ctx) =>
        {
            var r = await svc.CreateRunAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/hr/payroll/runs/{r.Id}", r);
        }).RequirePermission(Permissions.PayrollRun);
        g.MapGet("/runs/{id:guid}", async (Guid id, PayrollService svc, HttpContext ctx) => Results.Ok(await svc.RunAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollRead);
        g.MapDelete("/runs/{id:guid}", async (Guid id, PayrollService svc, HttpContext ctx) =>
        {
            await svc.DeleteRunAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.PayrollRun);
        g.MapPost("/runs/{id:guid}/lines", async (Guid id, AddLineRequest body, PayrollService svc, HttpContext ctx) =>
        {
            var l = await svc.AddLineAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/hr/payroll/runs/{id}/lines/{l.Id}", l);
        }).RequirePermission(Permissions.PayrollRun);
        g.MapDelete("/runs/{id:guid}/lines/{lineId:guid}", async (Guid id, Guid lineId, PayrollService svc, HttpContext ctx) =>
        {
            await svc.DeleteLineAsync(id, lineId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.PayrollRun);
        g.MapPost("/runs/{id:guid}/thr", async (Guid id, PayrollService svc, HttpContext ctx) => Results.Ok(new { added = await svc.AddThrAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted) })).RequirePermission(Permissions.PayrollRun);
        g.MapPost("/runs/{id:guid}/calculate", async (Guid id, PayrollService svc, HttpContext ctx) => Results.Ok(await svc.CalculateAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollRun);
        g.MapPost("/runs/{id:guid}/approve", async (Guid id, PayrollService svc, HttpContext ctx) => Results.Ok(await svc.ApproveAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollApprove);
        g.MapPost("/runs/{id:guid}/reject", async (Guid id, RejectRunRequest body, PayrollService svc, HttpContext ctx)
            => Results.Ok(await svc.RejectAsync(id, body.Note, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollApprove);
        g.MapPost("/runs/{id:guid}/lock", async (Guid id, PayrollService svc, HttpContext ctx) => Results.Ok(await svc.LockAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollLock);
        g.MapPost("/runs/{id:guid}/paid", async (Guid id, PayrollService svc, HttpContext ctx) => Results.Ok(await svc.MarkPaidAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollLock);

        // ---- files: the bank file moves money, so it needs the lock permission; the register is a read
        g.MapGet("/runs/{id:guid}/bank-file", async (Guid id, PayrollService svc, HttpContext ctx) =>
        {
            var f = await svc.BankFileAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.File(f.Bytes, f.ContentType, f.FileName);
        }).RequirePermission(Permissions.PayrollLock);
        g.MapGet("/runs/{id:guid}/register", async (Guid id, PayrollService svc, HttpContext ctx) =>
        {
            var f = await svc.RegisterAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.File(f.Bytes, f.ContentType, f.FileName);
        }).RequirePermission(Permissions.PayrollRead);

        // ---- loans, severance, reports
        g.MapGet("/loans", async (bool? activeOnly, PayrollExtrasService svc, HttpContext ctx) => Results.Ok(await svc.LoansAsync(activeOnly ?? false, ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollRead);
        g.MapPost("/loans", async (CreateLoanRequest body, PayrollExtrasService svc, HttpContext ctx) =>
        {
            var l = await svc.CreateLoanAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.Created($"/api/hr/payroll/loans/{l.Id}", l);
        }).RequirePermission(Permissions.PayrollConfigure);
        g.MapDelete("/loans/{id:guid}", async (Guid id, PayrollExtrasService svc, HttpContext ctx) =>
        {
            await svc.CancelLoanAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.PayrollConfigure);
        g.MapPost("/severance/preview", async (SeveranceRequest body, PayrollExtrasService svc, HttpContext ctx) => Results.Ok(await svc.PreviewSeveranceAsync(body, ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.PayrollRead);
        g.MapPost("/runs/{id:guid}/severance", async (Guid id, SeveranceRequest body, PayrollExtrasService svc, HttpContext ctx) => Results.Ok(new { added = await svc.ApplySeveranceAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted) })).RequirePermission(Permissions.PayrollRun);
        g.MapGet("/runs/{id:guid}/bpjs", async (Guid id, PayrollExtrasService svc, HttpContext ctx) =>
        {
            var f = await svc.BpjsReportAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.File(f.Bytes, f.ContentType, f.FileName);
        }).RequirePermission(Permissions.PayrollRead);
        g.MapGet("/runs/{id:guid}/journal", async (Guid id, PayrollExtrasService svc, HttpContext ctx) =>
        {
            var f = await svc.JournalAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.File(f.Bytes, f.ContentType, f.FileName);
        }).RequirePermission(Permissions.PayrollRead);
        g.MapGet("/reports/annual-tax", async (int? year, PayrollExtrasService svc, HttpContext ctx) =>
        {
            var f = await svc.AnnualTaxAsync(year ?? DateTime.UtcNow.Year, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.File(f.Bytes, f.ContentType, f.FileName);
        }).RequirePermission(Permissions.PayrollRead);
    }
}
