using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Each test gets its own database (a pay run covers every employee, so tests must not share one) and uses last year's months so results never depend on today's date.</summary>
[Collection("db")]
public sealed class HrPayrollTests(PostgresFixture fx)
{
    private sealed record Env(ApiFactory F, TestClock Clock, Seed Seed, TestBrowser Hr, TestBrowser Officer, TestBrowser Approver, int Year)
    {
        public DateOnly Hire => DateOnly.FromDateTime(Clock.UtcNow.UtcDateTime.AddYears(-3));
    }

    private async Task<Env> NewEnv()
    {
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock);
        async Task<TestBrowser> As(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); await b.Post("/api/auth/reauth", new { password = Seed.Password }); return b; }
        return new Env(f, clock, seed, await As("hr_admin"), await As("payroll_officer"), await As("payroll_approver"), clock.UtcNow.Year - 1);
    }

    private sealed record Person(Guid EmployeeId, TestBrowser B, string Email);

    private async Task<Person> Employee(Env e, string name, decimal basic = 10_000_000m, string ptkp = "TK0", bool bank = true, string account = "1234567890", object? opening = null)
    {
        var u = await e.Seed.UserAsync(role: "user");
        var r = await e.Hr.Post("/api/hr/employees", new { fullName = name, hireDate = e.Hire.ToString("yyyy-MM-dd"), jobTitle = "Analyst", userId = u.Id, bankName = bank ? "BCA" : null, bankAccount = bank ? account : null });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var id = (await r.Body()).GetProperty("id").GetGuid();
        var body = new Dictionary<string, object?> { ["effectiveFrom"] = e.Hire.ToString("yyyy-MM-dd"), ["basic"] = basic, ["allowance"] = 0, ["ptkpStatus"] = ptkp };
        if (opening is not null) foreach (var p in opening.GetType().GetProperties()) body[char.ToLowerInvariant(p.Name[0]) + p.Name[1..]] = p.GetValue(opening);
        var c = await e.Officer.SendAsync(HttpMethod.Put, $"/api/hr/payroll/compensation/{id}", body);
        Assert.Equal(HttpStatusCode.OK, c.StatusCode);
        var b = TestBrowser.Create(e.F); await b.LoginAsync(u.Email, Seed.Password);
        return new Person(id, b, u.Email);
    }

    private static async Task<Guid> NewRun(Env e, int month)
    {
        var r = await e.Officer.Post("/api/hr/payroll/runs", new { year = e.Year, month });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode); return (await r.Body()).GetProperty("id").GetGuid();
    }

    private static async Task<System.Text.Json.JsonElement> Calc(Env e, Guid run)
    {
        var r = await e.Officer.Post($"/api/hr/payroll/runs/{run}/calculate"); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return await r.Body();
    }

    private static async Task Lock(Env e, Guid run)
    {
        Assert.Equal(HttpStatusCode.OK, (await e.Approver.Post($"/api/hr/payroll/runs/{run}/approve")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await e.Approver.Post($"/api/hr/payroll/runs/{run}/lock")).StatusCode);
    }

    [Fact]
    public async Task A_monthly_run_matches_the_hand_computed_figures_and_the_salary_is_encrypted_at_rest()
    {
        var e = await NewEnv(); var w = await Employee(e, "Budi Santoso");
        var d = await Calc(e, await NewRun(e, 1));
        var item = d.GetProperty("items")[0];
        Assert.Equal(10_454_000m, item.GetProperty("gross").GetDecimal() + 400_000m + 24_000m + 30_000m); // gross + employer health, JKK and JKM = the taxable base
        Assert.Equal(261_350m, item.GetProperty("tax").GetDecimal()); Assert.Equal(9_338_650m, item.GetProperty("net").GetDecimal()); Assert.Equal("TER", item.GetProperty("taxMethod").GetString());
        Assert.Equal(1, d.GetProperty("run").GetProperty("employeeCount").GetInt32()); Assert.Equal(9_338_650m, d.GetProperty("run").GetProperty("totalNet").GetDecimal());
        var stored = await e.Seed.Db(db => db.Compensations.Select(c => c.BasicEnc).SingleAsync());
        Assert.DoesNotContain("10000000", stored);
    }

    [Fact]
    public async Task The_final_month_of_the_year_settles_tax_using_the_opening_year_to_date_figures()
    {
        var e = await NewEnv();
        await Employee(e, "Dewi Lestari", opening: new { OpeningYear = e.Year, OpeningMonths = 11, OpeningGross = 11 * 10_454_000m, OpeningPension = 11 * 300_000m, OpeningTax = 11 * 261_350m });
        var item = (await Calc(e, await NewRun(e, 12))).GetProperty("items")[0];
        Assert.Equal("TRUE-UP", item.GetProperty("taxMethod").GetString()); Assert.Equal(402_350m, item.GetProperty("tax").GetDecimal()); Assert.Equal(9_197_650m, item.GetProperty("net").GetDecimal());
    }

    [Fact]
    public async Task The_person_who_calculated_cannot_approve_or_lock_and_each_step_needs_its_own_permission()
    {
        var e = await NewEnv(); await Employee(e, "Siti Rahma"); var run = await NewRun(e, 1); await Calc(e, run);

        Assert.Equal(HttpStatusCode.Forbidden, (await e.Officer.Post($"/api/hr/payroll/runs/{run}/approve")).StatusCode); // the preparer role has no approve permission
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Approver.Post($"/api/hr/payroll/runs/{run}/calculate")).StatusCode);
        // someone who holds every payroll permission still may not check their own work
        await e.Seed.RoleAsync("pay_all", "payroll.read", "payroll.run", "payroll.approve", "payroll.lock", "payroll.configure");
        var both = await e.Seed.UserAsync(); var roleId = await e.Seed.RoleId("pay_all");
        await e.Seed.Db(async db => { db.UserRoles.Add(new UserRole { UserId = both.Id, RoleId = roleId, AssignedAt = e.Clock.UtcNow }); return await db.SaveChangesAsync(); });
        var b = TestBrowser.Create(e.F); await b.LoginAsync(both.Email, Seed.Password); await b.Post("/api/auth/reauth", new { password = Seed.Password });
        Assert.Equal(HttpStatusCode.OK, (await b.Post($"/api/hr/payroll/runs/{run}/calculate")).StatusCode);
        var self = await b.Post($"/api/hr/payroll/runs/{run}/approve");
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode); Assert.Contains("cannot approve", (await self.Body()).GetProperty("error").GetProperty("message").GetString());
        Assert.True(await e.Seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "hr.payroll.denied" && a.ActorId == both.Id)));

        Assert.Equal(HttpStatusCode.Conflict, (await e.Approver.Post($"/api/hr/payroll/runs/{run}/lock")).StatusCode); // approval comes first
        await Lock(e, run);
        Assert.Equal(HttpStatusCode.OK, (await e.Approver.Post($"/api/hr/payroll/runs/{run}/paid")).StatusCode);
    }

    [Fact]
    public async Task A_locked_run_cannot_be_changed_through_the_api_or_the_database()
    {
        var e = await NewEnv(); var w = await Employee(e, "Rizky Pratama"); var run = await NewRun(e, 1); await Calc(e, run); await Lock(e, run);

        Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.Post($"/api/hr/payroll/runs/{run}/calculate")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.Post($"/api/hr/payroll/runs/{run}/lines", new { employeeId = w.EmployeeId, kind = "earning", label = "Bonus", amount = 1000 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.SendAsync(HttpMethod.Delete, $"/api/hr/payroll/runs/{run}")).StatusCode);

        // even raw SQL from the application's own login is refused by the database triggers
        var item = await Assert.ThrowsAnyAsync<Exception>(() => e.Seed.Db(db => db.Database.ExecuteSqlRawAsync("UPDATE hr_pay_run_items SET net = net + 1")));
        Assert.Contains("locked", item.ToString());
        await Assert.ThrowsAnyAsync<Exception>(() => e.Seed.Db(db => db.Database.ExecuteSqlRawAsync("DELETE FROM hr_pay_run_items")));
        await Assert.ThrowsAnyAsync<Exception>(() => e.Seed.Db(db => db.Database.ExecuteSqlRawAsync("UPDATE hr_pay_runs SET total_net = 1")));
        await Assert.ThrowsAnyAsync<Exception>(() => e.Seed.Db(db => db.Database.ExecuteSqlRawAsync("UPDATE hr_pay_runs SET status = 'Draft'")));
        await Assert.ThrowsAnyAsync<Exception>(() => e.Seed.Db(db => db.Database.ExecuteSqlRawAsync("DELETE FROM hr_pay_runs")));
        Assert.Equal(9_338_650m, await e.Seed.Db(db => db.PayRunItems.Select(i => i.Net).SingleAsync()));
        // the one allowed move after locking: marking it paid
        Assert.Equal(HttpStatusCode.OK, (await e.Approver.Post($"/api/hr/payroll/runs/{run}/paid")).StatusCode);
    }

    [Fact]
    public async Task Payroll_data_needs_the_permission_and_a_recent_password_check_and_payslips_stay_private()
    {
        var e = await NewEnv(); var a = await Employee(e, "Alpha Worker"); var b = await Employee(e, "Beta Worker"); var run = await NewRun(e, 1); await Calc(e, run);

        Assert.Equal(HttpStatusCode.Forbidden, (await e.Hr.Get("/api/hr/payroll/runs")).StatusCode);            // HR admin has no payroll permission
        Assert.Equal(HttpStatusCode.Forbidden, (await a.B.Get("/api/hr/payroll/runs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.B.Get($"/api/hr/payroll/compensation/{b.EmployeeId}")).StatusCode);
        var u = await e.Seed.UserAsync(role: "payroll_officer"); var stale = TestBrowser.Create(e.F); await stale.LoginAsync(u.Email, Seed.Password); // no recent password check
        Assert.Equal("reauth_required", await (await stale.Get("/api/hr/payroll/runs")).ErrorCode());

        Assert.Equal(HttpStatusCode.NotFound, (await a.B.Get($"/api/hr/payroll/payslips/{Guid.NewGuid()}")).StatusCode);
        var mineBefore = await (await a.B.Get("/api/hr/payroll/payslips")).Body(); Assert.Equal(0, mineBefore.GetArrayLength()); // not visible before the run is locked
        await Lock(e, run);
        var mine = await (await a.B.Get("/api/hr/payroll/payslips")).Body(); Assert.Equal(1, mine.GetArrayLength());
        var slipId = mine[0].GetProperty("itemId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await a.B.Get($"/api/hr/payroll/payslips/{slipId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.B.Get($"/api/hr/payroll/payslips/{slipId}")).StatusCode); // someone else's slip looks missing
        Assert.True(await e.Seed.Db(db => db.AuditEvents.AnyAsync(x => x.Action == "hr.payroll.payslip_viewed")));
    }

    [Fact]
    public async Task Extra_lines_change_the_result_and_send_a_calculated_run_back_to_draft_and_warnings_flag_problems()
    {
        var e = await NewEnv(); var w = await Employee(e, "Line Worker", bank: false); var run = await NewRun(e, 1);
        var first = await Calc(e, run);
        Assert.Contains("No bank account", first.GetProperty("items")[0].GetProperty("warnings").GetString());
        var gross0 = first.GetProperty("items")[0].GetProperty("gross").GetDecimal();

        var add = await e.Officer.Post($"/api/hr/payroll/runs/{run}/lines", new { employeeId = w.EmployeeId, kind = "earning", label = "Performance bonus", amount = 5_000_000, taxable = true });
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        Assert.Equal("draft", (await (await e.Officer.Get($"/api/hr/payroll/runs/{run}")).Body()).GetProperty("run").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await e.Approver.Post($"/api/hr/payroll/runs/{run}/approve")).StatusCode); // stale numbers can't be approved
        var second = (await Calc(e, run)).GetProperty("items")[0];
        Assert.Equal(gross0 + 5_000_000m, second.GetProperty("gross").GetDecimal()); Assert.True(second.GetProperty("tax").GetDecimal() > 261_350m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.Post($"/api/hr/payroll/runs/{run}/lines", new { employeeId = w.EmployeeId, kind = "earning", label = "", amount = -1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.Post("/api/hr/payroll/runs", new { year = e.Year, month = 1 })).StatusCode); // one run per month
    }

    [Fact]
    public async Task Employees_without_compensation_are_listed_as_missing_even_before_the_first_calculation()
    {
        var e = await NewEnv(); await Employee(e, "Has Pay");
        var bare = await e.Hr.Post("/api/hr/employees", new { fullName = "No Pay Yet", hireDate = e.Hire.ToString("yyyy-MM-dd"), jobTitle = "Analyst" });
        Assert.Equal(HttpStatusCode.Created, bare.StatusCode);
        var run = await NewRun(e, 1);
        var before = (await (await e.Officer.Get($"/api/hr/payroll/runs/{run}")).Body()).GetProperty("missing");
        Assert.Single(before.EnumerateArray()); Assert.Contains("No Pay Yet", before[0].GetString());
        var after = await Calc(e, run);
        Assert.Single(after.GetProperty("missing").EnumerateArray()); Assert.Equal(1, after.GetProperty("run").GetProperty("employeeCount").GetInt32());
    }

    [Fact]
    public async Task A_month_cannot_be_calculated_while_an_earlier_month_of_the_year_is_open()
    {
        var e = await NewEnv(); await Employee(e, "Order Worker"); var jan = await NewRun(e, 1); var feb = await NewRun(e, 2);
        var early = await e.Officer.Post($"/api/hr/payroll/runs/{feb}/calculate");
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        await Calc(e, jan); await Lock(e, jan);
        Assert.Equal(HttpStatusCode.OK, (await e.Officer.Post($"/api/hr/payroll/runs/{feb}/calculate")).StatusCode);
        // changing pay for a month that is already locked is refused
        var emp = await e.Seed.Db(db => db.Employees.Select(x => x.Id).SingleAsync());
        var change = await e.Officer.SendAsync(HttpMethod.Put, $"/api/hr/payroll/compensation/{emp}", new { effectiveFrom = $"{e.Year}-01-15", basic = 12_000_000, ptkpStatus = "TK0" });
        Assert.Equal(HttpStatusCode.Conflict, change.StatusCode); Assert.Contains("locked pay run", (await change.Body()).GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task The_bank_file_exists_only_after_locking_for_the_lock_permission_and_skips_people_without_an_account()
    {
        var e = await NewEnv(); await Employee(e, "Paid Worker", account: "9988776655"); await Employee(e, "No Account", bank: false);
        var run = await NewRun(e, 1); await Calc(e, run);
        Assert.Equal(HttpStatusCode.Conflict, (await e.Approver.Get($"/api/hr/payroll/runs/{run}/bank-file")).StatusCode);
        await Lock(e, run);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Officer.Get($"/api/hr/payroll/runs/{run}/bank-file")).StatusCode);
        var csv = await (await e.Approver.Get($"/api/hr/payroll/runs/{run}/bank-file")).Content.ReadAsStringAsync();
        Assert.Contains("9988776655", csv); Assert.Contains("9338650", csv); Assert.DoesNotContain("No Account", csv);
        Assert.True(await e.Seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "hr.payroll.bank_file")));
        var register = await (await e.Officer.Get($"/api/hr/payroll/runs/{run}/register")).Content.ReadAsStringAsync();
        Assert.Contains("261350", register);
    }
}
