using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Shared;
using Merqo.Vertexa.Windows.Wmi;

namespace Merqo.Vertexa.Windows.Providers;

/// <summary>
/// Thermal telemetry. Only firmware-exposed sensors are reported; when none exist the
/// section says so and the UI can never show a synthetic temperature.
/// </summary>
internal sealed class ThermalCollector : WindowsCollectorBase
{
    public ThermalCollector(IAppLogger? log = null) : base(log) { }
    public override string SectionKey => SectionKeys.Thermal;
    public override string Title => "Thermal";
    public override int Order => 55;

    protected override void CollectCore(DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
    {
        var section = Section(snapshot);
        var found = 0;

        var zones = Wmi("SELECT InstanceName, CurrentTemperature, Active, CriticalTripPoint FROM MSAcpi_ThermalZoneTemperature", WmiQuery.WmiScope);
        if (zones.Ok)
        {
            foreach (var zone in zones.Rows)
            {
                var raw = zone.UInt("CurrentTemperature");
                if (raw is null or 0) continue;
                var celsius = Math.Round((raw.Value / 10.0) - 273.15, 1);
                if (celsius is < -40 or > 150) continue;
                var name = zone.Text("InstanceName") ?? "ACPI thermal zone";
                section.Metric(name, celsius, "root\\wmi:MSAcpi_ThermalZoneTemperature", "ACPI thermal zone",
                    unit: Units.Celsius, confidence: ConfidenceLevel.High,
                    limitation: "ACPI thermal zone values are firmware-provided and often represent a board/CPU package zone, not a core sensor.");
                found++;
            }
            if (zones.Rows.Count == 0)
                section.Missing<double>("ACPI thermal zones", AvailabilityState.Unsupported,
                    "the firmware exposes no ACPI thermal zone on this machine", "root\\wmi:MSAcpi_ThermalZoneTemperature", "ACPI thermal zone", Units.Celsius);
        }
        else
        {
            section.Missing<double>("ACPI thermal zones", AvailabilityState.Unavailable,
                zones.Error ?? "thermal zone class not present", "root\\wmi:MSAcpi_ThermalZoneTemperature", "ACPI thermal zone", Units.Celsius);
        }

        var counters = Wmi("SELECT Name, Temperature, HighPrecisionTemperature, ThrottleReasons FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
        if (counters.Ok)
        {
            foreach (var row in counters.Rows)
            {
                var kelvin = row.ULong("HighPrecisionTemperature") ?? row.ULong("Temperature");
                if (kelvin is null or 0) continue;
                // HighPrecisionTemperature is in tenths of kelvin; Temperature is kelvin.
                var celsius = row.ULong("HighPrecisionTemperature") is not null
                    ? (kelvin.Value / 10.0) - 273.15
                    : kelvin.Value - 273.15;
                celsius = Math.Round(celsius, 1);
                if (celsius is < -40 or > 150) continue;
                section.Metric($"{(row.Text("Name") ?? "Zone")} (counter)", celsius,
                    "WMI:Win32_PerfFormattedData_Counters_ThermalZoneInformation", "Performance counter",
                    unit: Units.Celsius, confidence: ConfidenceLevel.Medium,
                    limitation: "Thermal performance counters report only zones the platform registers.");
                found++;
                if (row.ULong("ThrottleReasons") is { } throttle && throttle != 0)
                    section.Metric($"{(row.Text("Name") ?? "Zone")} · throttle reasons", $"0x{throttle:X}", 
                        "Win32_PerfFormattedData_Counters_ThermalZoneInformation", "Performance counter", confidence: ConfidenceLevel.Medium);
            }
        }

        if (found == 0)
        {
            snapshot.AddWarning("No temperature sensor is exposed by this firmware; thermal readings are unavailable (this is normal on many laptops and in virtual machines).");
        }

        var fan = Wmi("SELECT Active, DesiredSpeed, CurrentSpeed FROM Win32_Fan");
        if (fan.Ok && fan.Rows.Count > 0)
        {
            var speeds = fan.Rows.Select(r => r.ULong("DesiredSpeed")).Where(v => v is > 0).ToList();
            if (speeds.Count > 0 && speeds[0] is { } rpm)
                section.Metric("Fan speed (reported)", (int)Math.Min(rpm, int.MaxValue), "WMI:Win32_Fan", "CIM query", unit: "rpm",
                    confidence: ConfidenceLevel.Low, limitation: "Windows very rarely exposes fan speed; this value is usually absent.");
            else
                section.Missing<int>("Fan speed", AvailabilityState.Unsupported, "Win32_Fan reports no speed", "WMI:Win32_Fan", "CIM query", "rpm");
        }
        else
        {
            section.Missing<int>("Fan speed", AvailabilityState.Unsupported,
                "Windows does not expose fan speed on this platform (vendor driver required)", "WMI:Win32_Fan", "CIM query", "rpm");
        }

        section.Missing<double>("CPU package power", AvailabilityState.Unsupported,
            "package power requires privileged MSR/vendor access that user-mode software cannot use",
            "none", "n/a", Units.Watt,
            limitation: "This is a platform restriction, not a missing implementation. VERTEXA never estimates power draw.");
    }
}
