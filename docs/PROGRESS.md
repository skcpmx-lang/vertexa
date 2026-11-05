# PROGRESS

Live state of the MERQO VERTEXA build. This file is updated with every meaningful change and is the
authoritative answer to "what actually works right now". **Only CI evidence counts as verification**:
a feature listed as *implemented* has code and tests, a feature listed as *verified* has a green
GitHub Actions run.

Legend: ✅ verified by a green CI run · 🟡 implemented, not yet verified on Windows · ⬜ planned · 🚫 impossible

_Last update: 2026-11-05_

## 1. Repository and documentation

| Item | State | Evidence |
| --- | --- | --- |
| Planning documents (20) | 🟡 | `README.md`, `docs/*.md` |
| Feature matrix (96 rows, generated) | 🟡 | `docs/feature_matrix.csv`, `scripts/gen_feature_matrix.py` |
| Licence and third-party notices | 🟡 | `LICENSE`, `THIRD-PARTY-NOTICES.md` |
| Application icon set (PNG + ICO, 7 sizes) | 🟡 | `assets/icons/` |
| CI pipeline | 🟡 | `.github/workflows/ci.yml` |

## 2. Platform layers

| Assembly | State | Notes |
| --- | --- | --- |
| `MERQO.VERTEXA.Shared` | 🟡 | `DiagnosticValue<T>`, `AvailabilityState` (11 states), confidence, verification, units |
| `MERQO.VERTEXA.Core` | 🟡 | Models, fallback engine, battery maths, SMART/NVMe interpretation, health scoring, recommendations, benchmarks with safety governor, settings, audio analysis, firmware parsing, logging |
| `MERQO.VERTEXA.Windows` | 🟡 | 14 collectors, NVMe log 0x02 + ATA SMART readers, thermal probe, monitoring, elevation probe |
| `MERQO.VERTEXA.Storage` | 🟡 | SQLite schema/migration, settings repository, diagnostic history |
| `MERQO.VERTEXA.Security` | 🟡 | Redaction service, path guard |
| `MERQO.VERTEXA.Reporting` | 🟡 | Report model/builder, HTML, CSV, JSON, in-house PDF 1.4 writer |
| `MERQO.VERTEXA.Diagnostics` | 🟡 | Scan orchestrator, benchmark service |
| `MERQO.VERTEXA.App` | 🟡 | MVVM kernel, 12 page view-models, 11 page views + shell, dark/light theme, command palette, headless self test |

## 3. Product surface

| Feature | State | Notes |
| --- | --- | --- |
| System / CPU / memory / graphics detection | 🟡 | WMI + SMBIOS type 0/1/2/17 |
| Storage inventory + SMART + NVMe health | 🟡 | Ladder: NVMe log 0x02 → ATA SMART 0xD0 → `MSFT_PhysicalDisk`; method recorded per drive |
| Battery design vs full-charge capacity | 🟡 | `root\wmi` static/full-charged capacity with `Win32_Battery` fallback; invalid ratios rejected |
| Thermal telemetry | 🟡 | ACPI zones + WMI; explicit *Unsupported* when absent |
| Display lab (patterns + guidance) | 🟡 | No automatic pixel detection — operator verifies |
| Keyboard lab | 🟡 | Fn / Ctrl+Alt+Del / media keys marked non-deliverable with reasons |
| Audio lab (tones, sweep, mic capture) | 🟡 | Local only; recording discarded on page exit |
| Camera inventory | 🟡 | Enumeration only; capture requires explicit user action |
| Network / peripherals / drivers inventory | 🟡 | Problem-code interpretation for `ConfigManagerErrorCode` |
| Benchmarks with safety governor | 🟡 | Thermal ceiling, duration cap, watchdog, honest abort reasons |
| Health score + recommendations | 🟡 | Weighted composite, insufficient-evidence state, rule-based recommendations with evidence |
| Reports (PDF/HTML/JSON/CSV, 4 types) | 🟡 | In-house PDF writer |
| Settings persistence | 🟡 | SQLite, validated and clamped |
| Premium dark + light themes | 🟡 | Design-token driven, `DynamicResource` only |

## 4. Verification status

| Gate | State | Evidence |
| --- | --- | --- |
| Unit tests (Linux, `net8.0`) | 🟡 | `tests/MERQO.VERTEXA.UnitTests` |
| Integration tests (Windows, real WMI/P-Invoke) | 🟡 | `tests/MERQO.VERTEXA.IntegrationTests` |
| Solution builds on `windows-latest` | 🟡 | `.github/workflows/ci.yml` |
| Published binary verified (PE x64 GUI, VERSIONINFO, icon) | 🟡 | `scripts/verify-publish.ps1` |
| Executable self test (`--selftest`) | 🟡 | `src/MERQO.VERTEXA.App/Services/SelfTest.cs` |
| Installer + portable ZIP | ⬜ | Release workflow pending |
| Official GitHub Release | ⬜ | Tagged release pending |

**No test result is claimed in this document without a corresponding green Actions run.** Where the state
is 🟡 the code exists and is listed for verification; the Actions tab is the source of truth.

## 5. Known open items

1. First Windows compile pass and its fix-ups (XAML resource names, VM member names) — expected, tracked in
   `docs/KNOWN_LIMITATIONS.md`.
2. Installer (`tools/MerqoVertexa.Installer`) and portable packaging — release phase.
3. Release workflow: tag → installer + ZIP + `SHA256SUMS.txt` + `VERSION.txt` → GitHub Release.
4. Hardware verification checklist completion on physical machines (`docs/HARDWARE_COMPATIBILITY.md`).
