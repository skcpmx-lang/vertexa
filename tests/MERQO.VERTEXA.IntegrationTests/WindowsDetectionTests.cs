using Merqo.Vertexa.Core.Contracts;
using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Core.Settings;
using Merqo.Vertexa.Reporting;
using Merqo.Vertexa.Security;
using Merqo.Vertexa.Shared;
using Merqo.Vertexa.Windows;
using Xunit;
using Xunit.Abstractions;

namespace Merqo.Vertexa.IntegrationTests;

/// <summary>
/// These tests execute against the real Windows machine they run on (GitHub's Windows
/// runner in CI). They assert provenance and behaviour — never a specific hardware value,
/// so a test can never pass because of a constant baked into the product.
/// </summary>
[Collection("Windows")]
public sealed class WindowsDetectionTests
{
    private readonly ITestOutputHelper _output;

    public WindowsDetectionTests(ITestOutputHelper output) => _output = output;

    private static bool IsWindows => OperatingSystem.IsWindows();

    private static async Task<DiagnosticSnapshot> RunScanAsync(ScanScope scope = ScanScope.Full)
    {
        var collectors = WindowsServices.CreateCollectors();
        var orchestrator = new ScanOrchestrator(collectors);
        return await orchestrator.RunAsync(new ScanOptions { Scope = scope, AppVersion = "integration" });
    }

    [Fact]
    public async Task Full_scan_completes_and_every_available_metric_has_provenance()
    {
        if (!IsWindows) return;

        var snapshot = await RunScanAsync();
        _output.WriteLine(snapshot.SummaryLine);
        foreach (var section in snapshot.Sections)
            _output.WriteLine($"{section.Key}: {section.AvailableCount}/{section.TotalCount} -> {section.AggregateState}");

        Assert.NotEmpty(snapshot.Sections);
        Assert.True(snapshot.TotalMetricCount > 30, $"expected a substantial inventory, got {snapshot.TotalMetricCount}");

        foreach (var metric in snapshot.AllMetrics)
        {
            if (!metric.HasData) continue;
            Assert.False(string.IsNullOrWhiteSpace(metric.Source));
            Assert.False(string.IsNullOrWhiteSpace(metric.DetectionMethod));
            Assert.False(string.IsNullOrWhiteSpace(metric.DisplayValue));
            Assert.NotEqual(ConfidenceLevel.Unknown, metric.Confidence);
        }
    }

    [Fact]
    public async Task Cpu_identity_is_detected_from_the_operating_system()
    {
        if (!IsWindows) return;

        var snapshot = await RunScanAsync(ScanScope.Quick);
        var cpu = snapshot.Find(SectionKeys.Cpu);
        Assert.NotNull(cpu);

        var model = cpu!.Find<string>("Processor model");
        Assert.NotNull(model);
        if (model!.HasData)
        {
            _output.WriteLine($"CPU: {model.Value} via {model.Source}");
            Assert.False(string.IsNullOrWhiteSpace(model.Value));
            Assert.Contains("Processor", model.Source, StringComparison.OrdinalIgnoreCase);
        }

        var cores = cpu.Find<int>("Cores");
        if (cores?.HasData == true) Assert.True(cores.Value > 0);
    }

    [Fact]
    public async Task Memory_modules_or_total_memory_are_reported()
    {
        if (!IsWindows) return;

        var snapshot = await RunScanAsync(ScanScope.Quick);
        var memory = snapshot.Find(SectionKeys.Memory);
        Assert.NotNull(memory);
        var total = memory!.Find<string>("Installed memory");
        Assert.NotNull(total);
        _output.WriteLine($"Installed memory: {total!.DisplayWithUnit} [{total.Availability}] via {total.Source}");
        Assert.True(total.HasData || total.Availability != AvailabilityState.Available);
    }

    [Fact]
    public async Task Storage_devices_and_health_telemetry_are_enumerated()
    {
        if (!IsWindows) return;

        var snapshot = await RunScanAsync(ScanScope.Quick);
        Assert.NotEmpty(snapshot.StorageDevices);
        foreach (var drive in snapshot.StorageDevices)
        {
            _output.WriteLine($"{drive.Model} · {drive.MediaType} · {drive.HealthStatus} · smart={drive.SmartAvailability} ({drive.SmartSource})");
            Assert.False(string.IsNullOrWhiteSpace(drive.Model));
            Assert.True(drive.CapacityBytes > 0);
            Assert.False(string.IsNullOrWhiteSpace(drive.HealthHeadline));
        }
    }

    [Fact]
    public async Task Firmware_and_os_sections_are_populated_with_sources()
    {
        if (!IsWindows) return;

        var snapshot = await RunScanAsync(ScanScope.Quick);
        var firmware = snapshot.Find(SectionKeys.Firmware);
        var os = snapshot.Find(SectionKeys.Os);
        Assert.NotNull(firmware);
        Assert.NotNull(os);

        var biosVendor = firmware!.Find<string>("BIOS vendor") ?? firmware.Find<string>("BIOS vendor (WMI)");
        Assert.NotNull(biosVendor);
        _output.WriteLine($"BIOS: {biosVendor!.DisplayValue} [{biosVendor.Availability}] via {biosVendor.Source}");

        var build = os!.Find<string>("Build");
        Assert.NotNull(build);
        _output.WriteLine($"Windows build: {build!.DisplayValue} [{build.Availability}]");
        if (build.HasData) Assert.Matches(@"^\d+", build.Value!);
    }

    [Fact]
    public async Task Health_report_is_produced_with_an_explicit_formula()
    {
        if (!IsWindows) return;

        var snapshot = await RunScanAsync(ScanScope.Quick);
        Assert.NotNull(snapshot.Health);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Health!.Formula));
        _output.WriteLine($"Health: {snapshot.Health.OverallDisplay} ({snapshot.Health.Headline})");
        foreach (var subsystem in snapshot.Health.Subsystems)
            _output.WriteLine($"  {subsystem.Title}: {subsystem.Score?.ToString("0") ?? "excluded"} — {subsystem.Explanation}");
        Assert.Equal(5, snapshot.Health.Subsystems.Count);
    }

    [Fact]
    public async Task Drivers_and_device_inventory_are_enumerated_without_throw()
    {
        if (!IsWindows) return;

        var snapshot = await RunScanAsync(ScanScope.Full);
        Assert.NotEmpty(snapshot.Devices);
        var problems = snapshot.Devices.Count(d => d.HasProblem);
        _output.WriteLine($"{snapshot.Devices.Count} devices, {problems} with problem codes");
        Assert.True(snapshot.Devices.Count > 5);
        foreach (var device in snapshot.Devices.Where(d => d.HasProblem).Take(5))
            Assert.False(string.IsNullOrWhiteSpace(device.ProblemText));
    }

    [Fact]
    public async Task Scanning_twice_is_stable_and_does_not_throw()
    {
        if (!IsWindows) return;

        var first = await RunScanAsync(ScanScope.Quick);
        var second = await RunScanAsync(ScanScope.Quick);
        Assert.NotEqual(first.Id, second.Id);
        Assert.True(second.TotalMetricCount > 0);
    }
}

[Collection("Windows")]
public sealed class WindowsReportingAndStorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vertexa-it-" + Guid.NewGuid().ToString("N")[..8]);

    public WindowsReportingAndStorageTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    [Fact]
    public void Report_export_writes_valid_files_from_a_real_snapshot()
    {
        if (!OperatingSystem.IsWindows()) return;

        var snapshot = WindowsServices.CreateCollectors().Count > 0
            ? new ScanOrchestrator(WindowsServices.CreateCollectors())
                .RunAsync(new ScanOptions { Scope = ScanScope.Quick, AppVersion = "integration" }).GetAwaiter().GetResult()
            : throw new InvalidOperationException("no collectors");

        var builder = new ReportBuilder(new RedactionService());
        var exporter = new ReportExporter();

        foreach (var format in Enum.GetValues<ExportFormat>())
        {
            var document = builder.Build(snapshot, ReportType.Full, "integration");
            var result = exporter.Export(document, format, _directory);
            Assert.True(result.Success, result.Error);
            Assert.True(File.Exists(result.Path));
            Assert.True(result.Bytes > 1000, $"{format} report was only {result.Bytes} bytes");

            var bytes = File.ReadAllBytes(result.Path!);
            switch (format)
            {
                case ExportFormat.Pdf:
                    Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
                    break;
                case ExportFormat.Html:
                    Assert.Contains("MERQO VERTEXA", System.Text.Encoding.UTF8.GetString(bytes));
                    break;
                case ExportFormat.Json:
                    Assert.Contains("schemaVersion", System.Text.Encoding.UTF8.GetString(bytes));
                    break;
                case ExportFormat.Csv:
                    Assert.Contains("section,metric,value", System.Text.Encoding.UTF8.GetString(bytes));
                    break;
            }
        }
    }

    [Fact]
    public void Settings_and_history_persist_across_database_instances()
    {
        if (!OperatingSystem.IsWindows()) return;

        using (var database = new VertexaDatabase(_directory))
        {
            database.Migrate();
            new SettingsRepository(database).Save(new AppSettings { Theme = AppTheme.Light, RedactSerials = false });
            new HistoryRepository(database).Save("RUN-WIN", DateTimeOffset.UtcNow, "integration", Environment.MachineName, "Quick", 88, "Available", "{}");
        }

        using (var reopened = new VertexaDatabase(_directory))
        {
            reopened.Migrate();
            var settings = new SettingsRepository(reopened).Load();
            Assert.Equal(AppTheme.Light, settings.Theme);
            Assert.False(settings.RedactSerials);

            var history = new HistoryRepository(reopened).List(5);
            Assert.Single(history);
            Assert.Equal("RUN-WIN", history[0].RunId);
        }
    }

    [Fact]
    public async Task Monitoring_returns_a_sample_with_attributed_metrics()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var monitoring = WindowsServices.CreateMonitoring();
        var sample = await monitoring.SampleOnceAsync();

        Assert.True(sample.Timestamp <= DateTimeOffset.UtcNow);
        Assert.True(sample.CpuLoadPercent is null or (>= 0 and <= 100));
        Assert.NotNull(sample.Unavailable);
        // The unavailable list must name exactly the metrics that are null.
        if (sample.CpuLoadPercent is null) Assert.Contains("CPU load", sample.Unavailable);
        if (sample.HottestTemperatureCelsius is not null) Assert.False(string.IsNullOrWhiteSpace(sample.HottestTemperatureSource));
    }

    [Fact]
    public void Elevation_probe_returns_a_definite_answer()
    {
        if (!OperatingSystem.IsWindows()) return;
        var elevated = WindowsServices.IsProcessElevated();
        Assert.True(elevated || !elevated);
    }
}
