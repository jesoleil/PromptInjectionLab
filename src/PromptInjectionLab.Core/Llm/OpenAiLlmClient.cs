using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PromptInjectionLab.Core.Config;

namespace PromptInjectionLab.Core.Llm;

/// <summary>
/// OpenAI Chat Completions adapter: POST {BaseUrl}/chat/completions with
/// "Authorization: Bearer &lt;key&gt;". Sends max_completion_tokens (the current
/// name; it includes reasoning tokens on reasoning models), and temperature /
/// seed / reasoning_effort only when configured. Reasoning models may reject a
/// non-default temperature: the API's 400 error is surfaced as BadRequest.
/// NOTE: verified with unit tests against mocked responses only.
/// </summary>
public sealed class OpenAiLlmClient : ILlmClient
{
    private const string Provider = "OpenAI";
    private readonly OpenAiOptions _o;
    private readonly HttpLlmTransport _transport;
    private readonly string _apiKey;

    public OpenAiLlmClient(HttpClient http, IOptions<OpenAiOptions> options, ILogger<OpenAiLlmClient> logger,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Func<string, string?>? env = null)
    {
        _o = options.Value;
        ProviderSupport.RequireCommon(_o, OpenAiOptions.SectionName, maxTemperature: 2.0);
        _apiKey = ProviderSupport.RequireApiKey(_o, Provider, OpenAiOptions.SectionName, env);
        _transport = new HttpLlmTransport(http, logger, delay);
        Info = new LlmClientInfo(Provider, _o.Model, _o.ChatUrl, _o.Temperature, _o.MaxTokens, _o.Seed,
            ApiVersion: "v1", IsPaid: true,
            ExtraParameters: _o.ReasoningEffort is null ? null : $"reasoning_effort={_o.ReasoningEffort}");
    }

    public LlmClientInfo Info { get; }

    public async Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = _o.Model,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userPrompt },
            },
            ["max_completion_tokens"] = _o.MaxTokens,
            ["stream"] = false,
        };
        if (_o.Temperature is { } t) body["temperature"] = t;
        if (_o.Seed is { } s) body["seed"] = s;
        if (!string.IsNullOrWhiteSpace(_o.ReasoningEffort)) body["reasoning_effort"] = _o.ReasoningEffort;
        var json = body.ToJsonString();

        var r = await _transport.SendAsync(() => Authorized(new HttpRequestMessage(HttpMethod.Post, _o.ChatUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }), _o, Provider, _apiKey, ct);

        return r.Ok ? ProviderSupport.ParseChatCompletion(r) : ProviderSupport.Failure(r);
    }

    /// <summary>GET /models/{model}: free metadata call, no tokens generated.</summary>
    public async Task<HealthResult> CheckAsync(CancellationToken ct = default)
    {
        var url = $"{_o.Root}/models/{Uri.EscapeDataString(_o.Model)}";
        var r = await _transport.SendAsync(() => Authorized(new HttpRequestMessage(HttpMethod.Get, url)),
            _o, Provider, _apiKey, ct);
        if (r.Ok) return new HealthResult(true, true, new[] { _o.Model }, null);
        if (r.Kind == LlmErrorKind.NotFound) return new HealthResult(true, false, Array.Empty<string>(), r.Error);
        return new HealthResult(r.Status is not null, false, Array.Empty<string>(), r.Error);
    }

    private HttpRequestMessage Authorized(HttpRequestMessage req)
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        return req;
    }
}
