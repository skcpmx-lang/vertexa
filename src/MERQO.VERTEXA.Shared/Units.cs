using System.Globalization;

namespace Merqo.Vertexa.Shared;

/// <summary>Unit constants and invariant-culture formatting helpers.</summary>
public static class Units
{
    public const string Megahertz = "MHz";
    public const string Gigahertz = "GHz";
    public const string Bytes = "bytes";
    public const string Percent = "%";
    public const string Celsius = "\u00B0C";
    public const string Watt = "W";
    public const string Volt = "V";
    public const string Ampere = "A";
    public const string Seconds = "s";
    public const string Milliseconds = "ms";
    public const string AmpereHour = "mWh";
    public const string Count = "";
    public const string Days = "days";
    public const string MebibytesPerSecond = "MB/s";
    public const string Iops = "IOPS";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Number(double value, int decimals = 1) =>
        value.ToString("N" + decimals.ToString(Inv), Inv);

    public static string BytesToHuman(ulong bytes)
    {
        if (bytes == 0) return "0 B";
        string[] suffix = { "B", "KB", "MB", "GB", "TB", "PB" };
        double value = bytes;
        int i = 0;
        while (value >= 1024 && i < suffix.Length - 1) { value /= 1024; i++; }
        return value.ToString(value >= 100 ? "N0" : "N1", Inv) + " " + suffix[i];
    }

    public static string BytesToHuman(long bytes) => bytes <= 0 ? "0 B" : BytesToHuman((ulong)bytes);

    public static string Duration(TimeSpan span)
    {
        if (span.TotalSeconds < 1) return Number(span.TotalMilliseconds, 0) + " ms";
        if (span.TotalMinutes < 1) return Number(span.TotalSeconds, 1) + " s";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min {span.Seconds} s";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} h {span.Minutes} min";
        return $"{(int)span.TotalDays} d {span.Hours} h";
    }

    public static string Uptime(TimeSpan span) => Duration(span);
}
