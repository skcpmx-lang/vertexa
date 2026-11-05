using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Windows.Providers;

/// <summary>USB, cameras, audio endpoints, HID and sensor inventory.</summary>
internal sealed class PeripheralCollector : WindowsCollectorBase
{
    public PeripheralCollector(IAppLogger? log = null) : base(log) { }
    public override string SectionKey => SectionKeys.Peripherals;
    public override string Title => "Peripherals";
    public override int Order => 70;
    public override bool IncludedInQuickScan => false;

    protected override void CollectCore(DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
    {
        var section = Section(snapshot);

        var usb = Wmi("SELECT Name, DeviceID, Status, Manufacturer FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB%'");
        if (usb.Ok)
        {
            section.Metric("USB devices", usb.Rows.Count, "WMI:Win32_PnPEntity (USB)", "CIM query");
            foreach (var device in usb.Rows.Take(25))
            {
                var deviceId = device.Text("DeviceID") ?? string.Empty;
                var name = device.Text("Name") ?? deviceId;
                var ids = ParseUsbIds(deviceId);
                var label = ids is null ? name : $"{name} · VID {ids.Value.Vid} PID {ids.Value.Pid}";
                section.Metric($"USB · {label}", device.Text("Status") ?? "unknown", "Win32_PnPEntity", "CIM query",
                    confidence: ConfidenceLevel.Medium);
            }
        }
        else
        {
            MissingFromQuery<int>(section, "USB devices", usb, "WMI:Win32_PnPEntity (USB)", "CIM query");
        }

        var cameras = Wmi("SELECT Name, DeviceID, Status, Manufacturer FROM Win32_PnPEntity WHERE PNPClass='Camera' OR PNPClass='Image' OR PNPClass='Media'");
        if (cameras.Ok)
        {
            section.Metric("Imaging devices", cameras.Rows.Count, "WMI:Win32_PnPEntity (Image/Camera)", "CIM query", confidence: ConfidenceLevel.Medium);
            foreach (var camera in cameras.Rows)
            {
                var name = camera.Text("Name");
                if (name is null) continue;
                section.Metric($"Camera · {name}", camera.Text("Status") ?? "unknown", "Win32_PnPEntity", "CIM query", confidence: ConfidenceLevel.Medium);
            }
            section.Missing<string>("Camera live preview", AvailabilityState.RequiresPhysicalHardware,
                "preview requires a capture device and Media Foundation session; enumeration only in 1.0",
                "MediaFoundation", "n/a",
                limitation: "Documented limitation: enumeration is real, live preview is not implemented in 1.0.");
        }

        var audio = Wmi("SELECT Name, Manufacturer, Status, DeviceID FROM Win32_SoundDevice");
        if (audio.Ok)
        {
            section.Metric("Audio devices", audio.Rows.Count, "WMI:Win32_SoundDevice", "CIM query", confidence: ConfidenceLevel.Medium);
            foreach (var device in audio.Rows)
            {
                var name = device.Text("Name");
                if (name is null) continue;
                section.Metric($"Audio · {name}", device.Text("Status") ?? "unknown", "Win32_SoundDevice", "CIM query", confidence: ConfidenceLevel.Medium);
            }
        }

        var hid = Wmi("SELECT Name, DeviceID, Status FROM Win32_PnPEntity WHERE PNPClass='HIDClass' OR PNPClass='Keyboard' OR PNPClass='Mouse'");
        if (hid.Ok)
        {
            section.Metric("HID / input devices", hid.Rows.Count, "WMI:Win32_PnPEntity (HID)", "CIM query", confidence: ConfidenceLevel.Medium);
            var touch = hid.Rows.Count(r => (r.Text("Name") ?? string.Empty).Contains("touch", StringComparison.OrdinalIgnoreCase));
            section.Metric("Touch-capable devices", touch, "WMI:Win32_PnPEntity", "CIM query", confidence: ConfidenceLevel.Low,
                limitation: "Detected by device name; multi-touch capability needs a live touch test.");
        }

        var sensors = Wmi("SELECT Name, DeviceID, Status, PNPClass FROM Win32_PnPEntity WHERE PNPClass='Sensor' OR PNPClass='SensorService' OR Name LIKE '%accelerometer%' OR Name LIKE '%gyroscope%' OR Name LIKE '%ambient light%'");
        if (sensors.Ok)
        {
            section.Metric("Sensor devices", sensors.Rows.Count, "WMI:Win32_PnPEntity (Sensor)", "CIM query", confidence: ConfidenceLevel.Medium);
            foreach (var sensor in sensors.Rows.Take(10))
            {
                var name = sensor.Text("Name");
                if (name is not null)
                    section.Metric($"Sensor · {name}", sensor.Text("Status") ?? "unknown", "Win32_PnPEntity", "CIM query", confidence: ConfidenceLevel.Low);
            }
        }

        // Port capability inventory — controller presence only; a port is never claimed working.
        var controllers = Wmi("SELECT Name, DeviceID, Status FROM Win32_PnPEntity WHERE Name LIKE '%Thunderbolt%' OR Name LIKE '%USB4%' OR PNPClass='USB'");
        if (controllers.Ok)
        {
            var thunderbolt = controllers.Rows.Count(r => (r.Text("Name") ?? string.Empty).Contains("Thunderbolt", StringComparison.OrdinalIgnoreCase));
            var usb4 = controllers.Rows.Count(r => (r.Text("Name") ?? string.Empty).Contains("USB4", StringComparison.OrdinalIgnoreCase));
            section.Metric("Thunderbolt controllers", thunderbolt, "WMI:Win32_PnPEntity", "CIM query", confidence: ConfidenceLevel.Low);
            section.Metric("USB4 controllers", usb4, "WMI:Win32_PnPEntity", "CIM query", confidence: ConfidenceLevel.Low);
            section.Missing<string>("Port physical function", AvailabilityState.RequiresPhysicalHardware,
                "a port's physical function can only be proven with a device connected to it",
                "WMI:Win32_PnPEntity", "CIM query",
                limitation: "VERTEXA reports which controllers exist; it never claims a port works without a physical test.");
        }

        // SD card reader / fingerprint
        var sdcard = Wmi("SELECT Name FROM Win32_PnPEntity WHERE PNPClass='SDHost' OR Name LIKE '%SD host%' OR Name LIKE '%card reader%'");
        if (sdcard.Ok)
            section.Metric("SD/card readers", sdcard.Rows.Count, "WMI:Win32_PnPEntity", "CIM query", confidence: ConfidenceLevel.Low);

        var biometric = Wmi("SELECT Name, Status FROM Win32_PnPEntity WHERE PNPClass='Biometric' OR Name LIKE '%fingerprint%'");
        if (biometric.Ok)
        {
            section.Metric("Biometric devices", biometric.Rows.Count, "WMI:Win32_PnPEntity (Biometric)", "CIM query", confidence: ConfidenceLevel.Low);
            foreach (var device in biometric.Rows.Take(5))
            {
                var name = device.Text("Name");
                if (name is not null)
                    section.Metric($"Biometric · {name}", device.Text("Status") ?? "unknown", "Win32_PnPEntity", "CIM query", confidence: ConfidenceLevel.Low);
            }
        }
    }

    /// <summary>Extracts VID/PID from a device instance id such as USB\\VID_046D&PID_C534\\...</summary>
    internal static (string Vid, string Pid)? ParseUsbIds(string deviceId)
    {
        var vidIndex = deviceId.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
        var pidIndex = deviceId.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
        if (vidIndex < 0 || pidIndex < 0) return null;
        if (vidIndex + 8 > deviceId.Length || pidIndex + 8 > deviceId.Length) return null;
        return (deviceId.Substring(vidIndex + 4, 4).ToUpperInvariant(), deviceId.Substring(pidIndex + 4, 4).ToUpperInvariant());
    }
}
