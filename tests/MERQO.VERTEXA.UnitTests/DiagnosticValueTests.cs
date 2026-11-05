using Merqo.Vertexa.Shared;
using Xunit;

namespace Merqo.Vertexa.UnitTests;

/// <summary>
/// The contract that makes fabrication impossible: a value cannot exist without a source,
/// and an unavailable metric cannot pretend to carry data.
/// </summary>
public sealed class DiagnosticValueTests
{
    [Fact]
    public void Available_value_carries_source_method_and_timestamp()
    {
        var metric = DiagnosticValue<int>.FromAvailable("Cores", 8, "WMI:Win32_Processor", "CIM query");

        Assert.True(metric.HasData);
        Assert.Equal(AvailabilityState.Available, metric.Availability);
        Assert.Equal("WMI:Win32_Processor", metric.Source);
        Assert.Equal("CIM query", metric.DetectionMethod);
        Assert.Equal(8, metric.Value);
        Assert.True(metric.CollectedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Available_value_without_source_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            DiagnosticValue<int>.FromAvailable("Cores", 8, "  ", "CIM query"));
    }

    [Fact]
    public void Available_value_without_detection_method_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            DiagnosticValue<int>.FromAvailable("Cores", 8, "WMI", ""));
    }

    [Fact]
    public void Unavailable_value_cannot_claim_data()
    {
        var metric = DiagnosticValue<double>.FromUnavailable("Temperature", AvailabilityState.Unsupported,
            "no sensor exposed", "WMI", "ACPI thermal zone");

        Assert.False(metric.HasData);
        Assert.Equal(AvailabilityState.Unsupported, metric.Availability);
        Assert.Equal("Unsupported", metric.DisplayValue);
        Assert.Contains("no sensor exposed", metric.UnavailableReason);
    }

    [Fact]
    public void Unavailable_factory_rejects_available_state()
    {
        Assert.Throws<ArgumentException>(() =>
            DiagnosticValue<double>.FromUnavailable("x", AvailabilityState.Available, "reason"));
    }

    [Fact]
    public void Missing_reason_is_replaced_with_explicit_default()
    {
        var metric = DiagnosticValue<int>.FromUnavailable("x", AvailabilityState.Unavailable, "   ");
        Assert.False(string.IsNullOrWhiteSpace(metric.UnavailableReason));
    }

    [Fact]
    public void Display_includes_unit_only_when_data_exists()
    {
        var withUnit = DiagnosticValue<int>.FromAvailable("Clock", 3200, "WMI", "CIM", unit: "MHz");
        Assert.Equal("3,200 MHz", withUnit.DisplayWithUnit);

        var unavailable = DiagnosticValue<int>.FromUnavailable("Clock", AvailabilityState.Unavailable, "absent", unit: "MHz");
        Assert.Equal("Unavailable", unavailable.DisplayWithUnit);
    }

    [Theory]
    [InlineData(true, "Yes")]
    [InlineData(false, "No")]
    public void Booleans_render_without_numbers(bool value, string expected)
    {
        var metric = DiagnosticValue<bool>.FromAvailable("Secure Boot", value, "REG", "Registry read");
        Assert.Equal(expected, metric.DisplayValue);
    }

    [Fact]
    public void Confidence_labels_are_explicit()
    {
        var metric = DiagnosticValue<int>.FromUnavailable("x", AvailabilityState.Invalid, "out of range");
        Assert.Equal(ConfidenceLevel.Unknown, metric.Confidence);
        Assert.Equal(VerificationState.Unverified, metric.Verification);
    }
}
