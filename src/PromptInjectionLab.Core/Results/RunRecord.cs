namespace PromptInjectionLab.Core.Results;

/// <summary>
/// One classification result row. The property order is the CSV column order.
/// These fields are sufficient to reproduce a single run: model, parameters,
/// seed, prompt version/hash and input hash are all captured.
/// </summary>
public sealed class RunRecord
{
    public string RunId { get; set; } = "";
    public string TimestampUtc { get; set; } = "";
    public string PrototypeVersion { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string ContentSha256 { get; set; } = "";
    public string Variant { get; set; } = "";
    public string ExperimentCondition { get; set; } = "";
    public string? AttackTemplateId { get; set; }
    public int? TargetCategoryId { get; set; }
    public int TrueCategoryId { get; set; }
    public string TrueCategory { get; set; } = "";
    public string ModelName { get; set; } = "";
    public double Temperature { get; set; }
    public int MaxTokens { get; set; }
    public int? Seed { get; set; }
    public string SystemInstructionVariant { get; set; } = "";
    public string PromptTemplateVersion { get; set; } = "";
    public string PromptSha256 { get; set; } = "";
    public string OllamaEndpoint { get; set; } = "";
    public string RawModelOutput { get; set; } = "";
    public int? PredictedCategoryId { get; set; }

    /// <summary>Category name of a Valid prediction; empty for any invalid outcome.</summary>
    public string PredictedCategory { get; set; } = "";
    public string ParseStatus { get; set; } = "";
    public bool IsCorrect { get; set; }
    public long LatencyMs { get; set; }
    public string? ErrorMessage { get; set; }
}
