using System.Text;

namespace Merqo.Vertexa.Core.Firmware;

/// <summary>One raw SMBIOS structure (spec §7.1): 4-byte header, formatted area, string set.</summary>
public sealed class SmbiosStructure
{
    public SmbiosStructure(byte type, byte length, ushort handle, byte[] formatted, IReadOnlyList<string> strings)
    {
        Type = type;
        Length = length;
        Handle = handle;
        Formatted = formatted;
        Strings = strings;
    }

    public byte Type { get; }
    public byte Length { get; }
    public ushort Handle { get; }
    public byte[] Formatted { get; }
    public IReadOnlyList<string> Strings { get; }

    /// <summary>SMBIOS strings are 1-based; index 0 means "no string".</summary>
    public string? Text(byte offset)
    {
        if (offset >= Formatted.Length) return null;
        var index = Formatted[offset];
        if (index == 0 || index > Strings.Count) return null;
        var value = Strings[index - 1];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public byte ByteAt(byte offset) => offset < Formatted.Length ? Formatted[offset] : (byte)0;

    public ushort WordAt(byte offset) =>
        (ushort)(ByteAt(offset) | (ByteAt((byte)(offset + 1)) << 8));
}

public sealed record SmbiosMemoryDevice(
    string? Locator,
    string? BankLocator,
    string? Manufacturer,
    string? PartNumber,
    string? SerialNumber,
    ulong SizeBytes,
    string? TypeName,
    string? FormFactor,
    int? SpeedMtPerSecond,
    int? ConfiguredSpeedMtPerSecond,
    int? Rank);

/// <summary>
/// Pure SMBIOS parser (SMBIOS 2.x–3.x). Written to never throw on malformed firmware
/// tables: a truncated or hostile table yields the structures parsed so far.
/// </summary>
public static class SmbiosParser
{
    public const int RawTableHeaderSize = 8;

    public static IReadOnlyList<SmbiosStructure> Parse(ReadOnlySpan<byte> rawTable)
    {
        var structures = new List<SmbiosStructure>();
        if (rawTable.Length <= RawTableHeaderSize) return structures;

        var offset = RawTableHeaderSize;
        var guard = 0;
        while (offset + 4 <= rawTable.Length && guard++ < 512)
        {
            var type = rawTable[offset];
            var length = rawTable[offset + 1];
            if (length < 4) break; // malformed — stop rather than read garbage
            if (offset + length > rawTable.Length) break;

            var handle = (ushort)(rawTable[offset + 2] | (rawTable[offset + 3] << 8));
            var formatted = rawTable.Slice(offset, length).ToArray();

            // Walk the string set: terminated by a double NUL.
            var cursor = offset + length;
            var strings = new List<string>();
            while (cursor + 1 < rawTable.Length)
            {
                if (rawTable[cursor] == 0 && rawTable[cursor + 1] == 0)
                {
                    cursor += 2;
                    break;
                }
                var end = cursor;
                while (end < rawTable.Length && rawTable[end] != 0) end++;
                if (end >= rawTable.Length) { cursor = rawTable.Length; break; }
                strings.Add(Encoding.ASCII.GetString(rawTable.Slice(cursor, end - cursor)));
                cursor = end + 1;
            }

            structures.Add(new SmbiosStructure(type, length, handle, formatted, strings));
            if (type == 127) break; // end-of-table
            offset = cursor;
        }
        return structures;
    }

    public static SmbiosStructure? Find(IReadOnlyList<SmbiosStructure> structures, byte type, int instance = 0) =>
        structures.Where(s => s.Type == type).Skip(instance).FirstOrDefault();

    public static string? BiosVendor(IReadOnlyList<SmbiosStructure> s) => Find(s, 0)?.Text(0x04);
    public static string? BiosVersion(IReadOnlyList<SmbiosStructure> s) => Find(s, 0)?.Text(0x05);
    public static string? BiosDate(IReadOnlyList<SmbiosStructure> s) => Find(s, 0)?.Text(0x08);
    public static string? SystemManufacturer(IReadOnlyList<SmbiosStructure> s) => Find(s, 1)?.Text(0x04);
    public static string? SystemProduct(IReadOnlyList<SmbiosStructure> s) => Find(s, 1)?.Text(0x05);
    public static string? SystemVersion(IReadOnlyList<SmbiosStructure> s) => Find(s, 1)?.Text(0x06);
    public static string? SystemSerial(IReadOnlyList<SmbiosStructure> s) => Find(s, 1)?.Text(0x07);
    public static string? SystemSku(IReadOnlyList<SmbiosStructure> s) => Find(s, 1)?.Text(0x19);
    public static string? BoardManufacturer(IReadOnlyList<SmbiosStructure> s) => Find(s, 2)?.Text(0x04);
    public static string? BoardProduct(IReadOnlyList<SmbiosStructure> s) => Find(s, 2)?.Text(0x05);
    public static string? BoardVersion(IReadOnlyList<SmbiosStructure> s) => Find(s, 2)?.Text(0x06);

    public static int? MemoryArraySlotCount(IReadOnlyList<SmbiosStructure> structures)
    {
        var array = Find(structures, 16);
        if (array is null || array.Length <= 0x0D) return null;
        var devices = array.WordAt(0x0D);
        return devices is 0 or 0xFFFF ? null : devices;
    }

    public static IReadOnlyList<SmbiosMemoryDevice> MemoryDevices(IReadOnlyList<SmbiosStructure> structures)
    {
        var devices = new List<SmbiosMemoryDevice>();
        foreach (var structure in structures.Where(s => s.Type == 17))
        {
            var sizeWord = structure.WordAt(0x0C);
            ulong sizeBytes;
            if (sizeWord == 0xFFFF)
            {
                sizeBytes = 0; // unknown
            }
            else if (sizeWord == 0x7FFF && structure.Length > 0x1F)
            {
                var extended = (ulong)(structure.Formatted[0x1F] | (structure.Formatted[0x1E] << 8) | (structure.Formatted[0x1D] << 16) | (structure.Formatted[0x1C] << 24));
                sizeBytes = extended * 1024UL * 1024UL;
            }
            else if (sizeWord == 0)
            {
                sizeBytes = 0; // no device installed
            }
            else
            {
                // Bit 15 set means kilobytes, otherwise megabytes (SMBIOS 2.7+).
                sizeBytes = (sizeWord & 0x8000) != 0
                    ? (ulong)(sizeWord & 0x7FFF) * 1024UL
                    : (ulong)sizeWord * 1024UL * 1024UL;
            }

            if (sizeBytes == 0) continue;

            var typeByte = structure.ByteAt(0x12);
            var formFactor = structure.ByteAt(0x0E);
            int? speed = structure.WordAt(0x15);
            int? configured = structure.Length > 0x21 ? structure.WordAt(0x20) : null;
            int? rank = structure.Length > 0x1B ? structure.ByteAt(0x1B) : null;

            devices.Add(new SmbiosMemoryDevice(
                Locator: structure.Text(0x10),
                BankLocator: structure.Text(0x11),
                Manufacturer: structure.Text(0x17),
                PartNumber: structure.Text(0x1A),
                SerialNumber: structure.Text(0x18),
                SizeBytes: sizeBytes,
                TypeName: MemoryTypeName(typeByte),
                FormFactor: MemoryFormFactor(formFactor),
                SpeedMtPerSecond: speed is > 0 and < 0xFFFF ? speed : null,
                ConfiguredSpeedMtPerSecond: configured is > 0 and < 0xFFFF ? configured : null,
                Rank: rank));
        }
        return devices;
    }

    public static string? MemoryTypeName(int type) => type switch
    {
        0x12 => "DDR",
        0x13 => "DDR2",
        0x18 => "DDR3",
        0x1A => "DDR4",
        0x1B => "LPDDR",
        0x1C => "LPDDR2",
        0x1D => "LPDDR3",
        0x1E => "LPDDR4",
        0x22 => "DDR5",
        0x23 => "LPDDR5",
        _ => null,
    };

    public static string? MemoryFormFactor(int formFactor) => formFactor switch
    {
        0x09 => "DIMM",
        0x0D => "SODIMM",
        0x0C => "SRIMM",
        0x02 => "Unknown",
        0x0B => "Row of chips",
        0x0F => "FB-DIMM",
        _ => null,
    };
}
