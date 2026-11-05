# MERQO VERTEXA — Diagnostic Fallback Strategy

## 1. Contract

```csharp
public interface IDiagnosticProvider<out TValue>
{
    string Id { get; }               // stable id, used in reports
    string Source { get; }           // human readable, e.g. "WMI:Win32_Processor"
    string DetectionMethod { get; }
    int Priority { get; }            // 0 = most trusted
    ValueTask<ProviderAvailability> ProbeAsync(CancellationToken ct);
    ValueTask<ProviderOutcome<TValue>> CollectAsync(CancellationToken ct);
}
```

`ProviderOutcome<T>` is a closed union: `Success(value, notes)` / `Unavailable(reason)`
/ `Denied(reason)` / `Unsupported(reason)` / `Failed(exceptionSummary)` /
`Timeout(ms)`.

## 2. Decision procedure (applied per capability)

```
attempts ← []
for provider in chain ordered by Priority:
    availability ← ProbeAsync()            # cached for the app session
    if availability ≠ Ready: record; continue
    outcome ← CollectAsync() with 2 000 ms–20 000 ms timeout (per capability)
    if outcome is Success:
        validation ← Validate(value)        # range, shape, freshness, monotonicity
        if validation is Ok: attempts += Accepted(provider, value); 
             if provider is authoritative → stop
        else: attempts += Rejected(provider, validation.Reason); continue
    else: attempts += outcome; continue
return Aggregate(attempts)
```

## 3. Aggregation rules

| Situation | Result |
|---|---|
| One accepted value | `Available` with that provider's attribution |
| Several accepted, numerically consistent (Δ ≤ tolerance) | `Available`, highest-priority value, `Confidence = High`, alternates kept |
| Several accepted, inconsistent | `Available` with `Confidence = Medium`, `Notes` records every candidate and its source; UI shows a "sources disagree" chip. **Never** averaged silently |
| All providers unavailable | `Unavailable` + joined reasons ("WMI class absent; power API returned unknown") |
| Access denied on all | `AccessDenied` + "run elevated for this metric" hint |
| Hardware absent (no battery, no sensor) | `Unsupported`/`RequiresPhysicalHardware` |

## 4. Confidence ladder (evidence quality, not statistics)

| Level | Criteria |
|---|---|
| `High` | Direct OS API or WMI class field, validated, refreshed ≤ 5 s, single unambiguous meaning |
| `Medium` | Derived value (e.g. health % from two capacities), or cross-source agreement within tolerance, or cached 5–60 s |
| `Low` | Aggregated/vendor-obscure interpretation, cached > 60 s, or known saturation (e.g. `AdapterRAM` 4 GB cap) |
| `Unknown` | No accepted provider |

## 5. Failure taxonomy surfaced to the user

`NotSupportedByOs` · `HardwareAbsent` · `DriverDoesNotExposeField` · `AccessDenied` ·
`ProviderTimeout` · `ProviderException` · `ValueRejected` · `RequiresPhysicalTest` ·
`RequiresUserAction`(microphone permission, activity) · `NotTested`.

Every state has: a short UI label, an explanation sentence, and a suggested action
("Run as administrator", "Connect a battery", "Vendor software exposes this
sensor", "This needs a physical inspection").

## 6. Caching & freshness

| Data | TTL | Reason |
|---|---|---|
| Static identity (CPU/board/BIOS) | session | doesn't change |
| Installed memory modules | session | requires reboot to change |
| Battery capacities | 5 min | capacity estimates drift |
| Live rates/utilisation | 1 s | monitor loop |
| Temperatures | 2 s | sensor loop |
| S.M.A.R.T. health | 60 s | device I/O cost |
| Driver inventory | 5 min | expensive enumeration |

## 7. Testing the fallback engine

`FallbackEngineTests` inject fake providers to prove: priority ordering, timeout
handling, validation rejection continuing to next provider, disagreement
preservation, and the all-fail path producing a reason string built from every
attempt. These run on Linux CI (no Windows needed) because the engine is pure.
