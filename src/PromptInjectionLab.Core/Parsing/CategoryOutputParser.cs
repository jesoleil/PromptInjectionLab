using PromptInjectionLab.Core.Domain;

namespace PromptInjectionLab.Core.Parsing;

/// <summary>
/// Maps raw model text to a category by exact name match.
/// Tolerated: surrounding whitespace, letter case and trailing punctuation
/// (e.g. "technical." -> Technical). Anything else is reported honestly and is
/// never treated as a correct prediction by downstream metrics:
/// a single word not in the list -> OutOfRange; multi-word text -> Unparseable.
/// </summary>
public sealed class CategoryOutputParser : ICategoryOutputParser
{
    private static readonly char[] TrailingPunctuation = { '.', ',', ';', ':', '!', '?' };

    public (int? CategoryId, ParseStatus Status) Parse(string rawText, CategoryRegistry categories)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return (null, ParseStatus.Empty);

        var candidate = rawText.Trim().TrimEnd(TrailingPunctuation).TrimEnd();
        if (candidate.Length == 0)
            return (null, ParseStatus.Unparseable);

        var category = categories.FindByName(candidate);
        if (category is not null)
            return (category.Id, ParseStatus.Valid);

        return candidate.Any(char.IsWhiteSpace)
            ? (null, ParseStatus.Unparseable)
            : (null, ParseStatus.OutOfRange);
    }
}
