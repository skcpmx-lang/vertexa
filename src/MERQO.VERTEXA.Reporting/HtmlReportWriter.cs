using System.Net;
using System.Text;

namespace Merqo.Vertexa.Reporting;

/// <summary>Self-contained HTML report (inline CSS, no scripts, no external assets).</summary>
public sealed class HtmlReportWriter
{
    public string Render(ReportDocument document)
    {
        var builder = new StringBuilder(16 * 1024);
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        builder.AppendLine($"<title>{Esc(document.Title)} — MERQO VERTEXA</title>");
        builder.AppendLine("<meta name=\"generator\" content=\"MERQO VERTEXA\">");
        builder.AppendLine("<style>");
        builder.AppendLine("""
            :root { color-scheme: light; }
            * { box-sizing: border-box; }
            body { margin:0; padding:32px; background:#f4f6f9; color:#0d1217;
                   font-family:"Segoe UI",system-ui,-apple-system,sans-serif; font-size:13px; line-height:1.5; }
            .sheet { max-width:1000px; margin:0 auto; background:#fff; border:1px solid #dfe5ec;
                     border-radius:10px; overflow:hidden; box-shadow:0 4px 18px rgba(11,15,20,.08); }
            header { background:#0b0f14; color:#e8eef6; padding:22px 28px; }
            header .brand { color:#2fd4e8; font-weight:700; letter-spacing:.14em; font-size:11px; text-transform:uppercase; }
            header h1 { margin:6px 0 2px; font-size:20px; font-weight:600; }
            header .sub { color:#9fb0c3; font-size:12px; }
            .meta { display:grid; grid-template-columns:repeat(auto-fit,minmax(220px,1fr)); gap:6px 24px; padding:18px 28px; background:#fafbfd; border-bottom:1px solid #e6ebf1; }
            .meta div { display:flex; justify-content:space-between; gap:12px; font-size:12px; }
            .meta span:first-child { color:#5d6f83; }
            main { padding:8px 28px 28px; }
            h2 { font-size:14px; text-transform:uppercase; letter-spacing:.08em; color:#3c4d61;
                 border-bottom:1px solid #e6ebf1; padding-bottom:6px; margin:26px 0 10px; }
            table { width:100%; border-collapse:collapse; font-size:12px; }
            th { text-align:left; font-weight:600; color:#5d6f83; background:#f7f9fc; padding:6px 8px; border-bottom:1px solid #e6ebf1; }
            td { padding:5px 8px; border-bottom:1px solid #f0f3f7; vertical-align:top; }
            td.value { font-family:"Cascadia Mono",Consolas,monospace; }
            .state { font-family:"Cascadia Mono",Consolas,monospace; font-size:11px; padding:1px 6px; border-radius:4px; background:#eef2f6; color:#48596c; }
            .state.OK { background:#e6f7ee; color:#136537; }
            .state.PART { background:#fdf3e2; color:#8a5a08; }
            .state.DENIED,.state.INVALID { background:#fdeaea; color:#a32020; }
            ul { margin:6px 0 0; padding-left:18px; }
            li { margin:3px 0; }
            .note { color:#5d6f83; font-size:11.5px; }
            footer { padding:14px 28px; border-top:1px solid #e6ebf1; background:#fafbfd; color:#6b7c8f; font-size:11px; display:flex; justify-content:space-between; }
            @media print { body { background:#fff; padding:0; } .sheet { box-shadow:none; border:0; } }
            """);
        builder.AppendLine("</style></head><body><div class=\"sheet\">");
        builder.AppendLine("<header>");
        builder.AppendLine("<div class=\"brand\">MERQO VERTEXA · Advanced PC &amp; Laptop Diagnostics</div>");
        builder.AppendLine($"<h1>{Esc(document.Title)}</h1>");
        builder.AppendLine($"<div class=\"sub\">{Esc(document.Subtitle)}</div>");
        builder.AppendLine("</header>");

        builder.AppendLine("<div class=\"meta\">");
        foreach (var (name, value) in document.Metadata)
            builder.AppendLine($"<div><span>{Esc(name)}</span><span>{Esc(value)}</span></div>");
        builder.AppendLine("</div><main>");

        foreach (var section in document.Sections)
        {
            builder.AppendLine($"<h2>{Esc(section.Title)}</h2>");
            if (!string.IsNullOrWhiteSpace(section.Intro))
                builder.AppendLine($"<p class=\"note\">{Esc(section.Intro!)}</p>");
            builder.AppendLine("<table><thead><tr><th style=\"width:34%\">Metric</th><th style=\"width:30%\">Value</th><th style=\"width:24%\">Source</th><th style=\"width:12%\">State</th></tr></thead><tbody>");
            foreach (var row in section.Rows)
            {
                var stateClass = Esc(row.State);
                builder.AppendLine($"<tr><td>{Esc(row.Name)}</td><td class=\"value\">{Esc(row.Value)}</td><td class=\"note\">{Esc(row.Source)}</td><td><span class=\"state {stateClass}\">{stateClass}</span> <span class=\"note\">{Esc(row.Confidence)}</span></td></tr>");
            }
            builder.AppendLine("</tbody></table>");
            if (section.Notes is { Count: > 0 })
            {
                builder.Append("<ul class=\"note\">");
                foreach (var note in section.Notes) builder.Append($"<li>{Esc(note)}</li>");
                builder.AppendLine("</ul>");
            }
        }

        if (document.Findings.Count > 0)
        {
            builder.AppendLine("<h2>Findings and recommendations</h2><ul>");
            foreach (var finding in document.Findings) builder.AppendLine($"<li>{Esc(finding)}</li>");
            builder.AppendLine("</ul>");
        }

        builder.AppendLine("<h2>Limitations and methodology</h2><ul class=\"note\">");
        foreach (var limitation in document.Limitations) builder.AppendLine($"<li>{Esc(limitation)}</li>");
        builder.AppendLine("</ul></main>");
        builder.AppendLine($"<footer><span>{Esc(document.Watermark)}</span><span>{Esc(document.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"))} · run {Esc(document.RunId)}</span></footer>");
        builder.AppendLine("</div></body></html>");
        return builder.ToString();
    }

    internal static string Esc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
