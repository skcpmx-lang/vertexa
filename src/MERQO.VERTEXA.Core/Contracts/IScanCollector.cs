using Merqo.Vertexa.Core.Models;

namespace Merqo.Vertexa.Core.Contracts;

/// <summary>
/// A unit of collection (one subsystem). Collectors are independent so a failing
/// subsystem degrades exactly one section instead of the whole scan.
/// </summary>
public interface IScanCollector
{
    string SectionKey { get; }
    string Title { get; }
    /// <summary>Lower runs first. Cheap identity collectors use 10, expensive ones 50+.</summary>
    int Order { get; }
    bool IncludedInQuickScan { get; }
    Task CollectAsync(DiagnosticSnapshot snapshot, CancellationToken cancellationToken);
}
