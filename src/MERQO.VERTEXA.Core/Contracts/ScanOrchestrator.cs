using System.Diagnostics;
using Merqo.Vertexa.Core.Health;
using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Core.Recommendations;
using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Core.Contracts;

public sealed class ScanOptions
{
    public ScanScope Scope { get; init; } = ScanScope.Full;
    public bool Elevated { get; init; }
    public bool RedactSerials { get; init; } = true;
    public string AppVersion { get; init; } = "0.0.0";
    public TimeSpan PerSectionTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Runs collectors in sequence (identity first, heavy sections after), isolating
/// failures, timing the scan, then scoring health and generating recommendations.
/// </summary>
public sealed class ScanOrchestrator
{
    private readonly IReadOnlyList<IScanCollector> _collectors;
    private readonly IAppLogger _log;
    private readonly HealthScorer _scorer;
    private readonly RecommendationEngine _recommendations;

    public ScanOrchestrator(IReadOnlyList<IScanCollector> collectors, IAppLogger? log = null,
        HealthScorer? scorer = null, RecommendationEngine? recommendations = null)
    {
        _collectors = collectors;
        _log = log ?? NullLogger.Instance;
        _scorer = scorer ?? new HealthScorer();
        _recommendations = recommendations ?? new RecommendationEngine();
    }

    public async Task<DiagnosticSnapshot> RunAsync(ScanOptions options, IProgress<(string Section, int Done, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = new DiagnosticSnapshot
        {
            Scope = options.Scope,
            AppVersion = options.AppVersion,
            RedactionEnabled = options.RedactSerials,
            IsElevated = options.Elevated,
        };

        var stopwatch = Stopwatch.StartNew();
        var collectors = _collectors
            .Where(c => options.Scope != ScanScope.Quick || c.IncludedInQuickScan)
            .OrderBy(c => c.Order)
            .ToList();

        var done = 0;
        foreach (var collector in collectors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((collector.Title, done, collectors.Count));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.PerSectionTimeout);
            var sectionStopwatch = Stopwatch.StartNew();
            try
            {
                await collector.CollectAsync(snapshot, timeout.Token).ConfigureAwait(false);
                _log.Debug("scan", $"{collector.SectionKey} collected in {sectionStopwatch.ElapsedMilliseconds} ms");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                snapshot.AddWarning($"{collector.Title}: timed out after {(int)options.PerSectionTimeout.TotalSeconds}s; section may be incomplete.");
                snapshot.Section(collector.SectionKey);
            }
            catch (Exception ex)
            {
                snapshot.AddWarning($"{collector.Title}: collection failed ({ex.GetType().Name}: {ex.Message}).");
                snapshot.Section(collector.SectionKey);
                _log.Error("scan", $"{collector.SectionKey} collector failed", ex);
            }
            done++;
        }

        progress?.Report(("Scoring", collectors.Count, collectors.Count));
        snapshot.Duration = stopwatch.Elapsed;
        snapshot.Health = _scorer.Score(snapshot);
        snapshot.Recommendations = _recommendations.Evaluate(snapshot);
        return snapshot;
    }
}

/// <summary>Fluent builder for metric sections, used by Windows providers.</summary>
public static class SectionBuilder
{
    public static DiagnosticValue<T> Metric<T>(this MetricSection section, string name, T value, string source, string method,
        string unit = "", ConfidenceLevel confidence = ConfidenceLevel.High,
        VerificationState verification = VerificationState.SingleSource, string? limitation = null)
    {
        var metric = DiagnosticValue<T>.FromAvailable(name, value, source, method, unit, confidence, verification, limitation: limitation);
        section.Add(metric);
        return metric;
    }

    public static DiagnosticValue<T> Missing<T>(this MetricSection section, string name, AvailabilityState state, string reason,
        string source = "", string method = "", string unit = "", string? limitation = null)
    {
        var metric = DiagnosticValue<T>.FromUnavailable(name, state, reason, source, method, unit, limitation);
        section.Add(metric);
        return metric;
    }
}
