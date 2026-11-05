using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Windows.Providers;

internal sealed class CpuCollector : WindowsCollectorBase
{
    private const string Source = "WMI:Win32_Processor";
    private const string Method = "CIM query";

    public CpuCollector(IAppLogger? log = null) : base(log) { }
    public override string SectionKey => SectionKeys.Cpu;
    public override string Title => "Processor";
    public override int Order => 15;

    protected override void CollectCore(DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
    {
        var section = Section(snapshot);
        var result = Wmi("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, CurrentClockSpeed, ExtClock, L2CacheSize, L3CacheSize, Architecture, AddressWidth, DataWidth, SocketDesignation, LoadPercentage, VirtualizationFirmwareEnabled, SecondLevelAddressTranslationExtensions, ProcessorId, Family, Stepping FROM Win32_Processor");
        var row = result.First;
        if (row is null)
        {
            MissingFromQuery<string>(section, "Processor model", result, Source, Method);
            MissingFromQuery<int>(section, "Cores", result, Source, Method);
            return;
        }

        var name = row.Text("Name");
        if (name is not null) section.Metric("Processor model", name, Source, Method);
        else section.Missing<string>("Processor model", AvailabilityState.Unavailable, "Name not reported", Source, Method);

        if (row.Text("Manufacturer") is { } vendor) section.Metric("Processor vendor", vendor, Source, Method);
        if (row.Text("SocketDesignation") is { } socket) section.Metric("Socket", socket, Source, Method, confidence: ConfidenceLevel.Medium);

        if (row.UInt("NumberOfCores") is { } cores) section.Metric("Cores", (int)cores, Source, Method);
        if (row.UInt("NumberOfLogicalProcessors") is { } threads) section.Metric("Logical processors", (int)threads, Source, Method);

        if (row.UInt("MaxClockSpeed") is { } maxClock)
        {
            section.Metric("Nominal clock", (int)maxClock, Source, Method,
                limitation: "This is the nominal/maximum rated clock reported by firmware, not a measured frequency.");
            section.Metric("Rated clock", Math.Round(maxClock / 1000.0, 2), Source, Method, unit: Units.Gigahertz);
        }
        if (row.UInt("CurrentClockSpeed") is { } currentClock)
            section.Metric("Current clock (reported)", (int)currentClock, Source, Method, confidence: ConfidenceLevel.Low,
                limitation: "Win32_Processor refreshes infrequently; the live monitoring page uses performance counters instead.");
        if (row.UInt("ExtClock") is { } busClock) section.Metric("Bus clock", (int)busClock, Source, Method, confidence: ConfidenceLevel.Low);

        if (row.UInt("L2CacheSize") is { } l2 && l2 > 0) section.Metric("L2 cache", (int)l2, Source, Method, unit: "KB");
        if (row.UInt("L3CacheSize") is { } l3 && l3 > 0) section.Metric("L3 cache", (int)l3, Source, Method, unit: "KB");

        if (row.UInt("AddressWidth") is { } width)
            section.Metric("Address width", (int)width, Source, Method, unit: "bit");
        if (row.UInt("Architecture") is { } architecture)
            section.Metric("Architecture", DescribeArchitecture(architecture), Source, Method);
        if (row.UInt("Family") is { } family) section.Metric("Processor family id", (int)family, Source, Method, confidence: ConfidenceLevel.Low);

        if (row.Bool("VirtualizationFirmwareEnabled") is { } vt)
            section.Metric("Virtualisation firmware enabled", vt, Source, Method, confidence: ConfidenceLevel.Medium);
        if (row.Bool("SecondLevelAddressTranslationExtensions") is { } slat)
            section.Metric("SLAT support", slat, Source, Method, confidence: ConfidenceLevel.Medium);
        if (row.Text("ProcessorId") is { } processorId)
            section.Metric("Processor ID", processorId, Source, Method, confidence: ConfidenceLevel.Medium,
                limitation: "Vendor-defined identifier; not a serial number.");

        // Current load from performance counters (authoritative, refreshed continuously).
        var counters = Wmi(@"SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'",
            "root\\cimv2");
        if (counters.First?.ULong("PercentProcessorTime") is { } load)
            section.Metric("CPU load (sampled)", (int)load, "WMI:Win32_PerfFormattedData_PerfOS_Processor", "Performance counter snapshot", unit: Units.Percent, confidence: ConfidenceLevel.Medium);
        else
            section.Missing<int>("CPU load (sampled)", AvailabilityState.Unavailable, counters.Error ?? "counter not returned", "Performance counter", "snapshot");
    }

    internal static string DescribeArchitecture(uint architecture) => architecture switch
    {
        0 => "x86",
        1 => "MIPS",
        2 => "Alpha",
        3 => "PowerPC",
        5 => "ARM",
        6 => "Itanium",
        9 => "x64",
        12 => "ARM64",
        _ => $"Unknown ({architecture})",
    };
}
