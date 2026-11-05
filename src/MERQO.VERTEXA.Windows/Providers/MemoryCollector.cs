using Merqo.Vertexa.Core.Firmware;
using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Windows.Providers;

internal sealed class MemoryCollector : WindowsCollectorBase
{
    private const string Source = "WMI:Win32_PhysicalMemory";
    private const string Method = "CIM query";

    public MemoryCollector(IAppLogger? log = null) : base(log) { }
    public override string SectionKey => SectionKeys.Memory;
    public override string Title => "Memory";
    public override int Order => 20;

    protected override void CollectCore(DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
    {
        var section = Section(snapshot);

        // Live usage from the Win32 API (cheap, always available).
        var status = Native.NativeMethods.ReadMemoryStatus();
        if (status.TotalPhysical > 0)
        {
            section.Metric("Installed memory", Units.BytesToHuman(status.TotalPhysical), "Kernel32!GlobalMemoryStatusEx", "Native API");
            section.Metric("Available memory", Units.BytesToHuman(status.AvailablePhysical), "Kernel32!GlobalMemoryStatusEx", "Native API");
            section.Metric("Memory load", (int)status.MemoryLoad, "Kernel32!GlobalMemoryStatusEx", "Native API", unit: Units.Percent);
            section.Metric("Commit total", Units.BytesToHuman(status.TotalPageFile - status.AvailablePageFile), "Kernel32!GlobalMemoryStatusEx", "Native API", confidence: ConfidenceLevel.Medium);
            section.Metric("Commit limit", Units.BytesToHuman(status.TotalPageFile), "Kernel32!GlobalMemoryStatusEx", "Native API");
        }
        else
        {
            section.Missing<string>("Installed memory", AvailabilityState.Unavailable, "GlobalMemoryStatusEx failed", "Kernel32!GlobalMemoryStatusEx", "Native API");
        }

        var modules = Wmi("SELECT Manufacturer, PartNumber, SerialNumber, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, FormFactor, DeviceLocator, BankLabel FROM Win32_PhysicalMemory");
        if (modules.Ok && modules.Rows.Count > 0)
        {
            section.Metric("Memory modules installed", modules.Rows.Count, Source, Method);
            var total = 0UL;
            var index = 0;
            foreach (var module in modules.Rows)
            {
                index++;
                var capacity = module.ULong("Capacity") ?? 0;
                total += capacity;
                var locator = module.Text("DeviceLocator") ?? $"Module {index}";
                section.Metric($"{locator} · capacity", Units.BytesToHuman(capacity), Source, Method);
                if (module.Text("Manufacturer") is { } vendor) section.Metric($"{locator} · manufacturer", vendor, Source, Method);
                if (module.Text("PartNumber") is { } part) section.Metric($"{locator} · part number", part.Trim(), Source, Method);
                if (module.UInt("SMBIOSMemoryType") is { } smbiosType && SmbiosParser.MemoryTypeName((int)smbiosType) is { } typeName)
                    section.Metric($"{locator} · type", typeName, Source, Method);
                if (module.UInt("Speed") is { } speed && speed > 0) section.Metric($"{locator} · rated speed", (int)speed, Source, Method, unit: "MT/s");
                if (module.UInt("ConfiguredClockSpeed") is { } configured && configured > 0)
                    section.Metric($"{locator} · configured speed", (int)configured, Source, Method, unit: "MT/s",
                        limitation: "Configured speed below rated speed usually means the memory controller is running it slower.");
                if (module.UInt("FormFactor") is { } form) section.Metric($"{locator} · form factor", FormFactorName(form), Source, Method);
            }
            section.Metric("Total across modules", Units.BytesToHuman(total), Source, Method);
        }
        else
        {
            MissingFromQuery<int>(section, "Memory modules installed", modules, Source, Method);
            // Fallback: SMBIOS type 17 via the firmware table.
            var table = FirmwareCollector.TryReadSmbiosTable();
            if (table is not null)
            {
                var devices = SmbiosParser.MemoryDevices(SmbiosParser.Parse(table));
                if (devices.Count > 0)
                {
                    section.Metric("Memory modules installed (SMBIOS)", devices.Count, "SMBIOS type 17", "Firmware table parse",
                        confidence: ConfidenceLevel.Medium, verification: VerificationState.Corroborated);
                    foreach (var device in devices)
                        section.Metric($"{device.Locator ?? "Module"} · capacity (SMBIOS)", Units.BytesToHuman(device.SizeBytes), "SMBIOS type 17", "Firmware table parse", confidence: ConfidenceLevel.Medium);
                }
            }
        }

        var array = Wmi("SELECT MemoryDevices, MaxCapacityEx FROM Win32_PhysicalMemoryArray");
        if (array.First is { } slotRow)
        {
            if (slotRow.UInt("MemoryDevices") is { } slotCount)
                section.Metric("Memory slots", (int)slotCount, "WMI:Win32_PhysicalMemoryArray", Method);
            if (slotRow.ULong("MaxCapacityEx") is { } maxCapacity && maxCapacity > 0)
                section.Metric("Maximum supported memory", Units.BytesToHuman(maxCapacity), "WMI:Win32_PhysicalMemoryArray", Method,
                    confidence: ConfidenceLevel.Medium, limitation: "Firmware-reported ceiling; the real limit can depend on the CPU memory controller.");
        }

        var installedModules = modules.Rows.Count;
        if (installedModules > 0 && array.First?.UInt("MemoryDevices") is { } totalSlots && totalSlots > 0)
        {
            section.Metric("Occupied slots", $"{installedModules} of {totalSlots}", Source, Method);
            if (installedModules < totalSlots)
                section.Metric("Free memory slots", (int)(totalSlots - installedModules), Source, Method,
                    limitation: "Physical slots may still be unreachable (e.g. soldered memory or a laptop with a single accessible slot).");
        }
    }

    private static string FormFactorName(uint form) => form switch
    {
        8 => "DIMM",
        12 => "SODIMM",
        13 => "SRIMM",
        11 => "Row of chips",
        _ => $"Form factor {form}",
    };
}
