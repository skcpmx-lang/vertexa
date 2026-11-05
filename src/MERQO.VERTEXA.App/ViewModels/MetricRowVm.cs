using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.App.ViewModels;

/// <summary>Presentation wrapper for any metric; keeps provenance visible in the UI.</summary>
public sealed class MetricRowVm
{
    public MetricRowVm(IDiagnosticMetric metric)
    {
        Name = metric.Name;
        DisplayWithUnit = metric.DisplayWithUnit;
        Source = metric.Source;
        DetectionMethod = metric.DetectionMethod;
        Availability = metric.Availability;
        Confidence = metric.Confidence;
        HasData = metric.HasData;
        Notes = metric.Notes.Count > 0 ? string.Join(" · ", metric.Notes) : (metric.UnavailableReason ?? string.Empty);
        Limitation = metric.Limitation ?? string.Empty;
        Tooltip = string.IsNullOrWhiteSpace(Notes) ? DetectionMethod : DetectionMethod + "\n" + Notes;
    }

    public string Name { get; }
    public string DisplayWithUnit { get; }
    public string Source { get; }
    public string DetectionMethod { get; }
    public AvailabilityState Availability { get; }
    public ConfidenceLevel Confidence { get; }
    public bool HasData { get; }
    public string Notes { get; }
    public string Limitation { get; }
    public string Tooltip { get; }
    public bool HasLimitation => !string.IsNullOrWhiteSpace(Limitation);

    public string StateLabel => Availability.Label();
    public string ConfidenceLabel => "Confidence: " + Confidence;
    public string Provenance => $"{Source} · {DetectionMethod}";
    public string DetailLine => string.IsNullOrWhiteSpace(Notes) ? Provenance : Provenance + " — " + Notes;
}
