# MERQO VERTEXA — Architecture Decision Records

Format: `ADR-nn: decision — context — consequence`.

**ADR-01: .NET 8 + WPF over WinUI 3 / Electron / Qt.**
Context: need deep native Windows access, high-DPI fidelity, small dependency
surface, reproducible CI. Consequence: Windows-only; some "modern" visual effects
must be hand-rolled; in exchange the app has 3 direct NuGet packages and no runtime
prerequisite (self-contained).

**ADR-02: Provider-based fallback engine as the central abstraction.**
Context: Windows exposes the same fact through several APIs that differ per
vendor/firmware. Consequence: every metric carries provenance; more code per metric;
enables the "sources disagree" honesty requirement.

**ADR-03: `DiagnosticValue<T>` with mandatory availability state.**
Context: a `double` default of 0 looks like a real measurement. Consequence: nullability
and state are unrepresentable-error-proof; UI cannot render a value without its source.

**ADR-04: In-house PDF writer.**
Context: PDF libraries bring large dependency trees and licences; our reports are
text/table/rect based. Consequence: ~600 lines of PDF 1.4 writer (WinAnsi base-14
fonts), fully deterministic, trivially unit-testable, no licence risk.

**ADR-05: In-house MVVM/chart primitives instead of CommunityToolkit/MahApps/LiveCharts.**
Context: dependency and licence surface vs. ~400 lines of code. Consequence: full
control of theming and DPI; more code we own and test.

**ADR-06: Microsoft.Data.Sqlite for persistence.**
Context: history/queries/settings need transactions and migrations; JSON files do not
scale. Consequence: one native dependency, bundled, verified on CI; documented
migration path.

**ADR-07: Self-contained single-file publish.**
Context: target machines may lack the desktop runtime. Consequence: ~70–90 MB EXE;
accepted for a diagnostic tool, and it removes an entire class of support failures.

**ADR-08: Per-user installer, generated in-repo (no Inno/NSIS/wine).**
Context: CI must build the installer on a stock Windows runner and the app must
install without admin. Consequence: our own C# installer (~500 lines) embedding the
payload ZIP, HKCU uninstall entry, silent mode; simpler and fully inspectable.

**ADR-09: Benchmarks are deterministic, relative and labelled.**
Context: absolute scores are meaningless across CPUs and invite misleading marketing.
Consequence: CPU benchmark reports ops/sec plus a reference-scaled index derived from a
documented baseline table with the baseline hardware *named*; never a bare "score".

**ADR-10: No automatic update download.**
Context: unsigned binaries + updates = supply-chain risk. Consequence: "Check for
updates" only opens the GitHub releases page in the user's browser, with consent.

**ADR-11: Redaction on by default.**
Context: reports get shared when buying/selling used hardware; serials enable fraud.
Consequence: serials, UUIDs and MACs masked unless the user opts in.

**ADR-12: Virtual-machine detection changes expectations, not results.**
Context: CI and some user machines are VMs, where battery/fan/thermal simply do not
exist. Consequence: results are `RequiresPhysicalHardware`/`Unsupported` with reasons,
never a lowered bar that fakes success.
