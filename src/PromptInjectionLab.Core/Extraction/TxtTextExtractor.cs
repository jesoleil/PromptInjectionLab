namespace PromptInjectionLab.Core.Extraction;

/// <summary>
/// Plain-text (.txt) extractor. No sanitization is performed — the raw file
/// text is returned as-is (BOM-aware UTF-8 / UTF-16 detection by StreamReader).
/// </summary>
public sealed class TxtTextExtractor : ITextExtractor
{
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".txt" };

    public bool CanHandle(string path)
        => string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase);

    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken ct = default)
        => new(await File.ReadAllTextAsync(path, ct), "txt");
}
