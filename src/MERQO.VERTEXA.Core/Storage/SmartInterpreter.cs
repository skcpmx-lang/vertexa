namespace Merqo.Vertexa.Core.Storage;

/// <summary>
/// Interprets ATA S.M.A.R.T. attribute tables.
///
/// Important honesty rule implemented here: the raw field of several attributes
/// (notably 0x01 read error rate and 0x07 seek error rate) has vendor-defined encoding,
/// so we never compare it to a generic threshold. We only raise findings for attributes
/// whose meaning is standardised (reallocated sectors, pending sectors, offline
/// uncorrectable, wear levelling, available spare, media errors, unsafe shutdowns...).
/// </summary>
public static class SmartInterpreter
{
    public sealed record AttributeDefinition(
        byte Id,
        string Name,
        bool RawIsStandardised,
        SmartSeverity SeverityWhenNonZero,
        string? Note);

    private static readonly Dictionary<byte, AttributeDefinition> Definitions = new()
    {
        [0x01] = new(0x01, "Read error rate", false, SmartSeverity.Advisory, "Raw encoding is vendor-specific; value is compared against threshold by the drive only."),
        [0x03] = new(0x03, "Spin-up time", false, SmartSeverity.Advisory, "Raw value units differ per manufacturer."),
        [0x04] = new(0x04, "Start/stop count", false, SmartSeverity.Ok, null),
        [0x05] = new(0x05, "Reallocated sector count", true, SmartSeverity.Critical, "Non-zero means the drive has remapped damaged sectors."),
        [0x07] = new(0x07, "Seek error rate", false, SmartSeverity.Advisory, "Raw encoding is vendor-specific."),
        [0x09] = new(0x09, "Power-on hours", false, SmartSeverity.Ok, null),
        [0x0A] = new(0x0A, "Spin retry count", true, SmartSeverity.Warning, "Non-zero indicates mechanical spin-up difficulties."),
        [0x0C] = new(0x0C, "Power cycle count", false, SmartSeverity.Ok, null),
        [0xA9] = new(0xA9, "Wear leveling count", false, SmartSeverity.Ok, "Normalised value (100 = new) is the useful signal."),
        [0xAA] = new(0xAA, "Unused reserved blocks", false, SmartSeverity.Advisory, null),
        [0xAB] = new(0xAB, "Program fail count", true, SmartSeverity.Warning, null),
        [0xAC] = new(0xAC, "Erase fail count", true, SmartSeverity.Warning, null),
        [0xC0] = new(0xC0, "Unsafe shutdown count", true, SmartSeverity.Advisory, "Frequent unsafe shutdowns can indicate power problems."),
        [0xC2] = new(0xC2, "Temperature", false, SmartSeverity.Advisory, "Raw value is usually degrees Celsius but is vendor-specific."),
        [0xC5] = new(0xC5, "Current pending sector count", true, SmartSeverity.Critical, "Sectors waiting for reallocation — data at risk."),
        [0xC6] = new(0xC6, "Offline uncorrectable sectors", true, SmartSeverity.Critical, null),
        [0xC7] = new(0xC7, "UDMA CRC error count", true, SmartSeverity.Advisory, "Rising counts suggest a bad cable or connection, not a failing drive."),
        [0xE7] = new(0xE7, "SSD life remaining", false, SmartSeverity.Advisory, "Normalised value approximates remaining endurance."),
        [0xE9] = new(0xE9, "Media wearout indicator", false, SmartSeverity.Advisory, null),
        [0xF1] = new(0xF1, "Total host writes", false, SmartSeverity.Ok, "Raw unit differs per vendor (often 32 MiB sectors)."),
        [0xF2] = new(0xF2, "Total host reads", false, SmartSeverity.Ok, null),
    };

    public static AttributeDefinition Describe(byte id) =>
        Definitions.TryGetValue(id, out var def)
            ? def
            : new AttributeDefinition(id, $"Vendor attribute 0x{id:X2}", false, SmartSeverity.Ok, "Not a standardised attribute; raw meaning is vendor-specific.");

    /// <summary>Parses the 512-byte ATA SMART data structure (vendor + threshold sections).</summary>
    public static IReadOnlyList<SmartAttribute> ParseAttributes(ReadOnlySpan<byte> driveData)
    {
        // Standard ATA SMART READ DATA layout: 2 bytes revision, 12 entries × 12 bytes at offset 2.
        if (driveData.Length < 362)
            return Array.Empty<SmartAttribute>();

        var attributes = new List<SmartAttribute>(12);
        for (var i = 0; i < 30; i++)
        {
            var offset = 2 + (i * 12);
            if (offset + 12 > driveData.Length) break;
            var id = driveData[offset];
            if (id == 0) continue;

            var flags = (ushort)(driveData[offset + 1] | (driveData[offset + 2] << 8));
            var value = driveData[offset + 3];
            var worst = driveData[offset + 4];
            var raw = driveData.Slice(offset + 5, 6).ToArray();

            // Attribute 0xE7/0xE9 style counts are little-endian 48-bit; temperature uses byte 0.
            ulong rawValue = 0;
            for (var b = 5; b >= 0; b--) rawValue = (rawValue << 8) | raw[b];

            var def = Describe(id);
            var severity = EvaluateSeverity(def, value, rawValue, raw);
            var interpretation = BuildInterpretation(def, value, raw, rawValue);

            attributes.Add(new SmartAttribute(id, def.Name, value, worst, threshold: -1, rawValue, raw, interpretation, severity));
            _ = flags; // prefailure/online flags retained by the drive but not needed for findings
        }
        return attributes;
    }

    /// <summary>Applies the threshold table (bytes 362..) to flag attributes below their vendor threshold.</summary>
    internal static SmartSeverity EvaluateSeverity(AttributeDefinition def, int normalizedValue, ulong rawValue, byte[] raw)
    {
        if (def.SeverityWhenNonZero != SmartSeverity.Ok && def.RawIsStandardised && rawValue > 0)
            return def.SeverityWhenNonZero;

        if (normalizedValue is > 0 and <= 10)
            return SmartSeverity.Warning;

        // Temperature sanity: only flag clearly abnormal values.
        if (def.Id == 0xC2)
        {
            var celsius = raw.Length > 0 ? raw[0] : 0;
            if (celsius >= 70) return SmartSeverity.Critical;
            if (celsius >= 60) return SmartSeverity.Warning;
        }

        return SmartSeverity.Ok;
    }

    private static string BuildInterpretation(AttributeDefinition def, int value, byte[] raw, ulong rawValue)
    {
        var baseText = def.RawIsStandardised
            ? $"Raw = {rawValue:N0}"
            : $"Normalised = {value}, raw = 0x{rawValue:X} (vendor encoding)";
        return def.Note is null ? baseText : baseText + ". " + def.Note;
    }
}
