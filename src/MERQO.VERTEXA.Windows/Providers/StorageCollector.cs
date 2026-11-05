using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Core.Storage;
using Merqo.Vertexa.Shared;
using Merqo.Vertexa.Windows.Storage;

namespace Merqo.Vertexa.Windows.Providers;

/// <summary>
/// Drive inventory plus health telemetry. Health is read with the highest-fidelity
/// method the device allows: NVMe health log, then ATA S.M.A.R.T., then Windows'
/// own health status — and the method used is always reported.
/// </summary>
internal sealed class StorageCollector : WindowsCollectorBase
{
    public StorageCollector(IAppLogger? log = null) : base(log) { }
    public override string SectionKey => SectionKeys.Storage;
    public override string Title => "Storage devices";
    public override int Order => 40;

    protected override void CollectCore(DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
    {
        var section = Section(snapshot);
        var smart = snapshot.Section(SectionKeys.Smart);

        var disks = Wmi("SELECT Index, Model, Manufacturer, FirmwareRevision, SerialNumber, InterfaceType, MediaType, Size, DeviceID, PNPDeviceID, Status, Partitions, BytesPerSector FROM Win32_DiskDrive");
        if (!disks.Ok || disks.Rows.Count == 0)
        {
            MissingFromQuery<int>(section, "Drives detected", disks, "WMI:Win32_DiskDrive", "CIM query");
            return;
        }

        // Bus/media detail comes from the modern storage namespace when available.
        var physical = Wmi("SELECT DeviceId, FriendlyName, MediaType, BusType, HealthStatus, OperationalStatus, Size, SerialNumber FROM MSFT_PhysicalDisk",
            @"root\Microsoft\Windows\Storage");
        var busById = new Dictionary<string, (string Bus, string Media, string Health)>(StringComparer.OrdinalIgnoreCase);
        if (physical.Ok)
        {
            foreach (var row in physical.Rows)
            {
                var id = row.Text("DeviceId");
                if (id is null) continue;
                busById[id] = (BusTypeName(row.UInt("BusType")), MediaTypeName(row.UInt("MediaType")),
                    string.Join(",", row.Values.TryGetValue("HealthStatus", out var h) && h is ushort[] arr ? arr.Select(a => a.ToString()) : new[] { row.Text("HealthStatus") ?? "Unknown" }));
            }
        }

        section.Metric("Drives detected", disks.Rows.Count, "WMI:Win32_DiskDrive", "CIM query");

        var reports = new List<StorageDeviceReport>();
        foreach (var disk in disks.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var deviceId = disk.Text("DeviceID") ?? string.Empty;
            var index = disk.UInt("Index") ?? ParseDriveIndex(deviceId);
            var model = disk.Text("Model")?.Trim() ?? "Unknown drive";
            var sizeBytes = disk.ULong("Size") ?? 0;
            var bus = disk.Text("InterfaceType") ?? "Unknown";
            var mediaText = disk.Text("MediaType") ?? "Unknown";
            var status = disk.Text("Status") ?? "Unknown";

            busById.TryGetValue(index.ToString(), out var detail);
            var media = ClassifyMedia(detail.Bus, mediaText, model);
            var health = string.IsNullOrWhiteSpace(detail.Health) ? status : detail.Health!;

            section.Metric($"{model} · capacity", Units.BytesToHuman(sizeBytes), "WMI:Win32_DiskDrive", "CIM query");
            section.Metric($"{model} · interface", string.IsNullOrEmpty(detail.Bus) ? bus : $"{detail.Bus} ({bus})", "WMI:MSFT_PhysicalDisk + Win32_DiskDrive", "CIM query",
                confidence: string.IsNullOrEmpty(detail.Bus) ? ConfidenceLevel.Medium : ConfidenceLevel.High);
            section.Metric($"{model} · media", MediaLabel(media), "WMI:MSFT_PhysicalDisk + Win32_DiskDrive", "CIM query", confidence: ConfidenceLevel.Medium);
            if (disk.Text("FirmwareRevision") is { } firmware) section.Metric($"{model} · firmware", firmware.Trim(), "WMI:Win32_DiskDrive", "CIM query");
            if (disk.UInt("Partitions") is { } partitions) section.Metric($"{model} · partitions", (int)partitions, "WMI:Win32_DiskDrive", "CIM query");
            section.Metric($"{model} · Windows health status", health, "WMI:MSFT_PhysicalDisk", "CIM query", confidence: ConfidenceLevel.Medium,
                limitation: "This is Windows' own health assessment; detailed telemetry is read separately below.");

            var report = ReadHealth(index, model, disk.Text("SerialNumber"), disk.Text("FirmwareRevision"), sizeBytes, media, bus, health, smart, cancellationToken);
            reports.Add(report);
        }

        snapshot.StorageDevices = reports;

        var volumes = Wmi("SELECT DeviceID, VolumeName, FileSystem, Size, FreeSpace, DriveType FROM Win32_LogicalDisk WHERE DriveType=3");
        if (volumes.Ok)
        {
            section.Metric("Volumes", volumes.Rows.Count, "WMI:Win32_LogicalDisk", "CIM query");
            foreach (var volume in volumes.Rows)
            {
                var id = volume.Text("DeviceID") ?? "?";
                var size = volume.ULong("Size") ?? 0;
                var free = volume.ULong("FreeSpace") ?? 0;
                var usedPercent = size == 0 ? (double?)null : Math.Round(100.0 * (size - free) / size, 1);
                var label = volume.Text("VolumeName");
                var name = string.IsNullOrWhiteSpace(label) ? id : $"{id} {label}";
                section.Metric($"{name} · file system", volume.Text("FileSystem") ?? "unknown", "WMI:Win32_LogicalDisk", "CIM query");
                section.Metric($"{name} · free space", $"{Units.BytesToHuman(free)} of {Units.BytesToHuman(size)}", "WMI:Win32_LogicalDisk", "CIM query");
                if (usedPercent is { } used)
                    section.Metric($"{name} · used", used, "WMI:Win32_LogicalDisk", "CIM query", unit: Units.Percent, confidence: ConfidenceLevel.Medium);
            }
        }
        else
        {
            MissingFromQuery<int>(section, "Volumes", volumes, "WMI:Win32_LogicalDisk", "CIM query");
        }
    }

    private StorageDeviceReport ReadHealth(int index, string model, string? serial, string? firmware, ulong size,
        StorageMediaType media, string bus, string healthStatus, MetricSection smart, CancellationToken cancellationToken)
    {
        var notes = new List<string>();

        if (media == StorageMediaType.NvmeSsd)
        {
            var status = NvmeHealthReader.TryRead(index, out var nvme, out var error);
            if (status == NvmeHealthReader.ReadStatus.Success && nvme is not null)
            {
                AddNvmeMetrics(smart, model, nvme);
                return new StorageDeviceReport
                {
                    Model = model, Serial = serial, Firmware = firmware, MediaType = media, BusType = bus,
                    CapacityBytes = size, SmartAvailability = AvailabilityState.Available,
                    SmartSource = "IOCTL_STORAGE_QUERY_PROPERTY (NVMe log 0x02)",
                    HealthStatus = healthStatus, Nvme = nvme, Notes = notes,
                };
            }

            notes.Add("NVMe health log unavailable: " + (error ?? "unknown reason"));
            smart.Missing<string>($"{model} · NVMe health log", status == NvmeHealthReader.ReadStatus.AccessDenied ? AvailabilityState.AccessDenied : AvailabilityState.Unavailable,
                error ?? "controller did not return the log page", "IOCTL_STORAGE_QUERY_PROPERTY", "NVMe log page 0x02");
        }

        // ATA path (HDD / SATA SSD).
        var ataStatus = AtaSmartReader.TryReadSmartData(index, out var smartData, out var ataError);
        if (ataStatus == AtaSmartReader.ReadStatus.Success && smartData is not null)
        {
            var attributes = SmartInterpreter.ParseAttributes(smartData);
            foreach (var attribute in attributes.Where(a => a.Severity != SmartSeverity.Ok))
            {
                smart.Metric($"{model} · {attribute.Name}", attribute.Interpretation,
                    "ata-smart", "SMART READ DATA (0xD0)",
                    confidence: ConfidenceLevel.High);
            }
            smart.Metric($"{model} · S.M.A.R.T. attributes read", attributes.Count, "ata-smart", "SMART READ DATA (0xD0)", confidence: ConfidenceLevel.High);
            var predict = AtaSmartReader.TryReadPredictFailure(index, out var failing, out _);
            if (predict == AtaSmartReader.ReadStatus.Success && failing)
                healthStatus = "Warning (predict failure)";
            return new StorageDeviceReport
            {
                Model = model, Serial = serial, Firmware = firmware, MediaType = media, BusType = bus,
                CapacityBytes = size, SmartAvailability = AvailabilityState.Available,
                SmartSource = "SMART READ DATA (ATA pass-through)", HealthStatus = healthStatus,
                Attributes = attributes, Notes = notes,
            };
        }

        notes.Add("ATA S.M.A.R.T. unavailable: " + (ataError ?? "unknown reason"));
        var state = ataStatus switch
        {
            AtaSmartReader.ReadStatus.AccessDenied => AvailabilityState.AccessDenied,
            AtaSmartReader.ReadStatus.NotSupported => AvailabilityState.Unsupported,
            _ => AvailabilityState.Unavailable,
        };
        smart.Missing<string>($"{model} · S.M.A.R.T.", state, ataError ?? "no S.M.A.R.T. data returned",
            "ata-smart", "SMART READ DATA (0xD0)");
        smart.Metric($"{model} · health status (Windows)", healthStatus, "WMI:MSFT_PhysicalDisk", "CIM query",
            confidence: ConfidenceLevel.Medium);

        return new StorageDeviceReport
        {
            Model = model, Serial = serial, Firmware = firmware, MediaType = media, BusType = bus,
            CapacityBytes = size, SmartAvailability = state,
            SmartSource = "none", SmartUnavailableReason = ataError, HealthStatus = healthStatus, Notes = notes,
        };
    }

    private static void AddNvmeMetrics(MetricSection smart, string model, NvmeHealthLog nvme)
    {
        const string Source = "IOCTL_STORAGE_QUERY_PROPERTY (NVMe log 0x02)";
        const string Method = "NVMe SMART/Health Information log page";
        smart.Metric($"{model} · temperature", nvme.CompositeTemperatureCelsius, Source, Method, unit: Units.Celsius, confidence: ConfidenceLevel.High);
        smart.Metric($"{model} · percentage used", nvme.PercentageUsed, Source, Method, unit: Units.Percent, confidence: ConfidenceLevel.High);
        smart.Metric($"{model} · available spare", nvme.AvailableSparePercent, Source, Method, unit: Units.Percent, confidence: ConfidenceLevel.High);
        smart.Metric($"{model} · spare threshold", nvme.AvailableSpareThresholdPercent, Source, Method, unit: Units.Percent);
        smart.Metric($"{model} · data written", Units.BytesToHuman(nvme.BytesWritten), Source, Method, confidence: ConfidenceLevel.High);
        smart.Metric($"{model} · data read", Units.BytesToHuman(nvme.BytesRead), Source, Method, confidence: ConfidenceLevel.High);
        smart.Metric($"{model} · power-on hours", (int)NvmeHealthParser.EstimatePowerOnHours(nvme.PowerOnHours), Source, Method);
        smart.Metric($"{model} · power cycles", (int)Math.Min(nvme.PowerCycles, int.MaxValue), Source, Method);
        smart.Metric($"{model} · unsafe shutdowns", (int)Math.Min(nvme.UnsafeShutdowns, int.MaxValue), Source, Method);
        smart.Metric($"{model} · media errors", (int)Math.Min(nvme.MediaErrors, int.MaxValue), Source, Method,
            confidence: ConfidenceLevel.High);
        smart.Metric($"{model} · critical warning", nvme.CriticalWarningText, Source, Method, confidence: ConfidenceLevel.High);
        smart.Metric($"{model} · controller busy", (int)Math.Min(nvme.ControllerBusyMinutes, int.MaxValue), Source, Method, unit: "min");
    }

    internal static StorageMediaType ClassifyMedia(string busType, string mediaType, string model)
    {
        if (busType.Contains("NVMe", StringComparison.OrdinalIgnoreCase)) return StorageMediaType.NvmeSsd;
        if (mediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) || mediaType.Contains("Solid", StringComparison.OrdinalIgnoreCase))
            return StorageMediaType.SataSsd;
        if (mediaType.Contains("Fixed hard disk", StringComparison.OrdinalIgnoreCase)) return StorageMediaType.Hdd;
        if (busType.Contains("USB", StringComparison.OrdinalIgnoreCase)) return StorageMediaType.Usb;
        if (model.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) return StorageMediaType.Virtual;
        return StorageMediaType.Unknown;
    }

    internal static string MediaLabel(StorageMediaType media) => media switch
    {
        StorageMediaType.Hdd => "Hard disk drive",
        StorageMediaType.SataSsd => "SATA SSD",
        StorageMediaType.NvmeSsd => "NVMe SSD",
        StorageMediaType.Usb => "USB storage",
        StorageMediaType.Virtual => "Virtual disk",
        _ => "Unknown",
    };

    internal static string BusTypeName(uint? bus) => bus switch
    {
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        4 => "IEEE1394",
        5 => "SSA",
        6 => "Fibre Channel",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        14 => "Virtual",
        15 => "File-backed virtual",
        16 => "Storage Spaces",
        17 => "NVMe",
        18 => "SCM",
        19 => "UFS",
        _ => "Unknown",
    };

    internal static string MediaTypeName(uint? media) => media switch
    {
        0 => "Unspecified",
        3 => "HDD",
        4 => "SSD",
        5 => "SCM",
        _ => "Unknown",
    };

    private static int ParseDriveIndex(string deviceId)
    {
        var digits = new string(deviceId.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }
}
