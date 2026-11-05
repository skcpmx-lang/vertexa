using Merqo.Vertexa.Core.Benchmarks;
using Merqo.Vertexa.Core.Contracts;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Diagnostics;

/// <summary>
/// Application-layer benchmark use case. Every run is wrapped by the safety governor,
/// a temperature watchdog and full cancellation support; results are only reported as
/// completed when the workload actually ran to completion.
/// </summary>
public sealed class BenchmarkService
{
    private readonly IMonitoringService _monitoring;
    private readonly SafetyGovernor _governor;
    private readonly IAppLogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BenchmarkService(IMonitoringService monitoring, IAppLogger log)
    {
        _monitoring = monitoring;
        _governor = new SafetyGovernor(log);
        _log = log;
    }

    public bool IsBusy => _gate.CurrentCount == 0;

    public async Task<Core.Benchmarks.BenchmarkResult> RunCpuAsync(bool multiCore, TimeSpan duration, SafetyLimits limits,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var start = _governor.EvaluateStart(await ReadTemperatureAsync(cancellationToken).ConfigureAwait(false), limits);
        if (!start.Allowed)
            return Aborted(multiCore ? BenchmarkKind.CpuMulti : BenchmarkKind.CpuSingle, "CPU benchmark", start.Reason);
        progress?.Report(start.Reason);

        return await RunGuardedAsync(
            multiCore ? "CPU multi-thread" : "CPU single-thread",
            limits,
            (token, report) => Task.Run(() =>
            {
                var benchmark = new CpuBenchmark();
                return multiCore ? benchmark.RunMultiCore(duration, token) : benchmark.RunSingleCore(duration, token);
            }, token),
            progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Core.Benchmarks.BenchmarkResult> RunMemoryAsync(bool latency, TimeSpan duration, SafetyLimits limits,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var start = _governor.EvaluateStart(await ReadTemperatureAsync(cancellationToken).ConfigureAwait(false), limits);
        if (!start.Allowed)
            return Aborted(latency ? BenchmarkKind.MemoryLatency : BenchmarkKind.MemoryBandwidth, "Memory benchmark", start.Reason);

        return await RunGuardedAsync(
            latency ? "Memory latency" : "Memory bandwidth",
            limits,
            (token, report) => Task.Run(() =>
            {
                var benchmark = new MemoryBenchmark();
                if (latency) return benchmark.RunLatency(cancellationToken: token);
                var bandwidth = benchmark.RunBandwidth(cancellationToken: token);
                _ = duration;
                return bandwidth;
            }, token),
            progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Core.Benchmarks.BenchmarkResult> RunStorageAsync(string directory, int sizeMegabytes, bool includeWrite,
        TimeSpan duration, SafetyLimits limits, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var start = _governor.EvaluateStart(await ReadTemperatureAsync(cancellationToken).ConfigureAwait(false), limits);
        if (!start.Allowed)
            return Aborted(BenchmarkKind.StorageSequentialRead, "Storage benchmark", start.Reason);

        return await RunGuardedAsync(
            "Storage sequential",
            limits,
            (token, report) => Task.Run(() =>
            {
                var benchmark = new StorageBenchmark();
                return benchmark.RunSequential(directory, sizeMegabytes, includeWrite, token);
            }, token),
            progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Core.Benchmarks.BenchmarkResult> RunGuardedAsync(string title, SafetyLimits limits,
        Func<CancellationToken, IProgress<string>, Task<Core.Benchmarks.BenchmarkResult>> workload,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var started = DateTimeOffset.UtcNow;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var watchdog = WatchAsync(limits, linked, progress, stopwatch);

            var result = await workload(linked.Token, progress!).ConfigureAwait(false);
            linked.Cancel();
            try { await watchdog.ConfigureAwait(false); } catch (OperationCanceledException) { }
            stopwatch.Stop();

            _log.Info("benchmark", $"{title}: {result.Summary} in {stopwatch.Elapsed.TotalSeconds:0.0}s");
            progress?.Report($"{title}: {result.Summary}");
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Aborted(BenchmarkKind.CpuSingle, title, "cancelled by user");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Samples temperature while the workload runs and aborts on the thermal ceiling.</summary>
    private async Task WatchAsync(SafetyLimits limits, CancellationTokenSource linked, IProgress<string>? progress,
        System.Diagnostics.Stopwatch stopwatch)
    {
        while (!linked.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1500, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var sample = await _monitoring.SampleOnceAsync(linked.Token).ConfigureAwait(false);
            var decision = _governor.EvaluateDuringRun(sample.HottestTemperatureCelsius, stopwatch.Elapsed, limits);
            if (decision.Warning is not null) progress?.Report(decision.Warning);
            if (!decision.Allowed)
            {
                progress?.Report("Safety stop: " + decision.Reason);
                linked.Cancel();
                return;
            }
        }
    }

    private async Task<double?> ReadTemperatureAsync(CancellationToken cancellationToken)
    {
        try
        {
            var sample = await _monitoring.SampleOnceAsync(cancellationToken).ConfigureAwait(false);
            return sample.HottestTemperatureCelsius;
        }
        catch
        {
            return null;
        }
    }

    private static Core.Benchmarks.BenchmarkResult Aborted(BenchmarkKind kind, string title, string reason) => new()
    {
        Kind = kind,
        Title = title,
        Completed = false,
        Aborted = true,
        AbortReason = reason,
    };
}
