using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Hr;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Infrastructure.Persistence;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Stage 17: hiring requests and manpower plans, the public career page, the applicant pipeline with interviews, tests and offers, and hiring into an employee record.</summary>
[Collection("db")]
public sealed class RecruitmentTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private DateOnly Today => DateOnly.FromDateTime(fx.Clock.UtcNow.UtcDateTime.AddHours(7));
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();
    private static readonly string Pdf = Convert.ToBase64String("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray());

    private async Task<TestBrowser> Login(string email, bool reauth = false)
    {
        var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return b;
    }
    private sealed record Person(TestBrowser B, Guid EmployeeId, string Name);
    private async Task<Person> Worker(TestBrowser hr, string role = "user", Guid? manager = null)
    {
        var u = await _seed.UserAsync(role: role); var name = "Staff " + Guid.NewGuid().ToString("N")[..6];
        var r = await hr.Post("/api/hr/employees", new { fullName = name, hireDate = D(Today.AddYears(-2)), jobTitle = "Analyst", managerId = manager, userId = u.Id });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return new Person(await Login(u.Email), (await r.Body()).GetProperty("id").GetGuid(), name);
    }
    private async Task<string> TenantSlug()
    {
        using var scope = fx.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AnnabaDigitalDbContext>().Tenants.AsNoTracking().Where(t => t.Id == Tenant.DefaultId).Select(t => t.Slug).SingleAsync();
    }
    /// <summary>A visitor of the public pages: no session, through the web application.</summary>
    private static async Task<HttpResponseMessage> Public(TestBrowser anon, HttpMethod method, string path, object? body = null, string? token = null)
    {
        using var req = new HttpRequestMessage(method, path); if (body is not null) req.Content = JsonContent.Create(body);
        req.Headers.Add("X-Internal-Proxy-Key", ApiFactory.ProxyKey); req.Headers.Add("X-Forwarded-For", anon.Ip); req.Headers.Add("Origin", ApiFactory.Origin);
        if (token is not null) req.Headers.Add("X-Candidate-Token", token);
        return await anon.Http.SendAsync(req);
    }

    [Fact]
    public async Task Managers_ask_to_hire_someone_else_decides_and_manpower_plans_show_the_gap()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true); var hr2 = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true);
        var boss = await Worker(hr, "manager"); await Worker(hr, manager: boss.EmployeeId); var plain = await Worker(hr);
        var title = "Data Engineer " + Guid.NewGuid().ToString("N")[..4];
        var plan = (await (await hr.Post("/api/hr/recruitment/manpower", new { year = Today.Year, jobTitle = title, headcount = 3, monthlyBudget = 45_000_000 })).Body()).EnumerateArray().Single(p => p.GetProperty("jobTitle").GetString() == title);
        Assert.Equal(3, plan.GetProperty("gap").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.B.Post("/api/hr/recruitment/requisitions", new { jobTitle = title, openings = 1, justification = "Need help" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await boss.B.Post("/api/hr/recruitment/requisitions", new { jobTitle = title, openings = 0, justification = "x" })).StatusCode);
        var req = await (await boss.B.Post("/api/hr/recruitment/requisitions", new { jobTitle = title, openings = 2, type = "permanent", salaryMin = 12_000_000, salaryMax = 16_000_000, justification = "New data platform team" })).Body();
        Assert.Matches(@"^REQ/\d{4}/\d{4}$", req.GetProperty("number").GetString()); Assert.Equal("pending", req.GetProperty("status").GetString()); Assert.False(req.GetProperty("overPlan").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await boss.B.Post($"/api/hr/recruitment/requisitions/{Id(req)}/decide", new { approve = true })).StatusCode);
        Assert.Single((await (await boss.B.Get("/api/hr/recruitment/requisitions")).Body()).EnumerateArray(), x => Id(x) == Id(req));
        Assert.Equal(HttpStatusCode.Forbidden, (await boss.B.Get("/api/hr/recruitment/requisitions?scope=all")).StatusCode);
        Assert.Equal(1, (await (await hr.Get("/api/hr/recruitment/manpower")).Body()).EnumerateArray().Single(p => p.GetProperty("jobTitle").GetString() == title).GetProperty("gap").GetInt32());   // 3 planned − 2 requested
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post($"/api/hr/recruitment/requisitions/{Id(req)}/decide", new { approve = false })).StatusCode);   // a rejection needs a reason
        var ok = await (await hr2.Post($"/api/hr/recruitment/requisitions/{Id(req)}/decide", new { approve = true, note = "Budgeted" })).Body();
        Assert.Equal("approved", ok.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await hr.Post($"/api/hr/recruitment/requisitions/{Id(req)}/decide", new { approve = true })).StatusCode);
    }

    [Fact]
    public async Task A_candidate_applies_on_the_career_page_is_interviewed_tested_offered_and_hired()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true); var hr2 = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true);
        var interviewer = await Worker(hr, "manager"); var stranger = await Worker(hr);
        var req = Id(await (await hr.Post("/api/hr/recruitment/requisitions", new { jobTitle = "Support Agent", openings = 1, justification = "Growing customer base" })).Body());
        await hr2.Post($"/api/hr/recruitment/requisitions/{req}/decide", new { approve = true });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/recruitment/postings", new { requisitionId = req, description = "Too short" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/recruitment/postings", new { requisitionId = req, description = "Help our customers by chat and phone every day.", stages = new[] { "hired" } })).StatusCode);
        var posting = await (await hr.Post("/api/hr/recruitment/postings", new { requisitionId = req, location = "Jakarta", description = "Help our customers by chat and phone every day.", requirements = "Good written Indonesian and English.", salaryMin = 6_000_000, salaryMax = 8_000_000, showSalary = true, stages = new[] { "screening", "assessment", "interview", "offer" } })).Body();
        var pid = Id(posting); var publicId = posting.GetProperty("publicId").GetString()!; Assert.Equal("Support Agent", posting.GetProperty("title").GetString());
        var slug = await TenantSlug(); var anon = TestBrowser.Create(fx.Factory);
        Assert.Equal(HttpStatusCode.NotFound, (await Public(anon, HttpMethod.Get, $"/api/careers/{slug}/{publicId}")).StatusCode);   // still a draft
        await hr.Post($"/api/hr/recruitment/postings/{pid}/open");
        var page = await (await Public(anon, HttpMethod.Get, $"/api/careers/{slug}")).Body();
        var job = page.GetProperty("jobs").EnumerateArray().Single(j => j.GetProperty("id").GetString() == publicId);
        Assert.Equal(6_000_000m, job.GetProperty("salaryMin").GetDecimal()); Assert.False(job.TryGetProperty("requisitionId", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await Public(anon, HttpMethod.Get, "/api/careers/no-such-company")).StatusCode);

        var mail = $"cand-{Guid.NewGuid():N}@example.com";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Public(anon, HttpMethod.Post, $"/api/careers/{slug}/{publicId}/apply", new { fullName = "Rina Putri", email = mail, cvName = "cv.pdf", cvBase64 = Pdf })).StatusCode);   // no consent
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Public(anon, HttpMethod.Post, $"/api/careers/{slug}/{publicId}/apply", new { fullName = "Rina Putri", email = mail, cvBase64 = Convert.ToBase64String("not a pdf"u8.ToArray()), consent = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Public(anon, HttpMethod.Post, $"/api/careers/{slug}/{publicId}/apply", new { fullName = "Rina Putri", email = mail, phone = "0812000111", coverLetter = "I love helping people.", cvName = "rina.pdf", cvBase64 = Pdf, consent = true })).StatusCode);
        await _seed.Mail.WaitFor(mail, "We received your application");
        Assert.Equal(HttpStatusCode.Conflict, (await Public(anon, HttpMethod.Post, $"/api/careers/{slug}/{publicId}/apply", new { fullName = "Rina Putri", email = mail.ToUpperInvariant(), cvBase64 = Pdf, consent = true })).StatusCode);

        var app = (await (await hr.Get($"/api/hr/recruitment/postings/{pid}/applications")).Body()).EnumerateArray().Single(); var aid = Id(app);
        Assert.Equal("applied", app.GetProperty("stage").GetString()); Assert.True(app.GetProperty("hasCv").GetBoolean());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.SendAsync(HttpMethod.Patch, $"/api/hr/recruitment/applications/{aid}", new { stage = "hired" })).StatusCode);
        Assert.Equal("screening", (await (await hr.SendAsync(HttpMethod.Patch, $"/api/hr/recruitment/applications/{aid}", new { stage = "screening", rating = 4, note = "Strong writing sample" })).Body()).GetProperty("application").GetProperty("stage").GetString());

        // a test with a pass mark; the candidate opens the private link from the email
        var test = Id(await (await hr.Post("/api/hr/recruitment/assessments", new { name = "Support basics", kind = "test", passScore = 50, timeLimitMinutes = 20, questions = new object[] {
            new { id = "q1", text = "A customer is angry. First you…", options = new object[] { new { text = "Listen and acknowledge", correct = true }, new { text = "Explain the policy" } } },
            new { id = "q2", text = "Reply time target for chat?", options = new object[] { new { text = "1 day" }, new { text = "2 minutes", correct = true } } } } })).Body());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/recruitment/assessments", new { name = "Broken", kind = "test", questions = new object[] { new { text = "No right answer", options = new object[] { new { text = "a" }, new { text = "b" } } } } })).StatusCode);
        var link = (await (await hr.Post($"/api/hr/recruitment/applications/{aid}/tests", new { templateId = test })).Body()).GetProperty("link").GetString()!;
        var tok = link[(link.IndexOf('#') + 1)..]; Assert.Contains(tok, (await _seed.Mail.WaitFor(mail, "Support basics")).Text);
        Assert.Equal(HttpStatusCode.NotFound, (await Public(anon, HttpMethod.Get, "/api/careers/test", token: "forged")).StatusCode);
        Assert.Equal(0, (await (await Public(anon, HttpMethod.Get, "/api/careers/test", token: tok)).Body()).GetProperty("questions").GetArrayLength());   // not started: nothing shown yet
        var started = await (await Public(anon, HttpMethod.Post, "/api/careers/test/start", token: tok)).Body();
        Assert.Equal(2, started.GetProperty("questions").GetArrayLength()); Assert.DoesNotContain("correct", started.GetRawText());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Public(anon, HttpMethod.Post, "/api/careers/test/submit", new { answers = new Dictionary<string, int> { ["q1"] = 0 } }, tok)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Public(anon, HttpMethod.Post, "/api/careers/test/submit", new { answers = new Dictionary<string, int> { ["q1"] = 0, ["q2"] = 0 } }, tok)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Public(anon, HttpMethod.Post, "/api/careers/test/submit", new { answers = new Dictionary<string, int> { ["q1"] = 0, ["q2"] = 1 } }, tok)).StatusCode);   // once only
        var detail = await (await hr.Get($"/api/hr/recruitment/applications/{aid}")).Body();
        Assert.Equal(50, detail.GetProperty("tests")[0].GetProperty("score").GetInt32()); Assert.Equal("50%", detail.GetProperty("application").GetProperty("testSummary").GetString());

        // interview: the interviewer is notified, may open the CV, and gives feedback; others may not
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post($"/api/hr/recruitment/applications/{aid}/interviews", new { at = fx.Clock.UtcNow.AddHours(-1), interviewerIds = new[] { interviewer.EmployeeId } })).StatusCode);
        var iv = await (await hr.Post($"/api/hr/recruitment/applications/{aid}/interviews", new { at = fx.Clock.UtcNow.AddDays(2), durationMinutes = 45, location = "Meeting room 2", interviewerIds = new[] { interviewer.EmployeeId } })).Body();
        await _seed.Mail.WaitFor(mail, "Interview invitation");
        var mine = (await (await interviewer.B.Get("/api/hr/recruitment/interviews/mine")).Body()).EnumerateArray().Single(x => Id(x) == Id(iv));
        Assert.Equal("Rina Putri", mine.GetProperty("candidate").GetString());
        Assert.Equal(HttpStatusCode.OK, (await interviewer.B.Get($"/api/hr/recruitment/applications/{aid}/cv")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.B.Get($"/api/hr/recruitment/applications/{aid}/cv")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.B.Get($"/api/hr/recruitment/applications/{aid}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.B.Post($"/api/hr/recruitment/interviews/{Id(iv)}/feedback", new { rating = 1, recommendation = "no_hire" })).StatusCode);
        Assert.Equal("done", (await (await interviewer.B.Post($"/api/hr/recruitment/interviews/{Id(iv)}/feedback", new { rating = 5, recommendation = "hire", feedback = "Calm and clear" })).Body()).GetProperty("status").GetString());

        // offer → the candidate accepts on the private link → hire
        Assert.Equal(HttpStatusCode.Conflict, (await hr.Post($"/api/hr/recruitment/applications/{aid}/hire", new { })).StatusCode);   // no accepted offer yet
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post($"/api/hr/recruitment/applications/{aid}/offers", new { monthlySalary = 7_000_000, startDate = D(Today.AddDays(30)), expiresOn = D(Today.AddDays(40)) })).StatusCode);
        var offer = await (await hr.Post($"/api/hr/recruitment/applications/{aid}/offers", new { monthlySalary = 7_000_000, startDate = D(Today.AddDays(30)), expiresOn = D(Today.AddDays(7)), terms = "Probation of three months applies." })).Body();
        Assert.Equal(HttpStatusCode.OK, (await hr.Get($"/api/hr/recruitment/offers/{Id(offer)}/pdf")).StatusCode);
        var olink = (await (await hr.Post($"/api/hr/recruitment/offers/{Id(offer)}/send")).Body()).GetProperty("link").GetString()!; var otok = olink[(olink.IndexOf('#') + 1)..];
        await _seed.Mail.WaitFor(mail, "Job offer");
        var view = await (await Public(anon, HttpMethod.Get, "/api/careers/offer", token: otok)).Body();
        Assert.Equal(7_000_000m, view.GetProperty("monthlySalary").GetDecimal()); Assert.Equal("sent", view.GetProperty("status").GetString());
        Assert.Equal("accepted", (await (await Public(anon, HttpMethod.Post, "/api/careers/offer/respond", new { accept = true }, otok)).Body()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Public(anon, HttpMethod.Post, "/api/careers/offer/respond", new { accept = false }, otok)).StatusCode);
        var hired = await (await hr.Post($"/api/hr/recruitment/applications/{aid}/hire", new { })).Body();
        Assert.Equal("hired", hired.GetProperty("application").GetProperty("status").GetString());
        var empId = hired.GetProperty("hiredEmployeeId").GetGuid();
        var emp = await (await hr.Get($"/api/hr/employees/{empId}")).Body();
        Assert.Equal("Rina Putri", emp.GetProperty("fullName").GetString()); Assert.Equal(D(Today.AddDays(30)), emp.GetProperty("hireDate").GetString());
        Assert.True((await (await hr.Get($"/api/hr/employees/{empId}/checklist")).Body()).GetArrayLength() > 0);   // the onboarding checklist came with it
        Assert.Equal("filled", (await (await hr.Get("/api/hr/recruitment/requisitions?scope=all")).Body()).EnumerateArray().Single(x => Id(x) == req).GetProperty("status").GetString());
        Assert.Equal("closed", (await (await hr.Get("/api/hr/recruitment/postings")).Body()).EnumerateArray().Single(x => Id(x) == pid).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await hr.SendAsync(HttpMethod.Delete, $"/api/hr/recruitment/candidates/{app.GetProperty("candidateId").GetGuid()}")).StatusCode);   // hired: lives on as an employee
        var summary = await hr.Get("/api/hr/recruitment/summary"); Assert.True(summary.IsSuccessStatusCode, await summary.Content.ReadAsStringAsync());
        Assert.True((await summary.Body()).GetProperty("hiredThisYear").GetInt32() >= 1);
    }

    [Fact]
    public async Task HR_adds_a_referral_rejects_with_a_kind_email_and_can_erase_the_candidate()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true);
        var pid = Id(await (await hr.Post("/api/hr/recruitment/postings", new { title = "Accountant", description = "Keep our books in order, monthly closing and tax filings." })).Body());
        var mail = $"ref-{Guid.NewGuid():N}@example.com";
        var row = await (await hr.Post($"/api/hr/recruitment/postings/{pid}/applications", new { fullName = "Budi Santoso", email = mail, source = "referral" })).Body();
        Assert.Equal("referral", row.GetProperty("source").GetString());
        var rejected = await (await hr.SendAsync(HttpMethod.Patch, $"/api/hr/recruitment/applications/{Id(row)}", new { reject = true, rejectReason = "Needs more tax experience", notifyCandidate = true })).Body();
        Assert.Equal("rejected", rejected.GetProperty("application").GetProperty("status").GetString());
        Assert.DoesNotContain("tax experience", (await _seed.Mail.WaitFor(mail, "Your application")).Text);   // the internal reason stays internal
        Assert.Equal(HttpStatusCode.Conflict, (await hr.SendAsync(HttpMethod.Patch, $"/api/hr/recruitment/applications/{Id(row)}", new { stage = "screening" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await hr.SendAsync(HttpMethod.Delete, $"/api/hr/recruitment/candidates/{row.GetProperty("candidateId").GetGuid()}")).StatusCode);
        Assert.Equal(0, (await (await hr.Get($"/api/hr/recruitment/postings/{pid}/applications")).Body()).GetArrayLength());
    }

    [Fact]
    public void The_personality_profile_reports_the_four_leanings()
    {
        AssessmentQuestion Q(string id, string a, string b) => new(id, "Which describes you better?", [new("x", null, a), new("y", null, b)]);
        var qs = new[] { Q("1", "E", "I"), Q("2", "E", "I"), Q("3", "S", "N"), Q("4", "S", "N"), Q("5", "T", "F"), Q("6", "T", "F"), Q("7", "J", "P"), Q("8", "J", "P") };
        var answers = new Dictionary<string, int> { ["1"] = 1, ["2"] = 1, ["3"] = 0, ["4"] = 1, ["5"] = 1, ["6"] = 1, ["7"] = 0, ["8"] = 0 };
        Assert.Equal("ISFJ", RecruitmentService.Score(AssessmentKind.Personality, qs, answers).Result);   // S/N tied: the first pole
        var test = new[] { new AssessmentQuestion("a", "?", [new("right", true, null), new("wrong", null, null)]), new AssessmentQuestion("b", "?", [new("wrong", null, null), new("right", true, null)]), new AssessmentQuestion("c", "?", [new("right", true, null), new("wrong", null, null)]) };
        Assert.Equal(67, RecruitmentService.Score(AssessmentKind.Test, test, new Dictionary<string, int> { ["a"] = 0, ["b"] = 1, ["c"] = 1 }).Score);
    }
}
