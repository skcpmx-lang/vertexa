using System.Globalization;
using System.Text;

namespace Merqo.Vertexa.Reporting.Pdf;

/// <summary>
/// Minimal, deterministic PDF 1.4 writer (WinAnsi, base-14 fonts). Written in-house so
/// reports have no third-party PDF dependency and no licence surface. Text is placed in
/// a top-left origin coordinate system and converted internally to PDF user space.
/// </summary>
public sealed class PdfWriter
{
    private sealed class Page
    {
        internal readonly StringBuilder Ops = new();
        internal double Width = 595.276;   // A4 width in points
        internal double Height = 841.89;   // A4 height in points
    }

    private readonly List<Page> _pages = new();
    private Page? _current;

    public double PageWidth => _current?.Width ?? 595.276;
    public double PageHeight => _current?.Height ?? 841.89;
    public int PageCount => _pages.Count;

    public void AddPage(double width = 595.276, double height = 841.89)
    {
        _current = new Page { Width = width, Height = height };
        _pages.Add(_current);
    }

    public void Text(double x, double top, string text, double size = 10, bool bold = false, Rgb? color = null)
    {
        if (_current is null) AddPage();
        var rgb = color ?? new Rgb(0.1, 0.1, 0.1);
        var y = _current!.Height - top;
        _current.Ops.AppendLine(FormattableString.Invariant(
            $"BT /{(bold ? "F2" : "F1")} {size:0.##} Tf {rgb.R:0.###} {rgb.G:0.###} {rgb.B:0.###} rg 1 0 0 1 {x:0.##} {y:0.##} Tm ({Escape(text)}) Tj ET"));
    }

    public void Rect(double x, double top, double width, double height, Rgb fill)
    {
        if (_current is null) AddPage();
        var y = _current!.Height - top - height;
        _current.Ops.AppendLine(FormattableString.Invariant(
            $"{fill.R:0.###} {fill.G:0.###} {fill.B:0.###} rg {x:0.##} {y:0.##} {width:0.##} {height:0.##} re f"));
        _current.Ops.AppendLine("0 0 0 RG 0.5 w");
    }

    public void Line(double x1, double top1, double x2, double top2, Rgb? stroke = null, double width = 0.5)
    {
        if (_current is null) AddPage();
        var rgb = stroke ?? new Rgb(0.75, 0.78, 0.82);
        var y1 = _current!.Height - top1;
        var y2 = _current.Height - top2;
        _current.Ops.AppendLine(FormattableString.Invariant(
            $"{rgb.R:0.###} {rgb.G:0.###} {rgb.B:0.###} RG {width:0.##} w {x1:0.##} {y1:0.##} m {x2:0.##} {y2:0.##} l S"));
    }

    public byte[] Build()
    {
        if (_pages.Count == 0) AddPage();

        var objectCount = 4 + (_pages.Count * 2);
        var objects = new List<string>(objectCount);
        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");                                     // 1
        var kids = string.Join(" ", Enumerable.Range(0, _pages.Count).Select(i => $"{5 + (i * 2)} 0 R"));
        objects.Add($"<< /Type /Pages /Kids [ {kids} ] /Count {_pages.Count} >>");              // 2
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");       // 3
        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");  // 4

        foreach (var page in _pages)
        {
            var content = page.Ops.ToString();
            objects.Add(FormattableString.Invariant(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {page.Width:0.##} {page.Height:0.##}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {objects.Count + 2} 0 R >>"));
            var stream = $"<< /Length {Encoding.WinAnsi.GetByteCount(content)} >>\nstream\n{content}endstream";
            objects.Add(stream);
        }

        var builder = new MemoryStream();
        void Write(string ascii) => builder.Write(Encoding.ASCII.GetBytes(ascii), 0, ascii.Length);

        Write("%PDF-1.4\n");
        // Binary comment marks the file as containing 8-bit data (PDF spec §7.5.2).
        builder.Write(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }, 0, 6);
        var offsets = new List<long>(objects.Count);
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(builder.Length);
            Write($"{i + 1} 0 obj\n");
            var payload = objects[i];
            var latin = Encoding.Latin1; builder.Write(latin.GetBytes(payload), 0, latin.GetByteCount(payload));
            Write("\nendobj\n");
        }

        var xrefOffset = builder.Length;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            Write($"{offset:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return builder.ToArray();
    }

    /// <summary>Escapes PDF string delimiters and maps unencodable characters deterministically.</summary>
    internal static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '(': builder.Append("\\("); break;
                case ')': builder.Append("\\)"); break;
                case '\\': builder.Append("\\\\"); break;
                case '\r': case '\n': builder.Append(' '); break;
                case '\t': builder.Append(' '); break;
                default:
                    if (ch < 128) builder.Append(ch);
                    else builder.Append(MapWinAnsi(ch));
                    break;
            }
        }
        return builder.ToString();
    }

    private static string MapWinAnsi(char ch) => ch switch
    {
        '\u00B0' => "\\260", // degree
        '\u00B7' => "\\267", // middle dot
        '\u2022' => "\\225", // bullet
        '\u00D7' => "\\327", // multiplication
        '\u2013' => "\\226",
        '\u2014' => "\\227",
        '\u2019' => "\\222",
        '\u201C' => "\\223",
        '\u201D' => "\\224",
        '\u00B5' => "\\265", // micro
        '\u2264' => "<=",
        '\u2265' => ">=",
        '\u2248' => "~",
        '\u00A0' => " ",
        _ => "?",
    };

    public readonly record struct Rgb(double R, double G, double B)
    {
        public static readonly Rgb Ink = new(0.086, 0.11, 0.14);
        public static readonly Rgb Muted = new(0.42, 0.47, 0.53);
        public static readonly Rgb Accent = new(0.05, 0.52, 0.60);
        public static readonly Rgb Critical = new(0.78, 0.16, 0.16);
        public static readonly Rgb Warning = new(0.72, 0.48, 0.05);
        public static readonly Rgb Band = new(0.96, 0.97, 0.98);
        public static readonly Rgb Rule = new(0.82, 0.85, 0.88);
        public static readonly Rgb White = new(1, 1, 1);
    }
}
