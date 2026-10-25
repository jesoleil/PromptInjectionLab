namespace PromptInjectionLab.Core.Llm;

public sealed record HealthResult(
    bool Reachable,
    bool ModelAvailable,
    IReadOnlyList<string> AvailableModels,
    string? Error);
