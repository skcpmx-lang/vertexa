using Merqo.Vertexa.Core.Battery;
using Xunit;

namespace Merqo.Vertexa.UnitTests;

/// <summary>Wear and health must be impossible to compute from invalid firmware data.</summary>
public sealed class BatteryMathTests
{
    [Fact]
    public void Health_and_wear_are_computed_from_two_valid_capacities()
    {
        var result = BatteryMath.Assess(50_000, 45_000);

        Assert.True(result.IsValid);
        Assert.Equal(90.0, result.HealthPercent);
        Assert.Equal(10.0, result.WearPercent);
        Assert.Contains("90.0", result.Explanation);
    }

    [Fact]
    public void New_battery_reports_zero_wear()
    {
        var result = BatteryMath.Assess(60_000, 60_000);
        Assert.True(result.IsValid);
        Assert.Equal(100.0, result.HealthPercent);
        Assert.Equal(0.0, result.WearPercent);
    }

    [Theory]
    [InlineData(0u, 40_000u)]
    [InlineData(50_000u, 0u)]
    [InlineData(0u, 0u)]
    public void Missing_capacity_prevents_calculation(uint design, uint full)
    {
        var result = BatteryMath.Assess(design, full);
        Assert.False(result.IsValid);
        Assert.Null(result.HealthPercent);
        Assert.Null(result.WearPercent);
        Assert.False(string.IsNullOrWhiteSpace(result.RejectReason));
    }

    [Fact]
    public void Full_charge_greater_than_design_is_rejected_as_inconsistent()
    {
        var result = BatteryMath.Assess(40_000, 60_000);
        Assert.False(result.IsValid);
        Assert.Contains("inconsistent", result.RejectReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Absurd_capacity_is_rejected()
    {
        var result = BatteryMath.Assess(50_000_000, 50_000_000);
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(95.0, "Excellent")]
    [InlineData(85.0, "Good")]
    [InlineData(70.0, "Fair")]
    [InlineData(50.0, "Poor")]
    [InlineData(20.0, "Replace")]
    [InlineData(null, "Unknown")]
    public void Health_bands_are_deterministic(double? health, string expected)
    {
        Assert.Equal(expected, BatteryMath.HealthBand(health));
    }

    [Fact]
    public void Runtime_estimate_requires_a_real_discharge_rate()
    {
        Assert.Null(BatteryMath.EstimateRuntimeHours(50_000, 10, 25_000));
        Assert.Null(BatteryMath.EstimateRuntimeHours(50_000, 5_000, 0));
        Assert.Equal(5.0, BatteryMath.EstimateRuntimeHours(50_000, 5_000, 25_000));
    }

    [Fact]
    public void Estimate_is_rejected_when_it_would_exceed_a_day()
    {
        Assert.Null(BatteryMath.EstimateRuntimeHours(50_000, 100, 50_000));
    }
}
