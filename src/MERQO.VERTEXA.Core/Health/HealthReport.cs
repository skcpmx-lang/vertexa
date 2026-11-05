using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Core.Health;

public sealed record SubsystemScore(
    string Key,
    string Title,
    double? Score,
    AvailabilityState State,
    double Weight,
    string Explanation,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Excluded);

public sealed record HealthReport(
    double? Overall,
    AvailabilityState State,
    string Headline,
    string Formula,
    IReadOnlyList<SubsystemScore> Subsystems,
    IReadOnlyList<string> Notes)
{
    public string OverallDisplay => Overall is null ? "Insufficient evidence" : $"{Overall:0} / 100";
    public bool HasScore => Overall is not null;
}
