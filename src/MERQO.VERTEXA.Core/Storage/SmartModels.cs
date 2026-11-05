using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Core.Storage;

public enum StorageMediaType { Unknown = 0, Hdd = 1, SataSsd = 2, NvmeSsd = 3, Usb = 4, Virtual = 5 }

public enum SmartSeverity { Ok = 0, Advisory = 1, Warning = 2, Critical = 3 }

/// <summary>One ATA S.M.A.R.T. attribute. Raw bytes are preserved because the meaning of
/// the raw field is vendor-specific for several attribute ids.</summary>
public sealed record SmartAttribute(
    byte Id,
    string Name,
    int Value,
    int Worst,
    int Threshold,
    ulong RawValue,
    byte[] RawBytes,
    string Interpretation,
    SmartSeverity Severity);

/// <summary>Result of reading a drive's health telemetry.</summary>
public sealed class StorageDeviceReport
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Model { get; init; } = "Unknown drive";
    public string? Serial { get; init; }
    public string? Firmware { get; init; }
    public StorageMediaType MediaType { get; init; }
    public string BusType { get; init; } = "Unknown";
    public ulong CapacityBytes { get; init; }
    public string? InterfaceSummary { get; init; }

    public AvailabilityState SmartAvailability { get; init; } = AvailabilityState.NotTested;
    public string SmartSource { get; init; } = string.Empty;
    public string? SmartUnavailableReason { get; init; }
    public string HealthStatus { get; init; } = "Unknown";
    public IReadOnlyList<SmartAttribute> Attributes { get; init; } = Array.Empty<SmartAttribute>();
    public NvmeHealthLog? Nvme { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    public SmartSeverity WorstSeverity =>
        Attributes.Count == 0 ? SmartSeverity.Ok : Attributes.Max(a => a.Severity);

    public bool IsFailing => Nvme?.HasCriticalWarning == true ||
                             HealthStatus.Contains("Warning", StringComparison.OrdinalIgnoreCase) ||
                             HealthStatus.Contains("Unhealthy", StringComparison.OrdinalIgnoreCase) ||
                             Attributes.Any(a => a.Severity == SmartSeverity.Critical) ||
                             Nvme?.MediaErrors > 0;

    public double? WearPercent => Nvme?.PercentageUsed is { } used ? used : null;

    public string HealthHeadline
    {
        get
        {
            if (IsFailing) return "Health warning";
            if (SmartAvailability != AvailabilityState.Available && Nvme is null) return "Health telemetry unavailable";
            if (WorstSeverity == SmartSeverity.Warning) return "Attention advised";
            if (Nvme?.PercentageUsed >= 80) return "Wear high";
            return "Healthy";
        }
    }
}

/// <summary>NVMe SMART / Health Information log page (identifier 0x02), parsed per NVMe 1.4.</summary>
public sealed record NvmeHealthLog(
    byte CriticalWarning,
    double CompositeTemperatureCelsius,
    byte AvailableSparePercent,
    byte AvailableSpareThresholdPercent,
    byte PercentageUsed,
    ulong DataUnitsRead,
    ulong DataUnitsWritten,
    ulong HostReadCommands,
    ulong HostWriteCommands,
    ulong ControllerBusyMinutes,
    ulong PowerCycles,
    ulong PowerOnHours,
    ulong UnsafeShutdowns,
    ulong MediaErrors,
    ulong ErrorLogEntries,
    double WarningTemperatureMinutes,
    double CriticalTemperatureMinutes)
{
    /// <summary>NVMe data units are 1000 × 512-byte blocks.</summary>
    public const ulong DataUnitBytes = 1000UL * 512UL;

    public ulong BytesRead => DataUnitsRead * DataUnitBytes;
    public ulong BytesWritten => DataUnitsWritten * DataUnitBytes;

    public bool HasCriticalWarning => (CriticalWarning & 0x7F) != 0;

    public string CriticalWarningText
    {
        get
        {
            if (CriticalWarning == 0) return "None reported";
            var parts = new List<string>();
            if ((CriticalWarning & 0x01) != 0) parts.Add("available spare below threshold");
            if ((CriticalWarning & 0x02) != 0) parts.Add("temperature outside safe range");
            if ((CriticalWarning & 0x04) != 0) parts.Add("NVM subsystem reliability degraded");
            if ((CriticalWarning & 0x08) != 0) parts.Add("media placed in read-only mode");
            if ((CriticalWarning & 0x10) != 0) parts.Add("volatile memory backup failed");
            if ((CriticalWarning & 0x20) != 0) parts.Add("persistent memory region read-only");
            return parts.Count == 0 ? $"0x{CriticalWarning:X2}" : string.Join(", ", parts);
        }
    }
}
