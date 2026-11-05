using Merqo.Vertexa.Core.Benchmarks;
using Merqo.Vertexa.Core.Contracts;
using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Shared;
using Xunit;

namespace Merqo.Vertexa.UnitTests;

public sealed class ScanOrchestratorTests
{
    private sealed class FakeCollector : IScanCollector
    {
        private readonly Action<DiagnosticSnapshot> _action;
        private readonly bool _throws;

        public FakeCollector(string key, Action<DiagnosticSnapshot> action, int order = 10, bool throws = false, bool quick = true)
        {
            SectionKey = key;
            _action = action;
            _throws = throws;
            Order = order;
            IncludedInQuickScan = quick;
        }

        public string SectionKey { get; }
        public string Title => SectionKey;
        public int Order { get; }
        public bool IncludedInQuickScan { get; }

        public Task CollectAsync(DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
        {
            if (_throws) throw new InvalidOperationException("collector exploded");
            _action(snapshot);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Collectors_populate_sections_in_order()
    {
        var order = new List<string>();
        var collectors = new IScanCollector[]
        {
            new FakeCollector("second", s => { order.Add("second"); s.Section("second").Metric("a", 1, "test", "test"); }, order: 20),
            new FakeCollector("first", s => { order.Add("first"); s.Section("first").Metric("b", 2, "test", "test"); }, order: 10),
        };

        var snapshot = await new ScanOrchestrator(collectors).RunAsync(new ScanOptions());

        Assert.Equal(new[] { "first", "second" }, order);
        Assert.Equal(2, snapshot.TotalMetricCount);
        Assert.NotNull(snapshot.Health);
        Assert.NotNull(snapshot.Recommendations);
        Assert.True(snapshot.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task A_failing_collector_degrades_one_section_only()
    {
        var collectors = new IScanCollector[]
        {
            new FakeCollector("broken", _ => { }, throws: true),
            new FakeCollector("good", s => s.Section("good").Metric("value", 42, "test", "test"), order: 20),
        };

        var snapshot = await new ScanOrchestrator(collectors).RunAsync(new ScanOptions());

        Assert.Contains(snapshot.Warnings, w => w.Contains("broken", StringComparison.OrdinalIgnoreCase));
        Assert.Single(snapshot.Section("good").Metrics);
        Assert.Equal(42, snapshot.Section("good").Find<int>("value")!.Value);
    }

    [Fact]
    public async Task Quick_scan_skips_collectors_marked_full_only()
    {
        var collectors = new IScanCollector[]
        {
            new FakeCollector("quick", s => s.Section("quick").Metric("x", 1, "t", "t")),
            new FakeCollector("expensive", s => s.Section("expensive").Metric("y", 1, "t", "t"), order: 90, quick: false),
        };

        var snapshot = await new ScanOrchestrator(collectors).RunAsync(new ScanOptions { Scope = ScanScope.Quick });

        Assert.NotNull(snapshot.Find("quick"));
        Assert.Null(snapshot.Find("expensive"));
    }

    [Fact]
    public async Task Cancellation_propagates_to_the_caller()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var collectors = new IScanCollector[] { new FakeCollector("any", _ => { }) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ScanOrchestrator(collectors).RunAsync(new ScanOptions(), null, cts.Token));
    }

    [Fact]
    public async Task Progress_is_reported_for_every_collector()
    {
        var reports = new List<(string, int, int)>();
        var progress = new Progress<(string Section, int Done, int Total)>(p => reports.Add(p));
        var collectors = new IScanCollector[]
        {
            new FakeCollector("one", _ => { }),
            new FakeCollector("two", _ => { }, order: 20),
        };

        await new ScanOrchestrator(collectors).RunAsync(new ScanOptions(), progress);
        await Task.Delay(50);

        Assert.NotEmpty(reports);
    }
}

public sealed class BenchmarkTests
{
    [Fact]
    public void Cpu_benchmark_measures_a_positive_throughput()
    {
        var result = new CpuBenchmark().RunSingleCore(TimeSpan.FromMilliseconds(150));
        Assert.True(result.Completed);
        Assert.True(result.PrimaryValue > 0);
        Assert.Equal("Mops/s", result.PrimaryUnit);
        Assert.False(string.IsNullOrWhiteSpace(result.WorkloadDescription));
        Assert.NotEmpty(result.Cautions);
    }

    [Fact]
    public void Cpu_benchmark_honours_cancellation_and_reports_it_honestly()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = new CpuBenchmark().RunSingleCore(TimeSpan.FromSeconds(1), cts.Token);
        Assert.False(result.Completed);
        Assert.True(result.Aborted);
        Assert.Contains("cancelled", result.AbortReason);
    }

    [Fact]
    public void Memory_bandwidth_reports_megabytes_per_second()
    {
        var result = new MemoryBenchmark().RunBandwidth(16);
        Assert.True(result.Completed);
        Assert.True(result.PrimaryValue > 0);
        Assert.Equal("MB/s", result.PrimaryUnit);
        Assert.Contains(result.Cautions, c => c.Contains("not a memory integrity test", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Storage_benchmark_writes_then_deletes_only_its_own_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vertexa-bench-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
        try
        {
            var result = new StorageBenchmark().RunSequential(directory, 16, includeWrite: true);
            Assert.True(result.Completed, result.AbortReason);
            Assert.True(result.PrimaryValue > 0);
            Assert.Equal("MB/s", result.PrimaryUnit);
            // The harness must clean up after itself.
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Storage_benchmark_refuses_a_missing_directory()
    {
        var result = new StorageBenchmark().RunSequential(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N")), 16, true);
        Assert.False(result.Completed);
        Assert.Contains("does not exist", result.AbortReason);
    }

    [Fact]
    public void Governor_blocks_a_hot_start_and_allows_a_cool_one()
    {
        var governor = new SafetyGovernor();
        var limits = new SafetyLimits { MaxTemperatureCelsius = 95 };

        Assert.False(governor.EvaluateStart(98, limits).Allowed);
        Assert.True(governor.EvaluateStart(65, limits).Allowed);
        Assert.True(governor.EvaluateStart(88, limits).Allowed);
        Assert.True(governor.EvaluateStart(88, limits).Warning is not null);
    }

    [Fact]
    public void Governor_allows_unknown_temperature_but_warns()
    {
        var governor = new SafetyGovernor();
        var decision = governor.EvaluateStart(null, new SafetyLimits());
        Assert.True(decision.Allowed);
        Assert.NotNull(decision.Warning);

        var strict = governor.EvaluateStart(null, new SafetyLimits { AllowWhenTemperatureUnknown = false });
        Assert.False(strict.Allowed);
    }

    [Fact]
    public void Governor_stops_a_run_at_the_thermal_ceiling_and_at_the_duration_limit()
    {
        var governor = new SafetyGovernor();
        var limits = new SafetyLimits { MaxTemperatureCelsius = 95, MaxDuration = TimeSpan.FromMinutes(1) };

        Assert.False(governor.EvaluateDuringRun(96, TimeSpan.Zero, limits).Allowed);
        Assert.False(governor.EvaluateDuringRun(70, TimeSpan.FromSeconds(61), limits).Allowed);
        Assert.True(governor.EvaluateDuringRun(70, TimeSpan.FromSeconds(5), limits).Allowed);
        Assert.True(governor.EvaluateDuringRun(70, TimeSpan.FromSeconds(5), limits).Warning is null);
    }
}
