using Merqo.Vertexa.Core.Settings;
using Merqo.Vertexa.Security;

namespace Merqo.Vertexa.Reporting;

public sealed record ExportResult(bool Success, string? Path, string? Error, long Bytes);

/// <summary>Writes a report document to disk in the requested format, safely.</summary>
public sealed class ReportExporter
{
    private readonly HtmlReportWriter _html = new();
    private readonly CsvReportWriter _csv = new();
    private readonly JsonReportWriter _json = new();
    private readonly ReportPdfRenderer _pdf = new();

    public ExportResult Export(ReportDocument document, ExportFormat format, string directory, string? fileName = null)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var extension = format switch
            {
                ExportFormat.Pdf => ".pdf",
                ExportFormat.Html => ".html",
                ExportFormat.Json => ".json",
                _ => ".csv",
            };
            var stem = fileName ?? $"{document.Type.FileStem()}-{DateTime.Now:yyyyMMdd-HHmmss}";
            var target = PathGuard.ValidateFileTarget(Path.Combine(directory, stem + extension), directory);
            if (!target.IsValid || target.SanitisedPath is null)
                return new ExportResult(false, null, target.Error, 0);

            var path = PathGuard.MakeUnique(target.SanitisedPath);
            var bytes = format switch
            {
                ExportFormat.Pdf => _pdf.Render(document),
                ExportFormat.Html => System.Text.Encoding.UTF8.GetBytes(_html.Render(document)),
                ExportFormat.Json => System.Text.Encoding.UTF8.GetBytes(_json.Render(document)),
                _ => System.Text.Encoding.UTF8.GetBytes(_csv.Render(document)),
            };
            File.WriteAllBytes(path, bytes);
            return new ExportResult(true, path, null, bytes.LongLength);
        }
        catch (Exception ex)
        {
            return new ExportResult(false, null, ex.Message, 0);
        }
    }

    public string RenderToString(ReportDocument document, ExportFormat format) => format switch
    {
        ExportFormat.Html => _html.Render(document),
        ExportFormat.Json => _json.Render(document),
        ExportFormat.Csv => _csv.Render(document),
        _ => "(binary PDF — use Export to write the file)",
    };
}
