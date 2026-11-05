using System.Text.RegularExpressions;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Security;

/// <summary>
/// Masks hardware identifiers in anything the user might share. Enabled by default
/// because diagnostic reports are frequently exchanged when buying/selling hardware.
/// </summary>
public sealed class RedactionService
{
    private static readonly Regex MacPattern = new(@"\b([0-9A-F]{2}[:-]){5}[0-9A-F]{2}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SerialPattern = new(@"\b(?:serial|s/n)\s*[:#]?\s*([A-Za-z0-9\-\.]{4,})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GuidPattern = new(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public RedactionService(bool enabled = true) => Enabled = enabled;

    public bool Enabled { get; set; }

    public string Redact(string? value)
    {
        if (!Enabled || string.IsNullOrEmpty(value)) return value ?? string.Empty;
        var result = MacPattern.Replace(value, "••:••:••:••:••:••");
        result = GuidPattern.Replace(result, "••••••••-••••-••••-••••-••••••••••••");
        result = SerialPattern.Replace(result, m => m.Value[..Math.Max(0, m.Value.Length - m.Groups[1].Length)] + Mask(m.Groups[1].Value));
        return result;
    }

    /// <summary>Masks everything except the last two characters, which is enough to match a sticker.</summary>
    public string MaskSerial(string? serial)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(serial)) return serial ?? string.Empty;
        return Mask(serial);
    }

    private static string Mask(string value) =>
        value.Length <= 2 ? new string('•', value.Length) : new string('•', Math.Max(0, value.Length - 2)) + value[^2..];
}
