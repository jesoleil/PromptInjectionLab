using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PromptInjectionLab.Core.Config;

namespace PromptInjectionLab.Core.Llm;

/// <summary>
/// DeepSeek adapter (OpenAI-compatible schema): POST {BaseUrl}/chat/completions
/// with "Authorization: Bearer &lt;key&gt;", max_tokens and optional temperature.
/// DeepSeek has no seed parameter (configuring one is an error), and its
/// documentation states temperature has no effect on "deepseek-reasoner", so a
/// configured temperature for that model is rejected instead of being recorded
/// as if it applied. "reasoning_content" is ignored; only "content" is classified.
/// NOTE: verified with unit tests against mocked responses only.
/// </summary>
public sealed class DeepSeekLlmClient : ILlmClient
{
    private const string Provider = "DeepSeek";
    private readonly DeepSeekOptions _o;
    private readonly HttpLlmTransport _transport;
    private readonly string _apiKey;

    public DeepSeekLlmClient(HttpClient http, IOptions<DeepSeekOptions> options, ILogger<DeepSeekLlmClient> logger,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Func<string, string?>? env = null)
    {
        _o = options.Value;
        ProviderSupport.RequireCommon(_o, DeepSeekOptions.SectionName, maxTemperature: 2.0);
        ProviderSupport.RejectSeed(_o, Provider, DeepSeekOptions.SectionName);
        if (_o.Temperature is not null && _o.Model.Contains("reasoner", StringComparison.OrdinalIgnoreCase))
            throw new LlmConfigurationException(
                $"DeepSeek model '{_o.Model}' does not apply temperature. Set {DeepSeekOptions.SectionName}:Temperature to null.");
        _apiKey = ProviderSupport.RequireApiKey(_o, Provider, DeepSeekOptions.SectionName, env);
        _transport = new HttpLlmTransport(http, logger, delay);
        Info = new LlmClientInfo(Provider, _o.Model, _o.ChatUrl, _o.Temperature, _o.MaxTokens, Seed: null,
            ApiVersion: null, IsPaid: true);
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
            ["max_tokens"] = _o.MaxTokens,
            ["stream"] = false,
        };
        if (_o.Temperature is { } t) body["temperature"] = t;
        var json = body.ToJsonString();

        var r = await _transport.SendAsync(() => Authorized(new HttpRequestMessage(HttpMethod.Post, _o.ChatUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }), _o, Provider, _apiKey, ct);

        return r.Ok ? ProviderSupport.ParseChatCompletion(r) : ProviderSupport.Failure(r);
    }

    /// <summary>GET /models: free listing call, no tokens generated.</summary>
    public async Task<HealthResult> CheckAsync(CancellationToken ct = default)
    {
        var r = await _transport.SendAsync(() => Authorized(new HttpRequestMessage(HttpMethod.Get, $"{_o.Root}/models")),
            _o, Provider, _apiKey, ct);
        if (!r.Ok) return new HealthResult(r.Status is not null, false, Array.Empty<string>(), r.Error);
        try
        {
            using var doc = JsonDocument.Parse(r.Body);
            var ids = doc.RootElement.Obj("data") is { ValueKind: JsonValueKind.Array } data
                ? data.EnumerateArray().Select(m => m.Str("id") ?? "").Where(x => x.Length > 0).ToList()
                : new List<string>();
            return new HealthResult(true, ids.Contains(_o.Model, StringComparer.OrdinalIgnoreCase), ids, null);
        }
        catch (JsonException ex)
        {
            return new HealthResult(true, false, Array.Empty<string>(), $"Invalid /models response: {ex.Message}");
        }
    }

    private HttpRequestMessage Authorized(HttpRequestMessage req)
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        return req;
    }
}
