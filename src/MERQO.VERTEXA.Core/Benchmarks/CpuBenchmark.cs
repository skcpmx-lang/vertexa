using System.Diagnostics;
using System.Numerics;

namespace Merqo.Vertexa.Core.Benchmarks;

/// <summary>
/// Deterministic CPU workload. The published number is measured operations per second
/// for a fixed, documented kernel — never a synthetic "score out of 1000".
/// </summary>
public sealed class CpuBenchmark
{
    public const string WorkloadDescription =
        "Integer mix (xorshift64 rotate/xor) + float64 FMA chain, fixed iteration count, no I/O, single thread unless stated.";

    public BenchmarkResult RunSingleCore(TimeSpan budget, CancellationToken cancellationToken = default)
    {
        var iterations = 4_000_000L;
        // Warm up so JIT time is not measured as compute throughput.
        Execute(200_000, out _, cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        var totalOps = 0L;
        var rounds = 0;
        while (stopwatch.Elapsed < budget && !cancellationToken.IsCancellationRequested)
        {
            Execute(iterations, out var checksum, cancellationToken);
            totalOps += iterations * 4;
            rounds++;
            if (checksum == 0) break; // keeps the JIT from eliding the work
        }
        stopwatch.Stop();
        if (cancellationToken.IsCancellationRequested)
            return Aborted(BenchmarkKind.CpuSingle, "Single-core CPU workload", "cancelled by safety governor or user");
        return new BenchmarkResult
        {
            Kind = BenchmarkKind.CpuSingle,
            Title = "CPU single-thread throughput",
            Completed = true,
            PrimaryValue = totalOps / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001) / 1_000_000.0,
            PrimaryUnit = "Mops/s",
            Duration = stopwatch.Elapsed,
            WorkloadDescription = WorkloadDescription,
            Environment = $"{Environment.ProcessorCount} logical processors · .NET {Environment.Version} · {rounds} rounds",
            Cautions = new[]
            {
                "Measured throughput depends on clock, boost behaviour and background load.",
                "Compare only against results from the same workload version.",
            },
        };
    }

    public BenchmarkResult RunMultiCore(TimeSpan budget, CancellationToken cancellationToken = default)
    {
        var threads = Math.Max(1, Environment.ProcessorCount);
        var perThread = 2_000_000L;
        var results = new long[threads];
        var stopwatch = Stopwatch.StartNew();
        Parallel.For(0, threads, new ParallelOptions { CancellationToken = cancellationToken }, index =>
        {
            var localStopwatch = Stopwatch.StartNew();
            var ops = 0L;
            while (localStopwatch.Elapsed < budget && !cancellationToken.IsCancellationRequested)
            {
                Execute(perThread, out _, cancellationToken);
                ops += perThread * 4;
            }
            results[index] = ops;
        });
        stopwatch.Stop();
        if (cancellationToken.IsCancellationRequested)
            return Aborted(BenchmarkKind.CpuMulti, "Multi-core CPU workload", "cancelled by safety governor or user");
        var total = results.Sum();
        return new BenchmarkResult
        {
            Kind = BenchmarkKind.CpuMulti,
            Title = "CPU multi-thread throughput",
            Completed = true,
            PrimaryValue = total / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001) / 1_000_000.0,
            PrimaryUnit = "Mops/s",
            Duration = stopwatch.Elapsed,
            WorkloadDescription = WorkloadDescription + " All logical processors loaded.",
            Environment = $"{threads} threads · .NET {Environment.Version}",
            Cautions = new[]
            {
                "On battery or thermally limited machines this is lower than on AC power.",
                "Do not run while another stress test is active.",
            },
        };
    }

    private static BenchmarkResult Aborted(BenchmarkKind kind, string title, string reason) => new()
    {
        Kind = kind,
        Title = title,
        Completed = false,
        Aborted = true,
        AbortReason = reason,
        WorkloadDescription = WorkloadDescription,
    };

    internal static void Execute(long iterations, out long checksum, CancellationToken cancellationToken)
    {
        var state = 0x9E3779B97F4A7C15UL;
        double accumulator = 0.6180339887498949;
        var counter = 0;
        for (long i = 0; i < iterations; i++)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            accumulator = Math.FusedMultiplyAdd(accumulator, 1.0000000001, 0.0000000001);
            if (accumulator > 1e6) accumulator -= 1e6;
            counter ^= (int)(state & 0xFFFF);
        }
        checksum = unchecked(state + (ulong)counter + BitConverter.DoubleToInt64Bits(accumulator));
        _ = Vector<ulong>.Zero; // documents that no SIMD intrinsics are assumed
        _ = cancellationToken;
    }
}
