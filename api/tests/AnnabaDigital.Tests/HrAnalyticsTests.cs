using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>RPT-002: each module's analytics add up from the module's own records, and are only for people who may see that module's organisation-wide list.</summary>
[Collection("db")]
public sealed class HrAnalyticsTests(PostgresFixture fx)
{
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");
    private static async Task<TestBrowser> As(ApiFactory f, Seed seed, string role, bool reauth = false)
    {
        var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return b;
    }
    private static Dictionary<string, JsonElement> ByKey(JsonElement arr) => arr.EnumerateArray().ToDictionary(x => x.GetProperty("key").GetString()!, x => x);
    /// <summary>A WIB wall-clock time as the UTC instant the database stores.</summary>
    private static DateTimeOffset At(DateOnly d, int hour, int minute) => new DateTimeOffset(d.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(7)).ToUniversalTime();

    [Fact]
    public async Task Attendance_leave_claims_and_payroll_analytics_add_up_from_the_modules_own_records()
    {
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock); var hr = await As(f, seed, "hr_admin", reauth: true); var t = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));
        async Task<Guid> Unit(string code, string name) => (await (await hr.Post("/api/hr/org-units", new { code, name })).Body()).GetProperty("id").GetGuid();
        var alpha = await Unit("AAA", "Alpha"); var beta = await Unit("BBB", "Beta");
        async Task<Guid> Emp(string name, Guid unit) => (await (await hr.Post("/api/hr/employees", new { fullName = name, hireDate = D(t.AddYears(-2)), jobTitle = "Analyst", orgUnitId = unit })).Body()).GetProperty("id").GetGuid();
        var one = await Emp("One", alpha); var two = await Emp("Two", alpha); var three = await Emp("Three", beta);
        var workday = t.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        _ = await (await hr.Get("/api/hr/leave/types")).Body(); // creates the default leave types on first use

        await seed.Db(async db =>
        {
            // attendance: One on time and still in, Two late and out after 8 hours remotely, Three absent today
            db.AttendanceLogs.Add(new AttendanceLog { EmployeeId = one, Date = t, ClockIn = At(t, 8, 55) });
            db.AttendanceLogs.Add(new AttendanceLog { EmployeeId = two, Date = t, ClockIn = At(t, 9, 30), ClockOut = At(t, 17, 30), WorkMode = WorkMode.Remote, OutsideZone = true });
            db.OvertimeRequests.Add(new OvertimeRequest { EmployeeId = one, Date = t, Hours = 2.5m, Reason = "Closing", Status = RequestStatus.Approved, CreatedAt = clock.UtcNow });
            db.OvertimeRequests.Add(new OvertimeRequest { EmployeeId = two, Date = t, Hours = 1m, Reason = "Support", Status = RequestStatus.Pending, CreatedAt = clock.UtcNow });
            // leave: Three is away today (approved, 2 days), One asked for a day next week, Two was refused
            var annual = await db.LeaveTypes.FirstAsync(x => x.Code == "ANNUAL"); var sick = await db.LeaveTypes.FirstAsync(x => x.Code == "SICK");
            db.LeaveRequests.Add(new LeaveRequest { EmployeeId = three, LeaveTypeId = annual.Id, StartDate = t, EndDate = t.AddDays(1), Days = 2, Status = LeaveStatus.Approved, CreatedAt = clock.UtcNow.AddHours(-10), DecidedAt = clock.UtcNow.AddHours(-4) });
            db.LeaveRequests.Add(new LeaveRequest { EmployeeId = one, LeaveTypeId = annual.Id, StartDate = t.AddDays(7), EndDate = t.AddDays(7), Days = 1, Status = LeaveStatus.Pending, CreatedAt = clock.UtcNow });
            db.LeaveRequests.Add(new LeaveRequest { EmployeeId = two, LeaveTypeId = sick.Id, StartDate = t.AddDays(-3), EndDate = t.AddDays(-3), Days = 1, Status = LeaveStatus.Rejected, CreatedAt = clock.UtcNow.AddHours(-30), DecidedAt = clock.UtcNow.AddHours(-28) });
            // claims: one waiting, one approved and not yet paid, one paid, one refused
            db.ExpenseClaims.Add(new ExpenseClaim { EmployeeId = one, Category = "transport", ExpenseDate = t, Amount = 100_000, Description = "Taxi", Status = ClaimStatus.Pending, CreatedAt = clock.UtcNow });
            db.ExpenseClaims.Add(new ExpenseClaim { EmployeeId = one, Category = "transport", ExpenseDate = t, Amount = 250_000, Description = "Train", Status = ClaimStatus.Approved, CreatedAt = clock.UtcNow.AddHours(-6), DecidedAt = clock.UtcNow.AddHours(-2) });
            db.ExpenseClaims.Add(new ExpenseClaim { EmployeeId = three, Category = "meals", ExpenseDate = t, Amount = 400_000, Description = "Client lunch", Status = ClaimStatus.Paid, CreatedAt = clock.UtcNow.AddHours(-8), DecidedAt = clock.UtcNow.AddHours(-6) });
            db.ExpenseClaims.Add(new ExpenseClaim { EmployeeId = two, Category = "other", ExpenseDate = t, Amount = 999_000, Description = "No receipt", Status = ClaimStatus.Rejected, CreatedAt = clock.UtcNow.AddHours(-4), DecidedAt = clock.UtcNow.AddHours(-2) });
            db.Loans.Add(new Loan { EmployeeId = one, Principal = 3_000_000, Installment = 500_000, Remaining = 2_000_000, Status = LoanStatus.Active, CreatedAt = clock.UtcNow });
            return await db.SaveChangesAsync();
        });

        // ---- attendance
        var a = await (await hr.Get("/api/hr/analytics/attendance")).Body();
        Assert.Equal(2, a.GetProperty("presentToday").GetInt32()); Assert.Equal(1, a.GetProperty("lateToday").GetInt32());
        Assert.Equal(workday ? 3 : 0, a.GetProperty("scheduledToday").GetInt32()); Assert.Equal(workday ? 1 : 0, a.GetProperty("onLeaveToday").GetInt32()); Assert.Equal(0, a.GetProperty("notInYetToday").GetInt32());
        Assert.Equal(50m, a.GetProperty("onTimePct").GetDecimal()); Assert.Equal(8m, a.GetProperty("avgHours").GetDecimal());
        Assert.Equal(1, a.GetProperty("outsideZone").GetInt32()); Assert.Equal(1, a.GetProperty("openSessions").GetInt32()); Assert.Equal(1, a.GetProperty("pendingOvertime").GetInt32());
        Assert.Equal(30, a.GetProperty("days").GetArrayLength());
        var lastDay = a.GetProperty("days")[29]; Assert.Equal(D(t), lastDay.GetProperty("date").GetString()); Assert.Equal(2, lastDay.GetProperty("present").GetInt32()); Assert.Equal(1, lastDay.GetProperty("late").GetInt32());
        var mode = ByKey(a.GetProperty("byMode")); Assert.Equal(1, mode["office"].GetProperty("count").GetInt32()); Assert.Equal(1, mode["remote"].GetProperty("count").GetInt32());
        Assert.Equal(6, a.GetProperty("overtimeHours").GetArrayLength()); Assert.Equal(2.5m, a.GetProperty("overtimeHours")[5].GetProperty("value").GetDecimal()); // only approved overtime counts
        Assert.Equal(1, ByKey(a.GetProperty("lateByDepartment"))["Alpha"].GetProperty("count").GetInt32());

        // ---- leave
        var l = await (await hr.Get("/api/hr/analytics/leave")).Body();
        Assert.Equal(1, l.GetProperty("onLeaveToday").GetInt32()); Assert.Equal(1, l.GetProperty("pending").GetInt32()); Assert.Equal(2m, l.GetProperty("daysThisYear").GetDecimal());
        Assert.Equal(50m, l.GetProperty("approvalRatePct").GetDecimal()); Assert.Equal(4m, l.GetProperty("avgDecisionHours").GetDecimal()); // (6 h + 2 h) / 2
        Assert.Equal(12, l.GetProperty("daysByMonth").GetArrayLength()); Assert.Equal(2m, l.GetProperty("daysByMonth")[11].GetProperty("value").GetDecimal());
        Assert.Equal(2m, ByKey(l.GetProperty("byType"))["Annual leave"].GetProperty("amount").GetDecimal()); Assert.Equal(2m, ByKey(l.GetProperty("byDepartment"))["Beta"].GetProperty("amount").GetDecimal());

        // ---- claims
        var c = await (await hr.Get("/api/hr/analytics/claims")).Body();
        Assert.Equal(1, c.GetProperty("pending").GetInt32()); Assert.Equal(100_000m, c.GetProperty("pendingAmount").GetDecimal()); Assert.Equal(250_000m, c.GetProperty("approvedUnpaidAmount").GetDecimal());
        Assert.Equal(400_000m, c.GetProperty("paidThisYear").GetDecimal()); Assert.Equal(66.7m, c.GetProperty("approvalRatePct").GetDecimal());
        var cat = ByKey(c.GetProperty("byCategory")); Assert.Equal(400_000m, cat["meals"].GetProperty("amount").GetDecimal()); Assert.Equal(250_000m, cat["transport"].GetProperty("amount").GetDecimal()); Assert.False(cat.ContainsKey("other")); // a refused claim is not spending
        Assert.Equal(650_000m, c.GetProperty("amountByMonth")[11].GetProperty("value").GetDecimal());

        // ---- payroll: only locked runs count, and the figures need the recent password check
        await seed.Db(async db =>
        {
            var entity = await db.LegalEntities.FirstOrDefaultAsync();
            if (entity is null) { entity = new LegalEntity { Name = "Analytics Co", IsDefault = true, CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow }; db.LegalEntities.Add(entity); await db.SaveChangesAsync(); }
            var prevMonth = new DateOnly(t.Year, t.Month, 1).AddMonths(-1);
            var locked = new PayRun { LegalEntityId = entity.Id, Year = t.Year, Month = t.Month, Status = PayRunStatus.Calculated, EmployeeCount = 2, TotalGross = 22_000_000, TotalNet = 19_000_000, TotalTax = 1_000_000, TotalEmployerCost = 24_000_000, CreatedAt = clock.UtcNow };
            var before = new PayRun { LegalEntityId = entity.Id, Year = prevMonth.Year, Month = prevMonth.Month, Status = PayRunStatus.Calculated, EmployeeCount = 2, TotalGross = 20_000_000, TotalNet = 17_500_000, TotalTax = 900_000, TotalEmployerCost = 21_800_000, CreatedAt = clock.UtcNow };
            db.PayRuns.AddRange(locked, before);
            db.PayRunItems.Add(new PayRunItem { PayRunId = locked.Id, EmployeeId = one, EmployeeNo = "E1", EmployeeName = "One", Basic = 10_000_000, Allowance = 1_000_000, OvertimePay = 500_000, Gross = 11_500_000, Net = 10_000_000, Tax = 500_000, EmployerCost = 12_500_000, KesEmployer = 400_000 });
            db.PayRunItems.Add(new PayRunItem { PayRunId = locked.Id, EmployeeId = three, EmployeeNo = "E3", EmployeeName = "Three", Basic = 10_000_000, Allowance = 500_000, Gross = 10_500_000, Net = 9_000_000, Tax = 500_000, EmployerCost = 11_500_000, KesEmployer = 400_000 });
            await db.SaveChangesAsync();
            locked.Status = PayRunStatus.Locked; before.Status = PayRunStatus.Paid;
            return await db.SaveChangesAsync();
        });
        var pay = await As(f, seed, "payroll_officer", reauth: true);
        var p = await (await pay.Get("/api/hr/analytics/payroll")).Body();
        Assert.Equal($"{t.Year:0000}-{t.Month:00}", p.GetProperty("latestPeriod").GetString()); Assert.Equal("locked", p.GetProperty("latestStatus").GetString());
        Assert.Equal(22_000_000m, p.GetProperty("latestGross").GetDecimal()); Assert.Equal(24_000_000m, p.GetProperty("latestEmployerCost").GetDecimal()); Assert.Equal(10m, p.GetProperty("changePct").GetDecimal());
        Assert.Equal(2_000_000m, p.GetProperty("loansOutstanding").GetDecimal()); Assert.Equal(1, p.GetProperty("activeLoans").GetInt32());
        Assert.Equal(12, p.GetProperty("months").GetArrayLength()); Assert.Equal(20_000_000m, p.GetProperty("months")[10].GetProperty("gross").GetDecimal()); Assert.Equal(22_000_000m, p.GetProperty("months")[11].GetProperty("gross").GetDecimal());
        var cost = ByKey(p.GetProperty("costByDepartment")); Assert.Equal(12_500_000m, cost["Alpha"].GetProperty("amount").GetDecimal()); Assert.Equal(11_500_000m, cost["Beta"].GetProperty("amount").GetDecimal());
        var comp = ByKey(p.GetProperty("composition")); Assert.Equal(20_000_000m, comp["basic"].GetProperty("amount").GetDecimal()); Assert.Equal(500_000m, comp["overtime"].GetProperty("amount").GetDecimal()); Assert.Equal(800_000m, comp["employer_bpjs"].GetProperty("amount").GetDecimal());

        // ---- who may look
        var plain = await As(f, seed, "user");
        foreach (var path in new[] { "attendance", "leave", "claims", "payroll" }) Assert.Equal(HttpStatusCode.Forbidden, (await plain.Get($"/api/hr/analytics/{path}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pay.Get("/api/hr/analytics/attendance")).StatusCode); // a payroll officer is not shown attendance
    }

    [Fact]
    public async Task Analytics_of_an_empty_organisation_are_zeros_not_errors()
    {
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock); var hr = await As(f, seed, "hr_admin", reauth: true);
        var a = await (await hr.Get("/api/hr/analytics/attendance")).Body(); Assert.Equal(0, a.GetProperty("presentToday").GetInt32()); Assert.Equal(0m, a.GetProperty("onTimePct").GetDecimal()); Assert.Equal(30, a.GetProperty("days").GetArrayLength());
        var l = await (await hr.Get("/api/hr/analytics/leave")).Body(); Assert.Equal(0m, l.GetProperty("daysThisYear").GetDecimal()); Assert.Equal(0, l.GetProperty("byType").GetArrayLength());
        var c = await (await hr.Get("/api/hr/analytics/claims")).Body(); Assert.Equal(0m, c.GetProperty("pendingAmount").GetDecimal()); Assert.Equal(12, c.GetProperty("amountByMonth").GetArrayLength());
        var p = await (await (await As(f, seed, "payroll_officer", reauth: true)).Get("/api/hr/analytics/payroll")).Body();
        Assert.Equal(JsonValueKind.Null, p.GetProperty("latestPeriod").ValueKind); Assert.Equal(JsonValueKind.Null, p.GetProperty("changePct").ValueKind); Assert.Equal(0m, p.GetProperty("latestGross").GetDecimal());
    }
}
