using System.Collections.ObjectModel;
using System.Windows.Input;
using Merqo.Vertexa.App.Mvvm;
using Merqo.Vertexa.App.Services;
using Merqo.Vertexa.Core.Contracts;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.App.ViewModels;

/// <summary>Live monitor. Every tile names its source and shows "unavailable" rather than zero.</summary>
public sealed class MonitorViewModel : PageViewModelBase
{
    private readonly AppHost _host;
    private bool _isRunning;
    private string _intervalText;
    private string _unavailableText = string.Empty;
    private int _sampleCount;

    public MonitorViewModel(AppHost host)
    {
        _host = host;
        _intervalText = $"{host.Settings.MonitoringIntervalMs} ms";
        CpuSeries = new ObservableCollection<double?>();
        MemorySeries = new ObservableCollection<double?>();
        DiskSeries = new ObservableCollection<double?>();
        NetworkSeries = new ObservableCollection<double?>();
        TemperatureSeries = new ObservableCollection<double?>();
        ToggleCommand = new RelayCommand(Toggle);
    }

    public override string Key => "monitor";
    public override string Title => "Live monitor";
    public override string Subtitle => "Sampled from WMI performance counters and power APIs — gaps stay gaps.";

    public ObservableCollection<double?> CpuSeries { get; }
    public ObservableCollection<double?> MemorySeries { get; }
    public ObservableCollection<double?> DiskSeries { get; }
    public ObservableCollection<double?> NetworkSeries { get; }
    public ObservableCollection<double?> TemperatureSeries { get; }
    public ICommand ToggleCommand { get; }

    public bool IsRunning { get => _isRunning; private set { if (SetProperty(ref _isRunning, value)) OnPropertyChanged(nameof(ToggleLabel)); } }
    public string ToggleLabel => IsRunning ? "Stop monitoring" : "Start monitoring";
    public string IntervalText { get => _intervalText; private set => SetProperty(ref _intervalText, value); }
    public string UnavailableText { get => _unavailableText; private set => SetProperty(ref _unavailableText, value); }
    public string SampleText => _sampleCount == 0 ? "no samples yet" : $"{_sampleCount} samples";
    public double CpuValue => CpuSeries.LastOrDefault(v => v.HasValue) ?? 0;
    public double MemoryValue => MemorySeries.LastOrDefault(v => v.HasValue) ?? 0;
    public double TemperatureValue => TemperatureSeries.LastOrDefault(v => v.HasValue) ?? 0;
    public string CpuDisplay => CpuSeries.LastOrDefault(v => v.HasValue) is { } cpu ? $"{cpu:0} %" : "unavailable";
    public string MemoryDisplay => MemorySeries.LastOrDefault(v => v.HasValue) is { } mem ? $"{mem:0} %" : "unavailable";
    public string TemperatureDisplay => TemperatureSeries.LastOrDefault(v => v.HasValue) is { } t ? $"{t:0.0} °C" : "no sensor";
    public string DiskDisplay => DiskSeries.LastOrDefault(v => v.HasValue) is { } d ? Units.BytesToHuman((long)d) + "/s" : "unavailable";
    public string NetworkDisplay => NetworkSeries.LastOrDefault(v => v.HasValue) is { } n ? Units.BytesToHuman((long)n) + "/s" : "unavailable";

    public override void OnActivated()
    {
        Start();
    }

    public override void OnDeactivated()
    {
        Stop();
    }

    private void Toggle()
    {
        if (IsRunning) Stop();
        else Start();
    }

    private void Start()
    {
        if (IsRunning) return;
        _host.Monitoring.Sampled += OnSampled;
        _host.Monitoring.Start(TimeSpan.FromMilliseconds(_host.Settings.MonitoringIntervalMs));
        IsRunning = true;
        IntervalText = $"{_host.Settings.MonitoringIntervalMs} ms";
    }

    private void Stop()
    {
        if (!IsRunning) return;
        _host.Monitoring.Sampled -= OnSampled;
        _host.Monitoring.Stop();
        IsRunning = false;
    }

    private void OnSampled(object? sender, MonitoringSample sample)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        void Apply()
        {
            Append(CpuSeries, sample.CpuLoadPercent);
            Append(MemorySeries, sample.MemoryUsedPercent);
            Append(DiskSeries, (sample.DiskReadBytesPerSecond ?? 0) + (sample.DiskWriteBytesPerSecond ?? 0));
            Append(NetworkSeries, sample.NetworkBytesPerSecond);
            Append(TemperatureSeries, sample.HottestTemperatureCelsius);
            _sampleCount++;
            UnavailableText = sample.Unavailable.Count == 0
                ? "All monitored metrics returned data."
                : "Unavailable in this environment: " + string.Join(", ", sample.Unavailable);
            OnPropertyChanged(nameof(CpuDisplay));
            OnPropertyChanged(nameof(MemoryDisplay));
            OnPropertyChanged(nameof(TemperatureDisplay));
            OnPropertyChanged(nameof(DiskDisplay));
            OnPropertyChanged(nameof(NetworkDisplay));
            OnPropertyChanged(nameof(SampleText));
        }

        if (dispatcher is null || dispatcher.CheckAccess()) Apply();
        else dispatcher.BeginInvoke(Apply);
    }

    private static void Append(ObservableCollection<double?> series, double? value)
    {
        series.Add(value);
        while (series.Count > 180) series.RemoveAt(0);
    }
}
