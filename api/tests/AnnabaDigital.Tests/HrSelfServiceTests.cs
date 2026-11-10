using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Employee change requests, the personal-data export, and erasure/anonymisation.</summary>
[Collection("db")]
public sealed class HrSelfServiceTests(PostgresFixture fx)
{
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");
    private sealed record Person(Guid EmployeeId, TestBrowser B, string Email, Guid UserId);

    private static async Task<TestBrowser> As(ApiFactory f, Seed seed, string role, bool reauth = false)
    {
        var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return b;
    }

    private static async Task<Person> Worker(ApiFactory f, Seed seed, TestClock clock, TestBrowser hr, string name, string? bank = "1234567890")
    {
        var u = await seed.UserAsync(role: "user");
        var r = await hr.Post("/api/hr/employees", new { fullName = name, hireDate = D(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddYears(-3))), jobTitle = "Analyst", userId = u.Id, bankName = "BCA", bankAccount = bank, taxId = "11.111.111.1-111.000", nationalId = "3174010101900001" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password);
        return new Person((await r.Body()).GetProperty("id").GetGuid(), b, u.Email, u.Id);
    }

    [Fact]
    public async Task Contact_details_change_directly_while_pay_and_identity_changes_wait_for_hr_and_protected_ones_need_a_recent_password_check()
    {
        var seed = new Seed(fx.Factory, fx.Clock); var hr = await As(fx.Factory, seed, "hr_admin", reauth: true); var w = await Worker(fx.Factory, seed, fx.Clock, hr, "Request Person");

        var me = await w.B.SendAsync(HttpMethod.Patch, "/api/hr/me", new { phone = "0812-000", address = "Jl. Baru 1" });
        Assert.Equal("0812-000", (await me.Body()).GetProperty("phone").GetString());
        Assert.True(await seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "hr.self.update" && a.TargetId == w.EmployeeId.ToString())));

        var name = await w.B.Post("/api/hr/me/change-requests", new { field = "full_name", value = "Request Person Putri", reason = "Marriage" });
        Assert.Equal(HttpStatusCode.Created, name.StatusCode); var nameId = (await name.Body()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await w.B.Post("/api/hr/me/change-requests", new { field = "full_name", value = "Again" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await w.B.Post("/api/hr/me/change-requests", new { field = "marital_status", value = "complicated" })).StatusCode);
        var bank = await w.B.Post("/api/hr/me/change-requests", new { field = "bank_account", value = "9876543210" });
        Assert.Equal("reauth_required", await bank.ErrorCode());                                          // protected: needs a fresh password check first
        await w.B.Post("/api/auth/reauth", new { password = Seed.Password });
        var bank2 = await w.B.Post("/api/hr/me/change-requests", new { field = "bank_account", value = "9876543210" }); Assert.Equal(HttpStatusCode.Created, bank2.StatusCode);
        var bankId = (await bank2.Body()).GetProperty("id").GetGuid();
        Assert.DoesNotContain("9876543210", await seed.Db(db => db.ChangeRequests.Where(c => c.Id == bankId).Select(c => c.ValueEnc).SingleAsync())); // stored encrypted

        Assert.Equal(HttpStatusCode.Forbidden, (await w.B.Post($"/api/hr/change-requests/{nameId}/approve")).StatusCode);                       // not their call
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As(fx.Factory, seed, "hr_head")).Post($"/api/hr/change-requests/{nameId}/approve")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post($"/api/hr/change-requests/{nameId}/reject", new { })).StatusCode);      // a rejection needs a reason

        Assert.Equal(HttpStatusCode.OK, (await hr.Post($"/api/hr/change-requests/{nameId}/approve")).StatusCode);
        Assert.Equal("Request Person Putri", await seed.Db(db => db.Employees.Where(e => e.Id == w.EmployeeId).Select(e => e.FullName).SingleAsync()));
        var shown = (await (await hr.Get("/api/hr/change-requests?status=pending")).Body()).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == bankId);
        Assert.Equal("9876543210", shown.GetProperty("value").GetString());                                // HR with the permission and a fresh check sees it
        var staleHr = await As(fx.Factory, seed, "hr_admin");
        Assert.True((await (await staleHr.Get("/api/hr/change-requests?status=pending")).Body()).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == bankId).GetProperty("valueHidden").GetBoolean());
        Assert.Equal("reauth_required", await (await staleHr.Post($"/api/hr/change-requests/{bankId}/approve")).ErrorCode());
        Assert.Equal(HttpStatusCode.OK, (await hr.Post($"/api/hr/change-requests/{bankId}/approve")).StatusCode);
        var sensitive = (await (await hr.Get($"/api/hr/employees/{w.EmployeeId}")).Body()).GetProperty("sensitive");
        Assert.Equal("9876543210", sensitive.GetProperty("bankAccount").GetString());
        var trail = (await seed.Db(db => db.AuditEvents.Select(a => new { a.MetaJson, a.DiffJson }).ToListAsync())).Select(a => a.MetaJson + a.DiffJson);
        Assert.DoesNotContain(trail, t => t.Contains("9876543210"));                                        // the audit trail never holds the value

        var cancel = await (await w.B.Post("/api/hr/me/change-requests", new { field = "marital_status", value = "married" })).Body();
        Assert.Equal(HttpStatusCode.OK, (await w.B.Post($"/api/hr/me/change-requests/{cancel.GetProperty("id").GetGuid()}/cancel")).StatusCode);
    }

    [Fact]
    public async Task The_personal_data_export_contains_my_record_including_protected_ids_after_a_password_check_and_nobody_elses()
    {
        var seed = new Seed(fx.Factory, fx.Clock); var hr = await As(fx.Factory, seed, "hr_admin", reauth: true);
        var me = await Worker(fx.Factory, seed, fx.Clock, hr, "Export Me", bank: "5550001111"); await Worker(fx.Factory, seed, fx.Clock, hr, "Somebody Else", bank: "7770002222");
        Assert.Equal("reauth_required", await (await me.B.Get("/api/hr/me/export")).ErrorCode());
        await me.B.Post("/api/auth/reauth", new { password = Seed.Password });
        var res = await me.B.Get("/api/hr/me/export"); Assert.Equal(HttpStatusCode.OK, res.StatusCode); Assert.Equal("application/json", res.Content.Headers.ContentType!.MediaType);
        var json = await res.Content.ReadAsStringAsync(); var doc = JsonDocument.Parse(json).RootElement;
        Assert.Equal("Export Me", doc.GetProperty("employee").GetProperty("fullName").GetString()); Assert.Equal("5550001111", doc.GetProperty("employee").GetProperty("bankAccount").GetString());
        Assert.Equal("3174010101900001", doc.GetProperty("employee").GetProperty("nationalId").GetString());
        Assert.DoesNotContain("Somebody Else", json); Assert.DoesNotContain("7770002222", json);
        Assert.True(doc.GetProperty("employments").GetArrayLength() >= 1);
        Assert.True(await seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "hr.self.export")));
    }

    [Fact]
    public async Task Anonymisation_erases_identity_keeps_payroll_figures_needs_a_reason_and_waits_for_retention_unless_forced()
    {
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock);
        var hr = await As(f, seed, "hr_admin", reauth: true); var officer = await As(f, seed, "payroll_officer", reauth: true); var approver = await As(f, seed, "payroll_approver", reauth: true);
        var w = await Worker(f, seed, clock, hr, "Leaver Person");
        Assert.Equal(HttpStatusCode.OK, (await officer.SendAsync(HttpMethod.Put, $"/api/hr/payroll/compensation/{w.EmployeeId}", new { effectiveFrom = D(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddYears(-3))), basic = 10_000_000, ptkpStatus = "TK0" })).StatusCode);
        var year = clock.UtcNow.Year - 1;
        var run = (await (await officer.Post("/api/hr/payroll/runs", new { year, month = 1 })).Body()).GetProperty("id").GetGuid();
        await officer.Post($"/api/hr/payroll/runs/{run}/calculate"); await approver.Post($"/api/hr/payroll/runs/{run}/approve"); await approver.Post($"/api/hr/payroll/runs/{run}/lock");
        var url = $"/api/hr/employees/{w.EmployeeId}/anonymize";

        Assert.Equal(HttpStatusCode.Conflict, (await hr.Post(url, new { reason = "x" })).StatusCode);                                   // still employed
        Assert.Equal(HttpStatusCode.OK, (await hr.Post($"/api/hr/employees/{w.EmployeeId}/terminate", new { reason = "Resigned" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post(url, new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await hr.Post(url, new { reason = "Early request" })).StatusCode);                       // retention not over, no force
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As(f, seed, "hr_head", reauth: true)).Post(url, new { reason = "x", force = true })).StatusCode);
        Assert.Equal("reauth_required", await (await (await As(f, seed, "hr_admin")).Post(url, new { reason = "x", force = true })).ErrorCode());

        Assert.Equal(HttpStatusCode.NoContent, (await hr.Post(url, new { reason = "Granted erasure request, legal sign-off ref 42", force = true })).StatusCode);
        var e = await seed.Db(db => db.Employees.SingleAsync(x => x.Id == w.EmployeeId));
        Assert.Equal("Former employee " + e.EmployeeNo, e.FullName); Assert.NotNull(e.AnonymizedAt); Assert.Null(e.BankAccountEnc); Assert.Null(e.TaxIdEnc); Assert.Null(e.NationalIdEnc); Assert.Null(e.UserId); Assert.Null(e.Phone);
        var item = await seed.Db(db => db.PayRunItems.SingleAsync()); Assert.Equal("Former employee " + e.EmployeeNo, item.EmployeeName); Assert.Equal(9_338_650m, item.Net);   // the money stays, the name goes
        Assert.Equal(HttpStatusCode.Conflict, (await hr.Post(url, new { reason = "again", force = true })).StatusCode);
        Assert.True(await seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "hr.employee.anonymize" && a.TargetId == w.EmployeeId.ToString())));

        // the trigger exception is narrow: no figure of a locked run can be edited, and names cannot be edited outside the anonymising transaction
        await Assert.ThrowsAnyAsync<Exception>(() => seed.Db(db => db.Database.ExecuteSqlRawAsync("UPDATE hr_pay_run_items SET net = net + 1")));
        await Assert.ThrowsAnyAsync<Exception>(() => seed.Db(db => db.Database.ExecuteSqlRawAsync("UPDATE hr_pay_run_items SET employee_name = 'Someone'")));
    }

    [Fact]
    public async Task The_retention_job_erases_people_who_left_long_enough_ago_and_nobody_else()
    {
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock); var hr = await As(f, seed, "hr_admin", reauth: true);
        var old = await Worker(f, seed, clock, hr, "Long Gone"); var recent = await Worker(f, seed, clock, hr, "Just Left"); var active = await Worker(f, seed, clock, hr, "Still Here");
        await seed.Db(async db =>
        {
            var o = await db.Employees.SingleAsync(x => x.Id == old.EmployeeId); o.HireDate = new DateOnly(2005, 1, 1); o.Status = EmployeeStatus.Terminated; o.TerminationDate = new DateOnly(2012, 5, 31);
            var r = await db.Employees.SingleAsync(x => x.Id == recent.EmployeeId); r.Status = EmployeeStatus.Terminated; r.TerminationDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddYears(-2));
            return await db.SaveChangesAsync();
        });
        using var scope = f.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Hr.AnonymizationService>().RunAllTenantsAsync(10));
        var names = await seed.Db(db => db.Employees.ToDictionaryAsync(x => x.Id, x => x.FullName));
        Assert.StartsWith("Former employee", names[old.EmployeeId]); Assert.Equal("Just Left", names[recent.EmployeeId]); Assert.Equal("Still Here", names[active.EmployeeId]);
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Hr.AnonymizationService>().RunAllTenantsAsync(10)); // nothing left to do
    }
}
