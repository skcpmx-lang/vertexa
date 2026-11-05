using System.Diagnostics;

namespace Merqo.Vertexa.Core.Benchmarks;

/// <summary>
/// Memory bandwidth and latency measured with plain managed buffers. This measures the
/// process-visible memory path; it is explicitly *not* a physical RAM integrity test.
/// </summary>
public sealed class MemoryBenchmark
{
    public BenchmarkResult RunBandwidth(int bufferMegabytes = 128, CancellationToken cancellationToken = default)
    {
        var size = Math.Max(8, bufferMegabytes) * 1024 * 1024;
        var source = new byte[size];
        var destination = new byte[size];
        new Random(12345).NextBytes(source);
        var stopwatch = new Stopwatch();
        var bytesMoved = 0L;
        var rounds = 0;
        while (rounds < 4 && !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Start();
            Buffer.BlockCopy(source, 0, destination, 0, size);
            stopwatch.Stop();
            bytesMoved += size;
            rounds++;
        }
        if (cancellationToken.IsCancellationRequested)
            return new BenchmarkResult { Kind = BenchmarkKind.MemoryBandwidth, Title = "Memory bandwidth", Aborted = true, AbortReason = "cancelled" };

        var seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
        return new BenchmarkResult
        {
            Kind = BenchmarkKind.MemoryBandwidth,
            Title = "Memory copy bandwidth",
            Completed = true,
            PrimaryValue = bytesMoved / seconds / (1024.0 * 1024.0),
            PrimaryUnit = "MB/s",
            Duration = stopwatch.Elapsed,
            WorkloadDescription = $"Buffer.BlockCopy over {bufferMegabytes} MB buffers, {rounds} rounds.",
            Environment = $"{Environment.ProcessorCount} logical processors · .NET {Environment.Version}",
            Cautions = new[]
            {
                "Single-threaded copy bandwidth is limited by the memory controller and cache hierarchy.",
                "This is not a memory integrity test — use a boot-time RAM tester for physical fault detection.",
            },
        };
    }

    public BenchmarkResult RunLatency(int samples = 5_000_000, CancellationToken cancellationToken = default)
    {
        // Pointer-chase over a buffer larger than typical L2 to expose main-memory latency.
        var length = 8 * 1024 * 1024;
        var indices = new int[length];
        var random = new Random(4242);
        for (var i = 0; i < length; i++) indices[i] = i;
        for (var i = length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        var position = 0;
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < samples; i++)
        {
            position = indices[position];
            if (cancellationToken.IsCancellationRequested) break;
        }
        stopwatch.Stop();
        if (cancellationToken.IsCancellationRequested)
            return new BenchmarkResult { Kind = BenchmarkKind.MemoryLatency, Title = "Memory latency", Aborted = true, AbortReason = "cancelled" };

        return new BenchmarkResult
        {
            Kind = BenchmarkKind.MemoryLatency,
            Title = "Memory latency (pointer chase)",
            Completed = true,
            PrimaryValue = stopwatch.Elapsed.TotalMilliseconds * 1_000_000.0 / Math.Max(samples, 1),
            PrimaryUnit = "ns",
            Duration = stopwatch.Elapsed,
            WorkloadDescription = $"Random pointer chase over a {length * 4 / (1024 * 1024)} MB permutation, {samples:N0} hops.",
            Environment = ".NET " + Environment.Version,
            Details = new[] { ("Checksum", position.ToString("N0")) },
            Cautions = new[] { "Includes TLB misses and page-table walks; treat the value as relative." },
        };
    }
}
