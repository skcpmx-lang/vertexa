using System.Collections.ObjectModel;
using Merqo.Vertexa.App.Services;
using Merqo.Vertexa.Core.Battery;
using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.App.ViewModels;

public sealed class BatteryViewModel : PageViewModelBase
{
    private readonly AppHost _host;
    private string _headline = "No battery data";
    private string _explanation = string.Empty;
    private string _health = "—";
    private string _wear = "—";
    private string _band = string.Empty;
    private string _runtime = "not available";
    private string _charge = "—";
    private string _powerSource = "unknown";
    private string _cycleCount = "not exposed";
    private bool _hasBattery = true;
    private double _healthBar;

    public BatteryViewModel(AppHost host)
    {
        _host = host;
        Rows = new ObservableCollection<MetricRowVm>();
    }

    public override string Key => "battery";
    public override string Title => "Battery & power";
    public override string Subtitle => "Capacity, wear and charge state read from the battery firmware.";

    public ObservableCollection<MetricRowVm> Rows { get; }
    public string Headline { get => _headline; private set => SetProperty(ref _headline, value); }
    public string Explanation { get => _explanation; private set => SetProperty(ref _explanation, value); }
    public string HealthText { get => _health; private set => SetProperty(ref _health, value); }
    public string WearText { get => _wear; private set => SetProperty(ref _wear, value); }
    public string Band { get => _band; private set => SetProperty(ref _band, value); }
    public string RuntimeText { get => _runtime; private set => SetProperty(ref _runtime, value); }
    public string ChargeText { get => _charge; private set => SetProperty(ref _charge, value); }
    public string PowerSource { get => _powerSource; private set => SetProperty(ref _powerSource, value); }
    public string CycleCountText { get => _cycleCount; private set => SetProperty(ref _cycleCount, value); }
    public bool HasBattery { get => _hasBattery; private set => SetProperty(ref _hasBattery, value); }
    public double HealthBar { get => _healthBar; private set => SetProperty(ref _healthBar, value); }

    public override Task RefreshAsync()
    {
        Rows.Clear();
        var snapshot = Snapshot;
        if (snapshot is null)
        {
            Headline = "No scan data yet";
            Explanation = "Run a scan to read battery telemetry.";
            return Task.CompletedTask;
        }

        foreach (var row in Rows(snapshot.Find(SectionKeys.Battery))) Rows.Add(row);

        var battery = snapshot.Battery;
        if (battery is null)
        {
            HasBattery = false;
            Headline = "Battery information unavailable";
            Explanation = "The battery collector did not run in the last scan.";
            return Task.CompletedTask;
        }

        HasBattery = battery.Present;
        Headline = battery.Present
            ? (battery.HealthPercent is null ? "Battery present — wear cannot be computed" : $"{battery.Band} · {battery.HealthPercent:0.0}% of design capacity")
            : "No battery installed";
        Explanation = battery.HealthExplanation;
        HealthText = battery.HealthPercent is null ? "not computable" : $"{battery.HealthPercent:0.0} %";
        WearText = battery.WearPercent is null ? "not computable" : $"{battery.WearPercent:0.0} %";
        Band = battery.Band;
        HealthBar = battery.HealthPercent ?? 0;
        RuntimeText = battery.RuntimeLabel;
        ChargeText = battery.ChargePercent is null ? "unknown" : $"{battery.ChargePercent:0} %";
        PowerSource = battery.PowerLine switch
        {
            PowerLineState.PluggedIn => battery.IsCharging ? "AC adapter · charging" : "AC adapter",
            PowerLineState.OnBattery => "Battery",
            _ => "unknown",
        };
        CycleCountText = battery.CycleCount is null ? "not exposed by this firmware" : battery.CycleCount.Value.ToString("N0");
        return Task.CompletedTask;
    }

    private static IEnumerable<MetricRowVm> Rows(MetricSection? section) =>
        section is null ? Array.Empty<MetricRowVm>() : section.Metrics.Select(m => new MetricRowVm(m));
}
