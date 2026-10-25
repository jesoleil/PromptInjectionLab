namespace PromptInjectionLab.Core.Config;

/// <summary>
/// Ollama connection settings (native /api/chat endpoint).
/// No secrets are stored in source. ApiKey, if ever needed (e.g. Ollama behind
/// an authenticating proxy), is read from appsettings.Local.json or environment variables.
/// </summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    /// <summary>Base URL of the Ollama server, e.g. http://localhost:11434</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Model tag to use, e.g. "qwen3.5:4b". Must be pulled on the Ollama host.</summary>
    public string Model { get; set; } = "";

    /// <summary>
    /// Optional API key. Local Ollama does not require one.
    /// Never committed to source control.
    /// </summary>
    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 120;
    public double Temperature { get; set; } = 0.0;

    /// <summary>Maximum generated tokens (sent as Ollama's options.num_predict).</summary>
    public int MaxTokens { get; set; } = 10;

    /// <summary>
    /// Seed for determinism attempts. NOTE: identical outputs across runs are
    /// not guaranteed by the model even with a fixed seed and temperature 0.
    /// </summary>
    public int? Seed { get; set; } = 42;

    public string ChatUrl => $"{BaseUrl.TrimEnd('/')}/api/chat";
    public string TagsUrl => $"{BaseUrl.TrimEnd('/')}/api/tags";
}
