using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PromptInjectionLab.Core.Config;

namespace PromptInjectionLab.Core.Llm;

/// <summary>
/// Talks to Ollama via its native /api/chat endpoint (non-streaming).
/// "think": false disables the reasoning phase of thinking models (e.g. qwen3.5)
/// so the short token budget is spent on the answer; the answer is read from
/// "message.content". Validate with the `check` command and a pilot run before
/// any result is trusted.
/// </summary>
public sealed class OllamaLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaLlmClient> _logger;

    public OllamaLlmClient(HttpClient http, IOptions<OllamaOptions> options, ILogger<OllamaLlmClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var body = new ChatRequest
            {
                Model = _options.Model,
                Options = new ChatOptions
                {
                    Temperature = _options.Temperature,
                    NumPredict = _options.MaxTokens,
                    Seed = _options.Seed,
                },
                Messages = new[]
                {
                    new ChatMessage { Role = "system", Content = systemPrompt },
                    new ChatMessage { Role = "user", Content = userPrompt },
                },
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, _options.ChatUrl)
            {
                Content = JsonContent.Create(body),
            };
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            using var resp = await _http.SendAsync(req, ct);
            sw.Stop();

            if (!resp.IsSuccessStatusCode)
            {
                var err = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
                _logger.LogWarning("Ollama returned non-success: {Error}", err);
                return new LlmResponse(false, "", err, sw.ElapsedMilliseconds);
            }

            var parsed = await resp.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct);
            var text = parsed?.Message?.Content ?? "";
            return new LlmResponse(true, text, null, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning("Ollama request timed out after {Ms} ms", sw.ElapsedMilliseconds);
            return new LlmResponse(false, "", "timeout", sw.ElapsedMilliseconds, TimedOut: true);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning("Ollama request failed: {Error}", ex.Message);
            return new LlmResponse(false, "", ex.Message, sw.ElapsedMilliseconds);
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

    // --- Wire DTOs (private) ---
    private sealed class ChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("messages")] public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();
        [JsonPropertyName("stream")] public bool Stream => false;
        [JsonPropertyName("think")] public bool Think => false;
        [JsonPropertyName("options")] public ChatOptions Options { get; set; } = new();
    }

    private sealed class ChatOptions
    {
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
        [JsonPropertyName("num_predict")] public int NumPredict { get; set; }
        [JsonPropertyName("seed")] public int? Seed { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
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
