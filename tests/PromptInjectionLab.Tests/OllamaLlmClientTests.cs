using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PromptInjectionLab.Core.Config;
using PromptInjectionLab.Core.Llm;
using PromptInjectionLab.Tests.Fakes;

namespace PromptInjectionLab.Tests;

public static class OllamaLlmClientTests
{
    private const string ChatOk =
        """{"model":"qwen3.5:4b","message":{"role":"assistant","content":"Technical"},"done":true}""";

    private static OllamaOptions Options() => new()
    {
        BaseUrl = "http://localhost:11434/",
        Model = "qwen3.5:4b",
        Temperature = 0.0,
        MaxTokens = 10,
        Seed = 42,
    };

    private static OllamaLlmClient Client(FakeHttpMessageHandler handler, OllamaOptions? o = null) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(o ?? Options()),
            NullLogger<OllamaLlmClient>.Instance);

    public static IEnumerable<(string, Func<Task>)> Tests()
    {
        yield return ("Ollama: POSTs to /api/chat", async () =>
        {
            var h = FakeHttpMessageHandler.Json(ChatOk);
            await Client(h).CompleteAsync("SYS", "USER");
            Check.Equal(1, h.Requests.Count);
            Check.Equal(HttpMethod.Post, h.Requests[0].Method);
            Check.Equal("http://localhost:11434/api/chat", h.Requests[0].Uri?.ToString());
        });

        yield return ("Ollama: request body has model, stream=false, think=false, messages, options", async () =>
        {
            var h = FakeHttpMessageHandler.Json(ChatOk);
            await Client(h).CompleteAsync("SYS", "USER");

            using var doc = JsonDocument.Parse(h.Requests[0].Body);
            var root = doc.RootElement;
            Check.Equal("qwen3.5:4b", root.GetProperty("model").GetString());
            Check.Equal(JsonValueKind.False, root.GetProperty("stream").ValueKind);
            Check.Equal(JsonValueKind.False, root.GetProperty("think").ValueKind);

            var msgs = root.GetProperty("messages");
            Check.Equal(2, msgs.GetArrayLength());
            Check.Equal("system", msgs[0].GetProperty("role").GetString());
            Check.Equal("SYS", msgs[0].GetProperty("content").GetString());
            Check.Equal("user", msgs[1].GetProperty("role").GetString());
            Check.Equal("USER", msgs[1].GetProperty("content").GetString());

            var opts = root.GetProperty("options");
            Check.AlmostEqual(0.0, opts.GetProperty("temperature").GetDouble(), 1e-9);
            Check.Equal(10, opts.GetProperty("num_predict").GetInt32());
            Check.Equal(42, opts.GetProperty("seed").GetInt32());

            // OpenAI-compatible fields must no longer be sent.
            Check.False(root.TryGetProperty("max_tokens", out _), "max_tokens present");
        });

        yield return ("Ollama: reads answer from message.content", async () =>
        {
            var resp = await Client(FakeHttpMessageHandler.Json(ChatOk)).CompleteAsync("s", "u");
            Check.True(resp.Ok);
            Check.Equal("Technical", resp.RawText);
            Check.Null(resp.Error);
            Check.False(resp.TimedOut);
        });

        yield return ("Ollama: missing message -> Ok with empty text", async () =>
        {
            var resp = await Client(FakeHttpMessageHandler.Json("""{"done":true}""")).CompleteAsync("s", "u");
            Check.True(resp.Ok);
            Check.Equal("", resp.RawText);
        });

        yield return ("Ollama: HTTP 500 -> not Ok with status in error", async () =>
        {
            var h = FakeHttpMessageHandler.Json("""{"error":"boom"}""", HttpStatusCode.InternalServerError);
            var resp = await Client(h).CompleteAsync("s", "u");
            Check.False(resp.Ok);
            Check.Equal("", resp.RawText);
            Check.Contains("HTTP 500", resp.Error ?? "");
            Check.False(resp.TimedOut);
        });

        yield return ("Ollama: HttpClient timeout -> TimedOut", async () =>
        {
            var h = new FakeHttpMessageHandler(_ => throw new TaskCanceledException("timeout"));
            var resp = await Client(h).CompleteAsync("s", "u");
            Check.False(resp.Ok);
            Check.True(resp.TimedOut);
        });

        yield return ("Ollama: CheckAsync finds configured model via /api/tags", async () =>
        {
            var h = FakeHttpMessageHandler.Json("""{"models":[{"name":"qwen3.5:4b"},{"name":"other:1b"}]}""");
            var r = await Client(h).CheckAsync();
            Check.Equal("http://localhost:11434/api/tags", h.Requests[0].Uri?.ToString());
            Check.True(r.Reachable);
            Check.True(r.ModelAvailable);
            Check.Equal(2, r.AvailableModels.Count);
        });

        yield return ("Ollama: CheckAsync reports missing model", async () =>
        {
            var h = FakeHttpMessageHandler.Json("""{"models":[{"name":"qwen3:4b"}]}""");
            var r = await Client(h).CheckAsync();
            Check.True(r.Reachable);
            Check.False(r.ModelAvailable);
        });
    }
}
