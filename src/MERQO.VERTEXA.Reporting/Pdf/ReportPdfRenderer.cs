using System.Globalization;
using Merqo.Vertexa.Reporting.Pdf;

namespace Merqo.Vertexa.Reporting;

/// <summary>Lays a <see cref="ReportDocument"/> onto A4 pages using the in-house PDF writer.</summary>
public sealed class ReportPdfRenderer
{
    private const double Margin = 42;
    private const double FooterY = 802;

    public byte[] Render(ReportDocument document)
    {
        var pdf = new PdfWriter();
        pdf.AddPage();
        double y = Margin;

        // Header band
        pdf.Rect(Margin, y, pdf.PageWidth - (Margin * 2), 54, new PdfWriter.Rgb(0.043, 0.059, 0.078));
        pdf.Text(Margin + 14, y + 20, "MERQO VERTEXA", 16, true, new PdfWriter.Rgb(0.18, 0.83, 0.91));
        pdf.Text(Margin + 140, y + 20, document.Title, 13, true, PdfWriter.Rgb.White);
        pdf.Text(Margin + 14, y + 40, document.Subtitle, 9, false, new PdfWriter.Rgb(0.65, 0.72, 0.80));
        y += 74;

        // Metadata grid
        foreach (var (name, value) in document.Metadata)
        {
            pdf.Text(Margin, y, name, 8.5, false, PdfWriter.Rgb.Muted);
            pdf.Text(Margin + 120, y, value, 8.5, false, PdfWriter.Rgb.Ink);
            y += 12;
        }
        y += 6;
        pdf.Line(Margin, y, pdf.PageWidth - Margin, y, PdfWriter.Rgb.Rule, 1);
        y += 16;

        foreach (var section in document.Sections)
        {
            y = EnsureSpace(pdf, y, 60);
            pdf.Text(Margin, y, section.Title, 12, true, PdfWriter.Rgb.Ink);
            y += 14;
            if (!string.IsNullOrWhiteSpace(section.Intro))
            {
                foreach (var line in Wrap(section.Intro!, 96))
                {
                    pdf.Text(Margin, y, line, 8.5, false, PdfWriter.Rgb.Muted);
                    y += 11;
                }
                y += 3;
            }

            var headerY = y;
            pdf.Rect(Margin, headerY - 2, pdf.PageWidth - (Margin * 2), 14, PdfWriter.Rgb.Band);
            pdf.Text(Margin + 4, headerY + 8, "Metric", 8, true, PdfWriter.Rgb.Muted);
            pdf.Text(Margin + 210, headerY + 8, "Value", 8, true, PdfWriter.Rgb.Muted);
            pdf.Text(Margin + 370, headerY + 8, "Source", 8, true, PdfWriter.Rgb.Muted);
            pdf.Text(Margin + 486, headerY + 8, "State", 8, true, PdfWriter.Rgb.Muted);
            y = headerY + 18;

            foreach (var row in section.Rows)
            {
                var nameLines = Wrap(row.Name, 44);
                var valueLines = Wrap(row.Value, 31);
                var lineCount = Math.Max(nameLines.Count, valueLines.Count);
                var rowHeight = (lineCount * 10) + 3;
                if (y + rowHeight > FooterY)
                {
                    AddFooter(pdf, document);
                    pdf.AddPage();
                    y = Margin + 10;
                }
                for (var i = 0; i < lineCount; i++)
                {
                    var lineY = y + (i * 10);
                    if (i < nameLines.Count) pdf.Text(Margin + 4, lineY, nameLines[i], 8.5, false, PdfWriter.Rgb.Ink);
                    if (i < valueLines.Count) pdf.Text(Margin + 210, lineY, valueLines[i], 8.5, false, PdfWriter.Rgb.Ink);
                    if (i == 0)
                    {
                        pdf.Text(Margin + 370, lineY, Truncate(row.Source, 24), 7.5, false, PdfWriter.Rgb.Muted);
                        pdf.Text(Margin + 486, lineY, row.State + " / " + row.Confidence, 7.5, false, StateColour(row.State));
                    }
                }
                y += rowHeight;
                pdf.Line(Margin, y - 2, pdf.PageWidth - Margin, y - 2, new PdfWriter.Rgb(0.93, 0.94, 0.96), 0.4);
            }

            if (section.Notes is { Count: > 0 })
            {
                y += 6;
                foreach (var note in section.Notes)
                {
                    y = EnsureSpace(pdf, y, 30);
                    foreach (var line in Wrap("- " + note, 100))
                    {
                        pdf.Text(Margin + 4, y, line, 7.5, false, PdfWriter.Rgb.Muted);
                        y += 9.5;
                    }
                }
            }
            y += 16;
        }

        if (document.Findings.Count > 0)
        {
            y = EnsureSpace(pdf, y, 60);
            pdf.Text(Margin, y, "Findings and recommendations", 12, true, PdfWriter.Rgb.Ink);
            y += 16;
            foreach (var finding in document.Findings)
            {
                foreach (var line in Wrap("- " + finding, 100))
                {
                    y = EnsureSpace(pdf, y, 18);
                    pdf.Text(Margin + 4, y, line, 8.5, false, PdfWriter.Rgb.Ink);
                    y += 11;
                }
            }
            y += 10;
        }

        y = EnsureSpace(pdf, y, 80);
        pdf.Text(Margin, y, "Limitations and methodology", 12, true, PdfWriter.Rgb.Ink);
        y += 16;
        foreach (var limitation in document.Limitations)
        {
            foreach (var line in Wrap("- " + limitation, 100))
            {
                y = EnsureSpace(pdf, y, 18);
                pdf.Text(Margin + 4, y, line, 8, false, PdfWriter.Rgb.Muted);
                y += 10.5;
            }
        }

        AddFooter(pdf, document);
        return pdf.Build();
    }

    internal static double EnsureSpace(PdfWriter pdf, double y, double required)
    {
        if (y + required <= FooterY) return y;
        pdf.AddPage();
        return Margin + 10;
    }

    private static void AddFooter(PdfWriter pdf, ReportDocument document)
    {
        pdf.Line(Margin, FooterY + 6, pdf.PageWidth - Margin, FooterY + 6, PdfWriter.Rgb.Rule, 0.5);
        pdf.Text(Margin, FooterY + 18, document.Watermark, 7, false, PdfWriter.Rgb.Muted);
        pdf.Text(pdf.PageWidth - Margin - 120, FooterY + 18,
            document.GeneratedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " · run " + document.RunId, 7, false, PdfWriter.Rgb.Muted);
    }

    private static PdfWriter.Rgb StateColour(string state) => state switch
    {
        "OK" => new PdfWriter.Rgb(0.13, 0.55, 0.30),
        "PART" => PdfWriter.Rgb.Warning,
        "INVALID" or "DENIED" => PdfWriter.Rgb.Critical,
        _ => PdfWriter.Rgb.Muted,
    };

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>Simple greedy wrap using an average Helvetica glyph width (0.5 em).</summary>
    internal static IReadOnlyList<string> Wrap(string text, int maxCharacters)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        var words = text.Replace("\r", " ").Replace("\n", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var word in words)
        {
            if (current.Length == 0)
            {
                current.Append(word);
            }
            else if (current.Length + 1 + word.Length <= maxCharacters)
            {
                current.Append(' ').Append(word);
            }
            else
            {
                lines.Add(current.ToString());
                current.Clear();
                current.Append(word);
            }
        }
        if (current.Length > 0) lines.Add(current.ToString());
        return lines;
    }
}
