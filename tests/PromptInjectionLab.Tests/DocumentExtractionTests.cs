using System.Text;
using PromptInjectionLab.Core.Extraction;
using PromptInjectionLab.Tests.Fakes;

namespace PromptInjectionLab.Tests;

public static class DocumentExtractionTests
{
    private static DocumentTextReader Reader(long maxBytes = 20 * 1024 * 1024) =>
        new(new ITextExtractor[] { new TxtTextExtractor(), new PdfTextExtractor() }, maxBytes);

    private static async Task<ExtractionFailure> ExpectFailure(Func<Task> act)
    {
        try
        {
            await act();
        }
        catch (DocumentExtractionException ex)
        {
            Check.NotNullOrEmpty(ex.Message);
            return ex.Failure;
        }
        throw new Exception("Expected DocumentExtractionException, none thrown.");
    }

    private static async Task WithTemp(byte[] bytes, string ext, Func<string, Task> body)
    {
        var path = TestPdfs.WriteTemp(bytes, ext);
        try { await body(path); }
        finally { File.Delete(path); }
    }

    public static IEnumerable<(string, Func<Task>)> Tests()
    {
        // ---- TXT ----
        yield return ("Extract: TXT returns text, type=txt, no page count", () =>
            WithTemp(Encoding.UTF8.GetBytes("Invoice total 100."), ".txt", async p =>
            {
                var doc = await Reader().ReadAsync(p);
                Check.Equal("Invoice total 100.", doc.Text);
                Check.Equal("txt", doc.DocumentType);
                Check.Null(doc.PageCount);
            }));

        yield return ("Extract: TXT keeps UTF-8 (Turkish chars)", () =>
            WithTemp(Encoding.UTF8.GetBytes("Çalışan izin politikası — İK"), ".txt", async p =>
            {
                var doc = await Reader().ReadAsync(p);
                Check.Equal("Çalışan izin politikası — İK", doc.Text);
            }));

        yield return ("Extract: empty TXT (0 bytes) -> EmptyDocument", () =>
            WithTemp(Array.Empty<byte>(), ".txt", async p =>
                Check.Equal(ExtractionFailure.EmptyDocument, await ExpectFailure(() => Reader().ReadAsync(p)))));

        yield return ("Extract: whitespace-only TXT -> EmptyDocument", () =>
            WithTemp(Encoding.UTF8.GetBytes("  \r\n\t "), ".txt", async p =>
                Check.Equal(ExtractionFailure.EmptyDocument, await ExpectFailure(() => Reader().ReadAsync(p)))));

        // ---- Path / type / size ----
        yield return ("Extract: missing file -> FileNotFound", async () =>
        {
            var missing = Path.Combine(Path.GetTempPath(), $"pil_missing_{Guid.NewGuid():N}.pdf");
            Check.Equal(ExtractionFailure.FileNotFound, await ExpectFailure(() => Reader().ReadAsync(missing)));
        });

        yield return ("Extract: .docx -> UnsupportedExtension with supported list", async () =>
        {
            try
            {
                await Reader().ReadAsync("report.docx");
                throw new Exception("expected failure");
            }
            catch (DocumentExtractionException ex)
            {
                Check.Equal(ExtractionFailure.UnsupportedExtension, ex.Failure);
                Check.Contains("'.docx'", ex.Message);
                Check.Contains(".pdf, .txt", ex.Message);
            }
        });

        yield return ("Extract: no extension -> UnsupportedExtension", async () =>
            Check.Equal(ExtractionFailure.UnsupportedExtension, await ExpectFailure(() => Reader().ReadAsync("README"))));

        yield return ("Extract: extension check is case-insensitive (.PDF)", () =>
            WithTemp(TestPdfs.Text(new[] { "Upper case extension." }), ".PDF", async p =>
                Check.Equal("pdf", (await Reader().ReadAsync(p)).DocumentType)));

        yield return ("Extract: file over MaxFileSizeBytes -> FileTooLarge", () =>
            WithTemp(Encoding.UTF8.GetBytes(new string('x', 2000)), ".txt", async p =>
                Check.Equal(ExtractionFailure.FileTooLarge, await ExpectFailure(() => Reader(maxBytes: 1000).ReadAsync(p)))));

        // ---- PDF ----
        yield return ("Extract: text PDF -> text, type=pdf, PageCount=1", () =>
            WithTemp(TestPdfs.Text(new[] { "SERVICE AGREEMENT", "Governing law and termination." }), ".pdf", async p =>
            {
                var doc = await Reader().ReadAsync(p);
                Check.Equal("pdf", doc.DocumentType);
                Check.Equal(1, doc.PageCount);
                Check.Contains("SERVICE AGREEMENT", doc.Text);
                Check.Contains("Governing law and termination.", doc.Text);
            }));

        yield return ("Extract: multi-page PDF keeps page order and count", () =>
            WithTemp(TestPdfs.Text(new[] { "Page one alpha" }, new[] { "Page two beta" }, new[] { "Page three gamma" }), ".pdf", async p =>
            {
                var doc = await Reader().ReadAsync(p);
                Check.Equal(3, doc.PageCount);
                var i1 = doc.Text.IndexOf("alpha", StringComparison.Ordinal);
                var i2 = doc.Text.IndexOf("beta", StringComparison.Ordinal);
                var i3 = doc.Text.IndexOf("gamma", StringComparison.Ordinal);
                Check.True(i1 >= 0 && i1 < i2 && i2 < i3, $"order wrong: '{doc.Text}'");
            }));

        yield return ("Extract: PDF words keep their spaces", () =>
            WithTemp(TestPdfs.Text(new[] { "Total due 1980.00 Net 30" }), ".pdf", async p =>
                Check.Contains("Total due 1980.00 Net 30", (await Reader().ReadAsync(p)).Text)));

        yield return ("Extract: PDF without text layer -> NoExtractableText (OCR not supported)", () =>
            WithTemp(TestPdfs.NoTextLayer(), ".pdf", async p =>
            {
                try
                {
                    await Reader().ReadAsync(p);
                    throw new Exception("expected failure");
                }
                catch (DocumentExtractionException ex)
                {
                    Check.Equal(ExtractionFailure.NoExtractableText, ex.Failure);
                    Check.Contains("OCR", ex.Message);
                }
            }));

        yield return ("Extract: password-protected PDF -> PasswordProtected", () =>
            WithTemp(TestPdfs.PasswordProtected(), ".pdf", async p =>
                Check.Equal(ExtractionFailure.PasswordProtected, await ExpectFailure(() => Reader().ReadAsync(p)))));

        yield return ("Extract: corrupt PDF -> CorruptDocument", () =>
            WithTemp(TestPdfs.Corrupt(), ".pdf", async p =>
                Check.Equal(ExtractionFailure.CorruptDocument, await ExpectFailure(() => Reader().ReadAsync(p)))));

        yield return ("Extract: plain text renamed to .pdf -> CorruptDocument", () =>
            WithTemp(TestPdfs.NotAPdf(), ".pdf", async p =>
                Check.Equal(ExtractionFailure.CorruptDocument, await ExpectFailure(() => Reader().ReadAsync(p)))));

        yield return ("Extract: 0-byte .pdf -> EmptyDocument", () =>
            WithTemp(Array.Empty<byte>(), ".pdf", async p =>
                Check.Equal(ExtractionFailure.EmptyDocument, await ExpectFailure(() => Reader().ReadAsync(p)))));

        yield return ("Extract: PDF with zero pages -> EmptyDocument or CorruptDocument", () =>
            WithTemp(TestPdfs.ZeroPages(), ".pdf", async p =>
            {
                var f = await ExpectFailure(() => Reader().ReadAsync(p));
                Check.True(f is ExtractionFailure.EmptyDocument or ExtractionFailure.CorruptDocument, $"got {f}");
            }));
    }
}
