using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Merqo.Vertexa.App.Mvvm;
using Merqo.Vertexa.App.Services;
using Merqo.Vertexa.Core.Benchmarks;

namespace Merqo.Vertexa.App.ViewModels;

public sealed class BenchmarkRow
{
    public BenchmarkRow(BenchmarkResult result)
    {
        Title = result.Title;
        Summary = result.Summary;
        Completed = result.Completed;
        Workload = result.WorkloadDescription;
        Cautions = string.Join(" ", result.Cautions);
        Details = string.Join(" · ", result.Details.Select(d => $"{d.Name}: {d.Value}"));
    }

    public string Title { get; }
    public string Summary { get; }
    public bool Completed { get; }
    public string Workload { get; }
    public string Cautions { get; }
    public string Details { get; }
}

/// <summary>Benchmark page: explicit workloads, safety ceiling, always-available cancel.</summary>
public sealed class PerformanceViewModel : PageViewModelBase
{
    private readonly AppHost _host;
    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private int _durationSeconds = 15;
    private int _storageSizeMb = 128;
    private string _storageDirectory;

    public PerformanceViewModel(AppHost host)
    {
        _host = host;
        _storageDirectory = Path.GetTempPath();
        Results = new ObservableCollection<BenchmarkRow>();
        CpuSingleCommand = new AsyncRelayCommand(() => RunAsync("CPU single-thread", (limits, progress, token) => _host.Benchmarks.RunCpuAsync(false, TimeSpan.FromSeconds(DurationSeconds), limits, progress, token)));
        CpuMultiCommand = new AsyncRelayCommand(() => RunAsync("CPU multi-thread", (limits, progress, token) => _host.Benchmarks.RunCpuAsync(true, TimeSpan.FromSeconds(DurationSeconds), limits, progress, token)));
        MemoryBandwidthCommand = new AsyncRelayCommand(() => RunAsync("Memory bandwidth", (limits, progress, token) => _host.Benchmarks.RunMemoryAsync(false, TimeSpan.FromSeconds(DurationSeconds), limits, progress, token)));
        MemoryLatencyCommand = new AsyncRelayCommand(() => RunAsync("Memory latency", (limits, progress, token) => _host.Benchmarks.RunMemoryAsync(true, TimeSpan.FromSeconds(DurationSeconds), limits, progress, token)));
        StorageCommand = new AsyncRelayCommand(() => RunAsync("Storage sequential", (limits, progress, token) => _host.Benchmarks.RunStorageAsync(StorageDirectory, StorageSizeMb, includeWrite: true, TimeSpan.FromSeconds(DurationSeconds), limits, progress, token)));
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
    }

    public override string Key => "performance";
    public override string Title => "Benchmarks";
    public override string Subtitle => "Measured workloads with a documented definition. No synthetic scores.";

    public ObservableCollection<BenchmarkRow> Results { get; }
    public AsyncRelayCommand CpuSingleCommand { get; }
    public AsyncRelayCommand CpuMultiCommand { get; }
    public AsyncRelayCommand MemoryBandwidthCommand { get; }
    public AsyncRelayCommand MemoryLatencyCommand { get; }
    public AsyncRelayCommand StorageCommand { get; }
    public RelayCommand CancelCommand { get; }

    public int DurationSeconds
    {
        get => _durationSeconds;
        set => SetProperty(ref _durationSeconds, Math.Clamp(value, 5, 120));
    }

    public int StorageSizeMb
    {
        get => _storageSizeMb;
        set => SetProperty(ref _storageSizeMb, Math.Clamp(value, 16, 2048));
    }

    public string StorageDirectory
    {
        get => _storageDirectory;
        set
        {
            if (!SetProperty(ref _storageDirectory, value)) return;
            if (!Directory.Exists(value)) StatusText = "That folder does not exist — the write test will refuse to start.";
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetProperty(ref _isRunning, value)) return;
            CancelCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(RunLabel));
        }
    }

    public string RunLabel => IsRunning ? "Running…" : "Idle";

    public string SafetyText =>
        $"Safety ceiling {_host.Settings.ThermalAbortCelsius:0} °C · duration limit {DurationSeconds}s · " +
        "every run can be cancelled and aborts automatically if the thermal ceiling is crossed. " +
        "Software monitoring cannot prevent hardware failure, so keep loads reasonable.";

    public double MaximumTemperature => _host.Settings.ThermalAbortCelsius;

    private async Task RunAsync(string title, Func<SafetyLimits, IProgress<string>, CancellationToken, Task<BenchmarkResult>> run)
    {
        if (IsRunning) return;
        IsRunning = true;
        _cts = new CancellationTokenSource();
        StatusText = title + " started…";
        var progress = new Progress<string>(message => StatusText = message);
        var limits = new SafetyLimits
        {
            MaxTemperatureCelsius = _host.Settings.ThermalAbortCelsius,
            MaxDuration = TimeSpan.FromSeconds(DurationSeconds + 30),
        };
        try
        {
            var result = await run(limits, progress, _cts.Token).ConfigureAwait(true);
            Results.Insert(0, new BenchmarkRow(result));
            StatusText = result.Completed ? $"{title} complete: {result.Summary}" : $"{title} aborted: {result.AbortReason}";
            if (Snapshot is not null && result.Completed)
            {
                Snapshot.Benchmarks = new[] { result }.Concat(Snapshot.Benchmarks).ToList();
            }
        }
        catch (Exception ex)
        {
            StatusText = title + " failed: " + ex.Message;
            _host.Logger.Error("benchmark", title + " failed", ex);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "Cancelling…";
    }

    public override void OnDeactivated() => Cancel();
}
