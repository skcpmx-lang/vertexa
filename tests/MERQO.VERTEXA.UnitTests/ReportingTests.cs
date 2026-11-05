using System.Text.Json;
using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Core.Settings;
using Merqo.Vertexa.Reporting;
using Merqo.Vertexa.Security;
using Merqo.Vertexa.Shared;
using Xunit;

namespace Merqo.Vertexa.UnitTests;

public sealed class ReportingTests
{
    private static DiagnosticSnapshot SampleSnapshot()
    {
        var snapshot = new DiagnosticSnapshot { MachineName = "TEST-PC", AppVersion = "1.0.0-test" };
        var system = snapshot.Section(SectionKeys.System);
        system.Metric("Manufacturer", "Merqo Systems", "WMI:Win32_ComputerSystem", "CIM query");
        system.Metric("Model", "VX-15 Pro", "WMI:Win32_ComputerSystem", "CIM query");
        system.Missing<string>("System SKU", AvailabilityState.Unavailable, "not reported by firmware", "WMI", "CIM query");
        snapshot.Section(SectionKeys.Cpu).Metric("Processor model", "Test CPU 9000", "WMI:Win32_Processor", "CIM query");
        snapshot.Section(SectionKeys.Battery).Missing<string>("Cycle count", AvailabilityState.Unsupported, "firmware does not expose it");
        snapshot.Health = new Core.Health.HealthScorer().Score(snapshot);
        return snapshot;
    }

    private static ReportDocument Build(DiagnosticSnapshot snapshot, ReportType type, bool redact = true)
    {
        var builder = new ReportBuilder(new RedactionService(redact));
        return builder.Build(snapshot, type, "1.0.0-test");
    }

    [Fact]
    public void Report_contains_provenance_and_states_for_every_row()
    {
        var document = Build(SampleSnapshot(), ReportType.Full);

        Assert.NotEmpty(document.Sections);
        foreach (var row in document.Sections.SelectMany(s => s.Rows))
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Name));
            Assert.False(string.IsNullOrWhiteSpace(row.State));
            Assert.False(string.IsNullOrWhiteSpace(row.Confidence));
            Assert.False(string.IsNullOrWhiteSpace(row.Value));
        }
    }

    [Fact]
    public void Report_never_invents_a_value_for_an_unavailable_metric()
    {
        var document = Build(SampleSnapshot(), ReportType.Full);
        var sku = document.Sections.SelectMany(s => s.Rows).Single(r => r.Name == "System SKU");
        Assert.Equal("Unavailable", sku.Value);
        Assert.Equal("N/A", sku.State);
    }

    [Fact]
    public void Buyer_report_is_a_strict_subset_with_extra_limitations()
    {
        var document = Build(SampleSnapshot(), ReportType.BuyerInspection);
        Assert.Contains(document.Limitations, l => l.Contains("physical inspection", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(document.Sections, s => s.Title is "Drivers & devices" or "Network");
    }

    [Fact]
    public void Metadata_carries_version_run_and_redaction_state()
    {
        var document = Build(SampleSnapshot(), ReportType.Technician);
        var names = document.Metadata.Select(m => m.Name).ToList();
        Assert.Contains("Application", names);
        Assert.Contains("Run ID", names);
        Assert.Contains("Serials", names);
        Assert.Contains(document.Metadata, m => m.Value.Contains("MERQO VERTEXA", StringComparison.Ordinal));
    }

    [Fact]
    public void Html_report_declares_charset_and_has_no_script_tags()
    {
        var html = new HtmlReportWriter().Render(Build(SampleSnapshot(), ReportType.Full));
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<meta charset=\"utf-8\">", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MERQO VERTEXA", html);
    }

    [Fact]
    public void Html_writer_escapes_hostile_metric_names()
    {
        var snapshot = new DiagnosticSnapshot();
        snapshot.Section(SectionKeys.System).Metric("Model", "<script>alert('x')</script>", "WMI", "CIM query");
        var html = new HtmlReportWriter().Render(Build(snapshot, ReportType.Full));
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Csv_writer_quotes_commas_and_embedded_quotes()
    {
        Assert.Equal("\"a,b\"", CsvReportWriter.Quote("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", CsvReportWriter.Quote("say \"hi\""));
        Assert.Equal("plain", CsvReportWriter.Quote("plain"));

        var csv = new CsvReportWriter().Render(Build(SampleSnapshot(), ReportType.Full));
        Assert.StartsWith("section,metric,value,source,state,confidence,run_id", csv);
        Assert.Contains("\r\n", csv);
    }

    [Fact]
    public void Json_report_is_versioned_and_round_trips()
    {
        var json = new JsonReportWriter().Render(Build(SampleSnapshot(), ReportType.Full));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("MERQO VERTEXA", root.GetProperty("generator").GetString());
        Assert.True(root.GetProperty("sections").GetArrayLength() > 0);
        Assert.True(root.GetProperty("limitations").GetArrayLength() > 0);
    }

    [Fact]
    public void Pdf_writer_emits_a_structurally_valid_document()
    {
        var pdf = new ReportPdfRenderer().Render(Build(SampleSnapshot(), ReportType.Full));
        var text = System.Text.Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("/Type /Catalog", text);
        Assert.Contains("/Type /Pages", text);
        Assert.Contains("xref", text);
        Assert.True(text.TrimEnd().EndsWith("%%EOF", StringComparison.Ordinal));
        Assert.Contains("MERQO VERTEXA", text);
        Assert.Contains("startxref", text);

        // The xref keyword must sit exactly at the offset declared by startxref.
        var startXrefIndex = text.LastIndexOf("startxref", StringComparison.Ordinal);
        var offsetText = text[(startXrefIndex + "startxref".Length)..].Trim().Split('\n')[0].Trim();
        var offset = int.Parse(offsetText, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("xref", text.Substring(offset, 4));
    }

    [Fact]
    public void Pdf_escape_handles_delimiters_and_unencodable_characters()
    {
        Assert.Equal("\\(test\\)", Pdf.PdfWriter.Escape("(test)"));
        Assert.Equal("a\\\\b", Pdf.PdfWriter.Escape("a\\b"));
        Assert.Equal("\\267 device", Pdf.PdfWriter.Escape("· device"));
        Assert.Equal("?", Pdf.PdfWriter.Escape("Ω"));
        Assert.Equal("line one line two", Pdf.PdfWriter.Escape("line one\nline two"));
    }

    [Fact]
    public void Long_reports_paginate()
    {
        var snapshot = new DiagnosticSnapshot();
        var section = snapshot.Section(SectionKeys.System);
        for (var i = 0; i < 200; i++)
            section.Metric($"Metric {i}", $"value {i}", "WMI", "CIM query");
        var pdf = new ReportPdfRenderer().Render(Build(snapshot, ReportType.Full));
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        var pageCount = System.Text.RegularExpressions.Regex.Matches(text, @"/Type /Page[^s]").Count;
        Assert.True(pageCount > 1, $"expected multiple pages, found {pageCount}");
    }

    [Fact]
    public void Exporter_writes_all_four_formats_to_disk()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vertexa-report-test-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var exporter = new ReportExporter();
            var document = Build(SampleSnapshot(), ReportType.Full);
            foreach (var format in Enum.GetValues<ExportFormat>())
            {
                var result = exporter.Export(document, format, directory);
                Assert.True(result.Success, result.Error);
                Assert.NotNull(result.Path);
                Assert.True(File.Exists(result.Path));
                Assert.True(result.Bytes > 100);
            }
            Assert.Equal(4, Directory.GetFiles(directory).Length);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Serials_are_redacted_by_default()
    {
        var redaction = new RedactionService();
        var masked = redaction.MaskSerial("SN-TEST-0042");
        Assert.NotEqual("SN-TEST-0042", masked);
        Assert.EndsWith("42", masked);

        redaction.Enabled = false;
        Assert.Equal("SN-TEST-0042", redaction.MaskSerial("SN-TEST-0042"));
    }
}
