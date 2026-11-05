using System.Collections.ObjectModel;
using Merqo.Vertexa.App.Services;
using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Core.Storage;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.App.ViewModels;

public sealed class DriveRow
{
    public DriveRow(StorageDeviceReport drive)
    {
        Model = drive.Model;
        Media = drive.MediaType switch
        {
            StorageMediaType.NvmeSsd => "NVMe SSD",
            StorageMediaType.SataSsd => "SATA SSD",
            StorageMediaType.Hdd => "Hard disk",
            StorageMediaType.Usb => "USB storage",
            StorageMediaType.Virtual => "Virtual disk",
            _ => "Unknown media",
        };
        Capacity = Units.BytesToHuman(drive.CapacityBytes);
        Health = drive.HealthHeadline;
        Failing = drive.IsFailing;
        Source = drive.SmartSource;
        Reason = drive.SmartUnavailableReason ?? string.Empty;
        Attributes = drive.Attributes.Select(a => new MetricRowVm(
            DiagnosticValue<string>.FromAvailable(a.Name,
                $"{a.Interpretation} (normalised {a.Value}, worst {a.Worst})",
                "ATA S.M.A.R.T.", "SMART READ DATA",
                confidence: a.Severity == SmartSeverity.Ok ? ConfidenceLevel.High : ConfidenceLevel.Medium))).ToList();
        NvmeRows = drive.Nvme is null ? new List<MetricRowVm>() : new List<MetricRowVm>
        {
            Row("Temperature", $"{drive.Nvme.CompositeTemperatureCelsius:0.0} °C"),
            Row("Percentage used", drive.Nvme.PercentageUsed + " %"),
            Row("Available spare", drive.Nvme.AvailableSparePercent + " % (threshold " + drive.Nvme.AvailableSpareThresholdPercent + " %)"),
            Row("Data written", Units.BytesToHuman(drive.Nvme.BytesWritten)),
            Row("Data read", Units.BytesToHuman(drive.Nvme.BytesRead)),
            Row("Power-on hours", drive.Nvme.PowerOnHours.ToString("N0")),
            Row("Power cycles", drive.Nvme.PowerCycles.ToString("N0")),
            Row("Unsafe shutdowns", drive.Nvme.UnsafeShutdowns.ToString("N0")),
            Row("Media errors", drive.Nvme.MediaErrors.ToString("N0")),
            Row("Critical warning", drive.Nvme.CriticalWarningText),
        };
        HasSmartDetail = NvmeRows.Count > 0 || Attributes.Count > 0;
    }

    private static MetricRowVm Row(string name, string value) =>
        new(DiagnosticValue<string>.FromAvailable(name, value, "NVMe health log 0x02", "IOCTL_STORAGE_QUERY_PROPERTY"));

    public string Model { get; }
    public string Media { get; }
    public string Capacity { get; }
    public string Health { get; }
    public bool Failing { get; }
    public string Source { get; }
    public string Reason { get; }
    public IReadOnlyList<MetricRowVm> Attributes { get; }
    public IReadOnlyList<MetricRowVm> NvmeRows { get; }
    public bool HasSmartDetail { get; }
    public bool HasReason => !string.IsNullOrWhiteSpace(Reason);
}

public sealed class StorageViewModel : PageViewModelBase
{
    private readonly AppHost _host;
    private string _summary = string.Empty;

    public StorageViewModel(AppHost host)
    {
        _host = host;
        Drives = new ObservableCollection<DriveRow>();
        VolumeRows = new ObservableCollection<MetricRowVm>();
    }

    public override string Key => "storage";
    public override string Title => "Storage";
    public override string Subtitle => "Reliability telemetry, wear and file-system usage.";

    public ObservableCollection<DriveRow> Drives { get; }
    public ObservableCollection<MetricRowVm> VolumeRows { get; }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public override Task RefreshAsync()
    {
        Drives.Clear();
        VolumeRows.Clear();
        var snapshot = Snapshot;
        if (snapshot is null)
        {
            Summary = "No scan data yet.";
            return Task.CompletedTask;
        }

        foreach (var drive in snapshot.StorageDevices) Drives.Add(new DriveRow(drive));
        var storageSection = snapshot.Find(SectionKeys.Storage);
        if (storageSection is not null)
            foreach (var metric in storageSection.Metrics.Where(m => m.Name.Contains("· free space") || m.Name.Contains("· used") || m.Name.Contains("file system")))
                VolumeRows.Add(new MetricRowVm(metric));

        var failing = snapshot.StorageDevices.Count(d => d.IsFailing);
        Summary = failing > 0
            ? $"{failing} drive(s) report a health problem — back up important data before any further testing."
            : $"{snapshot.StorageDevices.Count} drive(s) inspected · {snapshot.StorageDevices.Count(d => d.SmartAvailability == AvailabilityState.Available)} with direct health telemetry";
        return Task.CompletedTask;
    }
}
