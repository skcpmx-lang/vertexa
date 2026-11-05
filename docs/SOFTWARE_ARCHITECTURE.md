# MERQO VERTEXA — Software Architecture

## 1. Layering

```
┌─────────────────────────────────────────────────────────────────────┐
│ Presentation   MERQO.VERTEXA.App (WPF, MVVM)                        │
│   Shell · Dashboard · 16 diagnostic pages · Reports · Settings      │
│   Design tokens · Charts · Dialogs · Command palette                │
├─────────────────────────────────────────────────────────────────────┤
│ Application    MERQO.VERTEXA.Diagnostics                            │
│   DiagnosticEngine · ScanOrchestrator · MonitoringService           │
│   BenchmarkHarness · SafetyGovernor · ProviderRegistry              │
├─────────────────────────────────────────────────────────────────────┤
│ Domain         MERQO.VERTEXA.Core                                   │
│   Models · HealthScoring · RecommendationEngine · Contracts ·       │
│   DiagnosticValue contract · Settings & history models              │
├─────────────────────────────────────────────────────────────────────┤
│ Integration    MERQO.VERTEXA.Windows                                │
│   WMI/CIM · P/Invoke · SetupAPI · DeviceIoControl (NVMe/ATA) ·      │
│   Registry · Performance counters · Power APIs · EventLog           │
├─────────────────────────────────────────────────────────────────────┤
│ Infrastructure MERQO.VERTEXA.Storage · .Reporting · .Security · .Shared│
│   SQLite + migrations · PDF/HTML/JSON/CSV writers · privilege &     │
│   redaction · logging · result primitives                           │
└─────────────────────────────────────────────────────────────────────┘
```

Dependency direction is strictly downward: `App → Diagnostics → Core → Shared`,
`Windows`/`Storage`/`Reporting`/`Security` implement interfaces declared in `Core`.
`Core` has **no** reference to WPF, WMI or SQLite, which is what makes it unit
testable on Linux CI.

## 2. Result contract (the spine of the product)

```csharp
DiagnosticValue<T> {
    T? Value; string? Unit; AvailabilityState Availability;
    ConfidenceLevel Confidence; VerificationState Verification;
    string Source;          // e.g. "WMI:Win32_Processor"
    string DetectionMethod; // e.g. "CIM query"
    DateTimeOffset CollectedAt;
    string? UnavailableReason; string? Limitation; IReadOnlyList<string> Notes;
}
```

Rules enforced in review:
- `Availability == Available` ⇔ `Value != null`.
- `Confidence` is derived from a documented evidence ladder, never guessed.
- When two providers disagree, `MultiSourceResult` keeps **both** values and flags
  `Disagreement`, it does not pick one silently.

## 3. Provider + fallback pipeline

```
Capability (e.g. "battery.designCapacity")
   └─ ordered ProviderChain: [WMI BatteryStaticData, WMI Win32_Battery,
                             PowerApi, Registry fallback]
        for each provider:
           1. IsAvailable()  – cheap capability probe, cached
           2. Collect(ct)    – with timeout, exceptions captured
           3. Validate()     – range/shape plausibility, freshness
           4. On success → attribute & return;  on failure → record reason, next
   └─ Aggregate → MultiSourceResult (chosen value + alternates + disagreement)
   └─ Still nothing → DiagnosticValue.Unavailable(reason from all attempts)
```

## 4. Threading model

- All providers are `async` and hop to a dedicated worker (`Task.Run`) for blocking
  COM/IO; nothing expensive touches the dispatcher.
- `MonitoringService` runs on a single dedicated sampler loop with per-metric
  intervals (CPU 1 s, sensors 2 s, storage 5 s, network 1 s) and coalesced events.
- Benchmarks run under `SafetyGovernor`, which samples thermals/load each second
  and aborts on threshold breach; cancellation propagates through every worker.
- WPF views bind only to `INotifyPropertyChanged` view-models; UI updates marshalled
  through `Dispatcher`.

## 5. Persistence

SQLite (`%LOCALAPPDATA%\MERQO\VERTEXA\vertexa.db`), WAL, `SchemaVersion` table and
forward-only migrations. Tables: `settings`, `history_runs`, `history_metrics`,
`reports_meta`, `app_log`. Backups copied to `backups/` before migration.

## 6. Security boundaries

- No network code in the 1.0 runtime path (update checking is a stub that only
  opens the release page when the user clicks it).
- No dynamic code execution; the only child processes are Windows built-ins
  (`powershell.exe` for two read-only fallbacks, invoked with an allow-listed
  script body and `CreateNoWindow`, never with user-supplied text).
- Report writers escape all interpolated strings (HTML entity escaping, CSV quoting,
  JSON serialisation).
- Redaction service removes serial numbers/serials-by-default and is applied at the
  model boundary, so a report cannot accidentally leak them.

## 7. Error handling & diagnostics of the app itself

`AppErrorBoundary` installs `DispatcherUnhandledException`, `AppDomain.UnhandledException`
and `TaskScheduler.UnobservedTaskException` handlers; each writes a JSONL log entry
with a correlation id and surfaces a non-fatal toast (or the crash dialog for fatal
cases) — a failing provider never terminates the process.

## 8. Extensibility

Adding a capability = adding a provider class + registering it in
`ProviderRegistry` + a feature-matrix row + unit tests for its normalisation. No page
or report code changes are needed because pages consume `DiagnosticSnapshot` sections.
