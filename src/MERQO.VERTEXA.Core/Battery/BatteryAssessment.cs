using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Core.Battery;

public enum PowerLineState { Unknown = 0, OnBattery = 1, PluggedIn = 2 }

/// <summary>Typed view of the battery section, produced from measured metrics only.</summary>
public sealed class BatteryAssessment
{
    public bool Present { get; init; }
    public string? Name { get; init; }
    public string? Manufacturer { get; init; }
    public string? Chemistry { get; init; }
    public uint DesignCapacityMWh { get; init; }
    public uint FullChargeCapacityMWh { get; init; }
    public uint RemainingCapacityMWh { get; init; }
    public int? CycleCount { get; init; }
    public double? HealthPercent { get; init; }
    public double? WearPercent { get; init; }
    public AvailabilityState HealthState { get; init; } = AvailabilityState.Unavailable;
    public string HealthExplanation { get; init; } = string.Empty;
    public double? ChargePercent { get; init; }
    public double? VoltageMillivolts { get; init; }
    public double? RateMilliwatts { get; init; }
    public PowerLineState PowerLine { get; init; }
    public bool IsCharging { get; init; }
    public bool IsDischarging { get; init; }
    public double? EstimatedRuntimeHours { get; init; }
    public string RuntimeLabel =>
        EstimatedRuntimeHours is null ? "Not available" : $"≈ {EstimatedRuntimeHours:0.0} h (estimate from measured discharge)";
    public string Band => Battery.BatteryMath.HealthBand(HealthPercent);
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public static BatteryAssessment Absent(string reason) => new()
    {
        Present = false,
        HealthExplanation = reason,
        HealthState = AvailabilityState.NotApplicable,
        Evidence = new[] { reason },
    };
}
