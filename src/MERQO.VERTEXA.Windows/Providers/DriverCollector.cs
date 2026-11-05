using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Windows.Wmi;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Windows.Providers;

/// <summary>Device + driver inventory with authoritative problem codes.</summary>
internal sealed class DriverCollector : WindowsCollectorBase
{
    public DriverCollector(IAppLogger? log = null) : base(log) { }
    public override string SectionKey => SectionKeys.Drivers;
    public override string Title => "Drivers & devices";
    public override int Order => 60;
    public override bool IncludedInQuickScan => false;

    protected override void CollectCore(DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
    {
        var section = Section(snapshot);

        var devices = Wmi("SELECT DeviceID, Name, PNPClass, ConfigManagerErrorCode, Status, Manufacturer, Service, PNPDeviceID FROM Win32_PnPEntity");
        var drivers = Wmi("SELECT DeviceID, DeviceName, DriverVersion, DriverDate, DriverProviderName, InfName FROM Win32_PnPSignedDriver");

        if (!devices.Ok || devices.Rows.Count == 0)
        {
            MissingFromQuery<int>(section, "Devices enumerated", devices, "WMI:Win32_PnPEntity", "CIM query");
            return;
        }

        var driverMap = new Dictionary<string, WmiDriverInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in drivers.Rows)
        {
            var id = row.Text("DeviceID");
            if (id is null) continue;
            driverMap[id] = new WmiDriverInfo(
                row.Text("DriverVersion"),
                row.Date("DriverDate")?.ToString("yyyy-MM-dd"),
                row.Text("DriverProviderName"),
                row.Text("InfName"));
        }

        var records = new List<DeviceRecord>(devices.Rows.Count);
        foreach (var row in devices.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var deviceId = row.Text("DeviceID") ?? string.Empty;
            driverMap.TryGetValue(deviceId, out var driver);
            var pnpClass = row.Text("PNPClass");
            var problem = row.Int("ConfigManagerErrorCode");
            var name = row.Text("Name") ?? deviceId;
            var isUsb = deviceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) ||
                        pnpClass is "USB" or "HIDClass";

            records.Add(new DeviceRecord(
                name,
                pnpClass,
                deviceId,
                row.Text("Manufacturer"),
                driver.Version,
                driver.Date,
                driver.Provider,
                problem,
                row.Text("Status") ?? "Unknown",
                isUsb,
                row.Text("Service")));
        }

        snapshot.Devices = records;

        var problems = records.Where(r => r.HasProblem).ToList();
        var unknown = records.Count(r => r.ProblemCode == 28 || (r.DriverVersion is null && r.PnpClass is not null));
        var disabled = records.Count(r => r.ProblemCode == 22);

        section.Metric("Devices enumerated", records.Count, "WMI:Win32_PnPEntity", "CIM query");
        section.Metric("Devices with problems", problems.Count, "WMI:Win32_PnPEntity", "CIM query",
            confidence: ConfidenceLevel.High, limitation: "Counted from ConfigManagerErrorCode, which mirrors Device Manager's problem codes.");
        section.Metric("Unknown devices (no driver)", unknown, "WMI:Win32_PnPEntity + Win32_PnPSignedDriver", "CIM query", confidence: ConfidenceLevel.Medium);
        section.Metric("Disabled devices", disabled, "WMI:Win32_PnPEntity", "CIM query", confidence: ConfidenceLevel.Medium);

        foreach (var group in records.Where(r => r.PnpClass is not null)
                     .GroupBy(r => r.PnpClass!)
                     .OrderByDescending(g => g.Count())
                     .Take(12))
        {
            section.Metric($"Class · {group.Key}", group.Count(), "WMI:Win32_PnPEntity", "CIM query", confidence: ConfidenceLevel.Medium);
        }

        if (problems.Count > 0)
        {
            section.Metric("Devices with problems", string.Join(", ", problems.Take(8).Select(p => $"{p.Name} ({p.ProblemText})")),
                "WMI:Win32_PnPEntity", "CIM query", confidence: ConfidenceLevel.High);
            foreach (var problem in problems.Take(20))
                section.Metric($"Problem · {problem.Name}", problem.ProblemText, "WMI:Win32_PnPEntity", "CIM query");
        }

        if (!drivers.Ok)
            MissingFromQuery<string>(section, "Driver versions", drivers, "WMI:Win32_PnPSignedDriver", "CIM query");
        else
            section.Metric("Signed drivers inventoried", drivers.Rows.Count, "WMI:Win32_PnPSignedDriver", "CIM query");
    }
}
