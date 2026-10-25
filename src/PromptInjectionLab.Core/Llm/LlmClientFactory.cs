using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PromptInjectionLab.Core.Config;

namespace PromptInjectionLab.Core.Llm;

/// <summary>
/// Builds the <see cref="ILlmClient"/> selected by Llm:Provider. Only the
/// selected adapter is constructed, so missing keys for other providers do not
/// matter. Default is Ollama (local, free).
/// </summary>
public static class LlmClientFactory
{
    public const string HttpClientName = "llm";

    public static readonly string[] Providers = { "Ollama", "OpenAI", "Anthropic", "DeepSeek" };

    public static ILlmClient Create(IServiceProvider sp)
    {
        var provider = sp.GetRequiredService<IOptions<LlmOptions>>().Value.Provider?.Trim();
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
        var logs = sp.GetRequiredService<ILoggerFactory>();

        return (provider ?? "").ToLowerInvariant() switch
        {
            "" or "ollama" => new OllamaLlmClient(http, sp.GetRequiredService<IOptions<OllamaOptions>>(),
                logs.CreateLogger<OllamaLlmClient>()),
            "openai" => new OpenAiLlmClient(http, sp.GetRequiredService<IOptions<OpenAiOptions>>(),
                logs.CreateLogger<OpenAiLlmClient>()),
            "anthropic" => new AnthropicLlmClient(http, sp.GetRequiredService<IOptions<AnthropicOptions>>(),
                logs.CreateLogger<AnthropicLlmClient>()),
            "deepseek" => new DeepSeekLlmClient(http, sp.GetRequiredService<IOptions<DeepSeekOptions>>(),
                logs.CreateLogger<DeepSeekLlmClient>()),
            _ => throw new LlmConfigurationException(
                $"Unknown Llm:Provider '{provider}'. Supported: {string.Join(", ", Providers)}."),
        };
    }
}
