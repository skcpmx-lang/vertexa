# MERQO VERTEXA — Release Checklist (v1.0.0)

Evidence column must contain a link/command/artifact reference. Empty evidence = not done.

## Build & test evidence
| # | Item | Evidence |
|---|---|---|
| 1 | Solution restores and builds warning-clean on Windows | CI `windows-build-test` job |
| 2 | Unit tests pass on Linux | CI `unit` job |
| 3 | Unit + integration tests pass on Windows | CI `windows-build-test` job |
| 4 | `dotnet list package --vulnerable` clean | CI log |
| 5 | Windows integration tests show real detected hardware facts | CI log (`docs/QA_REPORT.md`) |
| 6 | Published EXE PE-inspected: subsystem GUI, icon resource present, VERSIONINFO populated | CI `verify-publish` step |

## Artifacts
| # | Item | Evidence |
|---|---|---|
| 7 | `MerqoVertexa.exe` self-contained produced | artifact |
| 8 | Portable ZIP produced | artifact |
| 9 | Installer EXE produced | artifact |
| 10 | `SHA256SUMS.txt` produced and matching | artifact + verification step |
| 11 | Version metadata = tag | CI assertion |

## Application smoke
| # | Item | Evidence |
|---|---|---|
| 12 | App process launches on Windows runner and exits 0 within timeout | CI smoke step |
| 13 | Settings persist across restart | integration test |
| 14 | Report export writes readable PDF/HTML/JSON/CSV | integration test |

## Manual (physical hardware) — NOT automated
| # | Item | Status |
|---|---|---|
| 15 | Battery health on a real laptop | pending user hardware |
| 16 | Display uniformity/backlight lab on a real panel | pending |
| 17 | Keyboard lab on ISO and ANSI laptops | pending |
| 18 | Audio left/right + mic recording on real hardware | pending |
| 19 | Camera preview on a real camera | pending |
| 20 | Thermal sensors on a machine that exposes them | pending |

## Security
| # | Item | Evidence |
|---|---|---|
| 21 | No secrets in source (grep + review) | `docs/SECURITY_AND_PRIVACY.md` |
| 22 | No network calls in runtime path | code review, no HttpClient |
| 23 | Unsigned-binary warning present in release notes and About page | docs + UI |
| 24 | Redaction default on | unit test |
