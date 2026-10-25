using PromptInjectionLab.Core.Config;
using PromptInjectionLab.Core.Llm;

namespace PromptInjectionLab.Core.Domain;

/// <summary>Run-wide metadata captured once per experiment run for reproducibility.</summary>
public sealed class RunContext
{
    public string RunId { get; init; } = Guid.NewGuid().ToString("N");
    public string Provider { get; init; } = "Ollama";
    public string ModelName { get; init; } = "";
    public double? Temperature { get; init; }
    public int MaxTokens { get; init; }
    public int? Seed { get; init; }
    public string? ApiVersion { get; init; }

    /// <summary>Provider-specific extras that affect output, e.g. "think=false".</summary>
    public string? ExtraParameters { get; init; }
    public string SystemInstructionVariant { get; init; } = "Standard";
    public string PromptTemplateVersion { get; init; } = "v1";

    /// <summary>Generation endpoint of the selected provider (column name kept for compatibility).</summary>
    public string OllamaEndpoint { get; init; } = "";
    public string PrototypeVersion { get; init; } = "";
    public string ExperimentId { get; init; } = "";
    public string DatasetVersion { get; init; } = "";
    public string ManifestSha256 { get; init; } = "";

    public static RunContext From(OllamaOptions o, LabOptions lab, string variant = "Standard") =>
        From(new LlmClientInfo("Ollama", o.Model, o.ChatUrl, o.Temperature, o.MaxTokens, o.Seed, null, false, "think=false"),
            lab, variant);

    public static RunContext From(LlmClientInfo info, LabOptions lab, string variant = "Standard",
        string? datasetVersion = null, string? manifestSha256 = null) => new()
    {
        Provider = info.Provider,
        ModelName = info.Model,
        Temperature = info.Temperature,
        MaxTokens = info.MaxTokens,
        Seed = info.Seed,
        ApiVersion = info.ApiVersion,
        ExtraParameters = info.ExtraParameters,
        SystemInstructionVariant = variant,
        PromptTemplateVersion = lab.PromptTemplateVersion,
        OllamaEndpoint = info.Endpoint,
        PrototypeVersion = lab.PrototypeVersion,
        ExperimentId = lab.ExperimentId,
        DatasetVersion = datasetVersion ?? lab.DatasetVersion,
        ManifestSha256 = manifestSha256 ?? "",
    };
}
