using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PromptInjectionLab.Core.Config;

namespace PromptInjectionLab.Core.Llm;

/// <summary>
/// Anthropic Messages API adapter: POST {BaseUrl}/messages with headers
/// "x-api-key" and "anthropic-version". The system prompt goes in the
/// top-level "system" field; max_tokens is required. Temperature range is
/// 0–1. The Messages API has no seed parameter, so configuring one is an error.
/// The answer is the concatenation of "text" content blocks.
/// NOTE: verified with unit tests against mocked responses only.
/// </summary>
public sealed class AnthropicLlmClient : ILlmClient
{
    private const string Provider = "Anthropic";
    private readonly AnthropicOptions _o;
    private readonly HttpLlmTransport _transport;
    private readonly string _apiKey;

    public AnthropicLlmClient(HttpClient http, IOptions<AnthropicOptions> options, ILogger<AnthropicLlmClient> logger,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Func<string, string?>? env = null)
    {
        _o = options.Value;
        ProviderSupport.RequireCommon(_o, AnthropicOptions.SectionName, maxTemperature: 1.0);
        ProviderSupport.RejectSeed(_o, Provider, AnthropicOptions.SectionName);
        if (string.IsNullOrWhiteSpace(_o.ApiVersion))
            throw new LlmConfigurationException($"{AnthropicOptions.SectionName}:ApiVersion is required (e.g. 2023-06-01).");
        _apiKey = ProviderSupport.RequireApiKey(_o, Provider, AnthropicOptions.SectionName, env);
        _transport = new HttpLlmTransport(http, logger, delay);
        Info = new LlmClientInfo(Provider, _o.Model, _o.MessagesUrl, _o.Temperature, _o.MaxTokens, Seed: null,
            ApiVersion: _o.ApiVersion, IsPaid: true);
    }

    public LlmClientInfo Info { get; }

    public async Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = _o.Model,
            ["max_tokens"] = _o.MaxTokens,
            ["system"] = systemPrompt,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = userPrompt },
            },
        };
        if (_o.Temperature is { } t) body["temperature"] = t;
        var json = body.ToJsonString();

        var r = await _transport.SendAsync(() => Authorized(new HttpRequestMessage(HttpMethod.Post, _o.MessagesUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }), _o, Provider, _apiKey, ct);

        if (!r.Ok) return ProviderSupport.Failure(r);

        try
        {
            using var doc = JsonDocument.Parse(r.Body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ProviderSupport.InvalidResponse(r, "body is not a JSON object");

            var sb = new StringBuilder();
            if (root.Obj("content") is { ValueKind: JsonValueKind.Array } blocks)
                foreach (var b in blocks.EnumerateArray())
                    if (b.Str("type") == "text") sb.Append(b.Str("text"));

            var usage = root.Obj("usage");
            return new LlmResponse(true, sb.ToString(), null, r.ElapsedMs)
            {
                HttpStatus = r.Status,
                Attempts = r.Attempts,
                ResponseModel = root.Str("model"),
                InputTokens = usage.Int("input_tokens"),
                OutputTokens = usage.Int("output_tokens"),
                FinishReason = root.Str("stop_reason"),
            };
        }
        catch (JsonException ex)
        {
            return ProviderSupport.InvalidResponse(r, ex.Message);
        }
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
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", _o.ApiVersion);
        return req;
    }
}
