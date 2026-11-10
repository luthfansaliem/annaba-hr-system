using System.Globalization;
using System.Text;

namespace AnnabaDigital.Application.Hr;

/// <summary>
/// A tiny single-page PDF writer (no dependency): Helvetica for words, Courier for numbers so right-aligning needs no font metrics. Text is limited to Latin-1; anything else becomes "?".
/// Enough for a payslip; not a general layout engine.
/// </summary>
public sealed class SimplePdf
{
    private readonly StringBuilder _content = new();
    public const double Width = 595, Height = 842;

    private static string Esc(string s)
    {
        var b = new StringBuilder(s.Length);
        foreach (var c in s) b.Append(c switch { '\\' => "\\\\", '(' => "\\(", ')' => "\\)", '\r' or '\n' => " ", _ when c > 255 || c < 32 => "?", _ => c.ToString() });
        return b.ToString();
    }

    private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Text with its left edge at x (points from the left, y from the top of the page).</summary>
    public SimplePdf Text(string text, double x, double y, double size = 10, bool bold = false) { _content.Append($"BT /{(bold ? "F2" : "F1")} {N(size)} Tf {N(x)} {N(Height - y)} Td ({Esc(text)}) Tj ET\n"); return this; }

    /// <summary>Number or amount whose right edge is at x (Courier: every character is 0.6 em wide).</summary>
    public SimplePdf Right(string text, double x, double y, double size = 10) { var w = text.Length * 0.6 * size; _content.Append($"BT /F3 {N(size)} Tf {N(x - w)} {N(Height - y)} Td ({Esc(text)}) Tj ET\n"); return this; }

    public SimplePdf Line(double x1, double y, double x2, double width = 0.5) { _content.Append($"{N(width)} w {N(x1)} {N(Height - y)} m {N(x2)} {N(Height - y)} l S\n"); return this; }

    public byte[] Build()
    {
        var latin1 = Encoding.Latin1; var ms = new MemoryStream(); var offsets = new List<long>();
        void W(string s) { var b = latin1.GetBytes(s); ms.Write(b, 0, b.Length); }
        void Obj(int n, string body) { offsets.Add(ms.Position); W($"{n} 0 obj\n{body}\nendobj\n"); }
        W("%PDF-1.4\n%âãÏÓ\n");
        var stream = _content.ToString();
        Obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Obj(3, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(Width)} {N(Height)}] /Contents 4 0 R /Resources << /Font << /F1 5 0 R /F2 6 0 R /F3 7 0 R >> >> >>");
        Obj(4, $"<< /Length {latin1.GetByteCount(stream)} >>\nstream\n{stream}endstream");
        Obj(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Obj(6, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        Obj(7, "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");
        var xref = ms.Position;
        W($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) W($"{o:D10} 00000 n \n");
        W($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }
}
