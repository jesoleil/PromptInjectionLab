namespace PromptInjectionLab.Core.Config;

/// <summary>Selects which LLM provider adapter is used ("Llm" section).</summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>Ollama (default, local) | OpenAI | Anthropic | DeepSeek. Case-insensitive.</summary>
    public string Provider { get; set; } = "Ollama";
}

/// <summary>
/// Settings shared by every provider adapter. A null parameter (Temperature,
/// Seed) means "do not send it; use the provider default". Setting a parameter
/// a provider does not support is a configuration error, never silently dropped.
/// </summary>
public abstract class ProviderOptions
{
    public string BaseUrl { get; set; } = "";

    /// <summary>Exact model identifier sent to the provider. Required.</summary>
    public string Model { get; set; } = "";

    /// <summary>
    /// API key. Never put it in appsettings.json: use appsettings.Local.json
    /// (git-ignored), the "&lt;Section&gt;__ApiKey" environment variable, or the
    /// variable named by <see cref="ApiKeyEnvironmentVariable"/>.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Environment variable read when <see cref="ApiKey"/> is empty (e.g. OPENAI_API_KEY).</summary>
    public string? ApiKeyEnvironmentVariable { get; set; }

    /// <summary>Per-attempt HTTP timeout.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    public double? Temperature { get; set; }

    /// <summary>Maximum generated tokens (mapped to the provider's own parameter name).</summary>
    public int MaxTokens { get; set; } = 10;

    public int? Seed { get; set; }

    /// <summary>Retries for rate limits (429), transient server errors (5xx/529) and network errors. Timeouts are not retried.</summary>
    public int MaxRetries { get; set; }

    /// <summary>First retry delay; doubles on each further retry unless the server sends Retry-After.</summary>
    public int RetryBaseDelayMs { get; set; } = 1000;

    /// <summary>Upper bound for any single retry wait (also caps Retry-After).</summary>
    public int MaxRetryDelaySeconds { get; set; } = 30;

    public string Root => BaseUrl.TrimEnd('/');
}

public sealed class OpenAiOptions : ProviderOptions
{
    public const string SectionName = "OpenAI";

    public OpenAiOptions()
    {
        BaseUrl = "https://api.openai.com/v1";
        ApiKeyEnvironmentVariable = "OPENAI_API_KEY";
        MaxRetries = 2;
    }

    /// <summary>Optional "reasoning_effort" for reasoning models (e.g. minimal/low/medium/high). Not sent when null.</summary>
    public string? ReasoningEffort { get; set; }

    public string ChatUrl => $"{Root}/chat/completions";
}

public sealed class AnthropicOptions : ProviderOptions
{
    public const string SectionName = "Anthropic";

    public AnthropicOptions()
    {
        BaseUrl = "https://api.anthropic.com/v1";
        ApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";
        MaxRetries = 2;
    }

    /// <summary>Value of the required "anthropic-version" header.</summary>
    public string ApiVersion { get; set; } = "2023-06-01";

    public string MessagesUrl => $"{Root}/messages";
}

public sealed class DeepSeekOptions : ProviderOptions
{
    public const string SectionName = "DeepSeek";

    public DeepSeekOptions()
    {
        BaseUrl = "https://api.deepseek.com";
        ApiKeyEnvironmentVariable = "DEEPSEEK_API_KEY";
        MaxRetries = 2;
    }

    public string ChatUrl => $"{Root}/chat/completions";
}
