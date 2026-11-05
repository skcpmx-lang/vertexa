using Merqo.Vertexa.Core.Battery;
using Merqo.Vertexa.Core.Storage;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Core.Models;

public enum ScanScope { Quick = 0, Full = 1, Targeted = 2 }
public enum MachineKind { Unknown = 0, Physical = 1, VirtualMachine = 2 }

/// <summary>
/// The complete, self-describing result of one scan. Everything the UI shows and every
/// report writes comes from here — there is no second path to data.
/// </summary>
public sealed class DiagnosticSnapshot
{
    private readonly Dictionary<string, MetricSection> _sections = new(StringComparer.Ordinal);
    private readonly List<string> _warnings = new();

    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string AppVersion { get; init; } = "0.0.0";
    public string MachineName { get; init; } = Environment.MachineName;
    public ScanScope Scope { get; init; } = ScanScope.Full;
    public TimeSpan Duration { get; set; }
    public MachineKind MachineKind { get; set; } = MachineKind.Unknown;
    public bool IsElevated { get; set; }
    public bool RedactionEnabled { get; init; } = true;

    public IReadOnlyCollection<MetricSection> Sections => _sections.Values;
    public IReadOnlyList<string> Warnings => _warnings;

    public BatteryAssessment? Battery { get; set; }
    public IReadOnlyList<StorageDeviceReport> StorageDevices { get; set; } = Array.Empty<StorageDeviceReport>();
    public IReadOnlyList<DeviceRecord> Devices { get; set; } = Array.Empty<DeviceRecord>();
    public IReadOnlyList<Benchmarks.BenchmarkResult> Benchmarks { get; set; } = Array.Empty<Benchmarks.BenchmarkResult>();
    public IReadOnlyList<Recommendations.Recommendation> Recommendations { get; set; } = Array.Empty<Recommendations.Recommendation>();
    public Health.HealthReport? Health { get; set; }

    public MetricSection Section(string key)
    {
        if (_sections.TryGetValue(key, out var existing)) return existing;
        var meta = SectionKeys.Catalogue.FirstOrDefault(c => c.Key == key);
        var created = new MetricSection(key, meta.Title ?? key, meta.Description ?? string.Empty);
        _sections[key] = created;
        return created;
    }

    public MetricSection? Find(string key) => _sections.TryGetValue(key, out var s) ? s : null;

    public void AddWarning(string warning)
    {
        if (!string.IsNullOrWhiteSpace(warning) && !_warnings.Contains(warning)) _warnings.Add(warning);
    }

    public IEnumerable<IDiagnosticMetric> AllMetrics => _sections.Values.SelectMany(s => s.Metrics);

    public int TotalMetricCount => AllMetrics.Count();
    public int AvailableMetricCount => AllMetrics.Count(m => m.HasData);

    public string? MachineModel => Find(SectionKeys.System)?.Text("Model");

    public string SummaryLine =>
        $"{MachineName} · {AvailableMetricCount}/{TotalMetricCount} metrics available · {Duration.TotalSeconds:N1}s · " +
        $"{Sections.Count(s => s.AggregateState is AvailabilityState.Available or AvailabilityState.PartiallyAvailable)} sections with data";
}
