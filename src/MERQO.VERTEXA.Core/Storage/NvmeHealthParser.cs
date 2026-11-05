namespace Merqo.Vertexa.Core.Storage;

/// <summary>
/// Pure parser for the NVMe SMART/Health log page. Kept byte-exact and unit-tested with
/// a captured fixture so that a wrong offset can never reach the UI unnoticed.
/// </summary>
public static class NvmeHealthParser
{
    public const int LogPageSize = 512;
    public const byte LogPageIdentifier = 0x02;

    public static bool TryParse(ReadOnlySpan<byte> log, out NvmeHealthLog health, out string? error)
    {
        health = null!;
        error = null;
        if (log.Length < 192)
        {
            error = $"NVMe health log is {log.Length} bytes, expected at least 192.";
            return false;
        }

        var criticalWarning = log[0];
        // Composite temperature is reported in kelvin (bytes 1-2, little endian).
        var kelvin = (ushort)(log[1] | (log[2] << 8));
        var celsius = kelvin == 0 ? 0d : kelvin - 273.15;

        health = new NvmeHealthLog(
            CriticalWarning: criticalWarning,
            CompositeTemperatureCelsius: Math.Round(celsius, 1),
            AvailableSparePercent: log[3],
            AvailableSpareThresholdPercent: log[4],
            PercentageUsed: log[5],
            DataUnitsRead: ReadUInt128Low64(log.Slice(32, 16)),
            DataUnitsWritten: ReadUInt128Low64(log.Slice(48, 16)),
            HostReadCommands: ReadUInt128Low64(log.Slice(64, 16)),
            HostWriteCommands: ReadUInt128Low64(log.Slice(80, 16)),
            ControllerBusyMinutes: ReadUInt128Low64(log.Slice(96, 16)),
            PowerCycles: ReadUInt128Low64(log.Slice(112, 16)),
            PowerOnHours: ReadUInt128Low64(log.Slice(128, 16)),
            UnsafeShutdowns: ReadUInt128Low64(log.Slice(144, 16)),
            MediaErrors: ReadUInt128Low64(log.Slice(160, 16)),
            ErrorLogEntries: ReadUInt128Low64(log.Slice(176, 16)),
            WarningTemperatureMinutes: ReadUInt128Low64(log.Slice(192, 16)),
            CriticalTemperatureMinutes: ReadUInt128Low64(log.Slice(208, 16)));
        return true;
    }

    /// <summary>128-bit little-endian counters are truncated to 64 bits — they would need ~10^23 bytes to overflow.</summary>
    private static ulong ReadUInt128Low64(ReadOnlySpan<byte> span)
    {
        if (span.Length < 8) return 0;
        ulong value = 0;
        for (var i = 7; i >= 0; i--) value = (value << 8) | span[i];
        return value;
    }

    public static int EstimatePowerOnHours(ulong rawHours) => rawHours > int.MaxValue ? int.MaxValue : (int)rawHours;
}
