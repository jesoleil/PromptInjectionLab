namespace PromptInjectionLab.Core.Domain;

/// <summary>One row of the dataset manifest.</summary>
public sealed class DatasetItem
{
    public string ItemId { get; set; } = "";
    public string Path { get; set; } = "";
    /// <summary>Internal ID resolved from the manifest's true_category name via <see cref="CategoryRegistry"/>.</summary>
    public int TrueCategoryId { get; set; }

    /// <summary>"clean" in Phase 1. "attacked" is introduced in a later phase.</summary>
    public string Variant { get; set; } = "clean";

    /// <summary>Reserved for the attack phase; null in Phase 1.</summary>
    public string? AttackTemplateId { get; set; }

    /// <summary>Reserved for targeted-attack evaluation; null in Phase 1.</summary>
    public int? TargetCategoryId { get; set; }
}
