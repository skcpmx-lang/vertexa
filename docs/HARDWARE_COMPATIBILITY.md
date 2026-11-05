# MERQO VERTEXA — Hardware Compatibility & Verification Matrix

Legend for *Verification*:
- `CI-AUTO` — automatically verified on the GitHub Windows runner (virtual hardware).
- `GUI-SMOKE` — application launched and screen exercised on the Windows runner.
- `PHYS-REQUIRED` — needs a real machine/component; **not** verified by this project yet.
- `UNSUPPORTED` — Windows does not expose it to user-mode software.

## Detection providers

| Area | Coverage expectation | Verification |
|---|---|---|
| CPU identity, cores, cache, load | Intel & AMD, all Windows 10/11 | CI-AUTO |
| CPU temperature | only if firmware exposes an ACPI thermal zone | PHYS-REQUIRED |
| CPU package power | not exposed to user mode without vendor/privileged access | UNSUPPORTED |
| GPU identity, VRAM, driver | Intel/AMD/NVIDIA via WMI+DXGI | CI-AUTO (virtual adapter) |
| GPU temperature/util | util via PDH (per-LUID); temp only where thermal zone exists | PHYS-REQUIRED |
| Memory modules DDR3/4/5 | SPD data as exposed by firmware | CI-AUTO (virtual) |
| Motherboard/BIOS/firmware mode | SMBIOS + registry | CI-AUTO |
| Secure Boot / TPM | documented sources | CI-AUTO |
| Battery capacities/cycles | laptops only | PHYS-REQUIRED |
| NVMe health log | NVMe drives reachable via `IOCTL_STORAGE_QUERY_PROPERTY` | PHYS-REQUIRED |
| SATA S.M.A.R.T. | ATA pass-through; some USB bridges refuse | PHYS-REQUIRED |
| Drive health status, partitions | WMI + IOCTL | CI-AUTO |
| Drivers/PnP problem codes | setupapi/cfgmgr | CI-AUTO |
| USB/network/Bluetooth inventory | WMI PnP classes | CI-AUTO |
| Sensors (ALS, accelerometer) | CIM sensor classes | PHYS-REQUIRED |
| Display panels, refresh rates | WMI monitor + EnumDisplaySettings | CI-AUTO (virtual), PHYS-REQUIRED for uniformity/bleed |
| Keyboard scan codes / rollover | Win32 keyboard messages | PHYS-REQUIRED |
| Audio output/loopback | WASAPI via `NAudio`-free waveOut/MediaFoundation (in-house) | PHYS-REQUIRED |
| Microphone capture | in-house WaveIn recorder | PHYS-REQUIRED |
| Camera preview | MediaCapture/MediaFoundation | PHYS-REQUIRED |
| Thermal throttling indicators | PDH `% of Maximum Frequency` | CI-AUTO (may be absent) |
| Fan speed | not exposed by Windows on most laptops | UNSUPPORTED (vendor tools only) |

## Operating systems

| OS | Status |
|---|---|
| Windows 11 21H2–25H2 x64 | Supported target |
| Windows 10 1809–22H2 x64 | Supported target |
| Windows 10 < 1809 | Not supported (missing APIs); app refuses with a clear message |
| Windows Server 2022 (CI runner) | Functional for detection; not a supported end-user target |
| Windows on ARM64 / x86 | Not built in 1.0 |
| Linux/macOS | Not supported (WPF); Core/unit tests only |

## Hardware tested by this project

| Item | Where | Result |
|---|---|---|
| Virtual CPU/RAM/board/BIOS/disk/NIC | GitHub `windows-latest` VM | automated detection verified (see `docs/QA_REPORT.md`) |
| Battery, thermal sensor, panel, keyboard, camera, audio, fan | — | **not verified on physical hardware; listed for manual verification** |
