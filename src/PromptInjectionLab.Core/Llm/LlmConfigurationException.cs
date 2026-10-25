namespace PromptInjectionLab.Core.Llm;

/// <summary>
/// Invalid provider configuration (missing model or API key, a parameter the
/// provider does not support, unknown provider). Raised before any request is
/// sent; messages never contain secret values.
/// </summary>
public sealed class LlmConfigurationException : Exception
{
    public LlmConfigurationException(string message) : base(message) { }
}
