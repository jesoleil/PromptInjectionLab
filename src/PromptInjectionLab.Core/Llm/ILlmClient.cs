namespace PromptInjectionLab.Core.Llm;

/// <summary>
/// Provider-independent model client. The classification pipeline only sees
/// this interface; HTTP schemas, auth and error mapping live in the adapters.
/// </summary>
public interface ILlmClient
{
    /// <summary>Provider, model and effective parameters, recorded with every run.</summary>
    LlmClientInfo Info { get; }

    Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);

    /// <summary>Checks backend reachability and whether the configured model is available (no generation).</summary>
    Task<HealthResult> CheckAsync(CancellationToken ct = default);
}

/// <param name="Provider">Ollama | OpenAI | Anthropic | DeepSeek.</param>
/// <param name="Endpoint">Generation endpoint URL (never contains credentials).</param>
/// <param name="ApiVersion">API version label, e.g. "2023-06-01" for Anthropic; null when the provider has none.</param>
/// <param name="IsPaid">True when calls may be billed; such runs need explicit opt-in.</param>
public sealed record LlmClientInfo(
    string Provider,
    string Model,
    string Endpoint,
    double? Temperature,
    int MaxTokens,
    int? Seed,
    string? ApiVersion,
    bool IsPaid,
    string? ExtraParameters = null);
