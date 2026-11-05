using System.Collections.ObjectModel;
using System.Windows.Input;
using Merqo.Vertexa.App.Mvvm;
using Merqo.Vertexa.App.Services;
using Merqo.Vertexa.Core.Contracts;
using Merqo.Vertexa.Core.Health;
using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Core.Recommendations;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.App.ViewModels;

public sealed class RecommendationRow
{
    public RecommendationRow(Recommendation recommendation)
    {
        Title = recommendation.Title;
        Severity = recommendation.SeverityLabel;
        SeverityValue = recommendation.Severity;
        Condition = recommendation.Condition;
        Action = recommendation.SuggestedAction;
        Evidence = string.Join(" · ", recommendation.Evidence.Take(3));
        Confidence = recommendation.Confidence;
        Safety = recommendation.SafetyNote ?? string.Empty;
    }

    public string Title { get; }
    public string Severity { get; }
    public Severity SeverityValue { get; }
    public string Condition { get; }
    public string Action { get; }
    public string Evidence { get; }
    public string Confidence { get; }
    public string Safety { get; }
    public bool HasSafety => !string.IsNullOrWhiteSpace(Safety);
}

public sealed class SubsystemRow
{
    public SubsystemRow(SubsystemScore score)
    {
        Title = score.Title;
        ScoreText = score.Score is null ? "—" : $"{score.Score:0}";
        Explanation = score.Explanation;
        State = score.State;
        Included = score.Score is not null;
        Evidence = string.Join(" · ", score.Evidence.Take(2));
    }

    public string Title { get; }
    public string ScoreText { get; }
    public string Explanation { get; }
    public AvailabilityState State { get; }
    public bool Included { get; }
    public string Evidence { get; }
}

/// <summary>Dashboard: real health evidence, live tiles and quick actions.</summary>
public sealed class DashboardViewModel : PageViewModelBase
{
    private readonly AppHost _host;
    private bool _isLive;
    private string _healthScore = "—";
    private string _healthHeadline = "No scan yet";
    private string _healthFormula = string.Empty;
    private string _scanSummary = string.Empty;
    private double? _cpuLoad;
    private double? _memoryLoad;
    private double? _temperature;
    private double? _diskRate;
    private double? _networkRate;
    private double? _battery;
    private string _liveNote = "Live telemetry is paused.";

    public DashboardViewModel(AppHost host)
    {
        _host = host;
        Subsystems = new ObservableCollection<SubsystemRow>();
        Alerts = new ObservableCollection<RecommendationRow>();
        KeyFacts = new ObservableCollection<MetricRowVm>();
        CpuHistory = new ObservableCollection<double?>();
        MemoryHistory = new ObservableCollection<double?>();
        ScanCommand = new AsyncRelayCommand(async () =>
        {
            if (RunScanAsync is not null) await RunScanAsync().ConfigureAwait(true);
        });
        OpenPageCommand = new RelayCommand(parameter => Navigate?.Invoke(parameter as string ?? string.Empty));
    }

    /// <summary>Wired by the shell so the dashboard can start a scan without owning the scan engine.</summary>
    public Func<Task>? RunScanAsync { get; set; }

    /// <summary>Wired by the shell: navigates to another page by key.</summary>
    public Action<string>? Navigate { get; set; }

    public override string Key => "dashboard";
    public override string Title => "Dashboard";
    public override string Subtitle => "One software. Complete PC intelligence.";

    public ObservableCollection<SubsystemRow> Subsystems { get; }
    public ObservableCollection<RecommendationRow> Alerts { get; }
    public ObservableCollection<MetricRowVm> KeyFacts { get; }
    public ObservableCollection<double?> CpuHistory { get; }
    public ObservableCollection<double?> MemoryHistory { get; }
    public ICommand ScanCommand { get; }
    public ICommand OpenPageCommand { get; }

    public string HealthScore { get => _healthScore; private set => SetProperty(ref _healthScore, value); }
    public string HealthHeadline { get => _healthHeadline; private set => SetProperty(ref _healthHeadline, value); }
    public string HealthFormula { get => _healthFormula; private set => SetProperty(ref _healthFormula, value); }
    public string ScanSummary { get => _scanSummary; private set => SetProperty(ref _scanSummary, value); }
    public string LiveNote { get => _liveNote; private set => SetProperty(ref _liveNote, value); }

    public double? CpuLoad { get => _cpuLoad; private set { if (SetProperty(ref _cpuLoad, value)) OnPropertyChanged(nameof(CpuText)); } }
    public double? MemoryLoad { get => _memoryLoad; private set { if (SetProperty(ref _memoryLoad, value)) OnPropertyChanged(nameof(MemoryText)); } }
    public double? Temperature { get => _temperature; private set { if (SetProperty(ref _temperature, value)) OnPropertyChanged(nameof(TemperatureText)); } }
    public double? DiskRate { get => _diskRate; private set { if (SetProperty(ref _diskRate, value)) OnPropertyChanged(nameof(DiskText)); } }
    public double? NetworkRate { get => _networkRate; private set { if (SetProperty(ref _networkRate, value)) OnPropertyChanged(nameof(NetworkText)); } }
    public double? BatteryPercent { get => _battery; private set { if (SetProperty(ref _battery, value)) OnPropertyChanged(nameof(BatteryText)); } }

    public string CpuText => CpuLoad is null ? "unavailable" : $"{CpuLoad:0} %";
    public string MemoryText => MemoryLoad is null ? "unavailable" : $"{MemoryLoad:0} %";
    public string TemperatureText => Temperature is null ? "no sensor" : $"{Temperature:0.0} °C";
    public string DiskText => DiskRate is null ? "unavailable" : Units.BytesToHuman((long)DiskRate.Value) + "/s";
    public string NetworkText => NetworkRate is null ? "unavailable" : Units.BytesToHuman((long)NetworkRate.Value) + "/s";
    public string BatteryText => BatteryPercent is null ? "no battery" : $"{BatteryPercent:0} %";

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        RefreshAsync();
    }

    public override Task RefreshAsync()
    {
        RefreshAsyncCore();
        return Task.CompletedTask;
    }

    private void RefreshAsyncCore()
    {
        var snapshot = Snapshot;
        if (snapshot is null)
        {
            HealthScore = "—";
            HealthHeadline = "Run a scan to see evidence-based health";
            ScanSummary = "No diagnostic run in this session yet.";
            return;
        }

        var health = snapshot.Health;
        HealthScore = health is null ? "—" : health.OverallDisplay;
        HealthHeadline = health?.Headline ?? "Insufficient evidence";
        HealthFormula = health?.Formula ?? string.Empty;

        Subsystems.Clear();
        if (health is not null)
            foreach (var score in health.Subsystems) Subsystems.Add(new SubsystemRow(score));

        Alerts.Clear();
        foreach (var recommendation in snapshot.Recommendations.Take(8))
            Alerts.Add(new RecommendationRow(recommendation));

        KeyFacts.Clear();
        foreach (var key in new[] { SectionKeys.System, SectionKeys.Cpu, SectionKeys.Gpu, SectionKeys.Memory })
        {
            var section = snapshot.Find(key);
            if (section is null) continue;
            foreach (var metric in section.Metrics.Where(m => m.HasData).Take(3))
                KeyFacts.Add(new MetricRowVm(metric));
        }

        var warnings = snapshot.Recommendations.Count(r => r.Severity >= Severity.Warning);
        ScanSummary = $"Run {snapshot.Id} · {snapshot.CreatedAt.ToLocalTime():HH:mm:ss} · {snapshot.AvailableMetricCount}/{snapshot.TotalMetricCount} metrics · " +
                      $"{warnings} warning(s) · {snapshot.Duration.TotalSeconds:0.0}s · {(snapshot.IsElevated ? "elevated" : "standard session")}";
    }

    public override void OnActivated()
    {
        _isLive = true;
        _host.Monitoring.Sampled += OnSampled;
        if (!_host.Monitoring.IsRunning) _host.Monitoring.Start(TimeSpan.FromMilliseconds(_host.Settings.MonitoringIntervalMs));
        LiveNote = $"Live telemetry · {_host.Settings.MonitoringIntervalMs} ms interval · values from WMI performance counters";
    }

    public override void OnDeactivated()
    {
        if (!_isLive) return;
        _isLive = false;
        _host.Monitoring.Sampled -= OnSampled;
        if (_host.Monitoring.IsRunning && !_host.Settings.MonitoringAutoStart) _host.Monitoring.Stop();
    }

    private void OnSampled(object? sender, MonitoringSample sample)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        void Apply()
        {
            CpuLoad = sample.CpuLoadPercent;
            MemoryLoad = sample.MemoryUsedPercent;
            Temperature = sample.HottestTemperatureCelsius;
            DiskRate = sample.DiskReadBytesPerSecond + sample.DiskWriteBytesPerSecond;
            NetworkRate = sample.NetworkBytesPerSecond;
            BatteryPercent = sample.BatteryPercent;
            Append(CpuHistory, sample.CpuLoadPercent);
            Append(MemoryHistory, sample.MemoryUsedPercent);
        }

        if (dispatcher is null || dispatcher.CheckAccess()) Apply();
        else dispatcher.BeginInvoke(Apply);
    }

    private static void Append(ObservableCollection<double?> series, double? value)
    {
        series.Add(value);
        while (series.Count > 90) series.RemoveAt(0);
    }
}
