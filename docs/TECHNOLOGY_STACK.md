# MERQO VERTEXA — Technology Stack Evaluation & Decision

## 1. Evaluation criteria

Real Windows hardware access · robustness for long-lived desktop products ·
UI fidelity at high DPI · footprint · dependency/licensing risk · testability ·
reproducibility of build on GitHub Actions · security surface.

## 2. Candidates considered

| # | Stack | Hardware access | UI fidelity | Deps risk | Verdict |
|---|---|---|---|---|---|
| A | **C# / .NET 8 + WPF + native Win32/WMI interop** | Excellent: `System.Management`, `SetupAPI`, `DeviceIoControl`, `P/Invoke` | Very high, GPU-rendered, DPI-aware, mature | Low: first-party Microsoft packages | **Selected** |
| B | WinUI 3 / Windows App SDK | Equal to A | Highest modern look | Medium-high: Windows App SDK runtime deployment, packaged/unpackaged complexity, XAML islands, larger installer | Rejected for 1.0 (revisit in 2.x) |
| C | C++ / Qt | Best possible | High | High: licensing (LGPL/commercial), toolchain weight | Rejected |
| D | Rust / Tauri | Good via `windows` crate | High (web UI) | High: WebView2 runtime dependency, two-language build, weaker WMI ergonomics | Rejected |
| E | TypeScript / Electron | Poor: everything through PowerShell/CIM text scraping | High | High: ~150 MB footprint, mandatory Chromium CVEs, IPC attack surface | Rejected: contradicts "real, typed, native" requirement |
| F | Avalonia | Good (same BCL interop) | High | Medium | Rejected: no Windows-specific advantage over WPF |

## 3. Selected stack

| Layer | Technology | Rationale |
|---|---|---|
| Runtime | .NET 8 (`net8.0-windows`), self-contained single-file publish for `win-x64` | LTS, no runtime prerequisite on target machines |
| UI | WPF (`UseWPF`) with MVVM | Stable, hardware-accelerated, full control of theming via resource dictionaries, no external UI package |
| MVVM plumbing | In-house minimal (`ObservableObject`, `RelayCommand`, `AsyncRelayCommand`) | Avoids a third-party dependency for ~120 lines of code |
| Hardware | `System.Management` (WMI/CIM), `Microsoft.Win32.Registry`, P/Invoke `kernel32`/`powrprof`/`setupapi`/`cfgmgr32`, `DeviceIoControl` for NVMe/ATA health | First-party or OS-API level, no scraping of console output for primary paths |
| Diagnostics architecture | Provider-based, in-house | Enables the mandatory multi-method fallback engine |
| Persistence | `Microsoft.Data.Sqlite` (ADO.NET, bundled native lib) | Real SQL, migrations, transactional history |
| Reporting | In-house writers: HTML, JSON (System.Text.Json), CSV, and a **hand-written PDF writer** (PDF 1.4, base-14 fonts) | Guarantees deterministic PDF output with zero third-party PDF dependency/licence surface |
| Charts | In-house WPF `FrameworkElement` renderers (sparkline, gauge, bar) | No dependency; full control of DPI and reduced-motion behaviour |
| Logging | In-house structured JSONL logger with rotation | No dependency, privacy-scoped |
| Testing | xUnit + `dotnet test` | Industry standard, runs on Linux (unit) and Windows (integration) |
| CI/CD | GitHub Actions, `windows-latest` + `ubuntu-latest` | Real Windows VM for build, tests and packaging |
| Packaging | `dotnet publish` + portable ZIP, and a self-contained **unpacked installer EXE** produced by an in-repo C# installer generator | No Inno Setup/NSIS/wine dependency; fully reproducible and inspectable |

## 4. Dependency inventory (must stay this small)

| Package | Version | Licence | Purpose | Reviewed |
|---|---|---|---|---|
| System.Management | 8.0.0 | MIT | WMI/CIM | yes |
| Microsoft.Data.Sqlite | 8.0.11 | MIT | Local DB | yes |
| xunit / xunit.runner.visualstudio / Microsoft.NET.Test.Sdk | 2.x / 2.8 / 17.x | Apache-2.0 / MIT | Tests | yes |

No package with a copyleft network clause, no telemetry-bearing SDK, no native
driver components.

## 5. Explicitly rejected patterns

- Screen-scraping `wmic`/`systeminfo` text output as a primary provider.
- Installing a kernel driver to reach S.M.A.R.T. (driver-signing + security risk).
- Any WebView-based UI (blurs trust boundary, larger attack surface).
- Third-party PDF/UI/chart libraries where ~300 lines of in-house code suffices.
