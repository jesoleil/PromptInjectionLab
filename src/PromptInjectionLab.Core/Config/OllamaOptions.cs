namespace PromptInjectionLab.Core.Config;

/// <summary>
/// Ollama connection settings (native /api/chat endpoint). Local Ollama needs
/// no API key. Defaults keep the original behaviour: temperature 0, seed 42,
/// 10 tokens, no retries.
/// </summary>
public sealed class OllamaOptions : ProviderOptions
{
    public const string SectionName = "Ollama";

    public OllamaOptions()
    {
        BaseUrl = "http://localhost:11434";
        Temperature = 0.0;
        Seed = 42;
        MaxRetries = 0;
    }

    public string ChatUrl => $"{Root}/api/chat";
    public string TagsUrl => $"{Root}/api/tags";
}
