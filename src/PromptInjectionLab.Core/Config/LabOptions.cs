namespace PromptInjectionLab.Core.Config;

public sealed class LabOptions
{
    public const string SectionName = "Lab";

    /// <summary>
    /// Max characters of document text sent to the model. Longer text is reduced
    /// with a safe head+tail truncation (see <see cref="ContentReducer"/>).
    /// </summary>
    public int MaxContentLength { get; set; } = 4000;

    public string PrototypeVersion { get; set; } = "0.1.0";

    /// <summary>Prompt template version label recorded with each run for reproducibility.</summary>
    public string PromptTemplateVersion { get; set; } = "v2";
}
