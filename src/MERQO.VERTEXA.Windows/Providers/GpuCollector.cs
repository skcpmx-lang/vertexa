using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Windows.Providers;

internal sealed class GpuCollector : WindowsCollectorBase
{
    private const string Source = "WMI:Win32_VideoController";
    private const string Method = "CIM query";

    public GpuCollector(IAppLogger? log = null) : base(log) { }
    public override string SectionKey => SectionKeys.Gpu;
    public override string Title => "Graphics";
    public override int Order => 35;

    protected override void CollectCore(DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
    {
        var section = Section(snapshot);
        var result = Wmi("SELECT Name, AdapterCompatibility, VideoProcessor, DriverVersion, DriverDate, AdapterRAM, VideoModeDescription, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate, PNPDeviceID, Status, InstalledDisplayDrivers, AdapterDACType FROM Win32_VideoController");

        if (!result.Ok || result.Rows.Count == 0)
        {
            MissingFromQuery<int>(section, "Graphics adapters", result, Source, Method);
            snapshot.AddWarning("No graphics adapter could be enumerated through WMI.");
            return;
        }

        section.Metric("Graphics adapters", result.Rows.Count, Source, Method);
        var index = 0;
        foreach (var row in result.Rows)
        {
            index++;
            var name = row.Text("Name") ?? $"Adapter {index}";
            section.Metric($"Adapter {index} · model", name, Source, Method);
            if (row.Text("AdapterCompatibility") is { } vendor) section.Metric($"Adapter {index} · vendor", vendor, Source, Method);
            if (row.Text("VideoProcessor") is { } processor) section.Metric($"Adapter {index} · video processor", processor, Source, Method);

            if (row.Text("DriverVersion") is { } driver)
                section.Metric($"Adapter {index} · driver version", driver, Source, Method);
            else
                section.Missing<string>($"Adapter {index} · driver version", AvailabilityState.Unavailable, "not reported", Source, Method);
            if (row.Date("DriverDate") is { } driverDate)
                section.Metric($"Adapter {index} · driver date", driverDate, Source, Method, confidence: ConfidenceLevel.Medium);

            var adapterRam = row.ULong("AdapterRAM");
            if (adapterRam is > 0)
            {
                section.Metric($"Adapter {index} · dedicated memory (reported)", Units.BytesToHuman(adapterRam.Value), Source, Method,
                    confidence: adapterRam.Value >= 4_294_967_296UL ? ConfidenceLevel.Low : ConfidenceLevel.Medium,
                    limitation: adapterRam.Value >= 4_294_967_296UL
                        ? "WMI AdapterRAM saturates at 4 GB; use the vendor tool for the true dedicated memory size."
                        : null);
            }
            else
            {
                section.Missing<string>($"Adapter {index} · dedicated memory (reported)", AvailabilityState.Unavailable,
                    "AdapterRAM not reported by this driver", Source, Method);
            }

            if (row.UInt("CurrentHorizontalResolution") is { } width && row.UInt("CurrentVerticalResolution") is { } height)
                section.Metric($"Adapter {index} · desktop mode", $"{width} × {height}", Source, Method);
            if (row.UInt("CurrentRefreshRate") is { } refresh)
                section.Metric($"Adapter {index} · refresh rate", (int)refresh, Source, Method, unit: "Hz");
            if (row.Text("PNPDeviceID") is { } pnp) section.Metric($"Adapter {index} · PnP ID", pnp, Source, Method, confidence: ConfidenceLevel.Medium);
            section.Metric($"Adapter {index} · status", row.Text("Status") ?? "Unknown", Source, Method, confidence: ConfidenceLevel.Medium);

            // Precision: dedicated VRAM from the signed driver registry block when exposed.
            if (pnp is not null && TryReadDedicatedMemory(pnp) is { } dedicated)
                section.Metric($"Adapter {index} · dedicated memory (driver)", Units.BytesToHuman(dedicated), @"REG:HKLM\SYSTEM\CurrentControlSet\Control\Class", "Registry read",
                    confidence: ConfidenceLevel.Medium, verification: VerificationState.Corroborated);
        }

        // GPU utilisation from the per-engine performance counters (sum across engines).
        var engines = Wmi("SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
        if (engines.Ok && engines.Rows.Count > 0)
        {
            var busiest = engines.Rows
                .Select(r => (Name: r.Text("Name") ?? "engine", Value: r.ULong("UtilizationPercentage") ?? 0))
                .OrderByDescending(r => r.Value)
                .First();
            section.Metric("GPU utilisation (busiest engine)", (int)busiest.Value, "WMI:GPUEngine performance counters", "Performance counter",
                unit: Units.Percent, confidence: ConfidenceLevel.Medium,
                limitation: "Windows reports utilisation per engine instance; VERTEXA shows the busiest engine rather than a synthetic total.");
        }
        else
        {
            section.Missing<int>("GPU utilisation (busiest engine)", AvailabilityState.Unavailable,
                engines.Error ?? "GPU Engine counters are not registered on this system",
                "WMI:GPUEngine performance counters", "Performance counter", Units.Percent);
        }

        // VRAM usage counters exist on Windows 10 1709+.
        var memory = Wmi("SELECT Name, DedicatedUsage, SharedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPULocalAdapterMemory");
        if (memory.Ok && memory.Rows.Count > 0)
        {
            var best = memory.Rows.OrderByDescending(r => r.ULong("DedicatedUsage") ?? 0).First();
            if (best.ULong("DedicatedUsage") is { } used)
                section.Metric("GPU dedicated memory in use", Units.BytesToHuman(used), "WMI:GPULocalAdapterMemory", "Performance counter", confidence: ConfidenceLevel.Medium);
            if (best.ULong("SharedUsage") is { } shared)
                section.Metric("GPU shared memory in use", Units.BytesToHuman(shared), "WMI:GPULocalAdapterMemory", "Performance counter", confidence: ConfidenceLevel.Medium);
        }
    }

    private static ulong? TryReadDedicatedMemory(string pnpDeviceId)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (key is null) return null;
            foreach (var subKeyName in key.GetSubKeyNames())
            {
                if (!subKeyName.All(char.IsDigit)) continue;
                using var sub = key.OpenSubKey(subKeyName);
                if (sub is null) continue;
                var matchingId = sub.GetValue("MatchingDeviceId") as string;
                if (matchingId is null) continue;
                if (!pnpDeviceId.Contains(matchingId.Replace("&", "&").ToUpperInvariant(), StringComparison.OrdinalIgnoreCase) &&
                    !matchingId.Contains(pnpDeviceId.Split('\\').Last().Split('&')[0], StringComparison.OrdinalIgnoreCase))
                    continue;
                if (sub.GetValue("HardwareInformation.qwMemorySize") is long qw && qw > 0) return (ulong)qw;
                if (sub.GetValue("HardwareInformation.MemorySize") is byte[] bytes && bytes.Length >= 4)
                    return (ulong)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | ((uint)bytes[3] << 24));
            }
        }
        catch
        {
            // Registry layout varies; absence is not an error.
        }
        return null;
    }
}
