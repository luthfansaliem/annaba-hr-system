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

        var slips = await (await a.B.Get("/api/hr/payroll/payslips")).Body(); var id = slips[0].GetProperty("itemId").GetGuid();
        var pdf = await a.B.Get($"/api/hr/payroll/payslips/{id}/pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode); Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        var bytes = await pdf.Content.ReadAsByteArrayAsync(); var text = System.Text.Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", text); Assert.EndsWith("%%EOF\n", text); Assert.Contains("Alpha Report", text); Assert.Contains("Take-home pay", text); Assert.Contains("9,338,650", text);
        var startxref = int.Parse(text[(text.LastIndexOf("startxref\n") + 10)..].Split('\n')[0]);
        Assert.StartsWith("xref", text[startxref..]);                                                                        // the cross-reference table is where the file says it is
        foreach (var n in new[] { 1, 2, 3, 4, 5, 6, 7 }) Assert.Contains($"\n{n} 0 obj\n", text);
        Assert.Equal(HttpStatusCode.NotFound, (await b.B.Get($"/api/hr/payroll/payslips/{id}/pdf")).StatusCode);              // someone else's slip
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
        Assert.Equal("ID-2026.1", overview.GetProperty("label").GetString()); Assert.Equal(0.01m, overview.GetProperty("rules").GetProperty("kesEmployeeRate").GetDecimal());

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
        Assert.Equal(["ID-2026.1", "ID-2026.1+1"], versions);                                                                 // January still shows the built-in rules, February the correction
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
    }
}

internal static class PayrollRulesServiceTestHelper { public static decimal Kes(string json) => AnnabaDigital.Application.Hr.PayrollRulesService.Deserialize(json).KesEmployeeRate; }
