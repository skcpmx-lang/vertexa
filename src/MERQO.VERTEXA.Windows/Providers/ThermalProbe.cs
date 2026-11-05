using Merqo.Vertexa.Windows.Wmi;

namespace Merqo.Vertexa.Windows.Providers;

/// <summary>Cheapest reliable temperature read used by the monitoring loop and safety checks.</summary>
internal static class ThermalProbe
{
    internal static (double? Celsius, string? Source) ReadHottest()
    {
        double? hottest = null;
        string? source = null;

        var zones = WmiQuery.Run("SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", WmiQuery.WmiScope, 3000);
        if (zones.Ok)
        {
            foreach (var zone in zones.Rows)
            {
                var raw = zone.UInt("CurrentTemperature");
                if (raw is null or 0) continue;
                var celsius = (raw.Value / 10.0) - 273.15;
                if (celsius is < -40 or > 150) continue;
                if (hottest is null || celsius > hottest) hottest = celsius;
                source = "ACPI thermal zone" + (zone.Text("InstanceName") is { } name ? " (" + name + ")" : string.Empty);
            }
        }

        var counters = WmiQuery.Run("SELECT Name, HighPrecisionTemperature, Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
        if (counters.Ok)
        {
            foreach (var row in counters.Rows)
            {
                var precision = row.ULong("HighPrecisionTemperature");
                var plain = row.ULong("Temperature");
                double? celsius = precision is > 0 ? (precision.Value / 10.0) - 273.15
                    : plain is > 0 ? plain.Value - 273.15
                    : null;
                if (celsius is null or < -40 or > 150) continue;
                if (hottest is null || celsius > hottest)
                {
                    hottest = celsius;
                    source = "Thermal performance counter" + (row.Text("Name") is { } name ? " (" + name + ")" : string.Empty);
                }
            }
        }

        return (hottest is null ? null : Math.Round(hottest.Value, 1), source);
    }
}
