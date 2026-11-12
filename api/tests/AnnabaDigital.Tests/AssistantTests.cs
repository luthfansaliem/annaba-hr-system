using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Stage 21: the HR assistant through the real SDK adapter against a scripted Claude endpoint — off by default, the policy cached, tools that only read the asker's own data, refusals.</summary>
[Collection("db")]
public sealed class AssistantTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private FakeClaude Claude => fx.Factory.Services.GetRequiredService<FakeClaude>();
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    [Fact]
    public async Task The_assistant_is_off_until_turned_on_answers_from_the_policy_and_reads_only_the_askers_own_data()
    {
        var admin = TestBrowser.Create(fx.Factory); await admin.LoginAsync((await _seed.UserAsync(role: "super_admin")).Email, Seed.Password);
        var hr = TestBrowser.Create(fx.Factory); await hr.LoginAsync((await _seed.UserAsync(role: "hr_admin")).Email, Seed.Password); await hr.Post("/api/auth/reauth", new { password = Seed.Password });
        var u = await _seed.UserAsync(role: "user");
        var today = DateOnly.FromDateTime(fx.Clock.UtcNow.UtcDateTime.AddHours(7));
        var emp = await (await hr.Post("/api/hr/employees", new { fullName = "Asking Person", hireDate = D(today.AddYears(-2)), jobTitle = "Analyst", userId = u.Id })).Body();
        var w = TestBrowser.Create(fx.Factory); await w.LoginAsync(u.Email, Seed.Password);
        await admin.SendAsync(HttpMethod.Put, "/api/hr/assistant/settings", new { enabled = false, allowPayData = false });

        var status = await (await w.Get("/api/hr/assistant/status")).Body();
        Assert.False(status.GetProperty("available").GetBoolean()); Assert.True(status.GetProperty("configured").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await w.Post("/api/hr/assistant/ask", new { question = "Berapa sisa cuti saya?" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.SendAsync(HttpMethod.Put, "/api/hr/assistant/settings", new { enabled = true })).StatusCode);
        var policy = "Pasal 12 Cuti tahunan: karyawan berhak 12 hari kerja per tahun setelah 12 bulan bekerja. " + string.Concat(Enumerable.Repeat("Ketentuan umum. ", 200));
        Assert.True((await (await admin.SendAsync(HttpMethod.Put, "/api/hr/assistant/settings", new { enabled = true, policyText = policy })).Body()).GetProperty("enabled").GetBoolean());

        // a question that needs the person's own data: the model calls a tool, gets the answer back, then replies
        while (Claude.Requests.TryDequeue(out _)) { } while (Claude.BetaHeaders.TryDequeue(out _)) { }
        Claude.Responses.Enqueue(FakeClaude.Message("tool_use",
            new { type = "thinking", thinking = "", signature = "sig-abc" },
            new { type = "tool_use", id = "toolu_01", name = "get_my_leave_balances", input = new { year = today.Year }, caller = new { type = "direct" } }));
        Claude.Responses.Enqueue(FakeClaude.Message("end_turn", new { type = "text", text = "Menurut Pasal 12 Anda berhak 12 hari cuti tahunan." }));
        var ans = await (await w.Post("/api/hr/assistant/ask", new { question = "Berapa hak cuti saya?", history = new[] { new { role = "user", text = "Halo" }, new { role = "assistant", text = "Halo! Ada yang bisa dibantu?" } } })).Body();
        Assert.Equal("Menurut Pasal 12 Anda berhak 12 hari cuti tahunan.", ans.GetProperty("answer").GetString());
        Assert.Equal("get_my_leave_balances", ans.GetProperty("toolsUsed")[0].GetString());

        Assert.True(Claude.Requests.TryDequeue(out var first)); Assert.True(Claude.Requests.TryDequeue(out var second));
        var r1 = first!.RootElement;
        Assert.Equal("claude-opus-5-5", r1.GetProperty("model").GetString());
        Assert.Equal("default", r1.GetProperty("fallbacks").GetString());                                   // a refused answer is re-served by a fallback model
        Assert.Contains("server-side-fallback-2026-07-01", Claude.BetaHeaders.First());
        Assert.Equal("ephemeral", r1.GetProperty("system")[0].GetProperty("cache_control").GetProperty("type").GetString());   // the policy is the cached prefix
        Assert.Contains("Pasal 12", r1.GetProperty("system")[0].GetProperty("text").GetString());
        Assert.Contains("Asking Person", r1.GetProperty("system")[1].GetProperty("text").GetString());
        var toolNames = r1.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("get_my_leave_balances", toolNames); Assert.DoesNotContain("get_my_payslips", toolNames);   // pay data is off unless the organisation allows it
        Assert.Equal(3, r1.GetProperty("messages").GetArrayLength());                                        // two history turns + the question
        var msgs = second!.RootElement.GetProperty("messages");
        var echoed = msgs[3].GetProperty("content");
        Assert.Equal("sig-abc", echoed[0].GetProperty("signature").GetString());                            // the thinking block goes back unchanged
        var result = msgs[4].GetProperty("content")[0];
        Assert.Equal("tool_result", result.GetProperty("type").GetString()); Assert.Equal("toolu_01", result.GetProperty("tool_use_id").GetString());

        // payslips only when allowed, and a locked payslip list stays locked
        await admin.SendAsync(HttpMethod.Put, "/api/hr/assistant/settings", new { allowPayData = true });
        await w.SendAsync(HttpMethod.Put, "/api/hr/payroll/payslips/lock", new { locked = true });
        for (var i = 0; i < 4; i++) { fx.Clock.Advance(TimeSpan.FromMinutes(4)); await w.Get("/api/hr/me/notifications"); }
        Claude.Responses.Enqueue(FakeClaude.Message("tool_use", new { type = "tool_use", id = "toolu_02", name = "get_my_payslips", input = new { }, caller = new { type = "direct" } }));
        Claude.Responses.Enqueue(FakeClaude.Message("end_turn", new { type = "text", text = "Slip gaji Anda terkunci." }));
        await w.Post("/api/hr/assistant/ask", new { question = "Berapa gaji bersih saya bulan lalu?" });
        var payReq = Claude.Requests.ToArray()[^1].RootElement;
        var payMsgs = payReq.GetProperty("messages"); var lastMsg = payMsgs[payMsgs.GetArrayLength() - 1];
        Assert.Contains("locked their payslips", lastMsg.GetProperty("content")[0].GetProperty("content").GetString());

        // a refusal never leaks partial output
        Claude.Responses.Enqueue(FakeClaude.Message("refusal", new { type = "text", text = "partial" }));
        var refused = await (await w.Post("/api/hr/assistant/ask", new { question = "Something not allowed" })).Body();
        Assert.True(refused.GetProperty("refused").GetBoolean()); Assert.DoesNotContain("partial", refused.GetProperty("answer").GetString());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await w.Post("/api/hr/assistant/ask", new { question = "" })).StatusCode);
        await admin.SendAsync(HttpMethod.Put, "/api/hr/assistant/settings", new { enabled = false, allowPayData = false });   // leave it off for other tests
        _ = emp;
    }
}
