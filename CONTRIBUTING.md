# Contributing to MERQO VERTEXA

MERQO VERTEXA is a proprietary product (see `LICENSE`). The repository is
published for transparency and reproducible builds. External code
contributions are accepted only by prior written agreement with the copyright
holder (merqoonline@gmail.com).

## For maintainers and reviewers

### Ground rules
1. **Never fabricate a measurement.** Every value shown in the UI or written to
   a report must trace to a named provider (`src/MERQO.VERTEXA.Windows`) and be
   carried through the `DiagnosticValue<T>` contract, which requires a source,
   a detection method, a timestamp, an availability state and a confidence
   level. Code review rejects hard-coded hardware values.
2. **Unavailable is a valid result.** Returning `AvailabilityState.Unavailable`
   with a reason is always preferred over an estimate or a default.
3. **No destructive operations.** Storage benchmarks only write temp files they
   created, in a user-chosen directory, and delete them afterwards.
4. **Least privilege.** Never require elevation for read-only inspection.
5. **Safety first in stress tests.** Every load test must honour cancellation,
   thermal ceilings and the `SafetyGovernor`.

### Workflow
- Branch from `main`; use `feat:`, `fix:`, `test:`, `docs:`, `build:`, `chore:`
  commit prefixes.
- `dotnet build` must be warning-clean for the analyzer set in
  `Directory.Build.props`.
- `dotnet test tests/MERQO.VERTEXA.UnitTests` must pass on Linux and Windows.
- Any change to a provider needs a corresponding unit test for its parsing /
  normalization logic (pure functions) and, where the runner exposes the
  interface, an integration test.
- Update `docs/FEATURE_MATRIX.md` and `docs/PROGRESS.md` in the same PR.

### Code style
- Nullable reference types enabled everywhere; no `!` suppressions without a
  comment explaining the invariant.
- Async all the way down for I/O; pass `CancellationToken` through every
  provider call.
- WMI/CIM queries are batched and cached; never poll an expensive class on a UI
  timer.
- Dispose COM/WMI objects and P/Invoke handles with `using`.
