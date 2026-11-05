namespace Merqo.Vertexa.Shared;

/// <summary>
/// Untyped read model of any diagnostic fact. Reports, tables and chart bindings
/// work against this interface so they can render any metric with full provenance.
/// </summary>
public interface IDiagnosticMetric
{
    string Name { get; }
    string DisplayValue { get; }
    string Unit { get; }
    AvailabilityState Availability { get; }
    ConfidenceLevel Confidence { get; }
    VerificationState Verification { get; }
    string Source { get; }
    string DetectionMethod { get; }
    DateTimeOffset CollectedAt { get; }
    string? UnavailableReason { get; }
    string? Limitation { get; }
    IReadOnlyList<string> Notes { get; }
    /// <summary>True when the UI may render <see cref="DisplayValue"/> as data.</summary>
    bool HasData { get; }
}
