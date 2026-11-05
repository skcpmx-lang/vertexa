using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Shared;
using Merqo.Vertexa.Windows.Wmi;

namespace Merqo.Vertexa.Windows.Providers;

internal sealed class DisplayCollector : WindowsCollectorBase
{
    public DisplayCollector(IAppLogger? log = null) : base(log) { }
    public override string SectionKey => SectionKeys.Display;
    public override string Title => "Display";
    public override int Order => 50;
    public override bool IncludedInQuickScan => false;

    protected override void CollectCore(DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
    {
        var section = Section(snapshot);

        var monitors = Wmi("SELECT InstanceName, ManufacturerName, UserFriendlyName, YearOfManufacture, WeekOfManufacture, SerialNumberID, ProductCodeID FROM WmiMonitorID", WmiQuery.WmiScope);
        if (monitors.Ok && monitors.Rows.Count > 0)
        {
            section.Metric("Monitors detected", monitors.Rows.Count, "root\\wmi:WmiMonitorID", "CIM query");
            var index = 0;
            foreach (var monitor in monitors.Rows)
            {
                index++;
                var name = DecodeWmiString(monitor["UserFriendlyName"]) ?? DecodeWmiString(monitor["InstanceName"]) ?? $"Monitor {index}";
                var vendor = DecodeWmiString(monitor["ManufacturerName"]);
                section.Metric($"Monitor {index} · name", name.Trim('\0', ' '), "root\\wmi:WmiMonitorID", "CIM query");
                if (vendor is not null) section.Metric($"Monitor {index} · manufacturer code", vendor.Trim('\0', ' '), "root\\wmi:WmiMonitorID", "CIM query");
                if (monitor.Int("YearOfManufacture") is { } year && year > 1990)
                    section.Metric($"Monitor {index} · year of manufacture", year, "root\\wmi:WmiMonitorID", "CIM query", confidence: ConfidenceLevel.Medium);
            }
        }
        else
        {
            section.Missing<int>("Monitors detected", AvailabilityState.Unavailable,
                monitors.Error ?? "monitor identity class returned nothing (common for virtual displays)",
                "root\\wmi:WmiMonitorID", "CIM query");
        }

        var basic = Wmi("SELECT InstanceName, Active, DisplayType, VideoInputType, HorizontalActivePixels, VerticalActivePixels FROM WmiMonitorBasicDisplayParams", WmiQuery.WmiScope);
        if (basic.Ok && basic.Rows.Count > 0)
        {
            var active = basic.Rows.Count(r => r.Bool("Active") == true);
            section.Metric("Active displays", active == 0 ? basic.Rows.Count : active, "root\\wmi:WmiMonitorBasicDisplayParams", "CIM query");
            var index = 0;
            foreach (var row in basic.Rows)
            {
                index++;
                if (row.Text("InstanceName") is { } instance) section.Metric($"Display {index} · instance", instance, "WmiMonitorBasicDisplayParams", "CIM query", confidence: ConfidenceLevel.Medium);
            }
        }

        var modes = Wmi("SELECT Name, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController");
        if (modes.Ok)
        {
            var index = 0;
            foreach (var row in modes.Rows)
            {
                index++;
                var width = row.UInt("CurrentHorizontalResolution");
                var height = row.UInt("CurrentVerticalResolution");
                var refresh = row.UInt("CurrentRefreshRate");
                if (width is > 0 && height is > 0)
                    section.Metric($"Output {index} · desktop resolution", $"{width} × {height}", "WMI:Win32_VideoController", "CIM query");
                if (refresh is > 0)
                    section.Metric($"Output {index} · refresh rate", (int)refresh, "WMI:Win32_VideoController", "CIM query", unit: "Hz",
                        limitation: "The reported refresh rate is the desktop mode, not a measurement of panel response.");
            }
        }
    }

    /// <summary>WmiMonitorID string properties arrive as arrays of ASCII codes.</summary>
    internal static string? DecodeWmiString(object? value)
    {
        if (value is ushort[] codes && codes.Length > 0)
            return new string(codes.Where(c => c != 0).Select(c => (char)c).ToArray());
        if (value is byte[] bytes && bytes.Length > 0)
            return new string(bytes.Where(b => b != 0).Select(b => (char)b).ToArray());
        if (value is string s) return s;
        return null;
    }
}
