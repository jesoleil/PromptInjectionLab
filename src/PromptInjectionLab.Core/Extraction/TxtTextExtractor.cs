namespace PromptInjectionLab.Core.Extraction;

/// <summary>
/// Phase 1: plain-text (.txt) only. No sanitization is performed — the raw file
/// text is returned as-is. (PDF/DOCX extractors are added in a later phase.)
/// </summary>
public sealed class TxtTextExtractor : ITextExtractor
{
    public bool CanHandle(string path)
        => string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase);

    public async Task<string> ExtractAsync(string path, CancellationToken ct = default)
        => await File.ReadAllTextAsync(path, ct);
}
