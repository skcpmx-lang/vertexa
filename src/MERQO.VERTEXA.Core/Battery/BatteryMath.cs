namespace Merqo.Vertexa.Core.Battery;

/// <summary>
/// Battery capacity arithmetic. Every entry point is pure and every failure path is
/// explicit: this type exists so that health/wear can never be invented for a battery
/// whose firmware does not report both capacities.
/// </summary>
public static class BatteryMath
{
    /// <summary>Above this ratio the reported capacities are nonsense (common on cheap firmware).</summary>
    public const double MaxPlausibleRatio = 1.25;

    public sealed record CapacityAssessment(
        bool IsValid,
        double? HealthPercent,
        double? WearPercent,
        string Explanation,
        string? RejectReason)
    {
        public static CapacityAssessment Invalid(string reason) => new(false, null, null, reason, reason);
    }

    /// <summary>
    /// health = FCC / design × 100 · wear = (design − FCC) / design × 100.
    /// Both are only produced when design &gt; 0, FCC &gt; 0 and FCC ≤ design × 1.25.
    /// </summary>
    public static CapacityAssessment Assess(uint designCapacityMWh, uint fullChargeCapacityMWh)
    {
        if (designCapacityMWh == 0 && fullChargeCapacityMWh == 0)
            return CapacityAssessment.Invalid("Neither design nor full-charge capacity is reported by the battery firmware.");

        if (designCapacityMWh == 0)
            return CapacityAssessment.Invalid("Design capacity is not reported, so wear cannot be computed honestly.");

        if (fullChargeCapacityMWh == 0)
            return CapacityAssessment.Invalid("Full-charge capacity is not reported (battery may be fully discharged or the firmware hides it).");

        if (designCapacityMWh > 1_000_000 || fullChargeCapacityMWh > 1_000_000)
            return CapacityAssessment.Invalid("Reported capacity is outside any plausible battery range (possible unit error).");

        var ratio = (double)fullChargeCapacityMWh / designCapacityMWh;
        if (ratio > MaxPlausibleRatio)
            return CapacityAssessment.Invalid(
                $"Full-charge capacity ({fullChargeCapacityMWh} mWh) exceeds design capacity by more than {MaxPlausibleRatio:P0}; the firmware data is inconsistent.");

        var health = Math.Round(ratio * 100.0, 1);
        var wear = Math.Round((1.0 - ratio) * 100.0, 1);
        if (wear < 0) wear = 0;

        var explanation =
            $"Health {health:0.0}% and wear {wear:0.0}% derived from design {designCapacityMWh} mWh and full-charge {fullChargeCapacityMWh} mWh " +
            "(health = FCC ÷ design × 100; wear = (design − FCC) ÷ design × 100).";

        return new CapacityAssessment(true, health, wear, explanation, null);
    }

    /// <summary>Grade band used for colour only — the numeric value stays authoritative.</summary>
    public static string HealthBand(double? healthPercent) => healthPercent switch
    {
        null => "Unknown",
        >= 90 => "Excellent",
        >= 80 => "Good",
        >= 65 => "Fair",
        >= 45 => "Poor",
        _ => "Replace",
    };

    /// <summary>
    /// Runtime estimate from observed discharge. Inputs are measured watts/mWh; the
    /// call site must label the result as an estimate in the UI.
    /// </summary>
    public static double? EstimateRuntimeHours(uint fullChargeCapacityMWh, double dischargeMilliwatts, uint remainingCapacityMWh)
    {
        if (dischargeMilliwatts <= 50) return null;      // too small to be a real discharge rate
        if (remainingCapacityMWh == 0) return null;
        var hours = remainingCapacityMWh / dischargeMilliwatts;
        if (hours > 24 || hours < 0) return null;
        return Math.Round(hours, 2);
    }
}
