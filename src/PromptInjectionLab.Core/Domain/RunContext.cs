using PromptInjectionLab.Core.Config;

namespace PromptInjectionLab.Core.Domain;

/// <summary>Run-wide metadata captured once per experiment run for reproducibility.</summary>
public sealed class RunContext
{
    public string RunId { get; init; } = Guid.NewGuid().ToString("N");
    public string ModelName { get; init; } = "";
    public double Temperature { get; init; }
    public int MaxTokens { get; init; }
    public int? Seed { get; init; }
    public string SystemInstructionVariant { get; init; } = "Standard";
    public string PromptTemplateVersion { get; init; } = "v1";
    public string OllamaEndpoint { get; init; } = "";
    public string PrototypeVersion { get; init; } = "";

    public static RunContext From(OllamaOptions o, LabOptions lab, string variant = "Standard") => new()
    {
        ModelName = o.Model,
        Temperature = o.Temperature,
        MaxTokens = o.MaxTokens,
        Seed = o.Seed,
        SystemInstructionVariant = variant,
        PromptTemplateVersion = lab.PromptTemplateVersion,
        OllamaEndpoint = o.ChatUrl,
        PrototypeVersion = lab.PrototypeVersion,
    };
}
