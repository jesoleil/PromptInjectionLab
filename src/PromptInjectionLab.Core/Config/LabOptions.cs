namespace PromptInjectionLab.Core.Config;

public sealed class LabOptions
{
    public const string SectionName = "Lab";

    /// <summary>
    /// Max characters of document text sent to the model. Longer text is reduced
    /// with a safe head+tail truncation (see <see cref="ContentReducer"/>).
    /// </summary>
    public int MaxContentLength { get; set; } = 4000;

    /// <summary>Input files larger than this are rejected before extraction (0 = no limit).</summary>
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;

    public string PrototypeVersion { get; set; } = "0.2.0";

    /// <summary>Free-form experiment label recorded in every row (CLI: --experiment).</summary>
    public string ExperimentId { get; set; } = "default";

    /// <summary>Dataset version label recorded in every row (CLI: --dataset-version). The manifest SHA-256 is recorded too.</summary>
    public string DatasetVersion { get; set; } = "";

    /// <summary>Prompt template version label recorded with each run for reproducibility.</summary>
    public string PromptTemplateVersion { get; set; } = "v2";
}
