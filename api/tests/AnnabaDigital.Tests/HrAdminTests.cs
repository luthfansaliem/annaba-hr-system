using System.Net;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Stage 12: multi-step approval of overtime, custom fields, letter templates, forms and surveys, helpdesk, calendar, directory and branches.</summary>
[Collection("db")]
public sealed class HrAdminTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private DateOnly Today => DateOnly.FromDateTime(fx.Clock.UtcNow.UtcDateTime.AddHours(7));
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    private async Task<TestBrowser> Login(string email, bool reauth = false)
    {
        var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return b;
    }
    private sealed record Person(TestBrowser B, Guid EmployeeId, string Name);
    private async Task<Person> Worker(TestBrowser hr, string role = "user", Guid? manager = null, object? extra = null)
    {
        var u = await _seed.UserAsync(role: role); var name = "Staff " + Guid.NewGuid().ToString("N")[..6];
        var body = new Dictionary<string, object?> { ["fullName"] = name, ["hireDate"] = D(Today.AddYears(-2)), ["jobTitle"] = "Analyst", ["managerId"] = manager, ["userId"] = u.Id };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        var r = await hr.Post("/api/hr/employees", body); Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return new Person(await Login(u.Email), (await r.Body()).GetProperty("id").GetGuid(), name);
    }

    [Fact]
    public async Task Overtime_can_need_the_managers_manager_and_HR_after_the_manager()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true);
        var big = await Worker(hr, "manager"); var boss = await Worker(hr, "manager", big.EmployeeId); var w = await Worker(hr, manager: boss.EmployeeId);
        var rule = await hr.Post("/api/hr/approval-rules", new { kind = "overtime", minAmount = 3, steps = new[] { "manager", "manager2" } });
        Assert.Equal(HttpStatusCode.Created, rule.StatusCode);
        var day = Today.AddDays(1);
        var ot = await (await w.B.Post("/api/hr/attendance/overtime", new { date = D(day), hours = 3, reason = "Month end" })).Body(); var id = ot.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await big.B.Post($"/api/hr/attendance/overtime/{id}/approve")).StatusCode);   // not their step yet
        var step1 = await (await boss.B.Post($"/api/hr/attendance/overtime/{id}/approve")).Body();
        Assert.Equal("pending", step1.GetProperty("status").GetString()); Assert.Equal("manager2", step1.GetProperty("waitingFor").GetString());
        var done = await (await big.B.Post($"/api/hr/attendance/overtime/{id}/approve")).Body();
        Assert.Equal("approved", done.GetProperty("status").GetString());
        // two hours stays under the 3-hour rule: the direct manager alone decides
        var small = await (await w.B.Post("/api/hr/attendance/overtime", new { date = D(day.AddDays(1)), hours = 2, reason = "Fix" })).Body();
        Assert.Equal("approved", (await (await boss.B.Post($"/api/hr/attendance/overtime/{small.GetProperty("id").GetGuid()}/approve")).Body()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Custom_fields_are_typed_and_employees_edit_only_what_they_may()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true); var w = await Worker(hr);
        Assert.Equal(HttpStatusCode.Created, (await hr.Post("/api/hr/custom-fields", new { key = "shirt_size", label = "Shirt size", type = "choice", options = new[] { "S", "M", "L" }, visibleToEmployee = true, editableByEmployee = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await hr.Post("/api/hr/custom-fields", new { key = "licence_no", label = "Driving licence", type = "text", visibleToEmployee = true })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/custom-fields", new { key = "Bad Key", label = "x", type = "text" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/custom-fields", new { key = "colour", label = "x", type = "choice", options = new[] { "one" } })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.SendAsync(HttpMethod.Put, $"/api/hr/employees/{w.EmployeeId}/custom-fields", new Dictionary<string, string?> { ["shirt_size"] = "XXL" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hr.SendAsync(HttpMethod.Put, $"/api/hr/employees/{w.EmployeeId}/custom-fields", new Dictionary<string, string?> { ["licence_no"] = "B1-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await w.B.SendAsync(HttpMethod.Put, "/api/hr/me/custom-fields", new Dictionary<string, string?> { ["licence_no"] = "hacked" })).StatusCode);   // HR-only
        var mine = await (await w.B.SendAsync(HttpMethod.Put, "/api/hr/me/custom-fields", new Dictionary<string, string?> { ["shirt_size"] = "M" })).Body();
        Assert.Equal("M", mine.EnumerateArray().Single(f => f.GetProperty("key").GetString() == "shirt_size").GetProperty("value").GetString());
        Assert.Equal("B1-123", mine.EnumerateArray().Single(f => f.GetProperty("key").GetString() == "licence_no").GetProperty("value").GetString());
    }

    [Fact]
    public async Task Letters_are_rendered_from_a_template_numbered_per_prefix_and_shown_to_the_employee()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true); var w = await Worker(hr);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/letter-templates", new { name = "Bad", prefix = "SP", title = "x", body = "Hello {{salary}}" })).StatusCode);   // unknown placeholder
        var t = await (await hr.Post("/api/hr/letter-templates", new { name = "Assignment", prefix = "st", title = "Surat Tugas {{name}}", body = "Dengan ini {{name}} ({{employee_no}}), {{job_title}}, ditugaskan.\nNomor {{number}}." })).Body();
        Assert.Equal("ST", t.GetProperty("prefix").GetString()); var tid = t.GetProperty("id").GetGuid();
        var first = await (await hr.Post("/api/hr/issued-letters", new { templateId = tid, employeeId = w.EmployeeId })).Body();
        var second = await (await hr.Post("/api/hr/issued-letters", new { templateId = tid, employeeId = w.EmployeeId })).Body();
        Assert.Equal($"ST/{Today.Year}/0001", first.GetProperty("number").GetString()); Assert.Equal($"ST/{Today.Year}/0002", second.GetProperty("number").GetString());
        Assert.Contains(w.Name, first.GetProperty("body").GetString()); Assert.Contains("Analyst", first.GetProperty("body").GetString()); Assert.Contains($"ST/{Today.Year}/0001", first.GetProperty("body").GetString());
        var mine = await (await w.B.Get("/api/hr/me/issued-letters")).Body(); Assert.Equal(2, mine.GetArrayLength());
        var pdf = await w.B.Get($"/api/hr/issued-letters/{first.GetProperty("id").GetGuid()}/pdf"); Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        var text = System.Text.Encoding.Latin1.GetString(await pdf.Content.ReadAsByteArrayAsync()); Assert.Contains("Verification code", text); Assert.Contains("Surat Tugas", text);
        var other = await Worker(hr); Assert.Equal(HttpStatusCode.NotFound, (await other.B.Get($"/api/hr/issued-letters/{first.GetProperty("id").GetGuid()}/pdf")).StatusCode);
    }

    [Fact]
    public async Task A_survey_is_answered_once_per_person_and_anonymous_results_carry_no_names()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true); var a = await Worker(hr); var b = await Worker(hr);
        var form = await (await hr.Post("/api/hr/forms", new { title = "Pulse", anonymous = true, questions = new object[] {
            new { id = "q1", type = "rating", label = "How was your week?", required = true }, new { id = "q2", type = "choice", label = "Office or home?", options = new[] { "Office", "Home" }, required = true },
            new { id = "q3", type = "text", label = "Anything else?", required = false } } })).Body();
        var id = form.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await a.B.Post($"/api/hr/me/forms/{id}", new { answers = new { q1 = 4, q2 = "Office" } })).StatusCode);   // still a draft
        Assert.Equal(HttpStatusCode.OK, (await hr.Post($"/api/hr/forms/{id}/open")).StatusCode);
        Assert.Contains((await (await a.B.Get("/api/hr/me/forms")).Body()).EnumerateArray(), f => f.GetProperty("id").GetGuid() == id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await a.B.Post($"/api/hr/me/forms/{id}", new { answers = new { q1 = 9, q2 = "Office" } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.B.Post($"/api/hr/me/forms/{id}", new { answers = new { q1 = 4, q2 = "Office", q3 = "Coffee machine" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.B.Post($"/api/hr/me/forms/{id}", new { answers = new { q1 = 5, q2 = "Home" } })).StatusCode);   // once only
        Assert.Equal(HttpStatusCode.OK, (await b.B.Post($"/api/hr/me/forms/{id}", new { answers = new { q1 = 2, q2 = "Home" } })).StatusCode);
        var res = await (await hr.Get($"/api/hr/forms/{id}/results")).Body();
        var qs = res.GetProperty("questions").EnumerateArray().ToList();
        Assert.Equal(3m, qs[0].GetProperty("average").GetDecimal());
        Assert.Equal("Coffee machine", qs[2].GetProperty("texts")[0].GetString());                     // anonymous: the text without a name
        Assert.DoesNotContain(a.Name, res.GetRawText());
        Assert.Equal(HttpStatusCode.Forbidden, (await a.B.Get($"/api/hr/forms/{id}/results")).StatusCode);
    }

    [Fact]
    public async Task Helpdesk_questions_are_private_to_their_author_and_internal_notes_stay_with_HR()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true); var w = await Worker(hr); var other = await Worker(hr);
        var t = await (await w.B.Post("/api/hr/helpdesk", new { category = "payroll", subject = "Missing overtime", body = "My overtime on Monday is not on the payslip." })).Body();
        var id = t.GetProperty("id").GetGuid(); Assert.Equal("open", t.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await other.B.Get($"/api/hr/helpdesk/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.B.Get("/api/hr/helpdesk?scope=all")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hr.Post($"/api/hr/helpdesk/{id}/messages", new { body = "Check the approval date", @internal = true })).StatusCode);
        var answered = await (await hr.Post($"/api/hr/helpdesk/{id}/messages", new { body = "It was approved after the cut-off; it will be in next month's pay." })).Body();
        Assert.Equal("in_progress", answered.GetProperty("ticket").GetProperty("status").GetString());
        var seen = await (await w.B.Get($"/api/hr/helpdesk/{id}")).Body();
        Assert.Equal(2, seen.GetProperty("messages").GetArrayLength()); Assert.DoesNotContain("approval date", seen.GetRawText());   // the internal note is not shown
        Assert.Equal(HttpStatusCode.Forbidden, (await w.B.Post($"/api/hr/helpdesk/{id}/status", new { status = "resolved" })).StatusCode);   // employees can only close
        Assert.Equal("closed", (await (await w.B.Post($"/api/hr/helpdesk/{id}/status", new { status = "closed" })).Body()).GetProperty("ticket").GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_calendar_shows_events_holidays_birthdays_and_the_directory_finds_colleagues_by_branch()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true);
        var branch = await (await hr.Post("/api/hr/branches", new { code = "sby", name = "Surabaya", kind = "branch" })).Body();
        Assert.Equal("SBY", branch.GetProperty("code").GetString());
        var w = await Worker(hr, extra: new { birthDate = D(new DateOnly(1990, Today.Month, Math.Min(Today.Day, 28))) });
        Assert.Equal(HttpStatusCode.NoContent, (await hr.SendAsync(HttpMethod.Put, $"/api/hr/employees/{w.EmployeeId}/branch", new { branchId = branch.GetProperty("id").GetGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await hr.Post("/api/hr/calendar/events", new { title = "Town hall", startDate = D(Today) })).StatusCode);
        var cal = (await (await w.B.Get($"/api/hr/calendar?year={Today.Year}&month={Today.Month}")).Body()).EnumerateArray().ToList();
        Assert.Contains(cal, i => i.GetProperty("kind").GetString() == "event" && i.GetProperty("title").GetString() == "Town hall");
        Assert.Contains(cal, i => i.GetProperty("kind").GetString() == "birthday" && i.GetProperty("title").GetString() == w.Name);
        Assert.DoesNotContain("1990", string.Join(" ", cal.Select(i => i.GetRawText())));            // the year of birth is never shown
        var dir = await (await w.B.Get($"/api/hr/directory?branchId={branch.GetProperty("id").GetGuid()}")).Body();
        var hit = dir.GetProperty("data").EnumerateArray().Single(); Assert.Equal(w.Name, hit.GetProperty("fullName").GetString()); Assert.Equal("Surabaya", hit.GetProperty("branch").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await w.B.Post("/api/hr/calendar/events", new { title = "Party", startDate = D(Today) })).StatusCode);
    }
}
