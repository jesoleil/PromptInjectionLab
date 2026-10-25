using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace PromptInjectionLab.Tests.Fakes;

/// <summary>
/// Small synthetic PDFs generated in memory for tests — no binary fixtures,
/// no real documents. Text PDFs use PdfPig's writer; the encrypted and
/// zero-page samples are hand-written raw PDF bytes with a valid xref table.
/// </summary>
public static class TestPdfs
{
    /// <summary>A text-based PDF; each inner array is one page, each string one line.</summary>
    public static byte[] Text(params string[][] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var lines in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            double y = 800;
            foreach (var line in lines)
            {
                page.AddText(line, 11, new PdfPoint(40, y), font);
                y -= 14;
            }
        }
        return builder.Build();
    }

    /// <summary>A valid PDF whose page has drawn graphics but no text layer (like a scan without OCR).</summary>
    public static byte[] NoTextLayer()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        page.DrawRectangle(new PdfPoint(50, 50), 200, 100, lineWidth: 2, fill: true);
        return builder.Build();
    }

    /// <summary>
    /// A PDF with a Standard security handler (RC4, R2) whose user password is
    /// NOT empty, so it cannot be opened without a password.
    /// </summary>
    public static byte[] PasswordProtected()
    {
        var o = new string('A', 64); // 32 bytes hex — arbitrary owner hash
        var u = new string('B', 64); // 32 bytes hex — does not match the empty password
        return Raw(new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>",
            $"<< /Filter /Standard /V 1 /R 2 /O <{o}> /U <{u}> /P -44 >>",
        }, "/Encrypt 4 0 R /ID [<0123456789ABCDEF0123456789ABCDEF> <0123456789ABCDEF0123456789ABCDEF>]");
    }

    /// <summary>A structurally valid PDF with an empty page tree.</summary>
    public static byte[] ZeroPages() => Raw(new[]
    {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [] /Count 0 >>",
    }, "");

    public static byte[] Corrupt() => Encoding.ASCII.GetBytes("%PDF-1.4\nthis is not really a pdf\n%%EOF");

    public static byte[] NotAPdf() => Encoding.ASCII.GetBytes("Plain text saved with a .pdf extension.");

    private static byte[] Raw(IReadOnlyList<string> objects, string trailerExtra)
    {
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var off in offsets) sb.Append($"{off:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R {trailerExtra} >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    public static string WriteTemp(byte[] bytes, string extension = ".pdf")
    {
        var p = Path.Combine(Path.GetTempPath(), $"pil_{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(p, bytes);
        return p;
    }
}
