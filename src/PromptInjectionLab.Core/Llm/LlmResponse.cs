namespace PromptInjectionLab.Core.Llm;

public sealed record LlmResponse(bool Ok, string RawText, string? Error, long LlmLatencyMs, bool TimedOut = false);
