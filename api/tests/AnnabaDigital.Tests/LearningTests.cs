using System.Net;
using System.Text.Json;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Stage 18: building a course with lessons and a quiz, assigning it, taking it to completion and the verifiable certificate.</summary>
[Collection("db")]
public sealed class LearningTests(PostgresFixture fx)
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
        var u = await _seed.UserAsync(role: role); var name = "Learner " + Guid.NewGuid().ToString("N")[..6];
        var r = await hr.Post("/api/hr/employees", new { fullName = name, hireDate = D(Today.AddYears(-1)), jobTitle = "Technician", managerId = manager, userId = u.Id });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return new Person(await Login(u.Email), (await r.Body()).GetProperty("id").GetGuid(), name);
    }

    [Fact]
    public async Task An_assigned_course_is_taken_lesson_by_lesson_passed_and_certified()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true);
        var boss = await Worker(hr, "manager"); var w = await Worker(hr, manager: boss.EmployeeId); var other = await Worker(hr);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/learning/courses", new { title = "K3", category = "safety", quiz = new object[] { new { text = "No answer", options = new object[] { new { text = "a" }, new { text = "b" } } } } })).StatusCode);
        var course = await (await hr.Post("/api/hr/learning/courses", new { title = "Workplace safety (K3) basics", category = "safety", mandatory = true, selfEnroll = false, passScore = 50, validMonths = 12,
            quiz = new object[] { new { id = "q1", text = "Where is the assembly point?", options = new object[] { new { text = "Car park", correct = true }, new { text = "Roof" } } },
                                  new { id = "q2", text = "Fire extinguisher class for electrical fires?", options = new object[] { new { text = "Water" }, new { text = "CO2", correct = true } } } } })).Body();
        var cid = Id(course.GetProperty("course"));
        Assert.Equal(HttpStatusCode.Conflict, (await hr.Post($"/api/hr/learning/courses/{cid}/publish")).StatusCode);   // no lessons yet
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post($"/api/hr/learning/courses/{cid}/lessons", new { title = "Video", kind = "video", url = "http://insecure.example/v" })).StatusCode);
        await hr.Post($"/api/hr/learning/courses/{cid}/lessons", new { title = "Why safety matters", kind = "text", body = "Every accident is preventable.", minutes = 5 });
        await hr.Post($"/api/hr/learning/courses/{cid}/lessons", new { title = "Evacuation video", kind = "video", url = "https://videos.example.com/evac", minutes = 8 });
        var withFile = await (await hr.Post($"/api/hr/learning/courses/{cid}/lessons", new { title = "Safety handbook", kind = "file", fileName = "handbook.pdf", fileBase64 = Pdf, minutes = 10 })).Body();
        var lessons = withFile.GetProperty("lessons").EnumerateArray().Select(Id).ToList(); Assert.Equal(3, lessons.Count);
        Assert.Equal(HttpStatusCode.Conflict, (await hr.Post($"/api/hr/learning/courses/{cid}/assign", new { employeeIds = new[] { w.EmployeeId } })).StatusCode);   // draft
        Assert.Equal("published", (await (await hr.Post($"/api/hr/learning/courses/{cid}/publish")).Body()).GetProperty("course").GetProperty("status").GetString());
        Assert.Equal(1, (await (await hr.Post($"/api/hr/learning/courses/{cid}/assign", new { employeeIds = new[] { w.EmployeeId }, dueDate = D(Today.AddDays(14)) })).Body()).GetProperty("added").GetInt32());
        Assert.Equal(0, (await (await hr.Post($"/api/hr/learning/courses/{cid}/assign", new { employeeIds = new[] { w.EmployeeId } })).Body()).GetProperty("added").GetInt32());   // already enrolled

        // not offered to people it was not assigned to (no self-enrolment)
        Assert.Equal(HttpStatusCode.NotFound, (await other.B.Get($"/api/hr/learning/take/{cid}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.B.Get($"/api/hr/learning/take/{cid}/lessons/{lessons[2]}/file")).StatusCode);
        var mine = await (await w.B.Get("/api/hr/learning/mine")).Body();
        Assert.Contains(mine.GetProperty("mine").EnumerateArray(), c => c.GetProperty("courseId").GetGuid() == cid && c.GetProperty("status").GetString() == "not_started");
        var player = await (await w.B.Get($"/api/hr/learning/take/{cid}")).Body();
        Assert.False(player.GetProperty("quizOpen").GetBoolean()); Assert.DoesNotContain("correct", player.GetProperty("quiz").GetRawText());
        Assert.Equal(HttpStatusCode.Conflict, (await w.B.Post($"/api/hr/learning/take/{cid}/quiz", new { answers = new Dictionary<string, int> { ["q1"] = 0, ["q2"] = 1 } })).StatusCode);   // lessons first
        Assert.Equal(HttpStatusCode.OK, (await w.B.Get($"/api/hr/learning/take/{cid}/lessons/{lessons[2]}/file")).StatusCode);
        foreach (var l in lessons) await w.B.Post($"/api/hr/learning/take/{cid}/lessons/{l}/done");
        var ready = await (await w.B.Get($"/api/hr/learning/take/{cid}")).Body();
        Assert.True(ready.GetProperty("quizOpen").GetBoolean()); Assert.Equal(100, ready.GetProperty("course").GetProperty("progress").GetInt32());
        var fail = await (await w.B.Post($"/api/hr/learning/take/{cid}/quiz", new { answers = new Dictionary<string, int> { ["q1"] = 1, ["q2"] = 0 } })).Body();
        Assert.False(fail.GetProperty("passed").GetBoolean()); Assert.Equal(0, fail.GetProperty("score").GetInt32());
        var pass = await (await w.B.Post($"/api/hr/learning/take/{cid}/quiz", new { answers = new Dictionary<string, int> { ["q1"] = 0, ["q2"] = 0 } })).Body();
        Assert.True(pass.GetProperty("passed").GetBoolean()); Assert.Equal(2, pass.GetProperty("attempts").GetInt32());
        var done = pass.GetProperty("course"); Assert.Equal("completed", done.GetProperty("status").GetString());
        Assert.StartsWith("CERT/", done.GetProperty("certificateNo").GetString()); Assert.Equal(D(Today.AddMonths(12)), done.GetProperty("expiresOn").GetString());

        var enr = done.GetProperty("enrollmentId").GetGuid();
        var cert = await w.B.Get($"/api/hr/learning/certificates/{enr}"); Assert.Equal(HttpStatusCode.OK, cert.StatusCode);
        var text = System.Text.Encoding.Latin1.GetString(await cert.Content.ReadAsByteArrayAsync());
        Assert.Contains("CERTIFICATE OF COMPLETION", text); Assert.Contains(w.Name, text); Assert.Contains("Verification code", text);
        Assert.Equal(HttpStatusCode.NotFound, (await other.B.Get($"/api/hr/learning/certificates/{enr}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hr.Get($"/api/hr/learning/certificates/{enr}")).StatusCode);

        var row = (await (await hr.Get($"/api/hr/learning/courses/{cid}/enrollments")).Body()).EnumerateArray().Single(); Assert.Equal("completed", row.GetProperty("status").GetString());
        Assert.Contains((await (await boss.B.Get("/api/hr/learning/team")).Body()).EnumerateArray(), x => x.GetProperty("employeeId").GetGuid() == w.EmployeeId && x.GetProperty("status").GetString() == "completed");
        Assert.Equal(HttpStatusCode.Forbidden, (await w.B.Get($"/api/hr/learning/courses/{cid}/enrollments")).StatusCode);
        var s = await (await hr.Get("/api/hr/learning/summary")).Body(); Assert.True(s.GetProperty("certificatesThisYear").GetInt32() >= 1);
    }

    [Fact]
    public async Task People_may_enrol_themselves_in_open_courses_and_a_course_without_a_quiz_completes_with_its_lessons()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true); var w = await Worker(hr);
        var cid = Id((await (await hr.Post("/api/hr/learning/courses", new { title = "Writing clear emails", category = "general", selfEnroll = true, certificate = false })).Body()).GetProperty("course"));
        var lid = Id((await (await hr.Post($"/api/hr/learning/courses/{cid}/lessons", new { title = "Short and kind", kind = "link", url = "https://example.com/emails" })).Body()).GetProperty("lessons")[0]);
        Assert.DoesNotContain((await (await w.B.Get("/api/hr/learning/mine")).Body()).GetProperty("catalog").EnumerateArray(), c => c.GetProperty("courseId").GetGuid() == cid);   // draft: not offered
        await hr.Post($"/api/hr/learning/courses/{cid}/publish");
        Assert.Contains((await (await w.B.Get("/api/hr/learning/mine")).Body()).GetProperty("catalog").EnumerateArray(), c => c.GetProperty("courseId").GetGuid() == cid);
        Assert.Equal(HttpStatusCode.Conflict, (await w.B.Post($"/api/hr/learning/take/{cid}/lessons/{lid}/done")).StatusCode);   // not enrolled yet
        Assert.Equal("not_started", (await (await w.B.Post($"/api/hr/learning/take/{cid}/enroll")).Body()).GetProperty("course").GetProperty("status").GetString());
        var after = await (await w.B.Post($"/api/hr/learning/take/{cid}/lessons/{lid}/done")).Body();
        Assert.Equal("completed", after.GetProperty("course").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("course").GetProperty("certificateNo").ValueKind);   // this course gives no certificate
    }
}
