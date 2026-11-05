using System.IO;
using System.Text;
using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Core.Settings;
using Merqo.Vertexa.Reporting;

namespace Merqo.Vertexa.App.Services;

/// <summary>
/// Headless end-to-end self test, invoked with <c>--selftest</c>. It composes the real
/// application graph, runs a real scan, exports real reports and returns a process exit
/// code. CI uses this as executable-level evidence that the shipped binary works.
/// </summary>
public static class SelfTest
{
    public static int Run()
    {
        var log = new StringBuilder();
        var failures = 0;
        var reportPath = Path.Combine(Path.GetTempPath(), "vertexa-selftest.txt");

        void Check(string name, bool condition, string detail)
        {
            if (!condition) failures++;
            log.AppendLine($"{(condition ? "PASS" : "FAIL")}  {name}  — {detail}");
        }

        try
        {
            log.AppendLine($"MERQO VERTEXA self test · {DateTimeOffset.Now:O}");
            log.AppendLine($"OS build {Environment.OSVersion.Version.Build} · x64={Environment.Is64BitProcess}");

            using var host = new AppHost();
            log.AppendLine($"Data directory: {host.DataDirectory}");
            log.AppendLine($"Database: {host.Database.DatabasePath}");
            Check("composition", true, $"version {host.AppVersion}, elevated={host.IsElevated}");

            var settings = host.Settings;
            settings.RedactSerials = true;

            var snapshot = host.Scan.ScanAsync(ScanScope.Quick, host.IsElevated, true, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            log.AppendLine(snapshot.SummaryLine);
            foreach (var section in snapshot.Sections)
                log.AppendLine($"  section {section.Key}: {section.AvailableCount}/{section.TotalCount} ({section.AggregateState})");

            Check("scan produced sections", snapshot.Sections.Count > 0, $"{snapshot.Sections.Count} sections");
            Check("scan produced metrics", snapshot.TotalMetricCount > 20, $"{snapshot.TotalMetricCount} metrics, {snapshot.AvailableMetricCount} available");
            Check("every available metric has provenance",
                snapshot.AllMetrics.Where(m => m.HasData).All(m => !string.IsNullOrWhiteSpace(m.Source) && !string.IsNullOrWhiteSpace(m.DetectionMethod)),
                "source + method present on all available metrics");
            Check("health report produced", snapshot.Health is not null, snapshot.Health?.OverallDisplay ?? "null");
            Check("recommendations evaluated", snapshot.Recommendations is not null, $"{snapshot.Recommendations.Count} items");

            var exportDirectory = Path.Combine(Path.GetTempPath(), "vertexa-selftest-" + snapshot.Id);
            Directory.CreateDirectory(exportDirectory);
            var builder = new ReportBuilder(host.Redaction);
            var exporter = new ReportExporter();
            foreach (var format in Enum.GetValues<ExportFormat>())
            {
                var document = builder.Build(snapshot, ReportType.Full, host.AppVersion);
                var result = exporter.Export(document, format, exportDirectory);
                Check($"export {format}", result.Success && result.Bytes > 500, result.Success ? $"{result.Bytes} bytes → {result.Path}" : result.Error ?? "failed");
            }

            var history = host.History.List(5);
            Check("history persisted", history.Count > 0, $"{history.Count} run(s) stored");

            var sample = host.Monitoring.SampleOnceAsync().GetAwaiter().GetResult();
            Check("monitoring sample", sample.Timestamp <= DateTimeOffset.UtcNow,
                $"cpu={sample.CpuLoadPercent?.ToString("0") ?? "n/a"} temp={sample.HottestTemperatureCelsius?.ToString("0.0") ?? "n/a"} unavailable={sample.Unavailable.Count}");

            log.AppendLine($"Reports written to {exportDirectory}");
        }
        catch (Exception ex)
        {
            failures++;
            log.AppendLine("FAIL  exception — " + ex);
        }

        log.AppendLine(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures} check(s))");
        try
        {
            File.WriteAllText(reportPath, log.ToString());
        }
        catch
        {
            // Reporting the result to disk is best-effort; the exit code is authoritative.
        }
        return failures == 0 ? 0 : 1;
    }
}
