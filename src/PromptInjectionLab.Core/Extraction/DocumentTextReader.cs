namespace PromptInjectionLab.Core.Extraction;

/// <summary>
/// Format-independent checks around the extractors: supported extension,
/// file existence, size limit and empty content. Every failure surfaces as a
/// <see cref="DocumentExtractionException"/> with a clear message.
/// </summary>
public sealed class DocumentTextReader
{
    private readonly IReadOnlyList<ITextExtractor> _extractors;
    private readonly long _maxFileSizeBytes;

    public DocumentTextReader(IEnumerable<ITextExtractor> extractors, long maxFileSizeBytes)
    {
        _extractors = extractors.ToList();
        _maxFileSizeBytes = maxFileSizeBytes;
    }

    public IEnumerable<string> SupportedExtensions =>
        _extractors.SelectMany(e => e.Extensions).Distinct().OrderBy(x => x);

    public async Task<ExtractedDocument> ReadAsync(string path, CancellationToken ct = default)
    {
        var extractor = _extractors.FirstOrDefault(e => e.CanHandle(path));
        if (extractor is null)
        {
            var ext = Path.GetExtension(path);
            throw new DocumentExtractionException(ExtractionFailure.UnsupportedExtension,
                $"Unsupported file type '{(ext.Length == 0 ? "(no extension)" : ext)}'. " +
                $"Supported: {string.Join(", ", SupportedExtensions)}.");
        }

        var info = new FileInfo(path);
        if (!info.Exists)
            throw new DocumentExtractionException(ExtractionFailure.FileNotFound, $"File not found: {path}");

        if (_maxFileSizeBytes > 0 && info.Length > _maxFileSizeBytes)
            throw new DocumentExtractionException(ExtractionFailure.FileTooLarge,
                $"File is {info.Length} bytes; limit is {_maxFileSizeBytes} bytes (Lab:MaxFileSizeBytes).");

        if (info.Length == 0)
            throw new DocumentExtractionException(ExtractionFailure.EmptyDocument, "File is empty (0 bytes).");

        ExtractedDocument doc;
        try
        {
            doc = await extractor.ExtractAsync(path, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DocumentExtractionException(ExtractionFailure.ReadError, $"File could not be read: {ex.Message}", ex);
        }

        if (string.IsNullOrWhiteSpace(doc.Text))
            throw new DocumentExtractionException(ExtractionFailure.EmptyDocument, "Document contains no text.");

        return doc;
    }
}
