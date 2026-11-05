using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Core.Models;

/// <summary>
/// An ordered, de-duplicated group of metrics. This is the one shape the whole UI,
/// the scoring engine and every report writer consume, which is why provenance can
/// never be lost on the way to the screen or the PDF.
/// </summary>
public sealed class MetricSection
{
    private readonly List<IDiagnosticMetric> _metrics = new();

    public MetricSection(string key, string title, string description = "")
    {
        Key = key;
        Title = title;
        Description = description;
    }

    public string Key { get; }
    public string Title { get; }
    public string Description { get; }
    public IReadOnlyList<IDiagnosticMetric> Metrics => _metrics;

    public int TotalCount => _metrics.Count;
    public int AvailableCount => _metrics.Count(m => m.HasData);

    public AvailabilityState AggregateState
    {
        get
        {
            if (_metrics.Count == 0) return AvailabilityState.NotTested;
            var available = _metrics.Count(m => m.HasData);
            if (available == 0)
            {
                // Report the most informative reason, not the most common one.
                foreach (var state in new[] { AvailabilityState.AccessDenied, AvailabilityState.RequiresAdministrator,
                                              AvailabilityState.RequiresPhysicalHardware, AvailabilityState.Unsupported,
                                              AvailabilityState.Unavailable, AvailabilityState.Invalid })
                {
                    if (_metrics.Any(m => m.Availability == state)) return state;
                }
                return AvailabilityState.Unavailable;
            }
            return available == _metrics.Count ? AvailabilityState.Available : AvailabilityState.PartiallyAvailable;
        }
    }

    public MetricSection Add(IDiagnosticMetric metric)
    {
        ArgumentNullException.ThrowIfNull(metric);
        if (_metrics.Any(m => string.Equals(m.Name, metric.Name, StringComparison.Ordinal))) return this;
        _metrics.Add(metric);
        return this;
    }

    public MetricSection AddRange(params IDiagnosticMetric[] metrics)
    {
        foreach (var m in metrics) Add(m);
        return this;
    }

    public MetricSection AddRange(IEnumerable<IDiagnosticMetric> metrics)
    {
        foreach (var m in metrics) Add(m);
        return this;
    }

    public DiagnosticValue<T>? Find<T>(string name) =>
        _metrics.OfType<DiagnosticValue<T>>().FirstOrDefault(m => m.Name == name);

    public DiagnosticValue<T>? FindStartsWith<T>(string prefix) =>
        _metrics.OfType<DiagnosticValue<T>>().FirstOrDefault(m => m.Name.StartsWith(prefix, StringComparison.Ordinal));

    public string? Text(string name) => Find<string>(name)?.Value;

    public IEnumerable<IDiagnosticMetric> Available() => _metrics.Where(m => m.HasData);
}
