using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace PromptInjectionLab.Core.Extraction;

/// <summary>
/// Extracts the text layer of text-based PDFs with PdfPig (Apache-2.0).
/// Pages are read in order and separated by a blank line; within a page,
/// <see cref="ContentOrderTextExtractor"/> rebuilds lines and word spacing.
///
/// Limitations (by design, Phase 1):
///  - Scanned / image-only PDFs have no text layer and are rejected with
///    <see cref="ExtractionFailure.NoExtractableText"/>; OCR is NOT performed.
///  - Password-protected PDFs are rejected; no password is ever supplied.
///  - Only text is read. Annotations, form fields, attachments and embedded
///    JavaScript are ignored and never executed.
/// </summary>
public sealed class PdfTextExtractor : ITextExtractor
{
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".pdf" };

    public bool CanHandle(string path)
        => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken ct = default)
    {
        // Read into memory first so file-system errors are reported separately
        // from PDF parsing errors, and the file handle is not held while parsing.
        var bytes = await File.ReadAllBytesAsync(path, ct);
        return Extract(bytes, ct);
    }

    /// <summary>Extracts text from in-memory PDF bytes (used directly by tests).</summary>
    public static ExtractedDocument Extract(byte[] bytes, CancellationToken ct = default)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(bytes);
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new DocumentExtractionException(ExtractionFailure.PasswordProtected,
                "PDF is password-protected; password-protected PDFs are not supported.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DocumentExtractionException(ExtractionFailure.CorruptDocument,
                $"PDF could not be parsed (corrupt or not a PDF): {ex.Message}", ex);
        }

        using (document)
        {
            int pageCount;
            var sb = new StringBuilder();
            try
            {
                pageCount = document.NumberOfPages;
                foreach (var page in document.GetPages())
                {
                    ct.ThrowIfCancellationRequested();
                    var pageText = ContentOrderTextExtractor.GetText(page);
                    if (string.IsNullOrWhiteSpace(pageText)) continue;
                    if (sb.Length > 0) sb.Append("\n\n");
                    sb.Append(pageText.Trim());
                }
            }
            catch (PdfDocumentEncryptedException ex)
            {
                throw new DocumentExtractionException(ExtractionFailure.PasswordProtected,
                    "PDF is password-protected; password-protected PDFs are not supported.", ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new DocumentExtractionException(ExtractionFailure.CorruptDocument,
                    $"PDF pages could not be read: {ex.Message}", ex);
            }

            if (pageCount == 0)
                throw new DocumentExtractionException(ExtractionFailure.EmptyDocument, "PDF has no pages.");

            var text = sb.ToString();
            if (string.IsNullOrWhiteSpace(text))
                throw new DocumentExtractionException(ExtractionFailure.NoExtractableText,
                    $"PDF has {pageCount} page(s) but no extractable text layer. " +
                    "It is probably scanned/image-based; OCR is not supported.");

            return new ExtractedDocument(text, "pdf", pageCount);
        }
    }
}
