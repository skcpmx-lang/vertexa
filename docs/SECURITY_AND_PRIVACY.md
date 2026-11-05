# MERQO VERTEXA — Security & Privacy

## 1. Privacy position (product-level)

| Principle | Implementation |
|---|---|
| Local-only | The 1.0 runtime performs **no network requests**. There is no HTTP client in the shipping code path. |
| No account, no telemetry | No analytics SDK, no identifiers, no crash upload. Logs stay in `%LOCALAPPDATA%`. |
| No cloud | Reports are written to user-chosen local paths. Nothing is uploaded. |
| Data minimisation | Only hardware/OS facts needed for diagnosis are read. No documents, browser data, credentials, clipboard, keystrokes outside the keyboard lab. |
| Serials redacted by default | `RedactionService` masks serials/UUIDs/MAC addresses in reports unless the user opts in. |
| Microphone/camera consent | Capture is user-initiated per session, never automatic, never persisted without an explicit "keep recording" action. |
| Local deletion | Settings → Data → "Delete history", "Delete logs", "Delete recordings" remove rows/files. |

## 2. Threat model

| # | Threat | Control |
|---|---|---|
| T1 | Command injection via provider fallbacks | No user text ever reaches a process argument list; the single PowerShell fallback uses a constant script string and `ProcessStartInfo.ArgumentList`; `UseShellExecute=false`; no `cmd /c`. |
| T2 | Malicious/oversized inputs from WMI strings | All external strings are length-capped, control-char stripped, and escaped at output (HTML entity, CSV quote, JSON serialiser, PDF string escape). |
| T3 | Path traversal on export/save | Export paths validated against a canonicalised absolute path, must remain under a chosen root, reserved names rejected. |
| T4 | Privilege abuse | App runs as invoker by default; elevation is per-operation with explicit explanation; no `runas` of arbitrary binaries; no long-lived elevated shell. |
| T5 | Insecure temp files | Benchmark files are created with `FileOptions.WriteThrough` in an app-owned subfolder with a GUID name, verified by directory prefix before deletion. |
| T6 | DLL/plugin hijack | No plugin loading, no `Assembly.LoadFrom`, no reflection over external paths. `DllImport` targets are system DLLs only, resolved from `System32` by default search order. |
| T7 | Local data disclosure | DB and logs under the user's roaming/local profile ACL; no secrets stored (there are none); settings file validated on load. |
| T8 | Supply chain | Exactly three direct NuGet packages, pinned versions, MIT/Apache-2.0, reviewed at `docs/TECHNOLOGY_STACK.md` §4; CI runs `dotnet list package --vulnerable`. |
| T9 | Unsigned binary reputation | Release is unsigned (no paid cert); release notes and app About page state this plainly and publish SHA-256 sums. No instruction to disable SmartScreen/Defender. |
| T10 | Stress-test harm | `SafetyGovernor` with conservative ceilings, user-controlled duration, always-available Stop, and refusal to start if thermal telemetry is unsafe-unknown above 85 °C-by-sensor. |

## 3. Elevation model

- Capabilities tagged `RequiresElevation = true` (e.g. some ATA SMART paths, TPM
  details, certain `Win32_*` classes under hardened policy) return `AccessDenied`
  with an explanation and an optional "Elevate for this scan" action.
- The main process, the monitoring loop and the whole UI run unelevated.

## 4. Secure coding checklist (enforced in review)

- [x] `nullable enable`, `TreatWarningsAsErrors` for the domains.
- [x] No `Process.Start` with interpolated user input.
- [x] No `eval`-equivalents (no dynamic C#/PowerShell string building).
- [x] All `P/Invoke` marshalling explicit (`CharSet`, `SetLastError`, sizes).
- [x] All COM/WMI objects disposed (`ManagementObjectSearcher/Collection/BaseObject`).
- [x] No credentials, keys or tokens in source (there are no secrets in the product).
- [x] Exceptions never swallow silent failures — they become diagnostic reasons.

## 5. Vulnerability & dependency process

`dotnet list package --vulnerable --include-transitive` in CI; a HIGH/CRITICAL
finding blocks release. Dependency additions require an entry in
`docs/TECHNOLOGY_STACK.md` §4 with licence and rationale.

## 6. Reporting a security issue

Email merqoonline@gmail.com with steps to reproduce and affected version. Do not
open a public issue for an unpatched vulnerability.
