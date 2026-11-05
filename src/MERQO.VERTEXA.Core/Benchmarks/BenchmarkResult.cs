using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Core.Benchmarks;

public enum BenchmarkKind { CpuSingle = 0, CpuMulti = 1, MemoryBandwidth = 2, MemoryLatency = 3, StorageSequentialRead = 4, StorageSequentialWrite = 5, StorageRandomRead = 6, StorageRandomWrite = 7, GpuRender = 8 }

/// <summary>
/// One measured benchmark. A benchmark never carries an absolute "score out of 100":
/// it carries the measured primitive, the exact workload definition, and (optionally) a
/// reference-scaled index whose baseline hardware is named in <see cref="ReferenceNote"/>.
/// </summary>
public sealed class BenchmarkResult
{
    public BenchmarkKind Kind { get; init; }
    public string Title { get; init; } = string.Empty;
    public bool Completed { get; init; }
    public bool Aborted { get; init; }
    public string? AbortReason { get; init; }
    public double PrimaryValue { get; init; }
    public string PrimaryUnit { get; init; } = string.Empty;
    public double? Index { get; init; }
    public string? ReferenceNote { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public TimeSpan Duration { get; init; }
    public string WorkloadDescription { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public IReadOnlyList<(string Name, string Value)> Details { get; init; } = Array.Empty<(string, string)>();
    public IReadOnlyList<string> Cautions { get; init; } = Array.Empty<string>();

    public string Summary => Completed
        ? $"{PrimaryValue:N1} {PrimaryUnit}" + (Index is null ? string.Empty : $" · index {Index:N0}")
        : Aborted ? "Aborted: " + AbortReason : "Not completed";

    public DiagnosticValue<double> ToMetric() => Completed
        ? DiagnosticValue<double>.FromAvailable(Title, PrimaryValue, "VERTEXA benchmark harness", WorkloadDescription,
            unit: PrimaryUnit, confidence: ConfidenceLevel.Medium, verification: VerificationState.SingleSource,
            limitation: ReferenceNote, notes: Cautions)
        : DiagnosticValue<double>.FromUnavailable(Title, AvailabilityState.NotTested,
            Aborted ? "Aborted: " + AbortReason : "Benchmark not executed.");
}
