using System.Net;
using System.Text.Json;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Stages 15–16: goals with alignment and check-ins, a 360° review cycle from launch to released results, calibration and the 9-box, development plans and succession.</summary>
[Collection("db")]
public sealed class PerformanceTests(PostgresFixture fx)
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
    private async Task<Person> Worker(TestBrowser hr, string role = "user", Guid? manager = null, Guid? unit = null)
    {
        var u = await _seed.UserAsync(role: role); var name = "Staff " + Guid.NewGuid().ToString("N")[..6];
        var r = await hr.Post("/api/hr/employees", new { fullName = name, hireDate = D(Today.AddYears(-2)), jobTitle = "Analyst", managerId = manager, orgUnitId = unit, userId = u.Id });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return new Person(await Login(u.Email), (await r.Body()).GetProperty("id").GetGuid(), name);
    }
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    [Fact]
    public async Task Goals_roll_up_to_company_goals_and_only_the_owner_their_manager_or_HR_change_them()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true);
        var boss = await Worker(hr, "manager"); var w = await Worker(hr, manager: boss.EmployeeId); var other = await Worker(hr);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.B.Post("/api/hr/goals", new { company = true, kind = "objective", title = "Grow revenue" })).StatusCode);
        var company = await (await hr.Post("/api/hr/goals", new { company = true, kind = "objective", title = "Grow revenue 20%" })).Body();
        Assert.Equal(JsonValueKind.Null, company.GetProperty("ownerEmployeeId").ValueKind);
        // the person's own key result, aligned to the company objective
        var kr = await w.B.Post("/api/hr/goals", new { kind = "key_result", title = "Close 10 new clients", measure = "clients", startValue = 0, targetValue = 10, weight = 60, parentId = Id(company) });
        Assert.Equal(HttpStatusCode.Created, kr.StatusCode); var krId = Id(await kr.Body());
        // the manager sets one for the direct report; an unrelated colleague cannot
        var kpi = await boss.B.Post("/api/hr/goals", new { ownerEmployeeId = w.EmployeeId, kind = "kpi", title = "Customer satisfaction", startValue = 3, targetValue = 4.5, weight = 40 });
        Assert.Equal(HttpStatusCode.Created, kpi.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.B.Post("/api/hr/goals", new { ownerEmployeeId = w.EmployeeId, kind = "kpi", title = "Sneaky", startValue = 0, targetValue = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.B.Post($"/api/hr/goals/{krId}/check-in", new { value = 9 })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await w.B.Post("/api/hr/goals", new { kind = "kpi", title = "Flat", startValue = 5, targetValue = 5 })).StatusCode);
        // a goal cannot end up supporting itself through its parents
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.SendAsync(HttpMethod.Put, $"/api/hr/goals/{Id(company)}", new { parentId = krId })).StatusCode);
        // check-in moves progress and keeps the history
        var after = await (await w.B.Post($"/api/hr/goals/{krId}/check-in", new { value = 4, status = "at_risk", note = "Two deals slipped" })).Body();
        Assert.Equal(40m, after.GetProperty("progress").GetDecimal()); Assert.Equal("at_risk", after.GetProperty("status").GetString());
        Assert.Equal(1, (await (await w.B.Get($"/api/hr/goals/{krId}/check-ins")).Body()).GetArrayLength());
        Assert.Equal(2, (await (await w.B.Get("/api/hr/goals?scope=mine")).Body()).GetArrayLength());
        Assert.Equal(2, (await (await boss.B.Get("/api/hr/goals?scope=team")).Body()).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await other.B.Get($"/api/hr/goals?scope=employee&employeeId={w.EmployeeId}")).StatusCode);
        var companyNow = (await (await w.B.Get("/api/hr/goals?scope=company")).Body()).EnumerateArray().Single(g => Id(g) == Id(company));
        Assert.Equal(1, companyNow.GetProperty("children").GetInt32()); Assert.False(companyNow.GetProperty("canEdit").GetBoolean());
    }

    [Fact]
    public async Task A_360_review_scores_goals_and_competencies_is_calibrated_and_released_without_peer_names()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true);
        var code = "P" + Guid.NewGuid().ToString("N")[..5];
        var unit = Id(await (await hr.Post("/api/hr/org-units", new { code, name = "Perf " + code })).Body());
        var boss = await Worker(hr, "manager", unit: unit); var w = await Worker(hr, manager: boss.EmployeeId, unit: unit); var peer = await Worker(hr, unit: unit);
        var comm = Id(await (await hr.Post("/api/hr/competencies", new { code = code + "C", name = "Communication", category = "core" })).Body());
        var lead = Id(await (await hr.Post("/api/hr/competencies", new { code = code + "L", name = "Ownership", category = "leadership" })).Body());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/competencies", new { code = code + "C", name = "Duplicate", category = "core" })).StatusCode);
        var g1 = Id(await (await w.B.Post("/api/hr/goals", new { kind = "kpi", title = "Tickets closed", startValue = 0, targetValue = 100, weight = 75 })).Body());
        var g2 = Id(await (await w.B.Post("/api/hr/goals", new { kind = "kpi", title = "Docs written", startValue = 0, targetValue = 4, weight = 25 })).Body());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post("/api/hr/review-cycles", new { name = "No comps", periodStart = D(new DateOnly(Today.Year, 1, 1)), periodEnd = D(new DateOnly(Today.Year, 12, 31)), goalsWeight = 60 })).StatusCode);
        var cycle = await (await hr.Post("/api/hr/review-cycles", new { name = "Annual " + code, periodStart = D(new DateOnly(Today.Year, 1, 1)), periodEnd = D(new DateOnly(Today.Year, 12, 31)), orgUnitId = unit, peerReview = true, goalsWeight = 60, competencyIds = new[] { comm, lead } })).Body();
        var cid = Id(cycle); Assert.Equal(40m, cycle.GetProperty("competencyWeight").GetDecimal());
        Assert.Equal(HttpStatusCode.Forbidden, (await w.B.Post($"/api/hr/review-cycles/{cid}/launch")).StatusCode);
        var launched = await (await hr.Post($"/api/hr/review-cycles/{cid}/launch")).Body();
        Assert.Equal("open", launched.GetProperty("status").GetString()); Assert.Equal(3, launched.GetProperty("people").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await hr.Post($"/api/hr/review-cycles/{cid}/launch")).StatusCode);
        // the manager picks a peer for the direct report; the peer cannot pick for themselves
        Assert.Equal(HttpStatusCode.Forbidden, (await peer.B.Post($"/api/hr/review-cycles/{cid}/peers", new { employeeId = w.EmployeeId, reviewerEmployeeId = peer.EmployeeId })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await boss.B.Post($"/api/hr/review-cycles/{cid}/peers", new { employeeId = w.EmployeeId, reviewerEmployeeId = peer.EmployeeId })).StatusCode);

        async Task<Guid> Task(Person p, string kind) => Id((await (await p.B.Get("/api/hr/reviews/mine")).Body()).EnumerateArray().Single(t => t.GetProperty("employeeId").GetGuid() == w.EmployeeId && t.GetProperty("kind").GetString() == kind));
        var self = await Task(w, "self"); var mgr = await Task(boss, "manager"); var pr = await Task(peer, "peer");
        Assert.Equal(HttpStatusCode.NotFound, (await peer.B.Get($"/api/hr/reviews/{mgr}")).StatusCode);   // someone else's review
        Assert.Equal(0, (await (await peer.B.Get($"/api/hr/reviews/{pr}")).Body()).GetProperty("goals").GetArrayLength());   // peers do not see the goals
        Assert.Equal(2, (await (await boss.B.Get($"/api/hr/reviews/{mgr}")).Body()).GetProperty("goals").GetArrayLength());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await boss.B.SendAsync(HttpMethod.Put, $"/api/hr/reviews/{mgr}", new { goals = new Dictionary<Guid, int> { [g1] = 4 }, submit = true })).StatusCode);   // not everything rated
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await boss.B.SendAsync(HttpMethod.Put, $"/api/hr/reviews/{mgr}", new { competencies = new Dictionary<Guid, int> { [comm] = 7 } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await boss.B.SendAsync(HttpMethod.Put, $"/api/hr/reviews/{mgr}", new { goals = new Dictionary<Guid, int> { [g1] = 4, [g2] = 2 }, competencies = new Dictionary<Guid, int> { [comm] = 4, [lead] = 4 }, strengths = "Calm under pressure", submit = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await boss.B.SendAsync(HttpMethod.Put, $"/api/hr/reviews/{mgr}", new { comment = "changed my mind" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await peer.B.SendAsync(HttpMethod.Put, $"/api/hr/reviews/{pr}", new { competencies = new Dictionary<Guid, int> { [comm] = 2, [lead] = 2 }, improvements = "Share updates earlier", submit = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.B.SendAsync(HttpMethod.Put, $"/api/hr/reviews/{self}", new { goals = new Dictionary<Guid, int> { [g1] = 5, [g2] = 5 }, competencies = new Dictionary<Guid, int> { [comm] = 5, [lead] = 5 }, submit = true })).StatusCode);

        // goals from the manager: (4×75 + 2×25)/100 = 3.5; competencies: manager 4 ×60 + peer 2 ×20 + self 5 ×20 = 3.8; final 3.5×60% + 3.8×40% = 3.62
        var row = (await (await hr.Get($"/api/hr/review-cycles/{cid}/results")).Body()).EnumerateArray().Single(r => r.GetProperty("employeeId").GetGuid() == w.EmployeeId);
        Assert.Equal(3.5m, row.GetProperty("goalScore").GetDecimal()); Assert.Equal(3.8m, row.GetProperty("competencyScore").GetDecimal()); Assert.Equal(3.62m, row.GetProperty("finalScore").GetDecimal());
        Assert.Equal(3, row.GetProperty("reviewsDone").GetInt32()); Assert.Equal(3, row.GetProperty("reviewsTotal").GetInt32());
        Assert.Equal(0, (await (await w.B.Get("/api/hr/reviews/results")).Body()).GetArrayLength());   // not released yet

        Assert.Equal(HttpStatusCode.Conflict, (await hr.Post($"/api/hr/review-cycles/{cid}/close")).StatusCode);   // calibrate first
        Assert.Equal(HttpStatusCode.OK, (await hr.Post($"/api/hr/review-cycles/{cid}/calibrate")).StatusCode);
        var cal = await (await hr.Post($"/api/hr/review-cycles/{cid}/results", new { employeeId = w.EmployeeId, rating = 4, potential = 3, note = "Ready for a bigger role" })).Body();
        Assert.Equal("star", cal.GetProperty("box").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post($"/api/hr/review-cycles/{cid}/results", new { employeeId = w.EmployeeId, potential = 4 })).StatusCode);
        Assert.Equal("closed", (await (await hr.Post($"/api/hr/review-cycles/{cid}/close")).Body()).GetProperty("status").GetString());

        var mine = (await (await w.B.Get("/api/hr/reviews/results")).Body()).EnumerateArray().Single(x => x.GetProperty("cycleId").GetGuid() == cid);
        Assert.Equal(4, mine.GetProperty("rating").GetInt32());
        Assert.Contains("Share updates earlier", mine.GetProperty("improvements").EnumerateArray().Select(x => x.GetString()));
        var raw = mine.GetRawText(); Assert.DoesNotContain(peer.Name, raw); Assert.DoesNotContain(peer.EmployeeId.ToString(), raw);
        Assert.Contains((await (await hr.Get("/api/hr/talent/pool")).Body()).EnumerateArray(), t => t.GetProperty("employeeId").GetGuid() == w.EmployeeId && t.GetProperty("box").GetString() == "star");
        Assert.Equal(HttpStatusCode.Forbidden, (await w.B.Get("/api/hr/talent/pool")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.B.Get($"/api/hr/review-cycles/{cid}/results")).StatusCode);
    }

    [Fact]
    public async Task Development_plans_are_private_to_the_person_their_manager_and_HR_and_succession_lists_ready_candidates()
    {
        var hr = await Login((await _seed.UserAsync(role: "hr_admin")).Email, reauth: true);
        var boss = await Worker(hr, "manager"); var w = await Worker(hr, manager: boss.EmployeeId); var other = await Worker(hr);
        var plan = await (await w.B.Post("/api/hr/talent/plans", new { title = "Become a team lead", aim = "Lead the support team in 2 years", actions = new[] { new { id = "", text = "Finish leadership course", due = D(Today.AddMonths(3)), done = false }, new { id = "", text = "Run weekly stand-ups", due = (string?)null, done = false } } })).Body();
        Assert.Equal(2, plan.GetProperty("actions").GetArrayLength()); var pid = Id(plan);
        Assert.Equal(1, (await (await boss.B.Get($"/api/hr/talent/plans?employeeId={w.EmployeeId}")).Body()).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await other.B.Get($"/api/hr/talent/plans?employeeId={w.EmployeeId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.B.SendAsync(HttpMethod.Put, $"/api/hr/talent/plans/{pid}", new { title = "Hijack" })).StatusCode);
        var actions = plan.GetProperty("actions").EnumerateArray().Select(a => new { id = a.GetProperty("id").GetString(), text = a.GetProperty("text").GetString(), due = (string?)null, done = true }).ToArray();
        Assert.Equal(2, (await (await boss.B.SendAsync(HttpMethod.Put, $"/api/hr/talent/plans/{pid}", new { actions, status = "done" })).Body()).GetProperty("done").GetInt32());

        Assert.Equal(HttpStatusCode.Forbidden, (await boss.B.Post("/api/hr/talent/succession", new { position = "Head of Support" })).StatusCode);
        var sp = Id(await (await hr.Post("/api/hr/talent/succession", new { position = "Head of Support", incumbentEmployeeId = boss.EmployeeId, critical = true })).Body());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await hr.Post($"/api/hr/talent/succession/{sp}/candidates", new { employeeId = w.EmployeeId, readiness = "soon" })).StatusCode);
        var withCand = await (await hr.Post($"/api/hr/talent/succession/{sp}/candidates", new { employeeId = w.EmployeeId, readiness = "one_to_two_years" })).Body();
        var cand = withCand.GetProperty("candidates").EnumerateArray().Single();
        Assert.Equal("one_to_two_years", cand.GetProperty("readiness").GetString()); Assert.Equal(boss.Name, withCand.GetProperty("incumbent").GetString());
        await hr.Post($"/api/hr/talent/succession/{sp}/candidates", new { employeeId = w.EmployeeId, readiness = "ready_now" });   // same person again: updated, not doubled
        var again = (await (await hr.Get("/api/hr/talent/succession")).Body()).EnumerateArray().Single(p => Id(p) == sp).GetProperty("candidates");
        Assert.Equal(1, again.GetArrayLength()); Assert.Equal("ready_now", again[0].GetProperty("readiness").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await hr.SendAsync(HttpMethod.Delete, $"/api/hr/talent/succession/{sp}/candidates/{Id(again[0])}")).StatusCode);
    }
}
