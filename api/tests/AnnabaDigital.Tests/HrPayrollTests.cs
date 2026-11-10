using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    private async Task<Person> Employee(Env e, string name, decimal basic = 10_000_000m, string ptkp = "TK0", bool bank = true, string account = "1234567890", object? opening = null, DateOnly? hired = null, string? taxId = null, Guid? entity = null, string? payBasis = null, decimal allowance = 0)
    {
        var u = await e.Seed.UserAsync(role: "user");
        var r = await e.Hr.Post("/api/hr/employees", new { fullName = name, hireDate = (hired ?? e.Hire).ToString("yyyy-MM-dd"), jobTitle = "Analyst", userId = u.Id, bankName = bank ? "BCA" : null, bankAccount = bank ? account : null, taxId, legalEntityId = entity });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var id = (await r.Body()).GetProperty("id").GetGuid();
        var body = new Dictionary<string, object?> { ["effectiveFrom"] = (hired ?? e.Hire).ToString("yyyy-MM-dd"), ["basic"] = basic, ["allowance"] = allowance, ["ptkpStatus"] = ptkp, ["payBasis"] = payBasis };
        if (opening is not null) foreach (var p in opening.GetType().GetProperties()) body[char.ToLowerInvariant(p.Name[0]) + p.Name[1..]] = p.GetValue(opening);
        var c = await e.Officer.SendAsync(HttpMethod.Put, $"/api/hr/payroll/compensation/{id}", body);
        Assert.Equal(HttpStatusCode.OK, c.StatusCode);
        var b = TestBrowser.Create(e.F); await b.LoginAsync(u.Email, Seed.Password);
        return new Person(id, b, u.Email);
    }

    private static async Task<Guid> NewRun(Env e, int month, Guid? entity = null)
    {
        var r = await e.Officer.Post("/api/hr/payroll/runs", new { year = e.Year, month, legalEntityId = entity });
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

    [Fact]
    public async Task Approved_overtime_is_paid_in_the_run_and_a_month_with_locked_payroll_cannot_be_reopened()
    {
        var e = await NewEnv(); var w = await Employee(e, "Overtime Worker");
        var monday = new DateOnly(e.Year, 1, 1); while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(1); var sunday = monday.AddDays(6);
        await e.Seed.Db(async db =>
        {
            var now = e.Clock.UtcNow;
            db.OvertimeRequests.AddRange(
                new OvertimeRequest { EmployeeId = w.EmployeeId, Date = monday, Hours = 2m, Reason = "Release", Status = RequestStatus.Approved, DecidedAt = now, CreatedAt = now },
                new OvertimeRequest { EmployeeId = w.EmployeeId, Date = sunday, Hours = 4m, Reason = "Migration", Status = RequestStatus.Approved, DecidedAt = now, CreatedAt = now },
                new OvertimeRequest { EmployeeId = w.EmployeeId, Date = monday.AddDays(1), Hours = 3m, Reason = "Not approved", Status = RequestStatus.Pending, CreatedAt = now });
            return await db.SaveChangesAsync();
        });
        var run = await NewRun(e, 1); var d = await Calc(e, run); var item = d.GetProperty("items")[0];
        Assert.Equal(664_740m, item.GetProperty("overtimePay").GetDecimal());   // 2 h working day + 4 h Sunday on a 10,000,000 wage (pending requests are not paid)
        Assert.Equal(10_664_740m, item.GetProperty("gross").GetDecimal());
        Assert.False(d.GetProperty("attendanceClosed").GetBoolean());
        Assert.Contains("overtime", item.GetProperty("warnings").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await e.Hr.Post($"/api/hr/attendance/periods/{e.Year}-01/close")).StatusCode); // the pending overtime request blocks closing
        await e.Seed.Db(async db => { var o = await db.OvertimeRequests.SingleAsync(x => x.Status == RequestStatus.Pending); o.Status = RequestStatus.Rejected; return await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.OK, (await e.Hr.Post($"/api/hr/attendance/periods/{e.Year}-01/close")).StatusCode);
        Assert.True((await (await e.Officer.Get($"/api/hr/payroll/runs/{run}")).Body()).GetProperty("attendanceClosed").GetBoolean());
        await Lock(e, run);
        var reopen = await e.Hr.Post($"/api/hr/attendance/periods/{e.Year}-01/reopen", new { reason = "Trying to change history" });
        Assert.Equal(HttpStatusCode.Conflict, reopen.StatusCode); Assert.Contains("locked", (await reopen.Body()).GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task THR_is_a_full_month_after_a_year_of_service_pro_rata_before_that_and_adding_it_twice_does_nothing()
    {
        var e = await NewEnv(); var veteran = await Employee(e, "Veteran"); var newer = await Employee(e, "Newer", basic: 12_000_000m, hired: new DateOnly(e.Year, 1, 15));
        var run = await NewRun(e, 6);
        var add = await e.Officer.Post($"/api/hr/payroll/runs/{run}/thr"); Assert.Equal(HttpStatusCode.OK, add.StatusCode); Assert.Equal(2, (await add.Body()).GetProperty("added").GetInt32());
        Assert.Equal(0, (await (await e.Officer.Post($"/api/hr/payroll/runs/{run}/thr")).Body()).GetProperty("added").GetInt32());
        var lines = (await (await e.Officer.Get($"/api/hr/payroll/runs/{run}")).Body()).GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("employeeName").GetString()!, l => l.GetProperty("amount").GetDecimal());
        Assert.Equal(10_000_000m, lines["Veteran"]);        // 1 × (10,000,000 + 0)
        Assert.Equal(5_000_000m, lines["Newer"]);           // hired 15 Jan, 5 full months by 30 Jun: 12,000,000 × 5 / 12
        var item = (await Calc(e, run)).GetProperty("items").EnumerateArray().First(i => i.GetProperty("employeeName").GetString() == "Veteran");
        Assert.Equal(20_000_000m, item.GetProperty("gross").GetDecimal());
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Approver.Post($"/api/hr/payroll/runs/{run}/thr")).StatusCode);
    }

    // ------------------------------------------------------------ extras: loans, absence, severance, reports, PDF
    [Theory]
    [InlineData(0, 0)]
    [InlineData(50_000_000, 0)]
    [InlineData(100_000_000, 2_500_000)]
    [InlineData(152_500_000, 10_375_000)]       // 2.5 m + 52.5 m × 15 %
    [InlineData(600_000_000, 87_500_000)]       // 2.5 m + 400 m × 15 % + 100 m × 25 %
    public void Severance_tax_follows_the_PP_68_2009_brackets(long amount, long expected)
        => Assert.Equal((decimal)expected, AnnabaDigital.Application.Hr.PayrollExtrasService.SeveranceTax(amount));

    [Fact]
    public async Task A_loan_is_deducted_every_month_as_a_system_line_reduces_when_the_run_is_locked_and_ends_when_repaid()
    {
        var e = await NewEnv(); var w = await Employee(e, "Borrower");
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Approver.Post("/api/hr/payroll/loans", new { employeeId = w.EmployeeId, principal = 3_000_000, installment = 1_000_000 })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.Post("/api/hr/payroll/loans", new { employeeId = w.EmployeeId, principal = 1_000_000, installment = 2_000_000 })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await e.Officer.Post("/api/hr/payroll/loans", new { employeeId = w.EmployeeId, principal = 3_000_000, installment = 1_000_000, note = "Laptop advance" })).StatusCode);

        for (var month = 1; month <= 3; month++)
        {
            var run = await NewRun(e, month); var d = await Calc(e, run); var item = d.GetProperty("items")[0];
            var line = d.GetProperty("lines").EnumerateArray().Single(); Assert.Equal(1_000_000m, line.GetProperty("amount").GetDecimal()); Assert.Contains("Laptop advance", line.GetProperty("label").GetString());
            Assert.Equal(9_338_650m - 1_000_000m, item.GetProperty("net").GetDecimal());
            if (month == 1) Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.SendAsync(HttpMethod.Delete, $"/api/hr/payroll/runs/{run}/lines/{line.GetProperty("id").GetGuid()}")).StatusCode); // generated, not editable
            Assert.Equal(3_000_000m - (month - 1) * 1_000_000m, await e.Seed.Db(db => db.Loans.Select(l => l.Remaining).SingleAsync())); // the balance only moves when a run is locked
            await Lock(e, run);
        }
        var loan = await e.Seed.Db(db => db.Loans.SingleAsync()); Assert.Equal(0m, loan.Remaining); Assert.Equal(LoanStatus.PaidOff, loan.Status);
        var april = await Calc(e, await NewRun(e, 4)); Assert.Equal(0, april.GetProperty("lines").GetArrayLength()); // repaid: nothing more is taken
    }

    [Fact]
    public async Task Unpaid_absence_is_deducted_only_when_asked_for_after_the_month_is_closed_and_leave_and_attendance_days_are_not_counted()
    {
        var e = await NewEnv(); var w = await Employee(e, "Sometimes Absent");
        _ = await (await w.B.Get("/api/hr/leave/types")).Body();                                                                  // creates the default leave types on first use
        var first = new DateOnly(e.Year, 1, 1); var days = Enumerable.Range(0, 31).Select(i => first.AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();
        await e.Seed.Db(async db =>
        {
            foreach (var d in days.Take(2)) db.AttendanceLogs.Add(new AttendanceLog { EmployeeId = w.EmployeeId, Date = d, ClockIn = new DateTimeOffset(d.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(7)).ToUniversalTime(), ClockOut = new DateTimeOffset(d.ToDateTime(new TimeOnly(17, 0)), TimeSpan.FromHours(7)).ToUniversalTime(), Source = "clock" });
            var sick = await db.LeaveTypes.SingleAsync(t => t.Code == "SICK");
            db.LeaveRequests.Add(new LeaveRequest { EmployeeId = w.EmployeeId, LeaveTypeId = sick.Id, StartDate = days[2], EndDate = days[3], Days = 2, Status = LeaveStatus.Approved, CreatedAt = e.Clock.UtcNow });
            return await db.SaveChangesAsync();
        });

        var plain = await NewRun(e, 1);                                                                                           // off by default: nothing is deducted
        Assert.Equal(0m, (await Calc(e, plain)).GetProperty("items")[0].GetProperty("unpaidDeduction").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.SendAsync(HttpMethod.Delete, $"/api/hr/payroll/runs/{plain}")).StatusCode);   // a calculated run cannot be deleted through the API
        await e.Seed.Db(db => db.Database.ExecuteSqlRawAsync("DELETE FROM hr_pay_runs"));                                                    // (test housekeeping: the run is not locked, so the database allows it)

        var made = await e.Officer.Post("/api/hr/payroll/runs", new { year = e.Year, month = 1, deductAbsence = true });
        Assert.Equal(HttpStatusCode.Created, made.StatusCode); var run = (await made.Body()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.Post($"/api/hr/payroll/runs/{run}/calculate")).StatusCode);          // attendance is not closed yet
        Assert.Equal(HttpStatusCode.OK, (await e.Hr.Post($"/api/hr/attendance/periods/{e.Year}-01/close")).StatusCode);
        var item = (await Calc(e, run)).GetProperty("items")[0];
        var absent = days.Count - 4;                                                                                               // 2 days present and 2 on approved leave do not count
        Assert.Equal(Math.Round(10_000_000m * absent / days.Count, 0, MidpointRounding.AwayFromZero), item.GetProperty("unpaidDeduction").GetDecimal());
        Assert.Contains($"Absent {absent} day", item.GetProperty("warnings").GetString());
    }

    [Fact]
    public async Task The_severance_calculator_applies_the_PP_35_2021_tables_and_adds_the_result_to_a_run_once()
    {
        var e = await NewEnv(); var w = await Employee(e, "Long Servant", hired: new DateOnly(2018, 3, 1)); var run = await NewRun(e, 1);
        async Task<System.Text.Json.JsonElement> Preview(string reason, string last) => await (await e.Officer.Post("/api/hr/payroll/severance/preview", new { employeeId = w.EmployeeId, reason, lastDay = last })).Body();

        var other = await Preview("layoff_other", "2024-09-15");                 // 6 full years: pesangon 7 months, UPMK 3 months, wage 10,000,000
        Assert.Equal(6, other.GetProperty("years").GetInt32());
        var lines = other.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("label").GetString()!, l => l.GetProperty("amount").GetDecimal());
        Assert.Equal(70_000_000m, lines["Severance: Pesangon"]); Assert.Equal(30_000_000m, lines["Severance: UPMK (long-service pay)"]); Assert.Equal(2_500_000m, lines["Severance: Final tax on pesangon/UPMK (PP 68/2009)"]);
        Assert.Equal(97_500_000m, other.GetProperty("netTotal").GetDecimal());
        Assert.Equal(122_500_000m, (await Preview("retirement", "2024-09-15")).GetProperty("lines").EnumerateArray().First(l => l.GetProperty("label").GetString() == "Severance: Pesangon").GetProperty("amount").GetDecimal()); // × 1.75
        Assert.Equal(0, (await Preview("resignation", "2024-09-15")).GetProperty("lines").GetArrayLength());                 // no pesangon, no unused leave on file
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.Post("/api/hr/payroll/severance/preview", new { employeeId = w.EmployeeId, reason = "nonsense" })).StatusCode);

        var fixedTerm = await Employee(e, "Contract Leaver", hired: new DateOnly(2024, 1, 1));
        var comp = (await (await e.Officer.Post("/api/hr/payroll/severance/preview", new { employeeId = fixedTerm.EmployeeId, reason = "end_of_contract", lastDay = "2024-10-31" })).Body()).GetProperty("lines")[0];
        Assert.Equal(7_500_000m, comp.GetProperty("amount").GetDecimal()); Assert.True(comp.GetProperty("taxable").GetBoolean());  // 9 months / 12 × 10,000,000

        Assert.Equal(HttpStatusCode.Forbidden, (await e.Approver.Post($"/api/hr/payroll/runs/{run}/severance", new { employeeId = w.EmployeeId, reason = "layoff_other", lastDay = "2024-09-15" })).StatusCode);
        var add = await e.Officer.Post($"/api/hr/payroll/runs/{run}/severance", new { employeeId = w.EmployeeId, reason = "layoff_other", lastDay = "2024-09-15" });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode); Assert.Equal(3, (await add.Body()).GetProperty("added").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.Post($"/api/hr/payroll/runs/{run}/severance", new { employeeId = w.EmployeeId, reason = "layoff_other", lastDay = "2024-09-15" })).StatusCode);
    }

    [Fact]
    public async Task Reports_and_the_payslip_pdf_come_out_right_balanced_complete_and_private()
    {
        var e = await NewEnv(); var a = await Employee(e, "Alpha Report", taxId: "09.254.294.3-407.000"); var b = await Employee(e, "Beta Report", basic: 15_000_000m); var run = await NewRun(e, 1); await Calc(e, run);
        Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.Get($"/api/hr/payroll/runs/{run}/journal")).StatusCode);   // not before locking
        Assert.Equal(HttpStatusCode.Forbidden, (await a.B.Get($"/api/hr/payroll/runs/{run}/bpjs")).StatusCode);
        var ver = (await (await e.Hr.Get($"/api/hr/employees/{a.EmployeeId}")).Body()).GetProperty("version").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await e.Hr.SendAsync(HttpMethod.Patch, $"/api/hr/employees/{a.EmployeeId}", new { version = ver, bpjsKesNo = "0001234567890", bpjsTkNo = "12345678901", religion = "islam" })).StatusCode);
        var bpjs = await (await e.Officer.Get($"/api/hr/payroll/runs/{run}/bpjs")).Content.ReadAsStringAsync();
        Assert.Contains("Alpha Report,0001234567890,12345678901,", bpjs);                                                    // membership numbers go on the report…
        Assert.DoesNotContain("0001234567890", await e.Seed.Db(db => db.Employees.Where(x => x.Id == a.EmployeeId).Select(x => x.BpjsKesNoEnc!).SingleAsync()));   // …and are encrypted at rest
        var seenByOfficer = (await (await e.Officer.Get($"/api/hr/employees/{a.EmployeeId}")).Body()).GetProperty("sensitive");   // payroll staff without the sensitive permission see only that a number exists
        Assert.True(seenByOfficer.GetProperty("hasBpjsKesNo").GetBoolean()); Assert.Equal(System.Text.Json.JsonValueKind.Null, seenByOfficer.GetProperty("bpjsKesNo").ValueKind);
        var seenByHr = await (await e.Hr.Get($"/api/hr/employees/{a.EmployeeId}")).Body(); Assert.Equal("0001234567890", seenByHr.GetProperty("sensitive").GetProperty("bpjsKesNo").GetString()); Assert.Equal("islam", seenByHr.GetProperty("religion").GetString());
        await Lock(e, run);

        var rows = (await (await e.Officer.Get($"/api/hr/payroll/runs/{run}/journal")).Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.Split(',')).ToList();
        decimal Sum(int col) => rows.Sum(r => decimal.Parse(r[col], System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(Sum(2) > 0); Assert.Equal(Sum(2), Sum(3));                                                               // debits equal credits

        var annual = await (await e.Officer.Get($"/api/hr/payroll/reports/annual-tax?year={e.Year}")).Content.ReadAsStringAsync();
        Assert.Contains("09.254.294.3-407.000", annual); Assert.Contains("Alpha Report", annual);
        Assert.StartsWith("company,company_npwp,employee_no,", annual.TrimStart('\uFEFF'));                                  // one certificate line per employee and paying company
        var none = await (await e.Officer.Get($"/api/hr/payroll/reports/annual-tax?year={e.Year}&legalEntityId={Guid.NewGuid()}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Alpha Report", none);                                                                         // another company's file does not include them
        Assert.True(await e.Seed.Db(db => db.AuditEvents.AnyAsync(x => x.Action == "hr.payroll.annual_tax")));

        // PAY-010: the figures of the yearly withholding slip (1721-A1), in the order of the form
        var a1 = (await (await e.Officer.Get($"/api/hr/payroll/reports/a1?year={e.Year}")).Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(",1_salary,3_allowances_overtime,5_employer_premiums,7_bonus_thr_other,8_gross,9_position_cost,10_pension_contributions,12_net,15_ptkp,16_taxable_income,17_tax_due_for_the_year,20_tax_withheld,", a1[0]);
        var alpha = a1.Single(l => l.Contains("Alpha Report"));
        // one month at 10,000,000: gross 10,454,000 with the employer's premiums · position cost capped at 500,000 · pension 300,000 · net 9,654,000 · PTKP 54,000,000 · nothing due · 261,350 withheld so far · not final before December
        Assert.EndsWith(",TK0,1,1,1,10000000,0,454000,0,10454000,500000,300000,9654000,54000000,0,0,261350,261350,no", alpha); Assert.Contains("09.254.294.3-407.000", alpha);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.B.Get($"/api/hr/payroll/reports/a1?year={e.Year}")).StatusCode);

        // RPT-002/003: labour cost per department, and any CSV export as an Excel workbook
        var cost = (await (await e.Officer.Get($"/api/hr/payroll/reports/labour-cost?year={e.Year}")).Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("department,employees,gross_pay", cost[0].TrimStart('\uFEFF')); Assert.StartsWith("(no department),2,25000000,", cost[1]); Assert.StartsWith("TOTAL,2,25000000,", cost[^1]);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.B.Get($"/api/hr/payroll/reports/labour-cost?year={e.Year}")).StatusCode);
        var xlsx = await e.Officer.Get($"/api/hr/payroll/runs/{run}/bpjs?format=xlsx");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType!.MediaType); Assert.EndsWith(".xlsx", xlsx.Content.Headers.ContentDisposition!.FileNameStar ?? xlsx.Content.Headers.ContentDisposition.FileName!.Trim('"'));
        using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(await xlsx.Content.ReadAsByteArrayAsync())))
        {
            Assert.NotNull(zip.GetEntry("xl/workbook.xml")); Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
            var sheet = System.Xml.Linq.XDocument.Load(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open()); System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var cells = sheet.Descendants(ns + "c").ToList();
            Assert.Contains(cells, c => (string?)c.Attribute("t") == "inlineStr" && c.Value == "0001234567890");                 // the membership number keeps its leading zeros as text
            Assert.Contains(cells, c => c.Attribute("t") is null && c.Element(ns + "v")?.Value == "100000");                     // amounts are real numbers
        }

        var slips = await (await a.B.Get("/api/hr/payroll/payslips")).Body(); var id = slips[0].GetProperty("itemId").GetGuid();
        var pdf = await a.B.Get($"/api/hr/payroll/payslips/{id}/pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode); Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        var bytes = await pdf.Content.ReadAsByteArrayAsync(); var text = System.Text.Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", text); Assert.EndsWith("%%EOF\n", text); Assert.Contains("Alpha Report", text); Assert.Contains("Take-home pay", text); Assert.Contains("9,338,650", text);
        var startxref = int.Parse(text[(text.LastIndexOf("startxref\n") + 10)..].Split('\n')[0]);
        Assert.StartsWith("xref", text[startxref..]);                                                                        // the cross-reference table is where the file says it is
        foreach (var n in new[] { 1, 2, 3, 4, 5, 6, 7 }) Assert.Contains($"\n{n} 0 obj\n", text);
        Assert.Equal(HttpStatusCode.NotFound, (await b.B.Get($"/api/hr/payroll/payslips/{id}/pdf")).StatusCode);              // someone else's slip

        // the slip carries a verification code: an outsider learns the company, the period and a masked name, and whether the take-home pay they were shown is the real one; never the amount itself
        var code = System.Text.RegularExpressions.Regex.Match(text, @"Verification code: ([0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4})").Groups[1].Value; Assert.NotEmpty(code);
        var bank = TestBrowser.Create(e.F);
        var plain = await bank.Post("/api/verify", new { code }); var seen = await plain.Body();
        Assert.True(seen.GetProperty("valid").GetBoolean()); Assert.Equal("payslip", seen.GetProperty("kind").GetString()); Assert.Equal("A**** R*****", seen.GetProperty("holder").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, seen.GetProperty("amountMatches").ValueKind); Assert.DoesNotContain("9338650", await plain.Content.ReadAsStringAsync());
        Assert.True((await (await bank.Post("/api/verify", new { code, amount = 9_338_650 })).Body()).GetProperty("amountMatches").GetBoolean());
        Assert.False((await (await bank.Post("/api/verify", new { code, amount = 19_338_650 })).Body()).GetProperty("amountMatches").GetBoolean());   // an edited slip is caught
        HttpResponseMessage last = plain; for (var n = 0; n < 30; n++) last = await bank.Post("/api/verify", new { code = "ZZZZ-ZZZZ-ZZZZ" });
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);                                                        // guessing is throttled per address
    }

    [Fact]
    public async Task A_back_dated_raise_pays_the_difference_for_months_already_paid_once_in_the_next_run()
    {
        var e = await NewEnv(); var w = await Employee(e, "Raise Person");
        foreach (var m in new[] { 1, 2 }) { var r = await NewRun(e, m); await Calc(e, r); await Lock(e, r); }
        var url = $"/api/hr/payroll/compensation/{w.EmployeeId}"; var jan1 = $"{e.Year}-01-01";

        var plain = await e.Officer.SendAsync(HttpMethod.Put, url, new { effectiveFrom = jan1, basic = 12_000_000, ptkpStatus = "TK0" });
        Assert.Equal(HttpStatusCode.Conflict, plain.StatusCode); Assert.Contains("back-date", (await plain.Body()).GetProperty("error").GetProperty("message").GetString());
        var ok = await e.Officer.SendAsync(HttpMethod.Put, url, new { effectiveFrom = jan1, basic = 12_000_000, ptkpStatus = "TK0", allowRetro = true });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode); Assert.Equal(4_000_000m, (await ok.Body()).GetProperty("retroPending").GetDecimal());   // 2 months × (12,000,000 − 10,000,000)
        var waiting = await (await e.Officer.Get("/api/hr/payroll/retro")).Body(); Assert.Equal(1, waiting.GetArrayLength()); Assert.Equal(4_000_000m, waiting[0].GetProperty("amount").GetDecimal());
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Hr.Get("/api/hr/payroll/retro")).StatusCode);

        var mar = await NewRun(e, 3); var d = await Calc(e, mar);
        var line = d.GetProperty("lines").EnumerateArray().Single(); Assert.StartsWith("Back pay adjustment", line.GetProperty("label").GetString()); Assert.Equal(4_000_000m, line.GetProperty("amount").GetDecimal());
        Assert.Equal(16_000_000m, d.GetProperty("items")[0].GetProperty("gross").GetDecimal());                                                        // the new 12,000,000 plus the 4,000,000 back pay
        Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.SendAsync(HttpMethod.Delete, $"/api/hr/payroll/runs/{mar}/lines/{line.GetProperty("id").GetGuid()}")).StatusCode);
        await Lock(e, mar);
        Assert.Equal(0, (await (await e.Officer.Get("/api/hr/payroll/retro")).Body()).GetArrayLength());                                                  // applied
        var apr = await Calc(e, await NewRun(e, 4)); Assert.Equal(0, apr.GetProperty("lines").GetArrayLength());                                          // never twice
        Assert.Equal(12_000_000m, apr.GetProperty("items")[0].GetProperty("gross").GetDecimal());
    }

    [Fact]
    public async Task A_back_dated_cut_recovers_the_overpayment_as_a_deduction()
    {
        var e = await NewEnv(); var w = await Employee(e, "Cut Person");
        var jan = await NewRun(e, 1); await Calc(e, jan); await Lock(e, jan);
        Assert.Equal(HttpStatusCode.OK, (await e.Officer.SendAsync(HttpMethod.Put, $"/api/hr/payroll/compensation/{w.EmployeeId}", new { effectiveFrom = $"{e.Year}-01-01", basic = 9_000_000, ptkpStatus = "TK0", allowRetro = true })).StatusCode);
        var feb = await Calc(e, await NewRun(e, 2)); var line = feb.GetProperty("lines").EnumerateArray().Single();
        Assert.Equal("deduction", line.GetProperty("kind").GetString()); Assert.Equal(1_000_000m, line.GetProperty("amount").GetDecimal());
    }

    // ------------------------------------------------------------ editable, versioned rules
    [Fact]
    public async Task A_rules_correction_applies_from_its_month_to_unlocked_runs_only_and_every_run_keeps_the_version_it_used()
    {
        var e = await NewEnv(); await Employee(e, "Rules Person");
        var overview = await (await e.Officer.Get($"/api/hr/payroll/rules?year={e.Year}&month=1")).Body();
        var builtIn = PayrollRules.BuiltInFor(e.Year, 1).Version; Assert.Equal(builtIn, overview.GetProperty("label").GetString());   // the built-in set of that period (the pension wage ceiling changes every March) Assert.Equal(0.01m, overview.GetProperty("rules").GetProperty("kesEmployeeRate").GetDecimal());

        var jan = await NewRun(e, 1); var feb = await NewRun(e, 2);                                                           // February exists as a draft before the correction is entered
        await Calc(e, jan); await Lock(e, jan);                                                                              // January is final: 9,338,650 net under the built-in rules
        Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.Post("/api/hr/payroll/rules", new { effectiveFrom = $"{e.Year}-01-01", reason = "Too late", kesEmployeeRate = 0.02 })).StatusCode); // a locked month cannot be re-ruled
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.Post("/api/hr/payroll/rules", new { effectiveFrom = $"{e.Year}-02-15", reason = "x", kesEmployeeRate = 0.02 })).StatusCode);   // must be the 1st
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.Post("/api/hr/payroll/rules", new { effectiveFrom = $"{e.Year}-02-01", kesEmployeeRate = 0.02 })).StatusCode);              // a reason is required
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.Post("/api/hr/payroll/rules", new { effectiveFrom = $"{e.Year}-02-01", reason = "x", kesEmployeeRate = 1.5 })).StatusCode);   // not a rate
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.Post("/api/hr/payroll/rules", new { effectiveFrom = $"{e.Year}-02-01", reason = "x", terA = new[] { new { upto = 5_000_000, pct = 0 }, new { upto = 4_000_000, pct = 1 } } })).StatusCode); // not ascending
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.Post("/api/hr/payroll/rules", new { effectiveFrom = $"{e.Year}-02-01", reason = "x", ptkpTable = new { TK0 = 54_000_000 } })).StatusCode);      // missing statuses
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.Post("/api/hr/payroll/rules", new { effectiveFrom = $"{e.Year}-02-01", reason = "No change" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Approver.Post("/api/hr/payroll/rules", new { effectiveFrom = $"{e.Year}-02-01", reason = "x", kesEmployeeRate = 0.02 })).StatusCode);                  // approving is not configuring

        var ok = await e.Officer.Post("/api/hr/payroll/rules", new { effectiveFrom = $"{e.Year}-02-01", reason = "Regulation 123/2026: health contribution raised", kesEmployeeRate = 0.02 });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode); var body = await ok.Body(); Assert.Equal("ID-2026.1+1", body.GetProperty("label").GetString()); Assert.Contains("kesEmployeeRate 0.01→0.02", body.GetProperty("versions")[0].GetProperty("changes").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.Post("/api/hr/payroll/rules", new { effectiveFrom = $"{e.Year}-02-01", reason = "Again", kesEmployeeRate = 0.03 })).StatusCode);   // one version per month

        var item = (await Calc(e, feb)).GetProperty("items")[0];                                                              // the draft picks the correction up
        Assert.Equal(200_000m, item.GetProperty("kesEmployee").GetDecimal());                                                 // 2 % of 10,000,000
        Assert.Equal(9_238_650m, item.GetProperty("net").GetDecimal());                                                       // 10,000,000 − (200,000 + 200,000 + 100,000) − 261,350
        var versions = await e.Seed.Db(db => db.PayRuns.OrderBy(r => r.Month).Select(r => r.RulesVersion).ToListAsync());
        Assert.Equal([builtIn, "ID-2026.1+1"], versions);                                                                 // January still shows the built-in rules, February the correction
        Assert.Equal(0.01m, await e.Seed.Db(async db => PayrollRulesServiceTestHelper.Kes((await db.PayRuns.OrderBy(r => r.Month).FirstAsync()).RulesJson)));   // and January's stored snapshot is unchanged
        Assert.True(await e.Seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "hr.payroll.rules_change")));

        var u = await e.Seed.UserAsync(role: "payroll_officer"); var stale = TestBrowser.Create(e.F); await stale.LoginAsync(u.Email, Seed.Password);                       // logged in, but no recent re-authentication
        Assert.Equal("reauth_required", await (await stale.Post("/api/hr/payroll/rules", new { effectiveFrom = $"{e.Year}-03-01", reason = "x", kesEmployeeRate = 0.02 })).ErrorCode());
    }

    [Fact]
    public void Custom_rules_change_the_calculation_and_survive_a_round_trip_through_json()
    {
        var rules = PayrollRules.Default with { TerA = [new(5_400_000, 0), new(10_000_000, 5), new(decimal.MaxValue, 34)], JpWageCap = 8_000_000m };
        var back = AnnabaDigital.Application.Hr.PayrollRulesService.Deserialize(AnnabaDigital.Application.Hr.PayrollRulesService.Serialize(rules));
        Assert.Equal(rules.TerA, back.TerA); Assert.Equal(decimal.MaxValue, back.TerA[^1].Upto); Assert.Equal(8_000_000m, back.JpWageCap); Assert.Equal(54_000_000m, back.PtkpTable["TK0"]);
        var input = new AnnabaDigital.Application.Hr.PayInput("TK0", true, true, true, 0.0024m, 10_000_000m, 0m, 1m, 0m, 0m, 0m, 0m, false, 12, 0, 0, 0);
        var r = AnnabaDigital.Application.Hr.PayrollCalculator.Calculate(input, back);
        Assert.Equal(80_000m, r.JpEmployee);                                                                                 // 1 % of the new 8,000,000 cap
        Assert.Equal(0.34m, back.TerRateFor('A', 10_454_000m));                                                              // the custom table: above 10,000,000 is 34 %
        Assert.Equal(3_554_360m, r.Tax);                                                                                     // 10,454,000 × 34 %
    }

    // ------------------------------------------------------------ several companies under one organisation
    private static async Task<Guid> Company(Env e, string name, string? npwp = null)
    {
        var r = await e.Hr.Post("/api/hr/legal-entities", new { name, taxId = npwp });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode); return (await r.Body()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Companies_have_a_default_a_valid_tax_number_and_only_HR_config_can_change_them()
    {
        var e = await NewEnv();
        var list = await (await e.Officer.Get("/api/hr/legal-entities")).Body();                                             // the default company is created on first use
        Assert.Equal(1, list.GetArrayLength()); Assert.True(list[0].GetProperty("isDefault").GetBoolean());
        var main = list[0].GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await e.Officer.Post("/api/hr/legal-entities", new { name = "PT Baru" })).StatusCode);                  // reading is open to payroll staff, changing is HR configuration
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Hr.Post("/api/hr/legal-entities", new { name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Hr.Post("/api/hr/legal-entities", new { name = "PT Salah", taxId = "12345" })).StatusCode);
        var b = await Company(e, "PT Cabang Dua", "01.234.567.8-901.000");
        Assert.Equal(HttpStatusCode.Conflict, (await e.Hr.Post("/api/hr/legal-entities", new { name = "PT Cabang Dua" })).StatusCode);
        var all = await (await e.Hr.Get("/api/hr/legal-entities")).Body();
        Assert.Equal("012345678901000", all.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == b).GetProperty("taxId").GetString()); // stored as digits

        Assert.Equal(HttpStatusCode.Conflict, (await e.Hr.SendAsync(HttpMethod.Patch, $"/api/hr/legal-entities/{main}", new { name = "Main", archived = true })).StatusCode);   // the default cannot be archived
        var sw = await e.Hr.SendAsync(HttpMethod.Patch, $"/api/hr/legal-entities/{b}", new { name = "PT Cabang Dua", taxId = "012345678901000", isDefault = true });
        Assert.Equal(HttpStatusCode.OK, sw.StatusCode);
        var after = (await (await e.Hr.Get("/api/hr/legal-entities")).Body()).EnumerateArray().ToList();
        Assert.Equal(b, after.Single(x => x.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid());            // exactly one default, and it moved
        Assert.True(await e.Seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "hr.legal_entity.default")));

        var w = await Employee(e, "Cabang Orang");                                                                           // no company chosen → the (new) default
        var detail = await (await e.Hr.Get($"/api/hr/employees/{w.EmployeeId}")).Body();
        Assert.Equal(b, detail.GetProperty("legalEntityId").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await e.Hr.SendAsync(HttpMethod.Patch, $"/api/hr/legal-entities/{b}", new { name = "PT Cabang Dua", archived = true })).StatusCode);   // still default
        Assert.Equal(HttpStatusCode.OK, (await e.Hr.SendAsync(HttpMethod.Patch, $"/api/hr/legal-entities/{main}", new { name = "Main", isDefault = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await e.Hr.SendAsync(HttpMethod.Patch, $"/api/hr/legal-entities/{b}", new { name = "PT Cabang Dua", archived = true })).StatusCode);   // has an active employee
    }

    [Fact]
    public async Task Each_company_runs_its_own_payroll_for_its_own_people()
    {
        var e = await NewEnv();
        var main = (await (await e.Officer.Get("/api/hr/legal-entities")).Body())[0].GetProperty("id").GetGuid(); var b = await Company(e, "PT Anak Perusahaan", "098765432109876");
        await Employee(e, "Budi Utama"); var sari = await Employee(e, "Sari Anak", basic: 20_000_000m, entity: b);

        var janMain = await NewRun(e, 1); var janB = await NewRun(e, 1, b);                                                  // same month, two companies: no clash
        Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.Post("/api/hr/payroll/runs", new { year = e.Year, month = 1, legalEntityId = b })).StatusCode);   // but one per company
        var dMain = await Calc(e, janMain); var dB = await Calc(e, janB);
        Assert.Equal(["Budi Utama"], dMain.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("employeeName").GetString()).ToList());
        Assert.Equal(["Sari Anak"], dB.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("employeeName").GetString()).ToList());
        Assert.Equal("PT Anak Perusahaan", dB.GetProperty("run").GetProperty("legalEntityName").GetString());
        Assert.Equal(1, dMain.GetProperty("run").GetProperty("employeeCount").GetInt32());

        // a manual line only for someone of the run's own company
        var other = await e.Officer.Post($"/api/hr/payroll/runs/{janMain}/lines", new { employeeId = sari.EmployeeId, kind = "earning", label = "Bonus", amount = 100000 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, other.StatusCode);

        // the "earlier month must be locked" rule is per company: B's January being open does not stop the main company's February... and vice versa
        await Lock(e, janMain);
        var febMain = await NewRun(e, 2); Assert.Equal(HttpStatusCode.OK, (await e.Officer.Post($"/api/hr/payroll/runs/{febMain}/calculate")).StatusCode);
        var febB = await NewRun(e, 2, b); var blocked = await e.Officer.Post($"/api/hr/payroll/runs/{febB}/calculate");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);                                                           // B's own January is not locked yet

        // an employee in an unfinished run of their company cannot be moved; once that run is locked they can, and then belong to the next run of the new company
        var ver = (await (await e.Hr.Get($"/api/hr/employees/{sari.EmployeeId}")).Body()).GetProperty("version").GetInt32();
        var move = await e.Hr.SendAsync(HttpMethod.Patch, $"/api/hr/employees/{sari.EmployeeId}", new { version = ver, legalEntityId = main });
        Assert.Equal(HttpStatusCode.Conflict, move.StatusCode);
        await Lock(e, janB);
        move = await e.Hr.SendAsync(HttpMethod.Patch, $"/api/hr/employees/{sari.EmployeeId}", new { version = ver, legalEntityId = main });
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        var names = (await Calc(e, febMain)).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("employeeName").GetString()).Order().ToList();
        Assert.Equal(["Budi Utama", "Sari Anak"], names);
        Assert.Equal(b, await e.Seed.Db(db => db.PayRuns.Where(r => r.Id == janB).Select(r => r.LegalEntityId).SingleAsync()));   // history keeps the company that paid
        }

    // ------------------------------------------------------------ daily-wage workers inside the monthly run
    [Fact]
    public async Task Daily_wage_workers_are_paid_for_the_days_they_worked_in_the_same_monthly_run()
    {
        var e = await NewEnv(); await Employee(e, "Budi Bulanan"); var joko = await Employee(e, "Joko Harian", basic: 300_000m, allowance: 20_000m, payBasis: "daily");
        _ = await (await joko.B.Get("/api/hr/leave/types")).Body();
        var first = new DateOnly(e.Year, 1, 1); var weekdays = Enumerable.Range(0, 31).Select(i => first.AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();
        var saturday = Enumerable.Range(0, 31).Select(i => first.AddDays(i)).First(d => d.DayOfWeek == DayOfWeek.Saturday);
        await e.Seed.Db(async db =>
        {
            foreach (var d in weekdays.Take(3).Append(saturday)) db.AttendanceLogs.Add(new AttendanceLog { EmployeeId = joko.EmployeeId, Date = d, ClockIn = new DateTimeOffset(d.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(7)).ToUniversalTime(), ClockOut = new DateTimeOffset(d.ToDateTime(new TimeOnly(17, 0)), TimeSpan.FromHours(7)).ToUniversalTime() });
            var sick = await db.LeaveTypes.SingleAsync(t => t.Code == "SICK"); var unpaid = await db.LeaveTypes.SingleAsync(t => t.Code == "UNPAID");
            db.LeaveRequests.Add(new LeaveRequest { EmployeeId = joko.EmployeeId, LeaveTypeId = sick.Id, StartDate = weekdays[5], EndDate = weekdays[5], Days = 1, Status = LeaveStatus.Approved, CreatedAt = e.Clock.UtcNow });       // paid leave counts as a paid day
            db.LeaveRequests.Add(new LeaveRequest { EmployeeId = joko.EmployeeId, LeaveTypeId = unpaid.Id, StartDate = weekdays[6], EndDate = weekdays[6], Days = 1, Status = LeaveStatus.Approved, CreatedAt = e.Clock.UtcNow });     // unpaid leave does not
            return await db.SaveChangesAsync();
        });
        var comp = await (await e.Officer.Get($"/api/hr/payroll/compensation/{joko.EmployeeId}")).Body(); Assert.Equal("daily", comp.GetProperty("payBasis").GetString());

        var run = await NewRun(e, 1);
        Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.Post($"/api/hr/payroll/runs/{run}/calculate")).StatusCode);          // their pay follows attendance, which must be final first
        Assert.Equal(HttpStatusCode.OK, (await e.Hr.Post($"/api/hr/attendance/periods/{e.Year}-01/close")).StatusCode);
        var items = (await Calc(e, run)).GetProperty("items").EnumerateArray().ToDictionary(x => x.GetProperty("employeeName").GetString()!);
        var j = items["Joko Harian"];                                                                                             // 3 weekdays + 1 Saturday worked + 1 paid-leave day = 5 days
        Assert.Equal(300_000m, j.GetProperty("dailyRate").GetDecimal()); Assert.Equal(5m, j.GetProperty("daysPaid").GetDecimal());
        Assert.Equal(1_500_000m, j.GetProperty("basic").GetDecimal()); Assert.Equal(100_000m, j.GetProperty("allowance").GetDecimal()); Assert.Equal(1_600_000m, j.GetProperty("gross").GetDecimal());
        Assert.Equal(0m, j.GetProperty("unpaidDeduction").GetDecimal()); Assert.Contains("Daily wage: 5 day", j.GetProperty("warnings").GetString());
        Assert.Equal(16_000m, j.GetProperty("kesEmployee").GetDecimal());                                                         // BPJS on what was actually earned
        var b = items["Budi Bulanan"]; Assert.Equal(System.Text.Json.JsonValueKind.Null, b.GetProperty("dailyRate").ValueKind); Assert.Equal(10_000_000m, b.GetProperty("basic").GetDecimal());   // the monthly worker is unaffected

        await Lock(e, run);                                                                                                       // a back-dated change makes no sense when pay follows days worked
        var back = await e.Officer.SendAsync(HttpMethod.Put, $"/api/hr/payroll/compensation/{joko.EmployeeId}", new { effectiveFrom = $"{e.Year}-01-01", basic = 350_000, ptkpStatus = "TK0", allowRetro = true });
        Assert.Equal(HttpStatusCode.Conflict, back.StatusCode); Assert.Contains("daily-wage", (await back.Body()).GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.OK, (await e.Officer.SendAsync(HttpMethod.Put, $"/api/hr/payroll/compensation/{joko.EmployeeId}", new { effectiveFrom = $"{e.Year}-02-01", basic = 350_000, allowance = 20_000, ptkpStatus = "TK0" })).StatusCode);   // a forward-dated raise is fine
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.SendAsync(HttpMethod.Put, $"/api/hr/payroll/compensation/{joko.EmployeeId}", new { effectiveFrom = $"{e.Year}-03-01", basic = 360_000, ptkpStatus = "TK0", payBasis = "weekly" })).StatusCode);
        Assert.Equal("daily", (await (await e.Officer.Get($"/api/hr/payroll/compensation/{joko.EmployeeId}")).Body()).GetProperty("payBasis").GetString());       // the basis is kept when not mentioned
    }

    [Fact]
    public async Task Approvers_are_told_when_a_run_is_ready_once_and_employees_when_their_payslip_is_out()
    {
        var e = await NewEnv(); var w = await Employee(e, "Notified Worker");
        async Task<System.Text.Json.JsonElement> Inbox(TestBrowser b) => await (await b.Get("/api/hr/me/notifications")).Body();
        var run = await NewRun(e, 1); await Calc(e, run);
        var ready = (await Inbox(e.Approver)).GetProperty("items").EnumerateArray().Where(x => x.GetProperty("kind").GetString() == "payroll.ready").ToList();
        Assert.Single(ready); Assert.Equal($"/hr/payroll/{run}", ready[0].GetProperty("link").GetString());
        await Calc(e, run);                                                                                                       // recalculating does not nag again while the first is unread
        Assert.Single((await Inbox(e.Approver)).GetProperty("items").EnumerateArray().Where(x => x.GetProperty("kind").GetString() == "payroll.ready"));
        Assert.Equal(0, (await Inbox(e.Officer)).GetProperty("unread").GetInt32());                                               // the person who prepared it is not asked to approve it

        Assert.Equal(0, (await Inbox(w.B)).GetProperty("unread").GetInt32());                                                     // the payslip is announced when the run is locked, not before
        await Lock(e, run);
        var slip = (await Inbox(w.B)).GetProperty("items")[0]; Assert.Equal("payslip.ready", slip.GetProperty("kind").GetString()); Assert.Equal("/hr/my-payslips", slip.GetProperty("link").GetString());
        var mail = await e.Seed.Mail.WaitFor(w.Email, "payslip");                                                                // PAY-009: an e-mail too, and it carries no amounts
        Assert.DoesNotContain("9,338,650", mail.Text); Assert.DoesNotContain("9338650", mail.Text); Assert.DoesNotContain("10,000,000", mail.Html); Assert.Contains("/hr/my-payslips", mail.Text);

        // ESS-002: the employee's own yearly income and tax statement, from locked runs only
        var st = await (await w.B.Get($"/api/hr/payroll/payslips/annual/{e.Year}")).Body(); var co = st.GetProperty("companies")[0];
        Assert.Equal(1, co.GetProperty("months").GetInt32()); Assert.Equal(10_454_000m, co.GetProperty("taxableIncome").GetDecimal()); Assert.Equal(261_350m, co.GetProperty("tax").GetDecimal()); Assert.Equal(300_000m, co.GetProperty("pension").GetDecimal());
        var pdf = await w.B.Get($"/api/hr/payroll/payslips/annual/{e.Year}/pdf"); Assert.Equal(HttpStatusCode.OK, pdf.StatusCode); Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.Contains("261,350", System.Text.Encoding.Latin1.GetString(await pdf.Content.ReadAsByteArrayAsync()));
        Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.Get($"/api/hr/payroll/payslips/annual/{e.Year}")).StatusCode);    // payroll staff without an employee record have no statement of their own
        Assert.Equal(HttpStatusCode.NotFound, (await w.B.Get($"/api/hr/payroll/payslips/annual/{e.Year - 1}/pdf")).StatusCode);
    }

    // ------------------------------------------------------------ recurring components
    [Fact]
    public async Task Recurring_allowances_and_fees_join_every_run_in_their_months_until_ended()
    {
        var e = await NewEnv(); var w = await Employee(e, "Recurring Person");
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Approver.Post("/api/hr/payroll/recurring", new { employeeId = w.EmployeeId, kind = "earning", label = "Transport", amount = 500_000 })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.Post("/api/hr/payroll/recurring", new { employeeId = w.EmployeeId, kind = "gift", label = "", amount = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.Post("/api/hr/payroll/recurring", new { employeeId = w.EmployeeId, kind = "earning", label = "X", amount = 1, from = $"{e.Year}-03-01", through = $"{e.Year}-02-01" })).StatusCode);
        async Task<Guid> Add(object body) { var r = await e.Officer.Post("/api/hr/payroll/recurring", body); Assert.Equal(HttpStatusCode.Created, r.StatusCode); return (await r.Body()).GetProperty("id").GetGuid(); }
        var transport = await Add(new { employeeId = w.EmployeeId, kind = "earning", label = "Transport allowance", amount = 500_000, from = $"{e.Year}-01-15" });   // any day of the month means that month
        await Add(new { employeeId = w.EmployeeId, kind = "deduction", label = "Cooperative fee", amount = 100_000, from = $"{e.Year}-01-01", through = $"{e.Year}-01-01" });   // January only
        await Add(new { employeeId = w.EmployeeId, kind = "earning", label = "Later allowance", amount = 999_000, from = $"{e.Year}-03-01" });                    // not yet in force

        var jan = await NewRun(e, 1); var d = await Calc(e, jan); var item = d.GetProperty("items")[0];
        var lines = d.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(["Cooperative fee", "Transport allowance"], lines.Select(l => l.GetProperty("label").GetString()).Order().ToList()); Assert.All(lines, l => Assert.True(l.GetProperty("auto").GetBoolean()));
        Assert.Equal(10_500_000m, item.GetProperty("gross").GetDecimal()); Assert.Equal(500_000m, item.GetProperty("taxableExtras").GetDecimal()); Assert.Equal(100_000m, item.GetProperty("otherDeductions").GetDecimal());
        Assert.Equal(328_620m, item.GetProperty("tax").GetDecimal());                                                            // (10,500,000 + 454,000 employer premiums) × 3 %
        Assert.Equal(10_500_000m - 400_000m - 328_620m - 100_000m, item.GetProperty("net").GetDecimal());
        await Calc(e, jan); Assert.Equal(2, (await (await e.Officer.Get($"/api/hr/payroll/runs/{jan}")).Body()).GetProperty("lines").GetArrayLength());   // recalculating does not duplicate them
        await Lock(e, jan);

        Assert.Equal(HttpStatusCode.NoContent, (await e.Officer.SendAsync(HttpMethod.Delete, $"/api/hr/payroll/recurring/{transport}")).StatusCode);       // ended: gone from the next run, history untouched
        Assert.Equal(HttpStatusCode.NotFound, (await e.Officer.SendAsync(HttpMethod.Delete, $"/api/hr/payroll/recurring/{transport}")).StatusCode);
        var feb = await Calc(e, await NewRun(e, 2));
        Assert.Equal(0, feb.GetProperty("lines").GetArrayLength()); Assert.Equal(10_000_000m, feb.GetProperty("items")[0].GetProperty("gross").GetDecimal());
        Assert.Equal(2, await e.Seed.Db(db => db.PayRunLines.CountAsync(l => l.PayRunId == jan)));
        var list = await (await e.Officer.Get("/api/hr/payroll/recurring?activeOnly=true")).Body(); Assert.Equal(2, list.GetArrayLength());
        Assert.True(await e.Seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "hr.payroll.recurring_end")));
    }

    // ------------------------------------------------------------ expense claims paid through payroll
    [Fact]
    public async Task An_approved_expense_claim_is_reimbursed_tax_free_in_the_next_run_once_and_marked_paid_when_it_locks()
    {
        var e = await NewEnv(); var w = await Employee(e, "Claim Person"); var other = await Employee(e, "Other Person");
        var today = DateOnly.FromDateTime(e.Clock.UtcNow.UtcDateTime.AddHours(7)); string D(DateOnly d) => d.ToString("yyyy-MM-dd");
        var png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await w.B.Post("/api/hr/claims", new { category = "transport", expenseDate = D(today.AddDays(1)), amount = 150_000, description = "Taxi" })).StatusCode);        // not yet spent
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await w.B.Post("/api/hr/claims", new { category = "yacht", expenseDate = D(today), amount = 0, description = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await w.B.Post("/api/hr/claims", new { category = "transport", expenseDate = D(today), amount = 1, description = "x", receiptName = "r.pdf", receiptBase64 = Convert.ToBase64String([0x4D, 0x5A, 0x90]) })).StatusCode);   // an executable is not a receipt
        var made = await w.B.Post("/api/hr/claims", new { category = "transport", expenseDate = D(today.AddDays(-2)), amount = 150_000, description = "Taxi to the client", receiptName = "taxi.png", receiptBase64 = png });
        Assert.Equal(HttpStatusCode.Created, made.StatusCode); var claim = await made.Body(); var id = claim.GetProperty("id").GetGuid();
        Assert.True(claim.GetProperty("hasReceipt").GetBoolean()); Assert.False(claim.GetProperty("canDecide").GetBoolean());
        var second = (await (await w.B.Post("/api/hr/claims", new { category = "meal", expenseDate = D(today), amount = 80_000, description = "Lunch with the client" })).Body()).GetProperty("id").GetGuid();
        var third = (await (await w.B.Post("/api/hr/claims", new { category = "other", expenseDate = D(today), amount = 10_000, description = "Parking" })).Body()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await w.B.Post($"/api/hr/claims/{id}/approve")).StatusCode);                        // not your own, not without the permission
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Approver.Post($"/api/hr/claims/{id}/approve")).StatusCode);                 // approving pay runs is not approving claims
        Assert.Equal(HttpStatusCode.NotFound, (await other.B.Get($"/api/hr/claims/{id}/receipt")).StatusCode);                      // a colleague cannot open the receipt
        var receipt = await e.Officer.Get($"/api/hr/claims/{id}/receipt"); Assert.Equal(HttpStatusCode.OK, receipt.StatusCode); Assert.Equal("image/png", receipt.Content.Headers.ContentType!.MediaType);
        Assert.Equal(3, (await (await e.Officer.Get("/api/hr/approvals")).Body()).GetProperty("claims").GetArrayLength());           // they wait in the approvals inbox

        Assert.Equal("approved", (await (await e.Officer.Post($"/api/hr/claims/{id}/approve")).Body()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.Post($"/api/hr/claims/{second}/reject", new { })).StatusCode);
        Assert.Equal("rejected", (await (await e.Officer.Post($"/api/hr/claims/{second}/reject", new { note = "Private lunch" })).Body()).GetProperty("status").GetString());
        Assert.Equal("cancelled", (await (await w.B.Post($"/api/hr/claims/{third}/cancel")).Body()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.Post($"/api/hr/claims/{id}/approve")).StatusCode);                   // decided once

        var jan = await NewRun(e, 1); var d = await Calc(e, jan);
        var item = d.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("employeeName").GetString() == "Claim Person");
        Assert.Equal(150_000m, item.GetProperty("nonTaxableExtras").GetDecimal()); Assert.Equal(261_350m, item.GetProperty("tax").GetDecimal());                 // tax is unchanged
        Assert.Equal(9_338_650m + 150_000m, item.GetProperty("net").GetDecimal());
        var line = d.GetProperty("lines").EnumerateArray().Single(); Assert.StartsWith("Reimbursement: transport", line.GetProperty("label").GetString()); Assert.True(line.GetProperty("auto").GetBoolean());
        Assert.Equal("approved", (await (await w.B.Get("/api/hr/claims")).Body()).GetProperty("data").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == id).GetProperty("status").GetString());   // not paid until the run is final
        await Lock(e, jan);
        Assert.Equal(ClaimStatus.Paid, await e.Seed.Db(db => db.ExpenseClaims.Where(c => c.Id == id).Select(c => c.Status).SingleAsync()));
        Assert.Equal(0, (await Calc(e, await NewRun(e, 2))).GetProperty("lines").GetArrayLength());                                 // never reimbursed twice
        Assert.Contains((await (await w.B.Get("/api/hr/me/notifications")).Body()).GetProperty("items").EnumerateArray(), n => n.GetProperty("kind").GetString() == "claim.decided");
    }

    // ------------------------------------------------------------ go-live self-check
    [Fact]
    public async Task The_go_live_check_reports_what_is_missing_and_lists_what_only_people_can_confirm()
    {
        var e = await NewEnv(); await Employee(e, "Ready Person");
        async Task<TestBrowser> Admin() { var u = await e.Seed.UserAsync(role: "super_admin"); var b = TestBrowser.Create(e.F); await b.LoginAsync(u.Email, Seed.Password); return b; }
        var admin = await Admin();
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Hr.Get("/api/hr/readiness")).StatusCode);                                   // HR may not read the deployment's configuration
        async Task<Dictionary<string, (string Status, string Detail)>> Run() => (await (await admin.Get("/api/hr/readiness")).Body()).GetProperty("checks").EnumerateArray().ToDictionary(c => c.GetProperty("key").GetString()!, c => (c.GetProperty("status").GetString()!, c.GetProperty("detail").GetString()!));
        var r = await Run();
        Assert.Equal("fail", r["super_admins"].Status);                                                                           // one person holding all the keys
        Assert.Equal("ok", r["maker_checker"].Status);                                                                            // the officer and the approver are two people
        Assert.NotEqual("ok", r["privileged_mfa"].Status); Assert.Contains("privileged account", r["privileged_mfa"].Detail);
        Assert.Equal("fail", r["https"].Status); Assert.Equal("fail", r["email"].Status); Assert.Equal("warn", r["antivirus"].Status); Assert.Equal("ok", r["monitoring"].Status);
        Assert.Equal("ok", r["compensation"].Status); Assert.Equal("warn", r["company_tax_id"].Status); Assert.Equal("warn", r["holidays"].Status);
        Assert.Equal("manual", r["payroll_rules"].Status); Assert.Contains("NOT signed off", r["payroll_rules"].Detail);
        Assert.Equal(6, r.Values.Count(v => v.Status == "manual"));

        await Admin();                                                                                                              // a second super admin
        e.F.Services.GetRequiredService<FakeFileScanner>().Enabled = true;
        var company = (await (await e.Hr.Get("/api/hr/legal-entities")).Body())[0];
        Assert.Equal(HttpStatusCode.OK, (await e.Hr.SendAsync(HttpMethod.Patch, $"/api/hr/legal-entities/{company.GetProperty("id").GetGuid()}", new { name = company.GetProperty("name").GetString(), taxId = "012345678901000" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await e.Hr.Post("/api/hr/employees", new { fullName = "No Pay Yet", hireDate = e.Hire.ToString("yyyy-MM-dd"), jobTitle = "Analyst" })).StatusCode);
        r = await Run();
        Assert.Equal("ok", r["super_admins"].Status); Assert.Equal("ok", r["antivirus"].Status); Assert.Equal("ok", r["company_tax_id"].Status);
        Assert.Equal("warn", r["compensation"].Status); Assert.Contains("1 of 2", r["compensation"].Detail);                       // the new hire has no salary record yet
    }

    // ------------------------------------------------------------ weekly payouts settled by the monthly run
    [Fact]
    public async Task Daily_wage_workers_can_be_paid_weekly_and_the_monthly_run_settles_those_payouts_exactly_once()
    {
        var e = await NewEnv(); await Employee(e, "Budi Bulanan"); var joko = await Employee(e, "Joko Mingguan", basic: 300_000m, allowance: 20_000m, payBasis: "daily");
        var today = DateOnly.FromDateTime(e.Clock.UtcNow.UtcDateTime.AddHours(7)); string D(DateOnly d) => d.ToString("yyyy-MM-dd");
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);                                                       // last month: its weeks are over and recent
        var monday = month.AddDays(4); while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(1);                   // a full week inside that month
        await e.Seed.Db(async db =>
        {
            foreach (var d in Enumerable.Range(0, 4).Select(i => monday.AddDays(i)))                                              // Monday to Thursday worked
                db.AttendanceLogs.Add(new AttendanceLog { EmployeeId = joko.EmployeeId, Date = d, ClockIn = new DateTimeOffset(d.ToDateTime(new TimeOnly(8, 0)), TimeSpan.FromHours(7)).ToUniversalTime(), ClockOut = new DateTimeOffset(d.ToDateTime(new TimeOnly(16, 0)), TimeSpan.FromHours(7)).ToUniversalTime() });
            return await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.Post("/api/hr/payroll/payouts", new { weekStart = D(monday.AddDays(1)) })).StatusCode);   // a pay week starts on a Monday
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.Post("/api/hr/payroll/payouts", new { weekStart = D(monday), holdbackPct = 80 })).StatusCode);
        var made = await e.Officer.Post("/api/hr/payroll/payouts", new { weekStart = D(monday) }); Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        var id = (await made.Body()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.Post("/api/hr/payroll/payouts", new { weekStart = D(monday) })).StatusCode);                           // one per company and week

        var calc = await (await e.Officer.Post($"/api/hr/payroll/payouts/{id}/calculate")).Body(); var item = calc.GetProperty("items").EnumerateArray().Single();   // the monthly-paid colleague is not in it
        Assert.Equal("Joko Mingguan", item.GetProperty("employeeName").GetString()); Assert.Equal(4m, item.GetProperty("days").GetDecimal());
        Assert.Equal(1_280_000m, item.GetProperty("gross").GetDecimal()); Assert.Equal(128_000m, item.GetProperty("holdback").GetDecimal()); Assert.Equal(1_152_000m, item.GetProperty("net").GetDecimal());   // 4 × 320,000, 10 % kept back
        Assert.Empty((await (await joko.B.Get("/api/hr/payroll/payouts/mine")).Body()).EnumerateArray());                           // nothing to see before approval

        Assert.Equal(HttpStatusCode.Forbidden, (await e.Officer.Post($"/api/hr/payroll/payouts/{id}/approve")).StatusCode);       // preparing is not approving
        var boss = await e.Seed.UserAsync(role: "super_admin"); var both = TestBrowser.Create(e.F); await both.LoginAsync(boss.Email, Seed.Password); await both.Post("/api/auth/reauth", new { password = Seed.Password });
        var second = (await (await both.Post("/api/hr/payroll/payouts", new { weekStart = D(monday.AddDays(7)) })).Body()).GetProperty("id").GetGuid();
        await both.Post($"/api/hr/payroll/payouts/{second}/calculate");
        Assert.Equal(HttpStatusCode.Conflict, (await both.Post($"/api/hr/payroll/payouts/{second}/approve")).StatusCode);         // even with every permission, not the person who calculated it
        Assert.Equal(HttpStatusCode.NoContent, (await both.SendAsync(HttpMethod.Delete, $"/api/hr/payroll/payouts/{second}")).StatusCode);

        Assert.Equal("approved", (await (await e.Approver.Post($"/api/hr/payroll/payouts/{id}/approve")).Body()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.Post($"/api/hr/payroll/payouts/{id}/calculate")).StatusCode);      // frozen for the application…
        Assert.Equal(HttpStatusCode.Conflict, (await e.Officer.SendAsync(HttpMethod.Delete, $"/api/hr/payroll/payouts/{id}")).StatusCode);
        foreach (var sql in new[] { "UPDATE hr_weekly_payout_items SET net = net + 1", "DELETE FROM hr_weekly_payout_items", "UPDATE hr_weekly_payouts SET total_net = 1", "UPDATE hr_weekly_payouts SET status = 'Draft'", "DELETE FROM hr_weekly_payouts" })
            await Assert.ThrowsAsync<PostgresException>(() => e.Seed.Db(db => db.Database.ExecuteSqlRawAsync(sql)));               // …and for anyone with database access
        var bank = await (await e.Approver.Get($"/api/hr/payroll/payouts/{id}/bank-file")).Content.ReadAsStringAsync();
        Assert.Contains("Joko Mingguan,BCA,1234567890,1152000,", bank);
        var mine = (await (await joko.B.Get("/api/hr/payroll/payouts/mine")).Body())[0]; Assert.Equal(1_152_000m, mine.GetProperty("net").GetDecimal()); Assert.False(mine.GetProperty("settled").GetBoolean());
        Assert.Equal("paid", (await (await e.Approver.Post($"/api/hr/payroll/payouts/{id}/paid")).Body()).GetProperty("status").GetString());

        // month end: the whole month is computed as usual and the weekly payment is taken off
        Assert.Equal(HttpStatusCode.OK, (await e.Hr.Post($"/api/hr/attendance/periods/{month:yyyy-MM}/close")).StatusCode);
        var runRes = await e.Officer.Post("/api/hr/payroll/runs", new { year = month.Year, month = month.Month }); Assert.Equal(HttpStatusCode.Created, runRes.StatusCode); var run = (await runRes.Body()).GetProperty("id").GetGuid();
        var d1 = await Calc(e, run); var j = d1.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("employeeName").GetString() == "Joko Mingguan");
        Assert.Equal(1_280_000m, j.GetProperty("gross").GetDecimal()); Assert.Equal(1_152_000m, j.GetProperty("otherDeductions").GetDecimal()); Assert.Equal(0m, j.GetProperty("tax").GetDecimal());
        Assert.Equal(76_800m, j.GetProperty("net").GetDecimal());                                                                 // 1,280,000 − 51,200 BPJS − 1,152,000 already paid = the holdback minus BPJS
        Assert.Single(d1.GetProperty("lines").EnumerateArray(), l => l.GetProperty("label").GetString()!.StartsWith("Paid in weekly payout"));
        var d2 = await Calc(e, run); Assert.Single(d2.GetProperty("lines").EnumerateArray(), l => l.GetProperty("label").GetString()!.StartsWith("Paid in weekly payout"));   // recalculating does not deduct twice
        await Lock(e, run);
        Assert.True((await (await joko.B.Get("/api/hr/payroll/payouts/mine")).Body())[0].GetProperty("settled").GetBoolean());
        Assert.Equal(run, await e.Seed.Db(db => db.WeeklyPayoutItems.Select(i => i.SettledRunId!.Value).SingleAsync()));
        Assert.Equal(0, await e.Seed.Db(db => db.WeeklyPayoutItems.CountAsync(i => i.SettledRunId == null)));                     // nothing is left for a later run to deduct again
        await Assert.ThrowsAsync<PostgresException>(() => e.Seed.Db(db => db.Database.ExecuteSqlRawAsync("UPDATE hr_weekly_payout_items SET gross = 1")));   // settling did not unfreeze the amounts
    }
    [Fact]
    public async Task A_parallel_run_lays_the_old_payrolls_figures_next_to_this_run_and_names_every_difference()
    {
        var e = await NewEnv(); var a = await Employee(e, "Alpha Parallel"); await Employee(e, "Beta Parallel", basic: 15_000_000m); await Employee(e, "Gamma Parallel", basic: 7_000_000m);
        var run = await NewRun(e, 1); await Calc(e, run);
        var items = (await (await e.Officer.Get($"/api/hr/payroll/runs/{run}")).Body()).GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("employeeName").GetString()!);
        string No(string n) => items[n].GetProperty("employeeNo").GetString()!;
        var id = new System.Globalization.CultureInfo("id-ID"); string V(string n, string f, decimal plus = 0) => (items[n].GetProperty(f).GetDecimal() + plus).ToString("#,0", id);   // "10.000.000"

        // the old payroll's export: Indonesian headings, semicolons, dots for thousands; Beta's tax is 25,000 lower there, Gamma is missing, and it pays someone this run does not know
        var csv = "\uFEFFNIK;Nama;Bruto;PPh 21;Gaji Bersih\r\n"
            + $"{No("Alpha Parallel")};Alpha;Rp {V("Alpha Parallel", "gross")};{V("Alpha Parallel", "tax")};\"{V("Alpha Parallel", "net")},00\"\r\n"
            + $"{No("Beta Parallel")};Beta;{V("Beta Parallel", "gross")};{V("Beta Parallel", "tax", -25_000)};{V("Beta Parallel", "net", 25_000)}\r\n"
            + "X-999;Stranger;5.000.000;0;5.000.000\r\n";
        Assert.Equal(HttpStatusCode.Forbidden, (await a.B.Post($"/api/hr/payroll/runs/{run}/compare", new { csv })).StatusCode);                 // staff do not read pay runs
        var res = await e.Officer.Post($"/api/hr/payroll/runs/{run}/compare", new { csv }); Assert.Equal(HttpStatusCode.OK, res.StatusCode); var r = await res.Body();
        Assert.Equal(1, r.GetProperty("matched").GetInt32()); Assert.Equal(1, r.GetProperty("different").GetInt32()); Assert.Equal(1, r.GetProperty("onlyInSystem").GetInt32()); Assert.Equal(1, r.GetProperty("onlyInLegacy").GetInt32());
        Assert.Equal(["gross", "tax", "net"], r.GetProperty("comparedFields").EnumerateArray().Select(x => x.GetString()!).ToList());      // BPJS was not in the file, so it is not judged
        var rows = r.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(["different", "only_system", "only_legacy", "match"], rows.Select(x => x.GetProperty("status").GetString()!).ToList());   // what needs attention comes first
        Assert.Equal("Beta Parallel", rows[0].GetProperty("name").GetString());
        Assert.Equal(25_000m, rows[0].GetProperty("tax").GetProperty("difference").GetDecimal()); Assert.Equal(-25_000m, rows[0].GetProperty("net").GetProperty("difference").GetDecimal()); Assert.Equal(0m, rows[0].GetProperty("gross").GetProperty("difference").GetDecimal());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, rows[0].GetProperty("bpjs").GetProperty("legacy").ValueKind);
        Assert.Equal("X-999", rows[2].GetProperty("employeeNo").GetString()); Assert.Equal(5_000_000m, rows[2].GetProperty("net").GetProperty("legacy").GetDecimal());
        Assert.Equal(25_000m, r.GetProperty("tax").GetProperty("difference").GetDecimal());                                                   // totals cover the employees found on both sides

        // a generous tolerance accepts the difference; a file without the needed columns, or naming someone twice, is refused with a reason
        Assert.Equal(0, (await (await e.Officer.Post($"/api/hr/payroll/runs/{run}/compare", new { csv, tolerance = 25_000 })).Body()).GetProperty("different").GetInt32());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.Post($"/api/hr/payroll/runs/{run}/compare", new { csv = "name,net\nA,1" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.Post($"/api/hr/payroll/runs/{run}/compare", new { csv = "employee_no,name\nA,B" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.Post($"/api/hr/payroll/runs/{run}/compare", new { csv = "employee_no,net\nA,1\nA,2" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await e.Officer.Post($"/api/hr/payroll/runs/{run}/compare", new { csv = "" })).StatusCode);
        var trail = await e.Seed.Db(db => db.AuditEvents.Where(x => x.Action == "hr.payroll.parallel_compare").OrderBy(x => x.Ts).Select(x => x.MetaJson).FirstAsync());
        Assert.Contains("\"different\": \"1\"", trail); Assert.DoesNotContain("25000", trail);                                                    // the trail says how it came out, not what anyone earns
    }

    [Theory]
    [InlineData("9.338.650", 9338650)] [InlineData("9,338,650", 9338650)] [InlineData("Rp 9.338.650,00", 9338650)] [InlineData("9,338,650.50", 9338650.5)] [InlineData("9338650", 9338650)]
    [InlineData("1.234", 1234)] [InlineData("1234.5", 1234.5)] [InlineData("12,5", 12.5)] [InlineData("(25.000)", -25000)] [InlineData("-25,000", -25000)] [InlineData("0", 0)]
    public void Amounts_from_the_old_payroll_are_read_in_indonesian_and_english_notation(string raw, double expected)
        => Assert.Equal((decimal)expected, AnnabaDigital.Application.Hr.ParallelRunService.ParseAmount(raw));

    [Theory] [InlineData("")] [InlineData("-")] [InlineData("n/a")]
    public void An_empty_amount_means_not_given(string raw) => Assert.Null(AnnabaDigital.Application.Hr.ParallelRunService.ParseAmount(raw));
}

internal static class PayrollRulesServiceTestHelper { public static decimal Kes(string json) => AnnabaDigital.Application.Hr.PayrollRulesService.Deserialize(json).KesEmployeeRate;  }
