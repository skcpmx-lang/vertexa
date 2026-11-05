using Merqo.Vertexa.Core.Contracts;
using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Windows.Providers;

/// <summary>Machine identity: manufacturer, model, system type, uptime and virtualisation state.</summary>
internal sealed class SystemCollector : WindowsCollectorBase
{
    private const string Source = "WMI:Win32_ComputerSystem";
    private const string Method = "CIM query";

    public SystemCollector(IAppLogger? log = null) : base(log) { }

    public override string SectionKey => SectionKeys.System;
    public override string Title => "System identity";
    public override int Order => 5;

    protected override void CollectCore(DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
    {
        var section = Section(snapshot);
        var cs = Wmi("SELECT Manufacturer, Model, SystemType, SystemFamily, SystemSKUNumber, Name, Domain, PartOfDomain, TotalPhysicalMemory, NumberOfLogicalProcessors, HypervisorPresent FROM Win32_ComputerSystem");
        cancellationToken.ThrowIfCancellationRequested();

        var row = cs.First;
        if (row is null)
        {
            MissingFromQuery<string>(section, "Manufacturer", cs, Source, Method);
            MissingFromQuery<string>(section, "Model", cs, Source, Method);
            snapshot.MachineKind = MachineKind.Unknown;
        }
        else
        {
            AddOrMissing(section, "Manufacturer", row.Text("Manufacturer"));
            AddOrMissing(section, "Model", row.Text("Model"));
            AddOrMissing(section, "System type", row.Text("SystemType"));
            AddOrMissing(section, "System family", row.Text("SystemFamily"));
            AddOrMissing(section, "System SKU", row.Text("SystemSKUNumber"));
            AddOrMissing(section, "Computer name", row.Text("Name"));

            var totalRam = row.ULong("TotalPhysicalMemory");
            if (totalRam is > 0)
                section.Metric("Total physical memory", Units.BytesToHuman(totalRam.Value), Source, Method);

            var logical = row.UInt("NumberOfLogicalProcessors");
            if (logical is > 0)
                section.Metric("Logical processors", (int)logical.Value, Source, Method);

            var hypervisor = row.Bool("HypervisorPresent");
            snapshot.MachineKind = Classify(row.Text("Manufacturer"), row.Text("Model"), hypervisor);
            section.Metric("Platform class", snapshot.MachineKind switch
            {
                MachineKind.VirtualMachine => "Virtual machine",
                MachineKind.Physical => "Physical machine",
                _ => "Unknown",
            }, Source, Method, confidence: ConfidenceLevel.Medium);
            section.Metric("Hypervisor present", hypervisor ?? false, Source, Method, confidence: ConfidenceLevel.Medium);

            var domain = row.Text("Domain");
            var partOfDomain = row.Bool("PartOfDomain");
            if (domain is not null)
                section.Metric("Workgroup / domain", domain, Source, Method,
                    confidence: partOfDomain is null ? ConfidenceLevel.Medium : ConfidenceLevel.High);
        }

        var os = Wmi("SELECT LastBootUpTime FROM Win32_OperatingSystem");
        var bootTime = os.First?.Date("LastBootUpTime");
        if (bootTime is not null)
        {
            var uptime = DateTime.Now - bootTime.Value;
            if (uptime > TimeSpan.Zero)
                section.Metric("Uptime", Units.Uptime(uptime), "WMI:Win32_OperatingSystem", Method);
            section.Metric("Last boot", bootTime.Value, "WMI:Win32_OperatingSystem", Method,
                confidence: ConfidenceLevel.Medium,
                limitation: "Fast Startup can make the recorded boot time later than the physical power-on time.");
        }
        else
        {
            MissingFromQuery<string>(section, "Uptime", os, "WMI:Win32_OperatingSystem", Method);
        }

        section.Metric("Process elevation", snapshot.IsElevated ? "Administrator" : "Standard user",
            "Win32 token (WindowsIdentity)", "WindowsPrincipal.IsInRole(Administrator)");
    }

    private static void AddOrMissing(MetricSection section, string name, string? value)
    {
        if (value is null)
            section.Missing<string>(name, AvailabilityState.Unavailable, "not reported by firmware", Source, Method);
        else
            section.Metric(name, value, Source, Method);
    }

    internal static MachineKind Classify(string? manufacturer, string? model, bool? hypervisorPresent)
    {
        var haystack = ((manufacturer ?? string.Empty) + " " + (model ?? string.Empty)).ToLowerInvariant();
        string[] vmSigns = { "virtual", "vmware", "virtualbox", "kvm", "xen", "hyper-v", "parallels", "qemu", "bochs", "bhyve" };
        if (vmSigns.Any(haystack.Contains)) return MachineKind.VirtualMachine;
        if (hypervisorPresent == true && haystack.Contains("cloud")) return MachineKind.VirtualMachine;
        if (!string.IsNullOrWhiteSpace(haystack.Trim())) return MachineKind.Physical;
        return MachineKind.Unknown;
    }
}
