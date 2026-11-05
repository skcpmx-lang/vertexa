using Merqo.Vertexa.Core.Firmware;
using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Windows.Providers;

/// <summary>SMBIOS firmware table + BIOS/board identity, with WMI corroboration.</summary>
internal sealed class FirmwareCollector : WindowsCollectorBase
{
    private const string SmbiosSource = "SMBIOS firmware table (RSMB)";
    private const string SmbiosMethod = "GetSystemFirmwareTable + structure parse";

    public FirmwareCollector(IAppLogger? log = null) : base(log) { }
    public override string SectionKey => SectionKeys.Firmware;
    public override string Title => "Board & firmware";
    public override int Order => 25;

    /// <summary>Reads the raw SMBIOS table. Returns null when the kernel refuses the provider.</summary>
    internal static byte[]? TryReadSmbiosTable()
    {
        var size = Native.NativeMethods.GetSystemFirmwareTable(Native.NativeMethods.RSMB, 0, IntPtr.Zero, 0);
        if (size == 0) return null;
        var buffer = new byte[size];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var written = Native.NativeMethods.GetSystemFirmwareTable(Native.NativeMethods.RSMB, 0, handle.AddrOfPinnedObject(), size);
            if (written == 0) return null;
            return written == buffer.Length ? buffer : buffer.Take((int)written).ToArray();
        }
        finally
        {
            handle.Free();
        }
    }

    protected override void CollectCore(DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
    {
        var section = Section(snapshot);
        var table = TryReadSmbiosTable();
        IReadOnlyList<SmbiosStructure> structures = Array.Empty<SmbiosStructure>();
        if (table is null)
        {
            section.Missing<string>("BIOS vendor", AvailabilityState.Unavailable, "SMBIOS table not provided by the kernel",
                SmbiosSource, SmbiosMethod);
        }
        else
        {
            structures = SmbiosParser.Parse(table);
            if (table.Length >= 8)
            {
                var major = table[4];
                var minor = table[5];
                section.Metric("SMBIOS version", $"{major}.{minor}", SmbiosSource, SmbiosMethod);
            }
            section.Metric("SMBIOS structures parsed", structures.Count, SmbiosSource, SmbiosMethod, confidence: ConfidenceLevel.Medium);

            Add(section, "BIOS vendor", SmbiosParser.BiosVendor(structures));
            Add(section, "BIOS version", SmbiosParser.BiosVersion(structures));
            Add(section, "BIOS release date", SmbiosParser.BiosDate(structures));
            Add(section, "System manufacturer", SmbiosParser.SystemManufacturer(structures));
            Add(section, "System product", SmbiosParser.SystemProduct(structures));
            Add(section, "Board manufacturer", SmbiosParser.BoardManufacturer(structures));
            Add(section, "Board product", SmbiosParser.BoardProduct(structures));
            Add(section, "Board version", SmbiosParser.BoardVersion(structures));
            Add(section, "System SKU", SmbiosParser.SystemSku(structures));

            if (SmbiosParser.Find(structures, 1) is { } system)
            {
                var chassis = SmbiosParser.Find(structures, 3);
                if (chassis is not null)
                    section.Metric("Chassis type", ChassisType(chassis.ByteAt(0x05)), SmbiosSource, SmbiosMethod);
            }

            if (SmbiosParser.MemoryArraySlotCount(structures) is { } slots)
                section.Metric("SMBIOS memory slots", slots, SmbiosSource, SmbiosMethod, confidence: ConfidenceLevel.Medium);
        }

        var bios = Wmi("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, Version, SMBIOSMajorVersion, SMBIOSMinorVersion, SerialNumber FROM Win32_BIOS");
        if (bios.First is { } biosRow)
        {
            if (biosRow.Text("Manufacturer") is { } vendor) section.Metric("BIOS vendor (WMI)", vendor, "WMI:Win32_BIOS", "CIM query", verification: VerificationState.SingleSource);
            if (biosRow.Text("SMBIOSBIOSVersion") is { } version) section.Metric("BIOS version (WMI)", version, "WMI:Win32_BIOS", "CIM query");
            if (biosRow.Date("ReleaseDate") is { } releaseDate)
                section.Metric("BIOS date (WMI)", releaseDate, "WMI:Win32_BIOS", "CIM query", confidence: ConfidenceLevel.Medium);
        }
        else
        {
            MissingFromQuery<string>(section, "BIOS vendor (WMI)", bios, "WMI:Win32_BIOS", "CIM query");
        }

        var board = Wmi("SELECT Manufacturer, Product, Version, SerialNumber FROM Win32_BaseBoard");
        if (board.First is { } boardRow)
        {
            if (boardRow.Text("Manufacturer") is { } boardVendor) section.Metric("Board manufacturer (WMI)", boardVendor, "WMI:Win32_BaseBoard", "CIM query");
            if (boardRow.Text("Product") is { } product) section.Metric("Board product (WMI)", product, "WMI:Win32_BaseBoard", "CIM query");
            if (boardRow.Text("Version") is { } boardVersion) section.Metric("Board version (WMI)", boardVersion, "WMI:Win32_BaseBoard", "CIM query");
        }
        else
        {
            MissingFromQuery<string>(section, "Board manufacturer (WMI)", board, "WMI:Win32_BaseBoard", "CIM query");
        }

        var cs = Wmi("SELECT SystemSKUNumber, PCSystemType, SystemFamily FROM Win32_ComputerSystem");
        if (cs.First?.Int("PCSystemType") is { } systemType)
        {
            section.Metric("System class", systemType switch
            {
                1 => "Desktop",
                2 => "Mobile (laptop)",
                3 => "Workstation",
                4 => "Enterprise server",
                5 => "Small office / home office server",
                6 => "Appliance PC",
                7 => "Performance server",
                _ => "Unspecified",
            }, "WMI:Win32_ComputerSystem", "CIM query", confidence: ConfidenceLevel.Medium);
        }
    }

    private static void Add(MetricSection section, string name, string? value)
    {
        if (value is null)
            section.Missing<string>(name, AvailabilityState.Unavailable, "structure absent or string empty", SmbiosSource, SmbiosMethod);
        else
            section.Metric(name, value, SmbiosSource, SmbiosMethod);
    }

    internal static string ChassisType(byte code) => code switch
    {
        1 => "Other",
        2 => "Unknown",
        3 => "Desktop",
        4 => "Low-profile desktop",
        5 => "Pizza box",
        6 => "Mini tower",
        7 => "Tower",
        8 => "Portable",
        9 => "Laptop",
        10 => "Notebook",
        11 => "Hand-held",
        12 => "Docking station",
        13 => "All-in-one",
        14 => "Sub-notebook",
        30 => "Tablet",
        31 => "Convertible",
        32 => "Detachable",
        _ => $"Chassis code {code}",
    };
}
