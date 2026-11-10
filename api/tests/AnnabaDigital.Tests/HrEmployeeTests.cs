using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class HrEmployeeTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);

    private async Task<TestBrowser> As(string role, bool reauth = false)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return b;
    }

    private static object NewEmployee(string name, object? extra = null, Guid? manager = null, Guid? unit = null, string? nationalId = null) => new
    {
        fullName = name, hireDate = "2024-03-01", jobTitle = "Engineer", type = "permanent", managerId = manager, orgUnitId = unit, nationalId, workEmail = (string?)null,
    };

    private static async Task<Guid> CreateAsync(TestBrowser b, string name, Guid? manager = null, Guid? unit = null)
    {
        var r = await b.Post("/api/hr/employees", NewEmployee(name, null, manager, unit));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Body()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Plain_users_and_read_only_roles_cannot_use_the_write_endpoints()
    {
        var user = await As("user");
        Assert.Equal(HttpStatusCode.Forbidden, (await user.Get("/api/hr/employees")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.Post("/api/hr/employees", NewEmployee("Nope Person"))).StatusCode);

        var head = await As("hr_head");
        Assert.Equal(HttpStatusCode.OK, (await head.Get("/api/hr/employees")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await head.Post("/api/hr/employees", NewEmployee("Nope Person"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await head.Post("/api/hr/org-units", new { code = "X1", name = "X" })).StatusCode);
    }

    [Fact]
    public async Task Create_assigns_a_number_and_get_and_list_return_the_current_job()
    {
        var hr = await As("hr_admin");
        var unit = await hr.Post("/api/hr/org-units", new { code = "ENG" + Guid.NewGuid().ToString("N")[..4], name = "Engineering" });
        var unitId = (await unit.Body()).GetProperty("id").GetGuid();
        var id = await CreateAsync(hr, "Siti Rahma", unit: unitId);

        var d = await (await hr.Get($"/api/hr/employees/{id}")).Body();
        Assert.StartsWith("EMP-", d.GetProperty("employeeNo").GetString());
        Assert.Equal("Engineer", d.GetProperty("current").GetProperty("jobTitle").GetString());
        Assert.Equal("Engineering", d.GetProperty("current").GetProperty("orgUnit").GetString());
        Assert.Equal(1, d.GetProperty("history").GetArrayLength());

        var list = await (await hr.Get("/api/hr/employees?q=siti%20rahma")).Body();
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal("Engineering", list.GetProperty("data")[0].GetProperty("orgUnit").GetString());
    }

    [Fact]
    public async Task Validation_errors_are_reported_per_field()
    {
        var hr = await As("hr_admin");
        var r = await hr.Post("/api/hr/employees", new { fullName = "A", jobTitle = "", type = "contract", workEmail = "nope" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var f = (await r.Body()).GetProperty("error").GetProperty("fields");
        foreach (var k in new[] { "fullName", "hireDate", "jobTitle", "workEmail" }) Assert.True(f.TryGetProperty(k, out _), k);
    }

    [Fact]
    public async Task Sensitive_fields_need_the_permission_and_a_recent_password_check_and_are_encrypted_at_rest()
    {
        var hr = await As("hr_admin"); // signed in but has not re-entered the password
        var denied = await hr.Post("/api/hr/employees", NewEmployee("Budi Santoso", nationalId: "3174010101900001"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Equal("reauth_required", await denied.ErrorCode());

        await hr.Post("/api/auth/reauth", new { password = Seed.Password });
        var ok = await hr.Post("/api/hr/employees", NewEmployee("Budi Santoso", nationalId: "3174010101900001"));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var created = await ok.Body(); var id = created.GetProperty("id").GetGuid();
        Assert.Equal("3174010101900001", created.GetProperty("sensitive").GetProperty("nationalId").GetString());

        var stored = await _seed.Db(db => db.Employees.Where(e => e.Id == id).Select(e => e.NationalIdEnc).SingleAsync());
        Assert.NotNull(stored); Assert.DoesNotContain("3174010101900001", stored);

        // a reader without employees.read_sensitive learns only that a value exists
        var head = await As("hr_head", reauth: true);
        var seen = (await (await head.Get($"/api/hr/employees/{id}")).Body()).GetProperty("sensitive");
        Assert.False(seen.GetProperty("visible").GetBoolean()); Assert.True(seen.GetProperty("hasNationalId").GetBoolean());
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("nationalId").ValueKind);

        // an HR admin whose recent check has lapsed gets the same locked view
        var stale = await As("hr_admin");
        Assert.False((await (await stale.Get($"/api/hr/employees/{id}")).Body()).GetProperty("sensitive").GetProperty("visible").GetBoolean());

        Assert.True(await _seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "hr.employee.sensitive_viewed" && a.TargetId == id.ToString())));
        var trail = (await _seed.Db(db => db.AuditEvents.Select(a => new { a.MetaJson, a.DiffJson }).ToListAsync())).Select(a => a.MetaJson + a.DiffJson).ToList();
        Assert.DoesNotContain(trail, t => t.Contains("3174010101900001")); // the audit trail never holds the value
    }

    [Fact]
    public async Task Job_changes_are_effective_dated_and_keep_history()
    {
        var hr = await As("hr_admin"); var id = await CreateAsync(hr, "Dewi Lestari");
        var r = await hr.Post($"/api/hr/employees/{id}/employment", new { effectiveFrom = "2025-01-01", jobTitle = "Senior Engineer", grade = "G5", reason = "Promotion" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var d = await r.Body(); var hist = d.GetProperty("history");
        Assert.Equal(2, hist.GetArrayLength());
        Assert.Equal("Senior Engineer", d.GetProperty("current").GetProperty("jobTitle").GetString());
        Assert.Equal("2024-12-31", hist[1].GetProperty("effectiveTo").GetString()); // the earlier period was closed the day before
        Assert.Equal("Engineer", hist[1].GetProperty("jobTitle").GetString());

        var same = await hr.Post($"/api/hr/employees/{id}/employment", new { effectiveFrom = "2024-06-01", jobTitle = "X" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, same.StatusCode); // cannot rewrite the past
        var none = await hr.Post($"/api/hr/employees/{id}/employment", new { effectiveFrom = "2025-02-01" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, none.StatusCode); // nothing changed
    }

    [Fact]
    public async Task The_reporting_line_cannot_loop_and_a_manager_with_reports_cannot_leave()
    {
        var hr = await As("hr_admin");
        var boss = await CreateAsync(hr, "Boss Person"); var staff = await CreateAsync(hr, "Staff Person", manager: boss);

        var self = await hr.Post($"/api/hr/employees/{boss}/employment", new { jobTitle = "Director", managerId = boss });
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        var loop = await hr.Post($"/api/hr/employees/{boss}/employment", new { jobTitle = "Director", managerId = staff });
        Assert.Equal(HttpStatusCode.Conflict, loop.StatusCode); Assert.Contains("loop", (await loop.Body()).GetProperty("error").GetProperty("message").GetString());

        var leave = await hr.Post($"/api/hr/employees/{boss}/terminate", new { date = "2025-06-30", reason = "Resigned" });
        Assert.Equal(HttpStatusCode.Conflict, leave.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await hr.Post($"/api/hr/employees/{staff}/terminate", new { date = "2025-06-30", reason = "Resigned" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hr.Post($"/api/hr/employees/{boss}/terminate", new { date = "2025-06-30", reason = "Retired" })).StatusCode);
        var again = await hr.Post($"/api/hr/employees/{boss}/terminate", new { date = "2025-07-01", reason = "Again" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Ending_employment_deactivates_the_linked_account_and_signs_it_out()
    {
        var hr = await As("hr_admin");
        var worker = await _seed.UserAsync(role: "user"); var wb = TestBrowser.Create(fx.Factory); await wb.LoginAsync(worker.Email, Seed.Password);
        Assert.Equal(HttpStatusCode.OK, (await wb.Get("/api/auth/me")).StatusCode);
        var created = await hr.Post("/api/hr/employees", new { fullName = "Linked Worker", hireDate = "2024-01-01", jobTitle = "Analyst", userId = worker.Id });
        var id = (await created.Body()).GetProperty("id").GetGuid();
        var dup = await hr.Post("/api/hr/employees", new { fullName = "Second Worker", hireDate = "2024-01-01", jobTitle = "Analyst", userId = worker.Id });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode); // one account per employee

        Assert.Equal(HttpStatusCode.OK, (await hr.Post($"/api/hr/employees/{id}/terminate", new { reason = "Resigned" })).StatusCode);
        Assert.Equal(UserStatus.Deactivated, await _seed.Db(db => db.Users.Where(u => u.Id == worker.Id).Select(u => u.Status).SingleAsync()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await wb.Get("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Departments_have_unique_codes_and_cannot_be_archived_while_staff_belong_to_them()
    {
        var hr = await As("hr_admin"); var code = "FIN" + Guid.NewGuid().ToString("N")[..4];
        var u = await hr.Post("/api/hr/org-units", new { code, name = "Finance " + code });
        var uid = (await u.Body()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await hr.Post("/api/hr/org-units", new { code = code.ToLower(), name = "Other " + code })).StatusCode);

        await CreateAsync(hr, "Finance Person", unit: uid);
        var arch = await hr.SendAsync(HttpMethod.Patch, $"/api/hr/org-units/{uid}", new { archived = true });
        Assert.Equal(HttpStatusCode.Conflict, arch.StatusCode);

        var child = await hr.Post("/api/hr/org-units", new { code = "C" + code, name = "Child " + code, parentId = uid });
        var cid = (await child.Body()).GetProperty("id").GetGuid();
        var cyc = await hr.SendAsync(HttpMethod.Patch, $"/api/hr/org-units/{uid}", new { parentId = cid });
        Assert.Equal(HttpStatusCode.Conflict, cyc.StatusCode);
    }

    [Fact]
    public async Task Concurrent_edits_are_detected_and_the_export_neutralises_spreadsheet_formulas()
    {
        var hr = await As("hr_admin");
        var r = await hr.Post("/api/hr/employees", new { fullName = "=HYPERLINK(1,2) Evil", hireDate = "2024-01-01", jobTitle = "Analyst" });
        var d = await r.Body(); var id = d.GetProperty("id").GetGuid(); var v = d.GetProperty("version").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await hr.SendAsync(HttpMethod.Patch, $"/api/hr/employees/{id}", new { version = v, phone = "0812" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await hr.SendAsync(HttpMethod.Patch, $"/api/hr/employees/{id}", new { version = v, phone = "0813" })).StatusCode);

        var csv = await (await hr.Get("/api/hr/employees/export?q=hyperlink")).Content.ReadAsStringAsync();
        Assert.Contains("'=HYPERLINK", csv); Assert.DoesNotContain(",=HYPERLINK", csv);
        var nobody = await As("hr_admin"); // export also needs its own permission: a role without it is refused
        var limited = await _seed.RoleAsync("hr_no_export", "employees.read");
        var u = await _seed.UserAsync(); await _seed.Db(async db => { db.UserRoles.Add(new UserRole { UserId = u.Id, RoleId = limited.Id, AssignedAt = fx.Clock.UtcNow }); return await db.SaveChangesAsync(); });
        var lb = TestBrowser.Create(fx.Factory); await lb.LoginAsync(u.Email, Seed.Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await lb.Get("/api/hr/employees/export")).StatusCode);
    }

    // ------------------------------------------------------------ bulk import (EMP-006)
    private static string Tag() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
    private async Task<System.Text.Json.JsonElement> Import(TestBrowser b, string csv, bool dry) => await (await b.Post("/api/hr/employees/import", new { csv, dryRun = dry })).Body();

    [Fact]
    public async Task Import_dry_run_checks_everything_and_writes_nothing()
    {
        var hr = await As("hr_admin"); var t = Tag();
        var csv = $"employee_no,full_name,hire_date,job_title\nI{t}1,Ani Wijaya,2023-01-02,Analyst\nI{t}2,Budi Hartono,15/03/2022,Engineer\n";
        var r = await Import(hr, csv, dry: true);
        Assert.Equal(2, r.GetProperty("total").GetInt32()); Assert.Equal(0, r.GetProperty("failed").GetInt32()); Assert.Equal(0, r.GetProperty("created").GetInt32());
        Assert.False(await _seed.Db(db => db.Employees.AnyAsync(e => e.EmployeeNo == $"I{t}1")));
    }

    [Fact]
    public async Task Import_is_all_or_nothing_and_reports_every_problem_with_its_line()
    {
        var hr = await As("hr_admin"); var t = Tag(); await hr.Post("/api/hr/employees", new { employeeNo = $"X{t}", fullName = "Already Here", hireDate = "2023-01-01", jobTitle = "Analyst" });
        var csv = string.Join("\n", "employee_no,full_name,hire_date,job_title,department,manager_no,type,contract_end",
            $"G{t}1,Good Person,2023-01-02,Analyst,,,,",                    // fine
            $"X{t},Duplicate Number,2023-01-02,Analyst,,,,",               // exists already
            $"G{t}3,Bad Date,not-a-date,Analyst,,,,",                      // date
            $"G{t}4,No Dept,2023-01-02,Analyst,NOPE,,,",                   // unknown department
            $"G{t}5,Ghost Boss,2023-01-02,Analyst,,MISSING,,",             // unknown manager
            $"G{t}6,Loop A,2023-01-02,Analyst,,G{t}7,,",                   // A -> B
            $"G{t}7,Loop B,2023-01-02,Analyst,,G{t}6,,",                   // B -> A
            $"G{t}8,Contract No End,2023-01-02,Analyst,,,contract,") + "\n";
        var r = await Import(hr, csv, dry: false);
        Assert.Equal(7, r.GetProperty("failed").GetInt32()); Assert.Equal(0, r.GetProperty("created").GetInt32());
        var bad = r.GetProperty("rows").EnumerateArray().Where(x => x.GetProperty("status").GetString() == "error").ToDictionary(x => x.GetProperty("line").GetInt32(), x => x.GetProperty("error").GetString()!);
        Assert.Contains("already exists", bad[3]); Assert.Contains("not a date", bad[4]); Assert.Contains("does not exist", bad[5]); Assert.Contains("not found", bad[6]);
        Assert.Contains("loop", bad[7]); Assert.Contains("loop", bad[8]); Assert.Contains("end date", bad[9]);
        Assert.False(await _seed.Db(db => db.Employees.AnyAsync(e => e.EmployeeNo == $"G{t}1"))); // the good row was not written either
    }

    [Fact]
    public async Task A_valid_import_creates_everyone_resolves_managers_that_appear_later_and_numbers_the_rest()
    {
        var hr = await As("hr_admin"); var t = Tag(); var unit = $"U{t}";
        await hr.Post("/api/hr/org-units", new { code = unit, name = "Imported Dept " + t });
        // explicit employee numbers make the manager reference deterministic; the last row has no number and is numbered by the system
        string csv;
        csv = string.Join("\n", "employee_no,full_name,hire_date,job_title,department,manager_no,work_email,gender",
            $"S{t},Staff One,2023-02-01,Analyst,{unit},M{t},staff1.{t.ToLower()}@example.com,female",
            $"M{t},\"Boss, The\",2020-01-01,Director,{unit},,,male",
            $",No Number,2024-05-05,Analyst,,M{t},,") + "\n";
        var r = await Import(hr, csv, dry: false);
        Assert.Equal(3, r.GetProperty("created").GetInt32()); Assert.Equal(0, r.GetProperty("failed").GetInt32());
        var staff = await _seed.Db(db => db.Employees.Include(e => e.Employments).SingleAsync(e => e.EmployeeNo == $"S{t}"));
        var boss = await _seed.Db(db => db.Employees.SingleAsync(e => e.EmployeeNo == $"M{t}"));
        Assert.Equal(boss.Id, staff.Employments.Single().ManagerId); Assert.Equal("Boss, The", boss.FullName);
        Assert.StartsWith("EMP-", (await _seed.Db(db => db.Employees.SingleAsync(e => e.FullName == "No Number"))).EmployeeNo);
        Assert.True(await _seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "hr.employee.import")));
        var again = await Import(hr, csv, dry: false); Assert.Equal(0, again.GetProperty("created").GetInt32()); // running the same file twice cannot double-load
    }

    [Fact]
    public async Task Import_needs_the_create_permission_and_protected_columns_need_the_sensitive_permission_and_a_recent_password_check()
    {
        var csv = $"full_name,hire_date,job_title,national_id\nProtected Person,2023-01-02,Analyst,3174010101900002\n";
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As("hr_head")).Post("/api/hr/employees/import", new { csv, dryRun = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As("user")).Post("/api/hr/employees/import", new { csv, dryRun = true })).StatusCode);

        var stale = await As("hr_admin"); // holds the permission but has not re-entered the password
        Assert.Equal("reauth_required", await (await stale.Post("/api/hr/employees/import", new { csv, dryRun = true })).ErrorCode());
        var role = await _seed.RoleAsync("hr_create_only", "employees.read", "employees.create"); var u = await _seed.UserAsync();
        await _seed.Db(async db => { db.UserRoles.Add(new UserRole { UserId = u.Id, RoleId = role.Id, AssignedAt = fx.Clock.UtcNow }); return await db.SaveChangesAsync(); });
        var limited = TestBrowser.Create(fx.Factory); await limited.LoginAsync(u.Email, Seed.Password); await limited.Post("/api/auth/reauth", new { password = Seed.Password });
        Assert.Equal(HttpStatusCode.Forbidden, (await limited.Post("/api/hr/employees/import", new { csv, dryRun = true })).StatusCode);

        var ok = await As("hr_admin", reauth: true); var r = await Import(ok, csv, dry: false);
        Assert.Equal(1, r.GetProperty("created").GetInt32());
        Assert.NotNull(await _seed.Db(db => db.Employees.Where(e => e.FullName == "Protected Person").Select(e => e.NationalIdEnc).SingleAsync()));
    }

    [Fact]
    public async Task The_org_chart_lists_active_people_with_their_manager_and_is_open_to_read_only_hr_but_not_to_staff()
    {
        var hr = await As("hr_admin"); var boss = await CreateAsync(hr, "Chart Boss"); var staff = await CreateAsync(hr, "Chart Staff", manager: boss); var gone = await CreateAsync(hr, "Chart Leaver");
        await hr.Post($"/api/hr/employees/{gone}/terminate", new { reason = "Resigned" });
        var chart = (await (await (await As("hr_head")).Get("/api/hr/employees/org-chart")).Body()).EnumerateArray().ToList();
        Assert.Equal("Chart Boss", chart.Single(x => x.GetProperty("id").GetGuid() == staff).GetProperty("managerName").GetString());
        Assert.DoesNotContain(chart, x => x.GetProperty("id").GetGuid() == gone);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await As("user")).Get("/api/hr/employees/org-chart")).StatusCode);
    }
}
