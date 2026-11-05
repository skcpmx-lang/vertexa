using System.Text.Json;

namespace Merqo.Vertexa.Reporting;

/// <summary>
/// Machine-readable report (one object per report, schema-versioned). Consumers can rely
/// on the shape: schemaVersion, report, metadata, sections[], findings[], limitations[].
/// </summary>
public sealed class JsonReportWriter
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string Render(ReportDocument document)
    {
        var payload = new
        {
            schemaVersion = SchemaVersion,
            generator = "MERQO VERTEXA",
            report = new
            {
                type = document.Type.ToString(),
                title = document.Title,
                subtitle = document.Subtitle,
                runId = document.RunId,
                generatedAt = document.GeneratedAt.ToString("O"),
                watermark = document.Watermark,
            },
            metadata = document.Metadata.ToDictionary(m => m.Name, m => m.Value),
            sections = document.Sections.Select(s => new
            {
                title = s.Title,
                intro = s.Intro,
                rows = s.Rows.Select(r => new
                {
                    metric = r.Name,
                    value = r.Value,
                    source = r.Source,
                    state = r.State,
                    confidence = r.Confidence,
                }),
                notes = s.Notes,
            }),
            findings = document.Findings,
            limitations = document.Limitations,
        };
        return JsonSerializer.Serialize(payload, Options);
    }
}
