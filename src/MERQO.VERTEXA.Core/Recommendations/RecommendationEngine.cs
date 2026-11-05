using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Core.Storage;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Core.Recommendations;

/// <summary>
/// Deterministic, rule-based troubleshooting. Every rule reads a measured metric and
/// emits the evidence that triggered it. Rules never execute a remediation.
/// </summary>
public sealed class RecommendationEngine
{
    public IReadOnlyList<Recommendation> Evaluate(DiagnosticSnapshot snapshot)
    {
        var results = new List<Recommendation>();
        EvaluateStorage(snapshot, results);
        EvaluateBattery(snapshot, results);
        EvaluateThermal(snapshot, results);
        EvaluateDrivers(snapshot, results);
        EvaluateMemory(snapshot, results);
        EvaluateSecurity(snapshot, results);
        EvaluateEnvironment(snapshot, results);
        return results
            .OrderByDescending(r => r.Severity)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static void EvaluateStorage(DiagnosticSnapshot snapshot, List<Recommendation> results)
    {
        foreach (var drive in snapshot.StorageDevices)
        {
            if (drive.IsFailing)
            {
                results.Add(new Recommendation(
                    $"STO-1-{drive.Id}",
                    $"Back up your data: {drive.Model} reports a health problem",
                    Severity.Critical,
                    $"Drive health status is '{drive.HealthStatus}'" + (drive.Nvme is null ? string.Empty : $" with critical warning '{drive.Nvme.CriticalWarningText}'"),
                    BuildDriveEvidence(drive),
                    "Worn flash memory, failing platters or a degrading controller can all produce this status.",
                    "Copy important files to another drive now, then verify the drive with the vendor's official tool before reusing it.",
                    "High — status comes directly from the drive",
                    "Do not run write-intensive tests on a drive in this state.",
                    "F-097"));
            }
            else
            {
                var reallocated = drive.Attributes.FirstOrDefault(a => a.Id is 0x05 or 0xC5 or 0xC6);
                if (reallocated is not null && reallocated.RawValue > 0)
                {
                    results.Add(new Recommendation(
                        $"STO-2-{drive.Id}",
                        $"{drive.Model}: {reallocated.Name} is above zero",
                        Severity.Warning,
                        $"{reallocated.Name} raw value = {reallocated.RawValue:N0}",
                        new[] { $"{reallocated.Name}: {reallocated.Interpretation}" },
                        "Bad sectors were remapped. This can be age-related wear, a shock event, or an early sign of failure.",
                        "Keep a current backup and monitor the count. If it rises over the next weeks, plan a replacement.",
                        "Medium — a single non-zero count is not proof of imminent failure",
                        null,
                        "F-094"));
                }

                if (drive.Nvme is { PercentageUsed: >= 80 })
                {
                    results.Add(new Recommendation(
                        $"STO-3-{drive.Id}",
                        $"{drive.Model} has used {drive.Nvme.PercentageUsed}% of its rated endurance",
                        Severity.Warning,
                        $"NVMe Percentage Used = {drive.Nvme.PercentageUsed}%, available spare {drive.Nvme.AvailableSparePercent}%",
                        new[]
                        {
                            $"Data written: {Units.BytesToHuman(drive.Nvme.BytesWritten)}",
                            $"Power-on hours: {drive.Nvme.PowerOnHours:N0}",
                            $"Unsafe shutdowns: {drive.Nvme.UnsafeShutdowns:N0}",
                        },
                        "Sustained write load and age reduce the endurance budget of NAND flash.",
                        "Keep backups current and treat remaining endurance as the planning horizon for replacement.",
                        "High — the figure is reported by the controller",
                        null,
                        "F-092"));
                }
            }

            if (drive.SmartAvailability is AvailabilityState.Unavailable or AvailabilityState.Unsupported or AvailabilityState.AccessDenied)
            {
                results.Add(new Recommendation(
                    $"STO-4-{drive.Id}",
                    $"{drive.Model}: detailed drive health telemetry is unavailable",
                    Severity.Advisory,
                    drive.SmartUnavailableReason ?? "No S.M.A.R.T./NVMe data source answered.",
                    new[] { $"Health status reported by Windows: {drive.HealthStatus}" },
                    "USB bridges, RAID/VMD controllers and some NVMe drivers block pass-through health queries.",
                    "Use the drive vendor's official tool for a full health check, especially before buying or selling the machine.",
                    "Medium — absence of telemetry is not evidence of a healthy drive",
                    null,
                    "F-093"));
            }
        }
    }

    private static IReadOnlyList<string> BuildDriveEvidence(StorageDeviceReport drive)
    {
        var evidence = new List<string> { $"Windows health status: {drive.HealthStatus}" };
        if (drive.Nvme is not null)
        {
            evidence.Add($"NVMe critical warning: {drive.Nvme.CriticalWarningText}");
            evidence.Add($"Media errors: {drive.Nvme.MediaErrors:N0}");
            evidence.Add($"Available spare: {drive.Nvme.AvailableSparePercent}%");
            evidence.Add($"Percentage used: {drive.Nvme.PercentageUsed}%");
        }
        foreach (var attribute in drive.Attributes.Where(a => a.Severity == SmartSeverity.Critical).Take(4))
            evidence.Add($"{attribute.Name}: {attribute.Interpretation}");
        return evidence;
    }

    private static void EvaluateBattery(DiagnosticSnapshot snapshot, List<Recommendation> results)
    {
        var battery = snapshot.Battery;
        if (battery is null) return;

        if (!battery.Present)
        {
            results.Add(new Recommendation("BAT-0", "No battery detected", Severity.Info,
                "The power API reports no system battery.",
                new[] { battery.HealthExplanation },
                "This is expected on desktops and on some mini-PCs.",
                "No action needed unless this is a laptop — then check that the battery is connected and recognised in Device Manager.",
                "High", null, "F-080"));
            return;
        }

        if (battery.HealthPercent is { } health && health < 65)
        {
            results.Add(new Recommendation("BAT-1", $"Battery capacity has dropped to {health:0.0}% of design capacity",
                health < 45 ? Severity.Critical : Severity.Warning,
                $"full-charge capacity {battery.FullChargeCapacityMWh} mWh vs design {battery.DesignCapacityMWh} mWh",
                battery.Evidence.ToList(),
                "Normal lithium-ion ageing, heat exposure, or a high number of charge cycles.",
                "Plan a battery replacement if runtime matters. Check whether the vendor offers an official replacement part.",
                "High — computed from two firmware-reported capacities",
                "A worn battery is a fire risk only if it is also swelling or overheating; that requires a physical inspection.",
                "F-081"));
        }
        else if (battery.HealthPercent is null)
        {
            results.Add(new Recommendation("BAT-2", "Battery wear could not be calculated",
                Severity.Advisory,
                "Design and/or full-charge capacity was not reported.",
                new[] { battery.HealthExplanation },
                "Some firmware hides capacity data; a fully discharged battery can also report zero full-charge capacity.",
                "Charge the battery fully, reconnect it and re-run the scan. If the data stays missing, use the vendor's official battery report tool.",
                "High — the values are simply absent",
                null, "F-080"));
        }

        if (battery.PowerLine == PowerLineState.PluggedIn && !battery.IsCharging && battery.ChargePercent is < 95 and not null)
        {
            results.Add(new Recommendation("BAT-3", "Plugged in but not charging",
                Severity.Advisory,
                $"AC connected, charge {battery.ChargePercent:0}%, charging state false",
                new[] { "Charge rate: " + (battery.RateMilliwatts is null ? "not reported" : battery.RateMilliwatts.Value.ToString("N0") + " mW") },
                "Battery care/charge-limit firmware features, a worn battery, or a charger that cannot supply enough power.",
                "Check the vendor's battery-care setting first. If the level stays stuck well below 100%, test with the original charger.",
                "Medium — charge-threshold firmware can explain this legitimately",
                null, "F-083"));
        }
    }

    private static void EvaluateThermal(DiagnosticSnapshot snapshot, List<Recommendation> results)
    {
        var thermal = snapshot.Find(SectionKeys.Thermal);
        if (thermal is null) return;

        var hottest = thermal.Metrics.OfType<DiagnosticValue<double>>()
            .Where(m => m.HasData && m.Unit == Units.Celsius)
            .OrderByDescending(m => m.Value)
            .FirstOrDefault();

        if (hottest is null)
        {
            results.Add(new Recommendation("THM-0", "No temperature sensor is exposed",
                Severity.Info,
                "No ACPI thermal zone or thermal performance counter answered.",
                thermal.Metrics.Select(m => $"{m.Name}: {m.DisplayValue} ({m.UnavailableReason})").Take(3).ToList(),
                "Modern laptops frequently hide sensor data behind the embedded controller; many desktops expose only a board sensor.",
                "Use the vendor's monitoring tool if you need temperatures. VERTEXA will never estimate a temperature.",
                "High — absence is itself the measurement",
                null, "F-034"));
            return;
        }

        if (hottest.Value is >= 90)
        {
            results.Add(new Recommendation("THM-1", $"{hottest.Name} reached {hottest.DisplayValue} {hottest.Unit}",
                hottest.Value >= 95 ? Severity.Critical : Severity.Warning,
                $"sensor {hottest.Source} reported {hottest.DisplayValue} {hottest.Unit}",
                new[] { $"{hottest.Name}: {hottest.DisplayValue} {hottest.Unit}", $"Source: {hottest.Source} · {hottest.DetectionMethod}" },
                "Dust-blocked heatsinks, dried thermal paste, a failing fan, or a workload that exceeds the cooling design.",
                "Clean the vents and fan, ensure the machine is on a hard surface with airflow, and check whether the fan is spinning. If it persists at idle, have the cooling assembly serviced.",
                "Medium — a single reading is a snapshot; sustained load matters",
                "Thermal damage is cumulative; stop heavy workloads until temperatures are addressed.",
                "F-034"));
        }
    }

    private static void EvaluateDrivers(DiagnosticSnapshot snapshot, List<Recommendation> results)
    {
        var drivers = snapshot.Find(SectionKeys.Drivers);
        if (drivers is null) return;

        var problemMetric = drivers.Metrics.OfType<DiagnosticValue<int>>()
            .FirstOrDefault(m => m.Name.EndsWith("problem devices", StringComparison.OrdinalIgnoreCase));
        if (problemMetric?.Value is > 0)
        {
            var detail = drivers.Find<string>("Devices with problems")?.Value;
            results.Add(new Recommendation("DRV-1", $"{problemMetric.Value} device(s) report a driver problem",
                Severity.Warning,
                $"SetupAPI problem codes present on {problemMetric.Value} device(s)",
                new[] { detail ?? "See Drivers page for per-device problem codes" },
                "Missing driver after a Windows update, a device that failed to start, or a disabled device.",
                "Open the Drivers page, identify the affected device, then obtain the driver from the PC or device manufacturer's official support site or Windows Update.",
                "High — problem codes are authoritative Windows data",
                "Do not install drivers from unofficial download sites.",
                "F-121"));
        }

        var unknown = drivers.Metrics.OfType<DiagnosticValue<int>>()
            .FirstOrDefault(m => m.Name.Contains("Unknown", StringComparison.OrdinalIgnoreCase));
        if (unknown?.Value is > 0)
        {
            results.Add(new Recommendation("DRV-2", $"{unknown.Value} device(s) have no driver installed",
                Severity.Advisory,
                "Devices enumerated without a matching driver",
                new[] { $"Unknown device count: {unknown.Value}" },
                "Windows could not find a driver, or the device is a vendor-specific function (e.g. a hotkey or sensor hub).",
                "Check Windows Update optional driver updates, then the manufacturer's support page for this exact model.",
                "High", null, "F-120"));
        }
    }

    private static void EvaluateMemory(DiagnosticSnapshot snapshot, List<Recommendation> results)
    {
        var memory = snapshot.Find(SectionKeys.Memory);
        var load = memory?.Find<double>("Memory load");
        if (load?.Value is > 90)
        {
            results.Add(new Recommendation("MEM-1", $"Memory is {load.Value:0}% in use",
                Severity.Advisory,
                "Committed memory is close to the installed physical memory",
                new[]
                {
                    $"Installed: {memory!.Find<string>("Installed memory")?.DisplayWithUnit ?? "unknown"}",
                    $"Available: {memory.Find<string>("Available memory")?.DisplayWithUnit ?? "unknown"}",
                },
                "Sustained pressure can come from browser tabs, virtual machines, or a memory leak in an application.",
                "Close unused applications. If the pressure persists at idle, check Task Manager for the consuming process; consider more RAM if the workload is genuinely heavy.",
                "Medium — a single sample; the monitoring page shows the trend",
                null, "F-051"));
        }
    }

    private static void EvaluateSecurity(DiagnosticSnapshot snapshot, List<Recommendation> results)
    {
        var security = snapshot.Find(SectionKeys.Security);
        var secureBoot = security?.Find<string>("Secure Boot state");
        if (secureBoot?.HasData == true && string.Equals(secureBoot.Value, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new Recommendation("SEC-1", "Secure Boot is disabled",
                Severity.Advisory,
                "UEFI Secure Boot is present but turned off",
                new[] { $"Secure Boot state: {secureBoot.Value}", $"Source: {secureBoot.Source}" },
                "Deliberate configuration (dual-boot, legacy compatibility) or a firmware default.",
                "Enable Secure Boot in firmware setup if nothing on this machine requires it disabled. VERTEXA never changes firmware settings.",
                "High", null, "F-061"));
        }
    }

    private static void EvaluateEnvironment(DiagnosticSnapshot snapshot, List<Recommendation> results)
    {
        if (snapshot.MachineKind == MachineKind.VirtualMachine)
        {
            results.Add(new Recommendation("ENV-1", "This machine is a virtual machine",
                Severity.Info,
                "Hypervisor and/or virtual hardware signatures detected",
                new[]
                {
                    $"Model: {snapshot.MachineModel ?? "unknown"}",
                    "Battery, thermal, keyboard, display and camera tests describe the host, not the guest.",
                },
                "VERTEXA is running inside a VM, so physical sensors are not present.",
                "Run VERTEXA on physical hardware for complete diagnostics.",
                "High", null, "F-025"));
        }

        if (!snapshot.IsElevated)
        {
            var denied = snapshot.AllMetrics.Count(m => m.Availability is AvailabilityState.AccessDenied or AvailabilityState.RequiresAdministrator);
            if (denied > 0)
            {
                results.Add(new Recommendation("ENV-2", $"{denied} metric(s) need administrator rights",
                    Severity.Info,
                    "Some providers reported access denied",
                    new[] { "Elevation is optional; read-only inspection works without it." },
                    "Windows restricts some classes to elevated callers.",
                    "Use 'Restart elevated' from the scan bar when you need those specific values.",
                    "High", null, "F-012"));
            }
        }
    }
}
