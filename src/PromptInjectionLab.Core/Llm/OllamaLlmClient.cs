using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PromptInjectionLab.Core.Config;

namespace PromptInjectionLab.Core.Llm;

/// <summary>
/// Talks to Ollama via its native /api/chat endpoint (non-streaming).
/// "think": false disables the reasoning phase of thinking models (e.g. qwen3.5)
/// so the short token budget is spent on the answer; the answer is read from
/// "message.content". Token counts come from prompt_eval_count / eval_count.
/// Validate with the `check` command and a pilot run before any result is trusted.
/// </summary>
public sealed class OllamaLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly HttpLlmTransport _transport;

    public OllamaLlmClient(HttpClient http, IOptions<OllamaOptions> options, ILogger<OllamaLlmClient> logger,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http;
        _options = options.Value;
        _transport = new HttpLlmTransport(http, logger, delay);
        Info = new LlmClientInfo("Ollama", _options.Model, _options.ChatUrl, _options.Temperature,
            _options.MaxTokens, _options.Seed, ApiVersion: null, IsPaid: false, ExtraParameters: "think=false");
    }

    public LlmClientInfo Info { get; }

    public async Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var options = new JsonObject { ["num_predict"] = _options.MaxTokens };
        if (_options.Temperature is { } t) options["temperature"] = t;
        if (_options.Seed is { } s) options["seed"] = s;

        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userPrompt },
            },
            ["stream"] = false,
            ["think"] = false,
            ["options"] = options,
        };
        var json = body.ToJsonString();

        var r = await _transport.SendAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, _options.ChatUrl)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            return req;
        }, _options, "Ollama", _options.ApiKey, ct);

        if (!r.Ok) return ProviderSupport.Failure(r);

        try
        {
            using var doc = JsonDocument.Parse(r.Body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ProviderSupport.InvalidResponse(r, "body is not a JSON object");
            return new LlmResponse(true, root.Obj("message").Str("content") ?? "", null, r.ElapsedMs)
            {
                HttpStatus = r.Status,
                Attempts = r.Attempts,
                ResponseModel = root.Str("model"),
                InputTokens = root.Int("prompt_eval_count"),
                OutputTokens = root.Int("eval_count"),
                FinishReason = root.Str("done_reason"),
            };
        }
        catch (JsonException ex)
        {
            return ProviderSupport.InvalidResponse(r, ex.Message);
        }
    }

    public async Task<HealthResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(_options.TagsUrl, ct);
            if (!resp.IsSuccessStatusCode)
                return new HealthResult(false, false, Array.Empty<string>(),
                    $"HTTP {(int)resp.StatusCode} from {_options.TagsUrl}");

            var tags = await resp.Content.ReadFromJsonAsync<TagsResponse>(cancellationToken: ct);
            var models = tags?.Models?
                .Select(m => m.Name ?? "")
                .Where(n => n.Length > 0)
                .ToList() ?? new List<string>();

            var available = !string.IsNullOrWhiteSpace(_options.Model) &&
                            models.Any(m => string.Equals(m, _options.Model, StringComparison.OrdinalIgnoreCase));
            return new HealthResult(true, available, models, null);
        }
        catch (Exception ex)
        {
            return new HealthResult(false, false, Array.Empty<string>(), ex.Message);
        }
    }

    private sealed class TagsResponse
    {
        [JsonPropertyName("models")] public List<TagModel>? Models { get; set; }
    }

    private sealed class TagModel
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }
}
