# MERQO VERTEXA — Testing Strategy

## 1. Test pyramid

| Level | Project | Runner | Count (target) | Purpose |
|---|---|---|---|---|
| Unit | `tests/MERQO.VERTEXA.UnitTests` | `ubuntu-latest` + `windows-latest` | 120+ | Pure logic: contract invariants, capacity maths, scoring, confidence ladder, recommendation rules, parsing of registry/SMBIOS/SMART blobs, HTML/CSV/JSON/PDF writers, settings validation, fallback engine |
| Integration | `tests/MERQO.VERTEXA.IntegrationTests` | `windows-latest` **only** | 40+ | Real OS: WMI providers, P/Invoke, SMBIOS table, device enumeration, SQLite round-trip, report export to disk |
| Functional/UI | manual + scripted UI harness | Windows | checklist | Navigation, buttons, dialogs, full-screen labs, resize/DPI |

Integration tests are `[Trait("Category","Windows")]` and are skipped
(`SkippableFact`-style guard, plain `if (!OperatingSystem.IsWindows()) return;`
pattern) on Linux, so the unit suite stays green on any dev machine.

## 2. What is asserted — and what is deliberately never asserted

Asserted (shape/provenance/behaviour):
- Contract invariants: `Available ⇔ Value != null`; every available value has a
  non-empty `Source` and `DetectionMethod`; timestamps are UTC and monotonic.
- Fallback semantics with fake providers (ordering, timeout, rejection, disagreement,
  all-fail reason aggregation).
- Battery maths: health/wear only with valid inputs (including the "FCC > design"
  plausibility guard) and correct rounding; explicit unavailable otherwise.
- SMART/NVMe parsing from captured byte arrays (fixture blobs), including
  endianness and unit conversion.
- Health score: excluded inputs, insufficient-evidence state, no 100 %-by-default.
- Report writers: valid HTML structure, RFC-4180 CSV quoting, JSON round-trip,
  PDF byte-level header/xref/`%%EOF` and text-extraction of key fields.
- Security: redaction applied, HTML/CSV escaping of hostile strings.

Never asserted: a *specific* hardware value (CPU model, capacity, temperature). A
test must never be able to pass because of a constant baked into the code.

## 3. CI matrix

| Job | Runner | Steps |
|---|---|---|
| `unit` | ubuntu-latest | restore → build → `dotnet test` (unit) |
| `windows-build-test` | windows-latest | restore → build (Release) → unit + integration tests → vulnerability scan → publish self-contained win-x64 → verify PE + icon + VERSIONINFO → upload artifacts |
| `package` | windows-latest (tag only) | build installer EXE + portable ZIP → SHA-256 → GitHub Release |

## 4. Virtual-machine caveat (documented, not hidden)

GitHub's `windows-latest` runner is a **virtual machine**. It has real WMI, real
SMBIOS, a virtual disk and a virtual NIC, but **no battery, no physical keyboard,
no display panel, no camera, no fan, no thermal sensors**. Therefore:
- Detection providers are genuinely exercised (data is real, just virtual hardware).
- Battery, display, keyboard, audio loopback, camera and thermal features are
  marked `RequiresPhysicalHardware` in those environments by design, and stay on
  the manual verification list (`docs/HARDWARE_COMPATIBILITY.md`).
No claim of physical-hardware validation is made anywhere in this repository.

## 5. Manual verification checklist (per release)

See `docs/RELEASE_CHECKLIST.md`. Results are recorded with the machine model,
Windows build and date. A row is only ticked with the evidence noted beside it.

## 6. Regression policy

Every fixed defect gets a failing test first (where the logic is pure) and the test
name references the defect id. Release candidate requires: unit 100 % pass,
integration 100 % pass on Windows, no `warning`s from the build, no HIGH/CRITICAL
vulnerable packages, and the manual checklist signed off.
