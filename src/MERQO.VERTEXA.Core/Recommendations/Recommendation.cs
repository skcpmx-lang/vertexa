namespace Merqo.Vertexa.Core.Recommendations;

public enum Severity { Info = 0, Advisory = 1, Warning = 2, Critical = 3 }

/// <summary>
/// A finding with evidence. The rule engine may only emit these when a real metric
/// supports it; <see cref="Evidence"/> must be non-empty for severity ≥ Advisory.
/// </summary>
public sealed record Recommendation(
    string Id,
    string Title,
    Severity Severity,
    string Condition,
    IReadOnlyList<string> Evidence,
    string PossibleCauses,
    string SuggestedAction,
    string Confidence,
    string? SafetyNote = null,
    string? FeatureRef = null,
    bool Confirmed = true)
{
    public string SeverityLabel => Severity switch
    {
        Severity.Critical => "Critical",
        Severity.Warning => "Warning",
        Severity.Advisory => "Advisory",
        _ => "Information",
    };
}
