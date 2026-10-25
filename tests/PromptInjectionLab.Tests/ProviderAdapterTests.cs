using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PromptInjectionLab.Core.Config;
using PromptInjectionLab.Core.Llm;
using PromptInjectionLab.Tests.Fakes;

namespace PromptInjectionLab.Tests;

/// <summary>
/// OpenAI / Anthropic / DeepSeek adapters against mocked HTTP only. The JSON
/// shapes follow each provider's public API documentation; these tests do NOT
/// prove the adapters work against the real services.
/// </summary>
public static class ProviderAdapterTests
{
    private const string FakeKey = "sk-test-0000000000000000FAKE";
    private static readonly Func<string, string?> NoEnv = _ => null;

    private sealed class DelayRecorder
    {
        public List<TimeSpan> Waits { get; } = new();
        public Task Wait(TimeSpan t, CancellationToken _) { Waits.Add(t); return Task.CompletedTask; }
    }

    // ---- factories ----
    private static OpenAiOptions OpenAi(Action<OpenAiOptions>? edit = null)
    {
        var o = new OpenAiOptions { Model = "gpt-test", ApiKey = FakeKey, Temperature = 0.0, MaxTokens = 16, RetryBaseDelayMs = 100 };
        edit?.Invoke(o);
        return o;
    }

    private static AnthropicOptions Anthropic(Action<AnthropicOptions>? edit = null)
    {
        var o = new AnthropicOptions { Model = "claude-test", ApiKey = FakeKey, Temperature = 0.0, MaxTokens = 16, RetryBaseDelayMs = 100 };
        edit?.Invoke(o);
        return o;
    }

    private static DeepSeekOptions DeepSeek(Action<DeepSeekOptions>? edit = null)
    {
        var o = new DeepSeekOptions { Model = "deepseek-chat", ApiKey = FakeKey, Temperature = 0.0, MaxTokens = 16, RetryBaseDelayMs = 100 };
        edit?.Invoke(o);
        return o;
    }

    private static OpenAiLlmClient OpenAiClient(FakeHttpMessageHandler h, OpenAiOptions? o = null, DelayRecorder? d = null) =>
        new(new HttpClient(h), Options.Create(o ?? OpenAi()), NullLogger<OpenAiLlmClient>.Instance, (d ?? new()).Wait, NoEnv);

    private static AnthropicLlmClient AnthropicClient(FakeHttpMessageHandler h, AnthropicOptions? o = null, DelayRecorder? d = null) =>
        new(new HttpClient(h), Options.Create(o ?? Anthropic()), NullLogger<AnthropicLlmClient>.Instance, (d ?? new()).Wait, NoEnv);

    private static DeepSeekLlmClient DeepSeekClient(FakeHttpMessageHandler h, DeepSeekOptions? o = null, DelayRecorder? d = null) =>
        new(new HttpClient(h), Options.Create(o ?? DeepSeek()), NullLogger<DeepSeekLlmClient>.Instance, (d ?? new()).Wait, NoEnv);

    private const string ChatCompletionOk = """
        {"id":"x","object":"chat.completion","model":"gpt-test-2026-01-01",
         "choices":[{"index":0,"message":{"role":"assistant","content":"Finance"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":123,"completion_tokens":2,"total_tokens":125}}
        """;

    private const string AnthropicOk = """
        {"id":"msg_1","type":"message","role":"assistant","model":"claude-test-20260101",
         "content":[{"type":"text","text":"Legal"}],"stop_reason":"end_turn",
         "usage":{"input_tokens":200,"output_tokens":3}}
        """;

    private static void ExpectConfigError(Action create, string fragment)
    {
        try
        {
            create();
        }
        catch (LlmConfigurationException ex)
        {
            Check.Contains(fragment, ex.Message);
            Check.DoesNotContain(FakeKey, ex.Message);
            return;
        }
        throw new Exception("Expected LlmConfigurationException");
    }

    public static IEnumerable<(string, Func<Task>)> Tests()
    {
        // ================= OpenAI =================
        yield return ("OpenAI: POST /v1/chat/completions with Bearer auth and expected body", async () =>
        {
            var h = FakeHttpMessageHandler.Json(ChatCompletionOk);
            await OpenAiClient(h, OpenAi(o => o.Seed = 7)).CompleteAsync("SYS", "USER");
            var req = h.Requests.Single();
            Check.Equal(HttpMethod.Post, req.Method);
            Check.Equal("https://api.openai.com/v1/chat/completions", req.Uri?.ToString());
            Check.Equal($"Bearer {FakeKey}", req.Headers["Authorization"]);

            using var doc = JsonDocument.Parse(req.Body);
            var root = doc.RootElement;
            Check.Equal("gpt-test", root.GetProperty("model").GetString());
            Check.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
            Check.Equal("USER", root.GetProperty("messages")[1].GetProperty("content").GetString());
            Check.Equal(16, root.GetProperty("max_completion_tokens").GetInt32());
            Check.Equal(7, root.GetProperty("seed").GetInt32());
            Check.AlmostEqual(0.0, root.GetProperty("temperature").GetDouble(), 1e-9);
            Check.False(root.TryGetProperty("max_tokens", out _), "legacy max_tokens must not be sent");
            Check.False(root.TryGetProperty("reasoning_effort", out _), "unset reasoning_effort must not be sent");
        });

        yield return ("OpenAI: null temperature/seed are omitted; reasoning_effort sent when set", async () =>
        {
            var h = FakeHttpMessageHandler.Json(ChatCompletionOk);
            await OpenAiClient(h, OpenAi(o => { o.Temperature = null; o.ReasoningEffort = "low"; })).CompleteAsync("s", "u");
            using var doc = JsonDocument.Parse(h.Requests[0].Body);
            Check.False(doc.RootElement.TryGetProperty("temperature", out _));
            Check.False(doc.RootElement.TryGetProperty("seed", out _));
            Check.Equal("low", doc.RootElement.GetProperty("reasoning_effort").GetString());
        });

        yield return ("OpenAI: parses content, model, usage tokens, finish_reason", async () =>
        {
            var r = await OpenAiClient(FakeHttpMessageHandler.Json(ChatCompletionOk)).CompleteAsync("s", "u");
            Check.True(r.Ok);
            Check.Equal("Finance", r.RawText);
            Check.Equal("gpt-test-2026-01-01", r.ResponseModel);
            Check.Equal(123, r.InputTokens);
            Check.Equal(2, r.OutputTokens);
            Check.Equal("stop", r.FinishReason);
            Check.Equal(1, r.Attempts);
            Check.Equal(LlmErrorKind.None, r.ErrorKind);
        });

        yield return ("OpenAI: refusal is kept as raw text (never a valid label)", async () =>
        {
            var h = FakeHttpMessageHandler.Json("""
                {"model":"m","choices":[{"message":{"role":"assistant","content":null,"refusal":"I can't help with that."},"finish_reason":"stop"}]}
                """);
            var r = await OpenAiClient(h).CompleteAsync("s", "u");
            Check.True(r.Ok);
            Check.Equal("I can't help with that.", r.RawText);
        });

        yield return ("OpenAI: 401 -> AuthenticationFailed, no retry, key redacted from error", async () =>
        {
            var h = FakeHttpMessageHandler.Json(
                "{\"error\":{\"message\":\"Incorrect API key provided: " + FakeKey +
                ". Also sk-abc***wxyz.\",\"type\":\"invalid_request_error\"}}",
                HttpStatusCode.Unauthorized);
            var d = new DelayRecorder();
            var r = await OpenAiClient(h, d: d).CompleteAsync("s", "u");
            Check.False(r.Ok);
            Check.Equal(LlmErrorKind.AuthenticationFailed, r.ErrorKind);
            Check.Equal(401, r.HttpStatus);
            Check.Equal(1, h.Requests.Count);
            Check.Equal(0, d.Waits.Count);
            Check.DoesNotContain(FakeKey, r.Error ?? "");
            Check.DoesNotContain("sk-abc", r.Error ?? "");
            Check.Contains(SecretRedactor.Mask, r.Error ?? "");
            Check.Contains("Incorrect API key", r.Error ?? "");
        });

        yield return ("OpenAI: 429 then 200 -> retried once, Ok, Attempts=2", async () =>
        {
            var h = FakeHttpMessageHandler.Sequence(
                () => FakeHttpMessageHandler.Response("""{"error":{"message":"Rate limit"}}""", HttpStatusCode.TooManyRequests),
                () => FakeHttpMessageHandler.Response(ChatCompletionOk));
            var d = new DelayRecorder();
            var r = await OpenAiClient(h, d: d).CompleteAsync("s", "u");
            Check.True(r.Ok);
            Check.Equal(2, r.Attempts);
            Check.Equal(2, h.Requests.Count);
            Check.Equal(1, d.Waits.Count);
            Check.Equal(TimeSpan.FromMilliseconds(100), d.Waits[0]);
        });

        yield return ("OpenAI: Retry-After header is honoured (and capped)", async () =>
        {
            HttpResponseMessage Limited(int seconds)
            {
                var resp = FakeHttpMessageHandler.Response("{}", HttpStatusCode.TooManyRequests);
                resp.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
                return resp;
            }
            var h = FakeHttpMessageHandler.Sequence(() => Limited(3), () => Limited(999), () => FakeHttpMessageHandler.Response(ChatCompletionOk));
            var d = new DelayRecorder();
            var r = await OpenAiClient(h, OpenAi(o => o.MaxRetryDelaySeconds = 30), d).CompleteAsync("s", "u");
            Check.True(r.Ok);
            Check.Equal(TimeSpan.FromSeconds(3), d.Waits[0]);
            Check.Equal(TimeSpan.FromSeconds(30), d.Waits[1]);
        });

        yield return ("OpenAI: persistent 503 -> ServerError after MaxRetries+1 attempts, exponential backoff", async () =>
        {
            var h = FakeHttpMessageHandler.Json("""{"error":{"message":"overloaded"}}""", HttpStatusCode.ServiceUnavailable);
            var d = new DelayRecorder();
            var r = await OpenAiClient(h, OpenAi(o => o.MaxRetries = 2), d).CompleteAsync("s", "u");
            Check.False(r.Ok);
            Check.Equal(LlmErrorKind.ServerError, r.ErrorKind);
            Check.Equal(3, r.Attempts);
            Check.Equal(3, h.Requests.Count);
            Check.Equal(TimeSpan.FromMilliseconds(100), d.Waits[0]);
            Check.Equal(TimeSpan.FromMilliseconds(200), d.Waits[1]);
        });

        yield return ("OpenAI: 400 unsupported parameter -> BadRequest with API message, not retried", async () =>
        {
            var h = FakeHttpMessageHandler.Json(
                """{"error":{"message":"Unsupported value: 'temperature' does not support 0 with this model.","type":"invalid_request_error"}}""",
                HttpStatusCode.BadRequest);
            var r = await OpenAiClient(h).CompleteAsync("s", "u");
            Check.Equal(LlmErrorKind.BadRequest, r.ErrorKind);
            Check.Contains("temperature", r.Error ?? "");
            Check.Equal(1, h.Requests.Count);
        });

        yield return ("OpenAI: timeout -> Timeout, not retried", async () =>
        {
            var h = new FakeHttpMessageHandler(_ => throw new TaskCanceledException("timeout"));
            var r = await OpenAiClient(h, OpenAi(o => o.MaxRetries = 3)).CompleteAsync("s", "u");
            Check.True(r.TimedOut);
            Check.Equal(LlmErrorKind.Timeout, r.ErrorKind);
            Check.Equal(1, h.Requests.Count);
        });

        yield return ("OpenAI: network error retried then reported as NetworkError", async () =>
        {
            var h = new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
            var r = await OpenAiClient(h, OpenAi(o => o.MaxRetries = 1)).CompleteAsync("s", "u");
            Check.Equal(LlmErrorKind.NetworkError, r.ErrorKind);
            Check.Equal(2, r.Attempts);
        });

        yield return ("OpenAI: caller cancellation -> Cancelled, no retry", async () =>
        {
            using var cts = new CancellationTokenSource();
            var h = new FakeHttpMessageHandler(_ => { cts.Cancel(); throw new TaskCanceledException(); });
            var r = await OpenAiClient(h, OpenAi(o => o.MaxRetries = 3)).CompleteAsync("s", "u", cts.Token);
            Check.Equal(LlmErrorKind.Cancelled, r.ErrorKind);
            Check.False(r.TimedOut);
            Check.Equal(1, h.Requests.Count);
        });

        yield return ("OpenAI: non-JSON 200 body -> InvalidResponse", async () =>
        {
            var h = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>gateway</html>") });
            var r = await OpenAiClient(h).CompleteAsync("s", "u");
            Check.False(r.Ok);
            Check.Equal(LlmErrorKind.InvalidResponse, r.ErrorKind);
        });

        yield return ("OpenAI: check uses GET /models/{model} (no generation)", async () =>
        {
            var h = FakeHttpMessageHandler.Json("""{"id":"gpt-test","object":"model"}""");
            var health = await OpenAiClient(h).CheckAsync();
            Check.True(health.Reachable && health.ModelAvailable);
            Check.Equal(HttpMethod.Get, h.Requests[0].Method);
            Check.Equal("https://api.openai.com/v1/models/gpt-test", h.Requests[0].Uri?.ToString());
        });

        yield return ("OpenAI: check 404 -> reachable but model unavailable", async () =>
        {
            var h = FakeHttpMessageHandler.Json("""{"error":{"message":"The model does not exist"}}""", HttpStatusCode.NotFound);
            var health = await OpenAiClient(h).CheckAsync();
            Check.True(health.Reachable);
            Check.False(health.ModelAvailable);
        });

        yield return ("OpenAI: Info is paid, has model/params, never the key", () =>
        {
            var info = OpenAiClient(FakeHttpMessageHandler.Json("{}")).Info;
            Check.Equal("OpenAI", info.Provider);
            Check.True(info.IsPaid);
            Check.Equal("gpt-test", info.Model);
            Check.DoesNotContain(FakeKey, info.ToString());
            return Task.CompletedTask;
        });

        // ---- configuration validation ----
        yield return ("Config: OpenAI without API key -> clear error naming the env var", () =>
        {
            ExpectConfigError(() => OpenAiClient(FakeHttpMessageHandler.Json("{}"), OpenAi(o => o.ApiKey = null)), "OPENAI_API_KEY");
            return Task.CompletedTask;
        });

        yield return ("Config: API key is read from the configured environment variable", () =>
        {
            var o = OpenAi(x => { x.ApiKey = null; x.ApiKeyEnvironmentVariable = "PIL_TEST_KEY"; });
            var client = new OpenAiLlmClient(new HttpClient(FakeHttpMessageHandler.Json("{}")), Options.Create(o),
                NullLogger<OpenAiLlmClient>.Instance, env: name => name == "PIL_TEST_KEY" ? "sk-from-env-123456789" : null);
            Check.Equal("OpenAI", client.Info.Provider);
            return Task.CompletedTask;
        });

        yield return ("Config: empty model -> error", () =>
        {
            ExpectConfigError(() => OpenAiClient(FakeHttpMessageHandler.Json("{}"), OpenAi(o => o.Model = "")), "Model is empty");
            return Task.CompletedTask;
        });

        yield return ("Config: OpenAI temperature > 2 -> error", () =>
        {
            ExpectConfigError(() => OpenAiClient(FakeHttpMessageHandler.Json("{}"), OpenAi(o => o.Temperature = 2.5)), "Temperature");
            return Task.CompletedTask;
        });

        yield return ("Config: Anthropic seed -> error (not silently ignored)", () =>
        {
            ExpectConfigError(() => AnthropicClient(FakeHttpMessageHandler.Json("{}"), Anthropic(o => o.Seed = 42)), "seed");
            return Task.CompletedTask;
        });

        yield return ("Config: Anthropic temperature > 1 -> error", () =>
        {
            ExpectConfigError(() => AnthropicClient(FakeHttpMessageHandler.Json("{}"), Anthropic(o => o.Temperature = 1.5)), "between 0 and 1");
            return Task.CompletedTask;
        });

        yield return ("Config: DeepSeek seed -> error", () =>
        {
            ExpectConfigError(() => DeepSeekClient(FakeHttpMessageHandler.Json("{}"), DeepSeek(o => o.Seed = 1)), "seed");
            return Task.CompletedTask;
        });

        yield return ("Config: deepseek-reasoner with temperature -> error", () =>
        {
            ExpectConfigError(() => DeepSeekClient(FakeHttpMessageHandler.Json("{}"),
                DeepSeek(o => { o.Model = "deepseek-reasoner"; o.Temperature = 0.0; })), "does not apply temperature");
            return Task.CompletedTask;
        });

        // ================= Anthropic =================
        yield return ("Anthropic: POST /v1/messages with x-api-key + anthropic-version; system top-level", async () =>
        {
            var h = FakeHttpMessageHandler.Json(AnthropicOk);
            await AnthropicClient(h).CompleteAsync("SYS", "USER");
            var req = h.Requests.Single();
            Check.Equal("https://api.anthropic.com/v1/messages", req.Uri?.ToString());
            Check.Equal(FakeKey, req.Headers["x-api-key"]);
            Check.Equal("2023-06-01", req.Headers["anthropic-version"]);
            Check.False(req.Headers.ContainsKey("Authorization"), "Anthropic must not use Bearer auth");

            using var doc = JsonDocument.Parse(req.Body);
            var root = doc.RootElement;
            Check.Equal("claude-test", root.GetProperty("model").GetString());
            Check.Equal(16, root.GetProperty("max_tokens").GetInt32());
            Check.Equal("SYS", root.GetProperty("system").GetString());
            var msgs = root.GetProperty("messages");
            Check.Equal(1, msgs.GetArrayLength());
            Check.Equal("user", msgs[0].GetProperty("role").GetString());
            Check.Equal("USER", msgs[0].GetProperty("content").GetString());
            Check.False(root.TryGetProperty("seed", out _));
        });

        yield return ("Anthropic: parses text blocks, model, usage, stop_reason", async () =>
        {
            var r = await AnthropicClient(FakeHttpMessageHandler.Json(AnthropicOk)).CompleteAsync("s", "u");
            Check.True(r.Ok);
            Check.Equal("Legal", r.RawText);
            Check.Equal("claude-test-20260101", r.ResponseModel);
            Check.Equal(200, r.InputTokens);
            Check.Equal(3, r.OutputTokens);
            Check.Equal("end_turn", r.FinishReason);
        });

        yield return ("Anthropic: 529 overloaded is retried as ServerError", async () =>
        {
            var h = FakeHttpMessageHandler.Sequence(
                () => FakeHttpMessageHandler.Response("""{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""", (HttpStatusCode)529),
                () => FakeHttpMessageHandler.Response(AnthropicOk));
            var r = await AnthropicClient(h).CompleteAsync("s", "u");
            Check.True(r.Ok);
            Check.Equal(2, r.Attempts);
        });

        yield return ("Anthropic: error body message is extracted", async () =>
        {
            var h = FakeHttpMessageHandler.Json("""{"type":"error","error":{"type":"invalid_request_error","message":"max_tokens: field required"}}""",
                HttpStatusCode.BadRequest);
            var r = await AnthropicClient(h).CompleteAsync("s", "u");
            Check.Equal(LlmErrorKind.BadRequest, r.ErrorKind);
            Check.Contains("invalid_request_error: max_tokens: field required", r.Error ?? "");
        });

        yield return ("Anthropic: check uses GET /v1/models/{model} with auth headers", async () =>
        {
            var h = FakeHttpMessageHandler.Json("""{"id":"claude-test","type":"model"}""");
            var health = await AnthropicClient(h).CheckAsync();
            Check.True(health.ModelAvailable);
            Check.Equal("https://api.anthropic.com/v1/models/claude-test", h.Requests[0].Uri?.ToString());
            Check.Equal(FakeKey, h.Requests[0].Headers["x-api-key"]);
        });

        // ================= DeepSeek =================
        yield return ("DeepSeek: POST /chat/completions, Bearer, max_tokens, no seed", async () =>
        {
            var h = FakeHttpMessageHandler.Json(ChatCompletionOk);
            var r = await DeepSeekClient(h).CompleteAsync("SYS", "USER");
            var req = h.Requests.Single();
            Check.Equal("https://api.deepseek.com/chat/completions", req.Uri?.ToString());
            Check.Equal($"Bearer {FakeKey}", req.Headers["Authorization"]);
            using var doc = JsonDocument.Parse(req.Body);
            Check.Equal("deepseek-chat", doc.RootElement.GetProperty("model").GetString());
            Check.Equal(16, doc.RootElement.GetProperty("max_tokens").GetInt32());
            Check.False(doc.RootElement.TryGetProperty("seed", out _));
            Check.Equal("Finance", r.RawText);
            Check.Equal(123, r.InputTokens);
        });

        yield return ("DeepSeek: reasoning_content is ignored; only content is classified", async () =>
        {
            var h = FakeHttpMessageHandler.Json("""
                {"model":"deepseek-reasoner","choices":[{"message":{"role":"assistant","reasoning_content":"Thinking... Legal?","content":"Technical"},"finish_reason":"stop"}]}
                """);
            var r = await DeepSeekClient(h, DeepSeek(o => { o.Model = "deepseek-reasoner"; o.Temperature = null; })).CompleteAsync("s", "u");
            Check.Equal("Technical", r.RawText);
        });

        yield return ("DeepSeek: check lists GET /models and finds the model", async () =>
        {
            var h = FakeHttpMessageHandler.Json("""{"object":"list","data":[{"id":"deepseek-chat"},{"id":"deepseek-reasoner"}]}""");
            var health = await DeepSeekClient(h).CheckAsync();
            Check.True(health.ModelAvailable);
            Check.Equal("https://api.deepseek.com/models", h.Requests[0].Uri?.ToString());
        });

        // ================= Ollama extras =================
        yield return ("Ollama: token counts, model and done_reason are parsed", async () =>
        {
            var h = FakeHttpMessageHandler.Json("""
                {"model":"qwen3.5:4b","message":{"role":"assistant","content":"HR"},"done":true,"done_reason":"stop","prompt_eval_count":321,"eval_count":2}
                """);
            var client = new OllamaLlmClient(new HttpClient(h), Options.Create(new OllamaOptions { Model = "qwen3.5:4b" }),
                NullLogger<OllamaLlmClient>.Instance);
            var r = await client.CompleteAsync("s", "u");
            Check.Equal("HR", r.RawText);
            Check.Equal(321, r.InputTokens);
            Check.Equal(2, r.OutputTokens);
            Check.Equal("stop", r.FinishReason);
            Check.False(client.Info.IsPaid);
        });

        yield return ("Ollama: default MaxRetries=0 keeps the original single-attempt behaviour", async () =>
        {
            var h = FakeHttpMessageHandler.Json("""{"error":"busy"}""", HttpStatusCode.ServiceUnavailable);
            var client = new OllamaLlmClient(new HttpClient(h), Options.Create(new OllamaOptions { Model = "m" }),
                NullLogger<OllamaLlmClient>.Instance);
            var r = await client.CompleteAsync("s", "u");
            Check.Equal(1, h.Requests.Count);
            Check.Equal(LlmErrorKind.ServerError, r.ErrorKind);
            Check.Contains("busy", r.Error ?? "");
        });
    }
}
