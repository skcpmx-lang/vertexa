using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Core.Storage;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Core.Health;

/// <summary>
/// Transparent health scoring.
///
/// Contract:
///  * A subsystem with no evidence is EXCLUDED, never given a default 100.
///  * Every included subsystem lists the exact metrics that produced its score.
///  * If the included weight is below <see cref="MinimumEvidenceWeight"/>, the overall
///    result is `Insufficient evidence` rather than a flattering number.
///  * Health is separate from performance: benchmark results never raise the score.
/// </summary>
public sealed class HealthScorer
{
    public const double MinimumEvidenceWeight = 50;

    private static readonly (string Key, string Title, double Weight)[] Weights =
    {
        (SectionKeys.Storage, "Storage", 30),
        (SectionKeys.Battery, "Battery", 20),
        (SectionKeys.Thermal, "Thermal", 20),
        (SectionKeys.Drivers, "Drivers & devices", 15),
        (SectionKeys.Security, "Platform security", 15),
    };

    public HealthReport Score(DiagnosticSnapshot snapshot)
    {
        var subsystems = new List<SubsystemScore>
        {
            ScoreStorage(snapshot),
            ScoreBattery(snapshot),
            ScoreThermal(snapshot),
            ScoreDrivers(snapshot),
            ScoreSecurity(snapshot),
        };

        var included = subsystems.Where(s => s.Score is not null).ToList();
        var weight = included.Sum(s => s.Weight);
        var notes = new List<string>();

        foreach (var excluded in subsystems.Where(s => s.Score is null))
            notes.Add($"{excluded.Title} excluded from the score: {excluded.Excluded.FirstOrDefault() ?? "no evidence available"}.");

        if (weight < MinimumEvidenceWeight)
        {
            return new HealthReport(null, AvailabilityState.NotTested, "Insufficient evidence",
                FormulaDescription(weight),
                subsystems,
                notes.Append($"Only {weight:0} of 100 weight points had evidence (minimum {MinimumEvidenceWeight:0}).").ToList());
        }

        var overall = Math.Round(included.Sum(s => s.Score!.Value * s.Weight) / weight, 0);
        var headline = overall switch
        {
            >= 90 => "Healthy",
            >= 75 => "Minor issues",
            >= 55 => "Attention needed",
            _ => "Action required",
        };

        return new HealthReport(overall, AvailabilityState.Available, headline, FormulaDescription(weight), subsystems, notes);
    }

    private static string FormulaDescription(double includedWeight) =>
        "Weighted mean of available subsystem scores: storage 30, battery 20, thermal 20, drivers 15, platform security 15. " +
        $"Included weight {includedWeight:0}/100. Unavailable evidence is excluded, never assumed healthy.";

    private SubsystemScore ScoreStorage(DiagnosticSnapshot snapshot)
    {
        var evidence = new List<string>();
        var drives = snapshot.StorageDevices;
        if (drives.Count == 0)
            return new("storage", "Storage", null, AvailabilityState.Unavailable, WeightOf("storage"),
                "No storage health telemetry was collected.", evidence, new[] { "No drive reported health data." });

        double worst = 100;
        var seenHealth = false;
        foreach (var drive in drives)
        {
            if (drive.IsFailing)
            {
                worst = Math.Min(worst, 15);
                seenHealth = true;
                evidence.Add($"{drive.Model}: {drive.HealthStatus}" + (drive.Nvme?.MediaErrors > 0 ? $", {drive.Nvme.MediaErrors} media errors" : string.Empty));
                continue;
            }

            var critical = drive.Attributes.Where(a => a.Severity == SmartSeverity.Critical).ToList();
            if (critical.Count > 0)
            {
                worst = Math.Min(worst, 35);
                seenHealth = true;
                evidence.Add($"{drive.Model}: {string.Join(", ", critical.Select(c => c.Name))}");
            }

            if (drive.Nvme is not null)
            {
                seenHealth = true;
                var used = drive.Nvme.PercentageUsed;
                evidence.Add($"{drive.Model}: {used}% of rated endurance used, {drive.Nvme.AvailableSparePercent}% spare left");
                worst = Math.Min(worst, used switch
                {
                    >= 95 => 40,
                    >= 80 => 65,
                    >= 60 => 80,
                    _ => 100,
                });
                if (drive.Nvme.UnsafeShutdowns > 100) evidence.Add($"{drive.Model}: {drive.Nvme.UnsafeShutdowns} unsafe shutdowns recorded");
            }
            else if (drive.SmartAvailability == AvailabilityState.Available)
            {
                seenHealth = true;
                var warnings = drive.Attributes.Where(a => a.Severity is SmartSeverity.Warning or SmartSeverity.Advisory).ToList();
                if (warnings.Count > 0) evidence.Add($"{drive.Model}: {string.Join(", ", warnings.Select(w => w.Name))}");
                if (warnings.Count > 0) worst = Math.Min(worst, 80);
            }
        }

        if (!seenHealth)
            return new("storage", "Storage", null, AvailabilityState.Unavailable, WeightOf("storage"),
                "Drives were found but none exposed health telemetry.", evidence,
                new[] { "S.M.A.R.T./NVMe health telemetry unavailable (common behind USB bridges and RAID controllers)." });

        return new("storage", "Storage", worst, AvailabilityState.Available, WeightOf("storage"),
            worst >= 100 ? "All detected drives report normal health telemetry." : "At least one drive has health findings.", evidence, Array.Empty<string>());
    }

    private SubsystemScore ScoreBattery(DiagnosticSnapshot snapshot)
    {
        var battery = snapshot.Battery;
        if (battery is null || !battery.Present)
            return new("battery", "Battery", null, AvailabilityState.NotApplicable, WeightOf("battery"),
                "No battery installed — excluded (this is normal for desktops).", Array.Empty<string>(),
                new[] { "No battery present." });

        if (battery.HealthPercent is null)
            return new("battery", "Battery", null, battery.HealthState, WeightOf("battery"),
                "Battery present but capacity-based wear could not be computed.", Array.Empty<string>(),
                new[] { battery.HealthExplanation });

        var score = battery.HealthPercent.Value switch
        {
            >= 90 => 95,
            >= 80 => 85,
            >= 65 => 70,
            >= 45 => 50,
            _ => 25,
        };
        return new("battery", "Battery", score, AvailabilityState.Available, WeightOf("battery"),
            $"Battery health {battery.HealthPercent:0.0}% ({battery.Band}).", new[] { battery.HealthExplanation }, Array.Empty<string>());
    }

    private SubsystemScore ScoreThermal(DiagnosticSnapshot snapshot)
    {
        var thermal = snapshot.Find(SectionKeys.Thermal);
        var evidence = new List<string>();
        var temps = thermal?.Metrics.OfType<DiagnosticValue<double>>()
            .Where(m => m.HasData && (m.Unit == Units.Celsius))
            .ToList() ?? new List<DiagnosticValue<double>>();

        if (temps.Count == 0)
            return new("thermal", "Thermal", null, AvailabilityState.Unavailable, WeightOf("thermal"),
                "No temperature sensor was exposed by the firmware.", evidence,
                new[] { "No ACPI thermal zone available (common on modern laptops where the embedded controller hides sensors)." });

        var hottest = temps.MaxBy(t => t.Value!.Value)!;
        evidence.Add($"{hottest.Name}: {hottest.DisplayValue} {hottest.Unit} via {hottest.Source}");
        var score = hottest.Value!.Value switch
        {
            >= 95 => 20,
            >= 85 => 55,
            >= 75 => 80,
            _ => 95,
        };
        return new("thermal", "Thermal", score, AvailabilityState.Available, WeightOf("thermal"),
            $"Hottest sensor reading {hottest.DisplayValue} {hottest.Unit}.", evidence, Array.Empty<string>());
    }

    private SubsystemScore ScoreDrivers(DiagnosticSnapshot snapshot)
    {
        var drivers = snapshot.Find(SectionKeys.Drivers);
        if (drivers is null || drivers.TotalCount == 0)
            return new("drivers", "Drivers & devices", null, AvailabilityState.Unavailable, WeightOf("drivers"),
                "Device inventory was not collected.", Array.Empty<string>(), new[] { "No device data." });

        var problems = drivers.Metrics.OfType<DiagnosticValue<int>>()
            .Where(m => m.Name.EndsWith("problem devices", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(m => m.HasData);

        var unknown = drivers.Metrics.OfType<DiagnosticValue<int>>()
            .FirstOrDefault(m => m.Name.Contains("Unknown", StringComparison.OrdinalIgnoreCase) && m.HasData);

        if (problems is null)
            return new("drivers", "Drivers & devices", null, AvailabilityState.Unavailable, WeightOf("drivers"),
                "Device problem-code summary unavailable.", Array.Empty<string>(), new[] { "Could not read device status." });

        var problemCount = problems.Value;
        var unknownCount = unknown?.Value ?? 0;
        var evidence = new List<string> { $"{problemCount.Value} device(s) with a driver problem code", $"{unknownCount} device(s) with no driver" };
        var score = problemCount.Value switch
        {
            0 => unknownCount == 0 ? 98 : 90,
            <= 2 => 72,
            <= 5 => 50,
            _ => 30,
        };
        return new("drivers", "Drivers & devices", score, AvailabilityState.Available, WeightOf("drivers"),
            $"{problemCount.Value} device(s) report a problem code.", evidence, Array.Empty<string>());
    }

    private SubsystemScore ScoreSecurity(DiagnosticSnapshot snapshot)
    {
        var security = snapshot.Find(SectionKeys.Security);
        if (security is null) 
            return new("security", "Platform security", null, AvailabilityState.Unavailable, WeightOf("security"),
                "Security platform data not collected.", Array.Empty<string>(), new[] { "No data." });

        var secureBoot = security.Find<string>("Secure Boot state");
        var tpm = security.Find<string>("TPM present");
        var virtualisation = security.Find<string>("Virtualisation-based security");

        if (secureBoot is null || !secureBoot.HasData)
            return new("security", "Platform security", null, AvailabilityState.Unavailable, WeightOf("security"),
                "Secure Boot state could not be read.", Array.Empty<string>(), new[] { "Firmware security state unavailable." });

        var evidence = new List<string> { $"Secure Boot: {secureBoot.Value}" };
        var score = 0.0;
        if (string.Equals(secureBoot.Value, "Enabled", StringComparison.OrdinalIgnoreCase)) score += 60; else score += 25;
        if (tpm?.HasData == true && !string.Equals(tpm.Value, "Not present", StringComparison.OrdinalIgnoreCase)) { score += 20; evidence.Add("TPM present"); }
        if (virtualisation?.HasData == true && string.Equals(virtualisation.Value, "Running", StringComparison.OrdinalIgnoreCase)) { score += 20; evidence.Add("VBS running"); }
        else score += 10;

        return new("security", "Platform security", Math.Min(score, 100), AvailabilityState.Available, WeightOf("security"),
            "Score reflects Secure Boot, TPM presence and virtualisation-based security state.", evidence, Array.Empty<string>());
    }

    private static double WeightOf(string key) => Weights.First(w => w.Key == key).Weight;
    public static IReadOnlyList<(string Key, string Title, double Weight)> WeightTable => Weights;
}
