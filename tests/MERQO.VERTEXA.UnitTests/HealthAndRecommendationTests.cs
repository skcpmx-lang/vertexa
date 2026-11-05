using Merqo.Vertexa.Core.Health;
using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Core.Recommendations;
using Merqo.Vertexa.Core.Storage;
using Merqo.Vertexa.Shared;
using Xunit;

namespace Merqo.Vertexa.UnitTests;

public sealed class HealthAndRecommendationTests
{
    private static DiagnosticSnapshot EmptySnapshot() => new();

    private static DiagnosticSnapshot SnapshotWithHealthyDrive()
    {
        var snapshot = EmptySnapshot();
        snapshot.StorageDevices = new[]
        {
            new StorageDeviceReport
            {
                Model = "Test NVMe",
                MediaType = StorageMediaType.NvmeSsd,
                CapacityBytes = 512UL * 1024 * 1024 * 1024,
                SmartAvailability = AvailabilityState.Available,
                SmartSource = "IOCTL_STORAGE_QUERY_PROPERTY (NVMe log 0x02)",
                HealthStatus = "Healthy",
                Nvme = new NvmeHealthLog(0, 41.5, 100, 10, 12, 1000, 2000, 0, 0, 0, 10, 900, 1, 0, 0, 0, 0),
            },
        };
        return snapshot;
    }

    [Fact]
    public void No_evidence_yields_insufficient_evidence_not_a_perfect_score()
    {
        var report = new HealthScorer().Score(EmptySnapshot());

        Assert.Null(report.Overall);
        Assert.Equal(AvailabilityState.NotTested, report.State);
        Assert.Equal("Insufficient evidence", report.Headline);
    }

    [Fact]
    public void Storage_evidence_alone_is_below_the_minimum_weight()
    {
        var report = new HealthScorer().Score(SnapshotWithHealthyDrive());

        var storage = report.Subsystems.Single(s => s.Key == "storage");
        Assert.NotNull(storage.Score);
        Assert.Equal(100, storage.Score);
        // 30 weight points < minimum 50 -> overall still insufficient.
        Assert.Null(report.Overall);
    }

    [Fact]
    public void Every_subsystem_explains_itself_and_never_defaults_to_100()
    {
        var report = new HealthScorer().Score(EmptySnapshot());

        Assert.All(report.Subsystems, subsystem =>
        {
            Assert.False(string.IsNullOrWhiteSpace(subsystem.Explanation));
            if (subsystem.Score is null) Assert.NotEmpty(subsystem.Excluded);
        });
    }

    [Fact]
    public void Failing_drive_drags_the_storage_score_down()
    {
        var snapshot = SnapshotWithHealthyDrive();
        snapshot.StorageDevices = new[]
        {
            new StorageDeviceReport
            {
                Model = "Failing HDD",
                MediaType = StorageMediaType.Hdd,
                SmartAvailability = AvailabilityState.Available,
                HealthStatus = "Warning",
                Attributes = new[]
                {
                    new SmartAttribute(0x05, "Reallocated sector count", 100, 100, 10, 42, new byte[6], "Raw = 42", SmartSeverity.Critical),
                },
            },
        };

        var report = new HealthScorer().Score(snapshot);
        var storage = report.Subsystems.Single(s => s.Key == "storage");
        Assert.NotNull(storage.Score);
        Assert.True(storage.Score < 40);
        Assert.Contains("Failing HDD", string.Join(" ", storage.Evidence));
    }

    [Fact]
    public void Battery_health_feeds_the_score_when_present()
    {
        var snapshot = SnapshotWithHealthyDrive();
        snapshot.Battery = new Core.Battery.BatteryAssessment
        {
            Present = true,
            HealthPercent = 55,
            WearPercent = 45,
            HealthState = AvailabilityState.Available,
            HealthExplanation = "Health 55.0% and wear 45.0% derived from measured capacities.",
        };

        var report = new HealthScorer().Score(snapshot);
        var battery = report.Subsystems.Single(s => s.Key == "battery");
        Assert.Equal(50, battery.Score);
    }

    [Fact]
    public void Desktop_without_battery_is_excluded_not_penalised()
    {
        var snapshot = SnapshotWithHealthyDrive();
        snapshot.Battery = Core.Battery.BatteryAssessment.Absent("No battery present.");

        var battery = new HealthScorer().Score(snapshot).Subsystems.Single(s => s.Key == "battery");
        Assert.Null(battery.Score);
        Assert.Equal(AvailabilityState.NotApplicable, battery.State);
    }

    [Fact]
    public void Failing_drive_generates_a_critical_backup_recommendation_with_evidence()
    {
        var snapshot = SnapshotWithHealthyDrive();
        snapshot.StorageDevices = new[]
        {
            new StorageDeviceReport
            {
                Model = "Worn SSD",
                SmartAvailability = AvailabilityState.Available,
                HealthStatus = "Unhealthy",
                Nvme = new NvmeHealthLog(0x04, 55, 5, 10, 96, 0, 0, 0, 0, 0, 0, 0, 0, 17, 0, 0, 0),
            },
        };

        var recommendations = new RecommendationEngine().Evaluate(snapshot);
        var critical = recommendations.First(r => r.Severity == Severity.Critical);
        Assert.Contains("Back up", critical.Title);
        Assert.NotEmpty(critical.Evidence);
        Assert.Contains("Media errors", string.Join(" ", critical.Evidence));
    }

    [Fact]
    public void Worn_battery_produces_a_replacement_advisory()
    {
        var snapshot = SnapshotWithHealthyDrive();
        snapshot.Battery = new Core.Battery.BatteryAssessment
        {
            Present = true,
            HealthPercent = 41,
            WearPercent = 59,
            DesignCapacityMWh = 50_000,
            FullChargeCapacityMWh = 20_500,
            HealthState = AvailabilityState.Available,
            HealthExplanation = "Health 41.0% and wear 59.0% derived from measured capacities.",
            Evidence = new[] { "Design 50000 mWh", "Full charge 20500 mWh" },
        };

        var recommendations = new RecommendationEngine().Evaluate(snapshot);
        Assert.Contains(recommendations, r => r.Id == "BAT-1" && r.Severity == Severity.Critical);
    }

    [Fact]
    public void Virtual_machine_environment_produces_an_informational_note()
    {
        var snapshot = SnapshotWithHealthyDrive();
        snapshot.MachineKind = MachineKind.VirtualMachine;

        var recommendations = new RecommendationEngine().Evaluate(snapshot);
        Assert.Contains(recommendations, r => r.Id == "ENV-1" && r.Severity == Severity.Info);
    }

    [Fact]
    public void Recommendations_are_ordered_by_severity()
    {
        var snapshot = SnapshotWithHealthyDrive();
        snapshot.MachineKind = MachineKind.VirtualMachine;
        snapshot.Battery = new Core.Battery.BatteryAssessment
        {
            Present = true,
            HealthPercent = 30,
            WearPercent = 70,
            DesignCapacityMWh = 50_000,
            FullChargeCapacityMWh = 15_000,
            HealthState = AvailabilityState.Available,
            HealthExplanation = "measured",
            Evidence = new[] { "design 50000", "full 15000" },
        };

        var recommendations = new RecommendationEngine().Evaluate(snapshot);
        var severities = recommendations.Select(r => r.Severity).ToList();
        Assert.Equal(severities.OrderByDescending(s => s), severities);
    }
}
