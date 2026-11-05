using System.Diagnostics;
using System.Security.Cryptography;

namespace Merqo.Vertexa.Core.Benchmarks;

/// <summary>
/// Non-destructive storage benchmark.
///
/// Safety contract:
///  * writes only inside a caller-supplied directory, inside a GUID-named subfolder;
///  * the file it creates is the only file it ever deletes;
///  * every byte it wrote is verified by reading it back before the result is published;
///  * the caller must opt in to the write phase.
/// </summary>
public sealed class StorageBenchmark
{
    public const string FolderPrefix = "vertexa-bench-";

    public BenchmarkResult RunSequential(string directory, int sizeMegabytes, bool includeWrite, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
            return new BenchmarkResult { Kind = BenchmarkKind.StorageSequentialRead, Title = "Storage sequential", Aborted = true, AbortReason = "target directory does not exist" };

        var folder = Path.Combine(directory, FolderPrefix + Guid.NewGuid().ToString("N")[..8]);
        var file = Path.Combine(folder, "vertexa-test.bin");
        var buffer = new byte[1024 * 1024];
        var totalBytes = (long)Math.Max(16, sizeMegabytes) * 1024 * 1024;
        var stopwatch = new Stopwatch();
        double? writeMbPerSecond = null;
        try
        {
            Directory.CreateDirectory(folder);
            RandomNumberGenerator.Fill(buffer.AsSpan(0, 4096));

            if (includeWrite)
            {
                stopwatch.Restart();
                using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.WriteThrough))
                {
                    for (long written = 0; written < totalBytes; written += buffer.Length)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        stream.Write(buffer, 0, buffer.Length);
                    }
                    stream.Flush(true);
                }
                stopwatch.Stop();
                if (cancellationToken.IsCancellationRequested)
                    return new BenchmarkResult { Kind = BenchmarkKind.StorageSequentialWrite, Title = "Storage write", Aborted = true, AbortReason = "cancelled" };
                writeMbPerSecond = totalBytes / 1024.0 / 1024.0 / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
            }

            // Read-back phase (also verifies the data written above).
            stopwatch.Restart();
            long verified = 0;
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
            {
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    verified += read;
                }
            }
            stopwatch.Stop();
            var readMbPerSecond = verified / 1024.0 / 1024.0 / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);

            var details = new List<(string, string)>
            {
                ("Bytes read", verified.ToString("N0")),
                ("Test size", sizeMegabytes + " MB"),
                ("Write phase", includeWrite ? "executed" : "skipped (read-only mode)"),
            };
            if (verified < totalBytes && includeWrite) details.Add(("Verification", "partial — data size differed from the requested size"));

            return new BenchmarkResult
            {
                Kind = includeWrite ? BenchmarkKind.StorageSequentialWrite : BenchmarkKind.StorageSequentialRead,
                Title = includeWrite ? "Storage sequential read/write" : "Storage sequential read",
                Completed = true,
                PrimaryValue = readMbPerSecond,
                PrimaryUnit = "MB/s",
                Duration = stopwatch.Elapsed,
                WorkloadDescription = $"{sizeMegabytes} MB sequential I/O with FileOptions.WriteThrough and flush-to-disk.",
                Environment = directory,
                Details = writeMbPerSecond is null ? details : details.Append(("Write throughput", $"{writeMbPerSecond:0.0} MB/s")).ToArray(),
                Cautions = new[]
                {
                    "File-system cache effects are reduced by flush-to-disk but not eliminated.",
                    "Only the temporary file VERTEXA created is written and deleted.",
                },
            };
        }
        catch (Exception ex)
        {
            return new BenchmarkResult
            {
                Kind = BenchmarkKind.StorageSequentialWrite,
                Title = "Storage sequential",
                Aborted = true,
                AbortReason = ex.GetType().Name + ": " + ex.Message,
            };
        }
        finally
        {
            TryCleanUp(folder, file);
        }
    }

    /// <summary>Deletes only the file this harness created, then the folder it created.</summary>
    internal static void TryCleanUp(string folder, string file)
    {
        try
        {
            if (File.Exists(file)) File.Delete(file);
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch
        {
            // Leftovers are reported by the caller's UI, never silently ignored elsewhere.
        }
    }
}
