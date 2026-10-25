namespace PromptInjectionLab.Core.Extraction;

public interface ITextExtractor
{
    /// <summary>Lower-case extensions handled by this extractor, including the dot (e.g. ".pdf").</summary>
    IReadOnlyCollection<string> Extensions { get; }

    bool CanHandle(string path);

    /// <summary>
    /// Extracts the document text. Throws <see cref="DocumentExtractionException"/>
    /// for expected, classifiable failures (corrupt, password-protected, no text...).
    /// </summary>
    Task<ExtractedDocument> ExtractAsync(string path, CancellationToken ct = default);
}
