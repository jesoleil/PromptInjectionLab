using PromptInjectionLab.Core.Llm;

namespace PromptInjectionLab.Tests.Fakes;

/// <summary>Deterministic ILlmClient for tests — no network, no Ollama required.</summary>
public sealed class FakeLlmClient : ILlmClient
{
    private readonly Func<string, string, LlmResponse> _responder;

    public FakeLlmClient(Func<string, string, LlmResponse> responder) => _responder = responder;

    public static FakeLlmClient Returning(string rawText) =>
        new((_, _) => new LlmResponse(true, rawText, null, 1));

    public LlmClientInfo Info { get; } =
        new("Fake", "fake-model", "http://fake.invalid/chat", 0.0, 10, 42, null, IsPaid: false);

    public Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
        => Task.FromResult(_responder(systemPrompt, userPrompt));

    public Task<HealthResult> CheckAsync(CancellationToken ct = default)
        => Task.FromResult(new HealthResult(true, true, new[] { "fake-model" }, null));
}
