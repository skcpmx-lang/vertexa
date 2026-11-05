using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Windows.Providers;

internal sealed class NetworkCollector : WindowsCollectorBase
{
    public NetworkCollector(IAppLogger? log = null) : base(log) { }
    public override string SectionKey => SectionKeys.Network;
    public override string Title => "Network";
    public override int Order => 65;
    public override bool IncludedInQuickScan => false;

    protected override void CollectCore(DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
    {
        var section = Section(snapshot);

        var adapters = Wmi("SELECT Name, InterfaceDescription, LinkSpeed, MediaType, State, MacAddress, PhysicalMediaType, InterfaceIndex, MtuSize, FullDuplex FROM MSFT_NetAdapter",
            @"root\StandardCimv2");
        if (adapters.Ok && adapters.Rows.Count > 0)
        {
            section.Metric("Network adapters", adapters.Rows.Count, "WMI:MSFT_NetAdapter", "CIM query");
            foreach (var adapter in adapters.Rows)
            {
                var name = adapter.Text("Name") ?? "Adapter";
                var description = adapter.Text("InterfaceDescription");
                var state = adapter.Text("State");
                var media = MediaKind(adapter.ULong("PhysicalMediaType"));
                section.Metric($"{name} · device", description ?? "unknown", "MSFT_NetAdapter", "CIM query");
                section.Metric($"{name} · state", state ?? "unknown", "MSFT_NetAdapter", "CIM query",
                    confidence: state is null ? ConfidenceLevel.Low : ConfidenceLevel.High);
                if (adapter.ULong("LinkSpeed") is { } speed && speed > 0)
                    section.Metric($"{name} · link speed", Units.BytesToHuman(speed) + "/s", "MSFT_NetAdapter", "CIM query",
                        confidence: ConfidenceLevel.Medium, limitation: "Advertised link speed, not a measured throughput.");
                if (media is not null) section.Metric($"{name} · media", media, "MSFT_NetAdapter", "CIM query", confidence: ConfidenceLevel.Medium);
                if (adapter.Text("MacAddress") is { } mac)
                    section.Metric($"{name} · MAC address", mac, "MSFT_NetAdapter", "CIM query", confidence: ConfidenceLevel.Medium,
                        limitation: "Redacted in exported reports unless the user opts in.");
                if (adapter.ULong("MtuSize") is { } mtu) section.Metric($"{name} · MTU", (int)mtu, "MSFT_NetAdapter", "CIM query", confidence: ConfidenceLevel.Medium);
            }
        }
        else
        {
            var legacy = Wmi("SELECT Description, NetConnectionStatus, Speed, MACAddress, AdapterType FROM Win32_NetworkAdapter WHERE PhysicalAdapter=True");
            if (legacy.Ok && legacy.Rows.Count > 0)
            {
                section.Metric("Network adapters", legacy.Rows.Count, "WMI:Win32_NetworkAdapter", "CIM query", confidence: ConfidenceLevel.Medium);
                foreach (var adapter in legacy.Rows)
                {
                    var description = adapter.Text("Description") ?? "Adapter";
                    section.Metric($"{description} · state", NetConnectionStatus(adapter.UInt("NetConnectionStatus")), "Win32_NetworkAdapter", "CIM query", confidence: ConfidenceLevel.Medium);
                    if (adapter.ULong("Speed") is { } speed && speed > 0)
                        section.Metric($"{description} · link speed", Units.BytesToHuman(speed) + "/s", "Win32_NetworkAdapter", "CIM query", confidence: ConfidenceLevel.Medium);
                }
            }
            else
            {
                MissingFromQuery<int>(section, "Network adapters", adapters, "WMI:MSFT_NetAdapter", "CIM query");
            }
        }

        var wireless = Wmi("SELECT Name, InterfaceDescription, State FROM MSFT_NetAdapter WHERE PhysicalMediaType=9", @"root\StandardCimv2");
        if (wireless.Ok)
        {
            section.Metric("Wi-Fi adapters", wireless.Rows.Count, "WMI:MSFT_NetAdapter (Native 802.11)", "CIM query", confidence: ConfidenceLevel.Medium);
            section.Missing<string>("Wi-Fi signal strength", AvailabilityState.Unsupported,
                "signal/SSID require the WlanAPI; VERTEXA 1.0 does not query WLAN state",
                "WlanAPI", "n/a",
                limitation: "Planned for a future release; no value is shown rather than an approximation.");
        }

        var bluetooth = Wmi("SELECT Name, DeviceID, Status, Manufacturer FROM Win32_PnPEntity WHERE PNPClass='Bluetooth' OR DeviceID LIKE 'BTH%'");
        if (bluetooth.Ok)
        {
            section.Metric("Bluetooth devices", bluetooth.Rows.Count, "WMI:Win32_PnPEntity (Bluetooth)", "CIM query", confidence: ConfidenceLevel.Medium);
            foreach (var device in bluetooth.Rows.Take(12))
            {
                var name = device.Text("Name") ?? device.Text("DeviceID");
                if (name is not null)
                    section.Metric($"Bluetooth · {name}", device.Text("Status") ?? "unknown", "Win32_PnPEntity", "CIM query", confidence: ConfidenceLevel.Low);
            }
        }

        var ethernet = Wmi("SELECT Name FROM MSFT_NetAdapter WHERE PhysicalMediaType IN (14, 0) AND InterfaceDescription LIKE '%Ethernet%'", @"root\StandardCimv2");
        if (ethernet.Ok && ethernet.Rows.Count > 0)
            section.Metric("Ethernet adapters", ethernet.Rows.Count, "WMI:MSFT_NetAdapter", "CIM query", confidence: ConfidenceLevel.Medium);
    }

    internal static string? MediaKind(ulong? physicalMediaType) => physicalMediaType switch
    {
        0 => "802.3 (Ethernet)",
        9 => "Native 802.11 (Wi-Fi)",
        10 => "Bluetooth",
        13 => "WWAN",
        14 => "Ethernet",
        _ => null,
    };

    internal static string NetConnectionStatus(uint? status) => status switch
    {
        0 => "Disconnected",
        1 => "Connecting",
        2 => "Connected",
        3 => "Disconnecting",
        4 => "Hardware not present",
        5 => "Hardware disabled",
        6 => "Hardware malfunction",
        7 => "Media disconnected",
        8 => "Authenticating",
        9 => "Authentication succeeded",
        10 => "Authentication failed",
        11 => "Invalid address",
        12 => "Credentials required",
        _ => "Unknown",
    };
}
