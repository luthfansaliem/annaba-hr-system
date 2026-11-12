using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AnnabaDigital.Application.Hr;

namespace AnnabaDigital.Infrastructure.Ai;

/// <summary>
/// The HR assistant's model: Claude through the official Anthropic SDK. Configured only when the server has a key (Ai:AnthropicApiKey or ANTHROPIC_API_KEY);
/// otherwise the assistant stays off. The HR policy is the cached prefix of the system prompt; the person-specific instructions follow it.
/// A refusal is re-served by a fallback model inside the same call (server-side fallback); the tool loop is capped.
/// </summary>
public sealed class AnthropicAiModel : IAiModel
{
    private const int MaxRounds = 6;
    private readonly AnthropicClient? _client;
    private readonly ILogger<AnthropicAiModel> _log;
    public string ModelName { get; }
    public bool Configured => _client is not null;

    public AnthropicAiModel(IConfiguration config, ILogger<AnthropicAiModel> log)
    {
        _log = log;
        ModelName = config["Ai:Model"] is { Length: > 0 } m ? m : "claude-opus-5-5";
        var key = config["Ai:AnthropicApiKey"] is { Length: > 0 } k ? k : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(key)) _client = new AnthropicClient { ApiKey = key };
    }

    private AnthropicAiModel(AnthropicClient client, string model, ILogger<AnthropicAiModel> log) { _client = client; ModelName = model; _log = log; }
    /// <summary>For tests: talks to the given HTTP client instead of the real API.</summary>
    public static AnthropicAiModel WithHttpClient(HttpClient http, string model, ILogger<AnthropicAiModel> log) => new(new AnthropicClient { ApiKey = "test-key", HttpClient = http, BaseUrl = http.BaseAddress?.ToString() ?? "https://api.anthropic.test", MaxRetries = 0 }, model, log);

    public async Task<AiAnswer> AnswerAsync(AiPrompt prompt, Func<string, JsonElement, CancellationToken, Task<string>> runTool, CancellationToken ct)
    {
        if (_client is null) throw new InvalidOperationException("The assistant has no API key.");
        var system = new List<BetaTextBlockParam>();
        // stable and large first, cached; then the per-person, per-day instructions
        if (!string.IsNullOrWhiteSpace(prompt.Policy)) system.Add(new BetaTextBlockParam { Text = "HR policy document of the company:\n\n" + prompt.Policy, CacheControl = new BetaCacheControlEphemeral() });
        system.Add(new BetaTextBlockParam { Text = prompt.Instructions });
        List<BetaToolUnion> tools = [.. prompt.Tools.Select(t => (BetaToolUnion)new BetaTool
        {
            Name = t.Name, Description = t.Description,
            InputSchema = new()
            {
                Properties = t.Schema.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
                Required = [.. t.Schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()!)],
            },
        })];
        List<BetaMessageParam> messages = [];
        foreach (var h in prompt.History) messages.Add(h.Role == "assistant" ? new BetaAssistantMessageParam { Content = h.Text } : new BetaUserMessageParam { Content = h.Text });
        messages.Add(new BetaUserMessageParam { Content = prompt.Question });

        var used = new List<string>(); long input = 0, output = 0, cache = 0;
        for (var round = 0; round < MaxRounds; round++)
        {
            var response = await _client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = ModelName,
                MaxTokens = 16000,
                Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
                Fallbacks = new Default(),   // fallbacks: "default" - the server picks the fallback model by refusal category
                OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
                System = system,
                Tools = tools,
                Messages = messages,
            }, ct);
            input += response.Usage.InputTokens; output += response.Usage.OutputTokens; cache += response.Usage.CacheReadInputTokens ?? 0;
            if (response.StopReason == "refusal") return new AiAnswer("", true, used, input, output, cache);

            // echo the assistant turn back unchanged (thinking blocks with their signatures, fallback markers, tool calls)
            List<BetaContentBlockParam> assistant = []; List<BetaContentBlockParam> results = []; var text = new System.Text.StringBuilder();
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out var t)) { assistant.Add(new BetaTextBlockParam { Text = t.Text }); text.Append(t.Text); }
                else if (block.TryPickThinking(out var th)) assistant.Add(new BetaThinkingBlockParam { Thinking = th.Thinking, Signature = th.Signature });
                else if (block.TryPickRedactedThinking(out var rt)) assistant.Add(new BetaRedactedThinkingBlockParam { Data = rt.Data });
                else if (block.TryPickFallback(out var fb)) assistant.Add(new BetaFallbackBlockParam { From = new() { Model = fb.From.Model }, To = new() { Model = fb.To.Model } });
                else if (block.TryPickToolUse(out var tu))
                {
                    assistant.Add(new BetaToolUseBlockParam { ID = tu.ID, Name = tu.Name, Input = tu.Input });
                    used.Add(tu.Name);
                    var json = JsonSerializer.SerializeToElement(tu.Input);
                    string result;
                    try { result = await runTool(tu.Name, json, ct); }
                    catch (Exception e) when (e is not OperationCanceledException) { _log.LogWarning(e, "Assistant tool {Tool} failed", tu.Name); result = "{\"error\":\"The data could not be read right now.\"}"; }
                    results.Add(new BetaToolResultBlockParam { ToolUseID = tu.ID, Content = result });
                }
            }
            if (response.StopReason != "tool_use" || results.Count == 0) return new AiAnswer(text.ToString().Trim(), false, used, input, output, cache);
            messages.Add(new BetaAssistantMessageParam { Content = assistant });
            messages.Add(new BetaUserMessageParam { Content = results });
        }
        return new AiAnswer("I needed too many steps to answer this. Please ask a more specific question.", false, used, input, output, cache);
    }
}
