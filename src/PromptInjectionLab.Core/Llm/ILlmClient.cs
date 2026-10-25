namespace PromptInjectionLab.Core.Llm;

public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);

    /// <summary>Checks backend reachability and whether the configured model is available.</summary>
    Task<HealthResult> CheckAsync(CancellationToken ct = default);
}
