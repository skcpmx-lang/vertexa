using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Core.Recommendations;
using Merqo.Vertexa.Security;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Reporting;

/// <summary>
/// Turns a diagnostic snapshot into a report document. Only real metrics are included;
/// unavailable metrics keep their state and reason so a report can never imply a value
/// that was never measured.
/// </summary>
public sealed class ReportBuilder
{
    private readonly RedactionService _redaction;

    public ReportBuilder(RedactionService redaction) => _redaction = redaction;

    public ReportDocument Build(DiagnosticSnapshot snapshot, ReportType type, string appVersion, string? singleTestName = null)
    {
        var sections = new List<ReportSection>();
        foreach (var section in SelectSections(snapshot, type))
        {
            var rows = section.Metrics
                .Select(metric => new ReportRow(
                    _redaction.Redact(metric.Name),
                    _redaction.Redact(metric.DisplayWithUnit),
                    _redaction.Redact(metric.Source),
                    metric.Availability.ShortCode(),
                    metric.Confidence.ToString()))
                .ToList();

            var notes = section.Metrics
                .Where(m => m.Limitation is not null)
                .Select(m => $"{m.Name}: {m.Limitation}")
                .Distinct()
                .ToList();

            sections.Add(new ReportSection(section.Title, section.Description, rows, notes.Count == 0 ? null : notes));
        }

        if (snapshot.StorageDevices.Count > 0 && (type is ReportType.Full or ReportType.Technician or ReportType.StorageHealth or ReportType.BuyerInspection))
            sections.Add(BuildStorageSection(snapshot));

        if (snapshot.Battery is not null && (type is ReportType.Full or ReportType.Technician or ReportType.BatteryHealth or ReportType.BuyerInspection))
            sections.Add(BuildBatterySection(snapshot));

        if (snapshot.Benchmarks.Count > 0 && (type is ReportType.Full or ReportType.Technician or ReportType.Performance))
            sections.Add(BuildBenchmarkSection(snapshot));

        var metadata = new List<(string, string)>
        {
            ("Report type", type.Title()),
            ("Run ID", snapshot.Id),
            ("Generated", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")),
            ("Application", $"MERQO VERTEXA {appVersion}"),
            ("Machine", snapshot.MachineName),
            ("Model", snapshot.MachineModel ?? "not reported"),
            ("Platform", snapshot.MachineKind switch
            {
                MachineKind.VirtualMachine => "Virtual machine",
                MachineKind.Physical => "Physical machine",
                _ => "Unknown",
            }),
            ("Scan scope", snapshot.Scope.ToString()),
            ("Scan duration", $"{snapshot.Duration.TotalSeconds:0.0} s"),
            ("Metrics recorded", $"{snapshot.AvailableMetricCount} available of {snapshot.TotalMetricCount}"),
            ("Elevated", snapshot.IsElevated ? "Yes" : "No"),
            ("Serials", _redaction.Enabled ? "Redacted (privacy default)" : "Included — user opted in"),
        };

        var findings = snapshot.Recommendations
            .Select(r => $"[{r.SeverityLabel}] {r.Title} — {r.ExpectedActionSummary()}")
            .ToList();
        findings.AddRange(snapshot.Warnings.Select(w => "[Scan warning] " + w));

        var limitations = new List<string>
        {
            "Values are read from the operating system, drivers and firmware; a metric the platform does not expose is reported as unavailable rather than estimated.",
            "Physical conditions (panel uniformity, key feel, port function, battery swelling) cannot be verified by software.",
            "Component temperatures and fan speeds are only present when the firmware exposes them.",
            "CPU package power and per-core power require privileged access and are not reported.",
            "This report is a measurement record, not a warranty or a guarantee of device condition.",
        };
        if (type == ReportType.BuyerInspection)
        {
            limitations.Add("A buyer inspection report never replaces a physical inspection of the screen, chassis, keyboard, ports and battery.");
            limitations.Add("Storage and battery figures describe the state at scan time; wear continues with use.");
        }

        return new ReportDocument(
            type.Title(),
            $"{snapshot.MachineName} · {snapshot.MachineModel ?? "unknown model"}",
            type,
            snapshot.Id,
            DateTimeOffset.Now,
            metadata,
            sections,
            findings,
            limitations,
            "MERQO VERTEXA · generated locally · no data uploaded");
    }

    private static IEnumerable<MetricSection> SelectSections(DiagnosticSnapshot snapshot, ReportType type)
    {
        var keys = type switch
        {
            ReportType.BuyerInspection => new[] { SectionKeys.System, SectionKeys.Os, SectionKeys.Cpu, SectionKeys.Gpu, SectionKeys.Memory, SectionKeys.Firmware, SectionKeys.Battery, SectionKeys.Storage, SectionKeys.Smart },
            ReportType.Technician => new[] { SectionKeys.System, SectionKeys.Os, SectionKeys.Firmware, SectionKeys.Cpu, SectionKeys.Gpu, SectionKeys.Memory, SectionKeys.Storage, SectionKeys.Smart, SectionKeys.Battery, SectionKeys.Thermal, SectionKeys.Security, SectionKeys.Drivers, SectionKeys.Network, SectionKeys.Peripherals, SectionKeys.Display },
            ReportType.BatteryHealth => new[] { SectionKeys.Battery, SectionKeys.System },
            ReportType.StorageHealth => new[] { SectionKeys.Storage, SectionKeys.Smart, SectionKeys.System },
            ReportType.Performance => new[] { SectionKeys.Cpu, SectionKeys.Gpu, SectionKeys.Memory, SectionKeys.Thermal },
            ReportType.DriverInspection => new[] { SectionKeys.Drivers, SectionKeys.Network, SectionKeys.Peripherals, SectionKeys.System },
            ReportType.SingleTest => new[] { SectionKeys.Cpu, SectionKeys.Thermal, SectionKeys.Gpu, SectionKeys.Memory, SectionKeys.Storage },
            _ => new[] { SectionKeys.System, SectionKeys.Os, SectionKeys.Firmware, SectionKeys.Cpu, SectionKeys.Gpu, SectionKeys.Memory, SectionKeys.Storage, SectionKeys.Smart, SectionKeys.Battery, SectionKeys.Thermal, SectionKeys.Security, SectionKeys.Drivers, SectionKeys.Network, SectionKeys.Peripherals, SectionKeys.Display },
        };

        foreach (var key in keys)
        {
            var section = snapshot.Find(key);
            if (section is not null && section.TotalCount > 0) yield return section;
        }
    }

    private ReportSection BuildStorageSection(DiagnosticSnapshot snapshot)
    {
        var rows = new List<ReportRow>();
        foreach (var drive in snapshot.StorageDevices)
        {
            rows.Add(new ReportRow($"{drive.Model} · health", drive.HealthHeadline, drive.SmartSource, drive.SmartAvailability.ShortCode(), "High"));
            rows.Add(new ReportRow($"{drive.Model} · capacity", Units.BytesToHuman(drive.CapacityBytes), "WMI:Win32_DiskDrive", "OK", "High"));
            if (drive.Serial is not null)
                rows.Add(new ReportRow($"{drive.Model} · serial", _redaction.MaskSerial(drive.Serial), "WMI", "OK", "High"));
            if (drive.Nvme is not null)
            {
                rows.Add(new ReportRow($"{drive.Model} · percentage used", drive.Nvme.PercentageUsed + " %", "NVMe log 0x02", "OK", "High"));
                rows.Add(new ReportRow($"{drive.Model} · data written", Units.BytesToHuman(drive.Nvme.BytesWritten), "NVMe log 0x02", "OK", "High"));
                rows.Add(new ReportRow($"{drive.Model} · media errors", drive.Nvme.MediaErrors.ToString("N0"), "NVMe log 0x02", "OK", "High"));
                rows.Add(new ReportRow($"{drive.Model} · power-on hours", drive.Nvme.PowerOnHours.ToString("N0"), "NVMe log 0x02", "OK", "High"));
                rows.Add(new ReportRow($"{drive.Model} · critical warning", drive.Nvme.CriticalWarningText, "NVMe log 0x02", "OK", "High"));
            }
            foreach (var attribute in drive.Attributes)
                rows.Add(new ReportRow($"{drive.Model} · {attribute.Name}", attribute.Interpretation, "ATA SMART", attribute.Severity.ToString(), "High"));
            if (drive.SmartUnavailableReason is not null)
                rows.Add(new ReportRow($"{drive.Model} · telemetry note", drive.SmartUnavailableReason, drive.SmartSource, "N/A", "Unknown"));
        }
        return new ReportSection("Drive health detail", "Reliability telemetry read directly from each drive where the platform allows it.", rows);
    }

    private ReportSection BuildBatterySection(DiagnosticSnapshot snapshot)
    {
        var battery = snapshot.Battery!;
        var rows = new List<ReportRow>();
        if (!battery.Present)
        {
            rows.Add(new ReportRow("Battery", "Not present", "Kernel32!GetSystemPowerStatus", "NAP", "High"));
            return new ReportSection("Battery", battery.HealthExplanation, rows);
        }
        rows.Add(new ReportRow("Design capacity", battery.DesignCapacityMWh + " mWh", "root\\wmi:BatteryStaticData", "OK", "High"));
        rows.Add(new ReportRow("Full charge capacity", battery.FullChargeCapacityMWh + " mWh", "root\\wmi:BatteryFullChargedCapacity", "OK", "High"));
        rows.Add(new ReportRow("Health", battery.HealthPercent is null ? "not computable" : $"{battery.HealthPercent:0.0} %", "VERTEXA battery engine", battery.HealthPercent is null ? "N/A" : "OK", battery.HealthPercent is null ? "Unknown" : "Medium"));
        rows.Add(new ReportRow("Wear", battery.WearPercent is null ? "not computable" : $"{battery.WearPercent:0.0} %", "VERTEXA battery engine", battery.WearPercent is null ? "N/A" : "OK", battery.WearPercent is null ? "Unknown" : "Medium"));
        rows.Add(new ReportRow("Cycle count", battery.CycleCount?.ToString("N0") ?? "not exposed by firmware", "root\\wmi:BatteryCycleCount", battery.CycleCount is null ? "UNSUP" : "OK", "Medium"));
        rows.Add(new ReportRow("Charge level", battery.ChargePercent is null ? "unknown" : $"{battery.ChargePercent:0} %", "Power API", "OK", "High"));
        rows.Add(new ReportRow("Power source", battery.PowerLine == Core.Battery.PowerLineState.PluggedIn ? "AC adapter" : "Battery", "Power API", "OK", "High"));
        rows.Add(new ReportRow("Estimated runtime", battery.RuntimeLabel, "VERTEXA battery engine", battery.EstimatedRuntimeHours is null ? "N/A" : "OK", "Low"));
        return new ReportSection("Battery", battery.HealthExplanation, rows, battery.Evidence.Count == 0 ? null : battery.Evidence);
    }

    private static ReportSection BuildBenchmarkSection(DiagnosticSnapshot snapshot)
    {
        var rows = snapshot.Benchmarks
            .Select(b => new ReportRow(b.Title, b.Summary, "VERTEXA benchmark harness", b.Completed ? "OK" : "N/A", "Medium"))
            .ToList();
        var notes = snapshot.Benchmarks
            .SelectMany(b => b.Cautions)
            .Distinct()
            .ToList();
        return new ReportSection("Benchmarks", "Measured workloads with a documented definition; no synthetic composite scores.", rows, notes);
    }
}

internal static class RecommendationTextExtensions
{
    internal static string ExpectedActionSummary(this Recommendation recommendation) =>
        recommendation.SuggestedAction.Length > 160
            ? recommendation.SuggestedAction[..157] + "…"
            : recommendation.SuggestedAction;
}
