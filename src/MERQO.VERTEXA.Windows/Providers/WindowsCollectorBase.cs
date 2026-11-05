using Merqo.Vertexa.Core.Contracts;
using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Shared;
using Merqo.Vertexa.Windows.Wmi;

namespace Merqo.Vertexa.Windows.Providers;

/// <summary>
/// Base for Windows collectors: provides section access, the WMI query helper and a
/// guarantee that blocking COM/I-O runs off the caller's thread.
/// </summary>
internal abstract class WindowsCollectorBase : IScanCollector
{
    protected WindowsCollectorBase(IAppLogger? log = null) => Log = log ?? NullLogger.Instance;

    protected IAppLogger Log { get; }

    public abstract string SectionKey { get; }
    public abstract string Title { get; }
    public virtual int Order => 50;
    public virtual bool IncludedInQuickScan => true;

    public Task CollectAsync(DiagnosticSnapshot snapshot, CancellationToken cancellationToken) =>
        Task.Run(() => CollectCore(snapshot, cancellationToken), cancellationToken);

    protected abstract void CollectCore(DiagnosticSnapshot snapshot, CancellationToken cancellationToken);

    protected MetricSection Section(DiagnosticSnapshot snapshot) => snapshot.Section(SectionKey);

    protected static WmiQuery.QueryResult Wmi(string wql, string scope = WmiQuery.DefaultScope, int timeoutMs = 8000) =>
        WmiQuery.Run(wql, scope, timeoutMs);

    protected void MissingFromQuery<T>(MetricSection section, string name, WmiQuery.QueryResult result,
        string source, string method, string unit = "")
    {
        var state = result.Error is not null && result.Error.Contains("access denied", StringComparison.OrdinalIgnoreCase)
            ? AvailabilityState.AccessDenied
            : AvailabilityState.Unavailable;
        section.Missing<T>(name, state, result.Error ?? "the class returned no instances", source, method, unit);
    }
}
