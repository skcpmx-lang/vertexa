using System.Globalization;
using System.Text;

namespace Merqo.Vertexa.Reporting;

/// <summary>RFC 4180 compliant CSV: every field quoted when needed, CRLF rows.</summary>
public sealed class CsvReportWriter
{
    public string Render(ReportDocument document)
    {
        var builder = new StringBuilder(4096);
        builder.Append("section,metric,value,source,state,confidence,run_id\r\n");
        foreach (var section in document.Sections)
        {
            foreach (var row in section.Rows)
            {
                builder.Append(Quote(section.Title)).Append(',')
                       .Append(Quote(row.Name)).Append(',')
                       .Append(Quote(row.Value)).Append(',')
                       .Append(Quote(row.Source)).Append(',')
                       .Append(Quote(row.State)).Append(',')
                       .Append(Quote(row.Confidence)).Append(',')
                       .Append(Quote(document.RunId)).Append("\r\n");
            }
        }
        builder.Append("\r\nfinding,severity_and_action,evidence,state,confidence,run_id\r\n");
        foreach (var finding in document.Findings)
            builder.Append("recommendation,").Append(Quote(finding)).Append(",,,, ").Append(Quote(document.RunId)).Append("\r\n");
        return builder.ToString();
    }

    internal static string Quote(string value)
    {
        var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        var escaped = value.Replace("\"", "\"\"");
        return needsQuotes ? "\"" + escaped + "\"" : escaped;
    }
}
