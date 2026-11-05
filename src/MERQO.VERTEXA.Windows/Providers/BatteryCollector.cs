using Merqo.Vertexa.Core.Battery;
using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Shared;
using Merqo.Vertexa.Windows.Wmi;

namespace Merqo.Vertexa.Windows.Providers;

/// <summary>
/// Battery intelligence. Every number here is read from the firmware through
/// root\wmi or the power API; nothing is estimated except the runtime estimate,
/// which is labelled as such.
/// </summary>
internal sealed class BatteryCollector : WindowsCollectorBase
{
    public BatteryCollector(IAppLogger? log = null) : base(log) { }
    public override string SectionKey => SectionKeys.Battery;
    public override string Title => "Battery & power";
    public override int Order => 45;

    protected override void CollectCore(DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
    {
        var section = Section(snapshot);
        var evidence = new List<string>();

        var staticData = Wmi("SELECT DesignedCapacity, Chemistry, ManufactureDate, SerialNumber, DeviceName, DefaultAlert1, DefaultAlert2 FROM BatteryStaticData", WmiQuery.WmiScope);
        var fullCharge = Wmi("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", WmiQuery.WmiScope);
        var batteryStatus = Wmi("SELECT RemainingCapacity, Voltage, Rate, PowerOnline, Charging, Discharging, RemainingCapacity FROM BatteryStatus", WmiQuery.WmiScope);
        var win32Battery = Wmi("SELECT Name, DeviceID, EstimatedChargeRemaining, BatteryStatus, DesignCapacity, FullChargeCapacity, Chemistry, Voltage, EstimatedRunTime FROM Win32_Battery");

        Native.NativeMethods.TryGetSystemPowerStatus(out var powerStatus);
        var hasStatic = staticData.First is not null;
        var hasWin32 = win32Battery.Rows.Count > 0;
        if (!hasStatic && !hasWin32 && powerStatus.BatteryFlag == 128)
        {
            snapshot.Battery = BatteryAssessment.Absent("No system battery is present (desktop or mini-PC).");
            section.Missing<uint>("Design capacity", AvailabilityState.NotApplicable, "no battery present", "root\\wmi", "CIM query");
            return;
        }

        uint design = staticData.First?.UInt("DesignedCapacity") ?? 0;
        uint full = fullCharge.First?.UInt("FullChargedCapacity") ?? 0;
        if (design == 0) design = win32Battery.First?.UInt("DesignCapacity") ?? 0;
        if (full == 0) full = win32Battery.First?.UInt("FullChargeCapacity") ?? 0;

        if (design > 0)
            section.Metric("Design capacity", design, "root\\wmi:BatteryStaticData", "CIM query", unit: Units.AmpereHour, confidence: ConfidenceLevel.High);
        else
            section.Missing<uint>("Design capacity", AvailabilityState.Unavailable,
                staticData.Error ?? "firmware does not report designed capacity", "root\\wmi:BatteryStaticData", "CIM query", Units.AmpereHour);

        if (full > 0)
            section.Metric("Full charge capacity", full, "root\\wmi:BatteryFullChargedCapacity", "CIM query", unit: Units.AmpereHour, confidence: ConfidenceLevel.High);
        else
            section.Missing<uint>("Full charge capacity", AvailabilityState.Unavailable,
                fullCharge.Error ?? "firmware does not report full-charged capacity", "root\\wmi:BatteryFullChargedCapacity", "CIM query", Units.AmpereHour);

        var assessment = BatteryMath.Assess(design, full);
        if (assessment.IsValid)
        {
            section.Metric("Battery health", assessment.HealthPercent!.Value, "VERTEXA battery engine", "FCC ÷ design × 100", unit: Units.Percent, confidence: ConfidenceLevel.Medium);
            section.Metric("Battery wear", assessment.WearPercent!.Value, "VERTEXA battery engine", "(design − FCC) ÷ design × 100", unit: Units.Percent, confidence: ConfidenceLevel.Medium);
            evidence.Add(assessment.Explanation);
        }
        else
        {
            section.Missing<double>("Battery health", AvailabilityState.Unavailable, assessment.RejectReason ?? "capacities invalid", "VERTEXA battery engine", "FCC ÷ design × 100");
            evidence.Add(assessment.RejectReason ?? "capacities unavailable");
        }

        if (staticData.First is { } sd)
        {
            if (sd.UInt("Chemistry") is { } chemistry)
                section.Metric("Chemistry", ChemistryName(chemistry), "root\\wmi:BatteryStaticData", "CIM query", confidence: ConfidenceLevel.Medium);
            if (sd.UInt("ManufactureDate") is { } manufactureDate && manufactureDate > 0)
                section.Metric("Manufacture date (firmware)", FormatBatteryDate(manufactureDate), "root\\wmi:BatteryStaticData", "CIM query", confidence: ConfidenceLevel.Medium);
            if (sd.Text("DeviceName") is { } deviceName) section.Metric("Battery name", deviceName, "root\\wmi:BatteryStaticData", "CIM query");
        }

        if (win32Battery.First?.Text("Name") is { } win32Name)
            section.Metric("Battery (WMI name)", win32Name, "WMI:Win32_Battery", "CIM query", confidence: ConfidenceLevel.Medium);

        // Cycle count: only where a class actually exposes it.
        var cycle = Wmi("SELECT CycleCount FROM BatteryCycleCount", WmiQuery.WmiScope);
        int? cycleCount = cycle.First?.Int("CycleCount");
        if (cycleCount is > 0)
            section.Metric("Cycle count", cycleCount.Value, "root\\wmi:BatteryCycleCount", "CIM query", confidence: ConfidenceLevel.Medium);
        else
            section.Missing<int>("Cycle count", AvailabilityState.Unsupported,
                "this battery/firmware does not expose a cycle counter to Windows",
                "root\\wmi:BatteryCycleCount", "CIM query",
                limitation: "Windows has no general API for battery cycle count; only firmware that publishes it can be read.");

        uint remaining = batteryStatus.First?.UInt("RemainingCapacity") ?? 0;
        if (remaining > 0)
            section.Metric("Remaining capacity", remaining, "root\\wmi:BatteryStatus", "CIM query", unit: Units.AmpereHour, confidence: ConfidenceLevel.High);

        if (batteryStatus.First?.UInt("Voltage") is { } voltage && voltage > 0)
            section.Metric("Voltage", (int)voltage, "root\\wmi:BatteryStatus", "CIM query", unit: "mV", confidence: ConfidenceLevel.High);

        var rate = batteryStatus.First?.Int("Rate") ?? 0;
        if (rate != 0)
            section.Metric(rate < 0 ? "Discharge rate" : "Charge rate", Math.Abs(rate), "root\\wmi:BatteryStatus", "CIM query", unit: "mW", confidence: ConfidenceLevel.High);
        else
            section.Missing<int>("Charge/discharge rate", AvailabilityState.Unavailable, "rate not reported by this battery", "root\\wmi:BatteryStatus", "CIM query", "mW");

        double? chargePercent = powerStatus.BatteryLifePercent is > 0 and <= 100 ? powerStatus.BatteryLifePercent : null;
        if (chargePercent is null && win32Battery.First?.UInt("EstimatedChargeRemaining") is { } estimated)
            chargePercent = estimated;
        if (chargePercent is not null)
            section.Metric("Charge level", Math.Round(chargePercent.Value, 0), "Kernel32!GetSystemPowerStatus / Win32_Battery", "Native power API", unit: Units.Percent, confidence: ConfidenceLevel.High);

        var onLine = powerStatus.AclineStatus == 1;
        var charging = batteryStatus.First?.Bool("Charging") ?? false;
        var discharging = batteryStatus.First?.Bool("Discharging") ?? false;
        section.Metric("Power source", onLine ? "AC adapter" : "Battery", "Kernel32!GetSystemPowerStatus", "Native power API");
        section.Metric("Charging", charging, "root\\wmi:BatteryStatus", "CIM query", confidence: ConfidenceLevel.High);
        section.Metric("Discharging", discharging, "root\\wmi:BatteryStatus", "CIM query", confidence: ConfidenceLevel.High);

        double? runtimeHours = null;
        if (rate < 0 && remaining > 0)
        {
            runtimeHours = BatteryMath.EstimateRuntimeHours(full, Math.Abs(rate), remaining);
            if (runtimeHours is not null)
                section.Metric("Estimated runtime", runtimeHours.Value, "VERTEXA battery engine", "remaining ÷ observed discharge", unit: "h",
                    confidence: ConfidenceLevel.Low,
                    limitation: "Estimate derived from the instantaneous discharge rate; real runtime varies with load and brightness.");
        }
        if (runtimeHours is null)
            section.Missing<double>("Estimated runtime", AvailabilityState.Unavailable,
                "no active discharge measurement", "VERTEXA battery engine", "remaining ÷ observed discharge", "h");

        snapshot.Battery = new BatteryAssessment
        {
            Present = true,
            Name = win32Battery.First?.Text("Name") ?? staticData.First?.Text("DeviceName"),
            Chemistry = staticData.First?.UInt("Chemistry") is { } chem ? ChemistryName(chem) : null,
            DesignCapacityMWh = design,
            FullChargeCapacityMWh = full,
            RemainingCapacityMWh = remaining,
            CycleCount = cycleCount,
            HealthPercent = assessment.HealthPercent,
            WearPercent = assessment.WearPercent,
            HealthState = assessment.IsValid ? AvailabilityState.Available : AvailabilityState.Unavailable,
            HealthExplanation = assessment.IsValid ? assessment.Explanation : assessment.RejectReason ?? "capacities unavailable",
            ChargePercent = chargePercent,
            VoltageMillivolts = batteryStatus.First?.UInt("Voltage"),
            RateMilliwatts = rate == 0 ? null : Math.Abs(rate),
            PowerLine = onLine ? PowerLineState.PluggedIn : PowerLineState.OnBattery,
            IsCharging = charging,
            IsDischarging = discharging,
            EstimatedRuntimeHours = runtimeHours,
            Evidence = evidence,
        };
    }

    internal static string ChemistryName(uint chemistry) => chemistry switch
    {
        1 => "Other",
        2 => "Unknown",
        3 => "Lead acid",
        4 => "Nickel cadmium",
        5 => "Nickel metal hydride",
        6 => "Lithium ion",
        7 => "Nickel zinc",
        8 => "Lithium polymer",
        _ => "Chemistry " + chemistry,
    };

    /// <summary>SMBIOS battery date encoding: bits 0-4 day, 5-8 month, 9-15 year-1980.</summary>
    internal static string FormatBatteryDate(uint packed)
    {
        var day = (int)(packed & 0x1F);
        var month = (int)((packed >> 5) & 0x0F);
        var year = 1980 + (int)((packed >> 9) & 0x7F);
        if (day is < 1 or > 31 || month is < 1 or > 12 || year is < 1990 or > 2100)
            return $"Firmware value 0x{packed:X} (not a valid date)";
        return $"{year:0000}-{month:00}-{day:00}";
    }
}
