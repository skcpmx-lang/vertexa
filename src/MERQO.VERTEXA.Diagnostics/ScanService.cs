using Merqo.Vertexa.Core.Contracts;
using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Reporting;
using Merqo.Vertexa.Security;
using Merqo.Vertexa.Shared;
using Merqo.Vertexa.Storage;

namespace Merqo.Vertexa.Diagnostics;

/// <summary>
/// The application-layer scan use case: run the collectors, score health, keep history.
/// The UI never talks to a provider directly.
/// </summary>
public sealed class ScanService
{
    private readonly ScanOrchestrator _orchestrator;
    private readonly HistoryRepository _history;
    private readonly ReportBuilder _reportBuilder;
    private readonly JsonReportWriter _jsonWriter;
    private readonly IAppLogger _log;
    private readonly string _appVersion;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ScanService(ScanOrchestrator orchestrator, HistoryRepository history, ReportBuilder reportBuilder,
        IAppLogger log, string appVersion)
    {
        _orchestrator = orchestrator;
        _history = history;
        _reportBuilder = reportBuilder;
        _jsonWriter = new JsonReportWriter();
        _log = log;
        _appVersion = appVersion;
    }

    public DiagnosticSnapshot? Current { get; private set; }
    public bool IsScanning => _gate.CurrentCount == 0;

    public async Task<DiagnosticSnapshot> ScanAsync(ScanScope scope, bool elevated, bool redactSerials,
        IProgress<(string Section, int Done, int Total)>? progress, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var options = new ScanOptions
            {
                Scope = scope,
                Elevated = elevated,
                RedactSerials = redactSerials,
                AppVersion = _appVersion,
            };
            var snapshot = await _orchestrator.RunAsync(options, progress, cancellationToken).ConfigureAwait(false);
            Current = snapshot;
            TryPersist(snapshot);
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void TryPersist(DiagnosticSnapshot snapshot)
    {
        try
        {
            var document = _reportBuilder.Build(snapshot, ReportType.Full, _appVersion);
            _history.Save(snapshot.Id, snapshot.CreatedAt, _appVersion, snapshot.MachineName,
                snapshot.Scope.ToString(), snapshot.Health?.Overall, snapshot.Health?.State.ToString() ?? "Unknown",
                _jsonWriter.Render(document));
            _history.Prune(100);
        }
        catch (Exception ex)
        {
            _log.Warn("scan", "history could not be written: " + ex.Message);
        }
    }
}
