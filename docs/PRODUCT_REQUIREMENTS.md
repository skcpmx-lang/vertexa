# MERQO VERTEXA — Product Requirements Specification

| Field | Value |
|---|---|
| Product | MERQO VERTEXA |
| Brand | MERQO |
| Category | Advanced PC & Laptop Diagnostics Software |
| Founder | Shohan Khan |
| Contact | merqoonline@gmail.com |
| Platforms | Windows 10 (1809+) and Windows 11, x64 |
| Distribution | GitHub Releases: Windows x64 installer + portable package |
| Version baseline | 1.0.0 |
| Status of this document | Authoritative functional requirements (baseline v1.0) |

## 1. Product statement

MERQO VERTEXA is a local, privacy-first Windows desktop application that
inspects real hardware and operating-system state, runs interactive and
performance tests, explains its findings in plain language and exports
professional diagnostic reports.

**Promise:** One software. Complete PC intelligence. Ultimate hardware diagnostics.

## 2. Non-negotiable product rules

| ID | Rule |
|---|---|
| R-01 | No fabricated hardware data, health values, benchmarks, temperatures, S.M.A.R.T. attributes or reports. |
| R-02 | Every measurement carries value, unit, source, detection method, timestamp, availability state, verification state and confidence. |
| R-03 | Missing data yields an explicit state (`Unavailable…`, `AccessDenied`, `Unsupported`, `RequiresHardware`, `NotTested`), never a substitute value. |
| R-04 | No destructive default action. No formatting, no deleting user files, no driver installs, no firmware flashing, no security-software tampering. |
| R-05 | No silent feature removal: unsupported capabilities are implemented as far as Windows allows, surfaced in the UI, and documented. |
| R-06 | Core diagnostic capability works offline with no account, no telemetry, no paid API. |
| R-07 | No secrets in source. No command injection. Validated, allow-listed process invocation only. |
| R-08 | No requirement for administrator rights for read-only inspection; elevation is optional, explained and per-capability. |

## 3. Users

Everyday PC owners · used-laptop/desktop buyers · repair and field technicians · IT support · gamers · workstation owners · hardware enthusiasts.

## 4. Functional requirements (summary by area)

### 4.1 Hardware inventory
FR-INV-01 CPU: vendor, model, architecture, cores, threads, base/current/max clock,
cache, instruction sets, virtualisation, current load. Temp/power only with a real sensor.
FR-INV-02 GPU: all adapters, model, vendor, device/pnp IDs, driver version/date,
dedicated VRAM vs shared memory, API support, display outputs.
FR-INV-03 RAM: total/available/used, module list with capacity, speed, configured
speed, form factor, type (DDR3/4/5 where exposed), slots and occupied slots.
FR-INV-04 Motherboard/BIOS: vendor, product, version, serial (redacted by
default), BIOS vendor/version/date, firmware mode (UEFI/legacy), TPM, Secure Boot.
FR-INV-05 Storage inventory: model, firmware, bus/interface, media type, capacity,
partition/file-system usage.
FR-INV-06 System: Windows edition/version/build/arch, install date, activation as
exposed, hostname, manufacturer/model, uptime, boot time, time zone.

### 4.2 Battery intelligence
FR-BAT-01 Design capacity, full-charge capacity, current charge, voltage, rate.
FR-BAT-02 Cycle count, manufacture date, chemistry, serial (redacted).
FR-BAT-03 Health % = FCC / design × 100 and wear % = (design − FCC)/design × 100,
computed **only** when both capacities are valid and non-zero.
FR-BAT-04 Runtime estimate labelled as an estimate derived from observed discharge.
FR-BAT-05 Charging/discharge event history from Windows kernel power events when
readable; explicit unavailable state otherwise.
FR-BAT-06 Multiple acquisition paths (WMI `BatteryStatus`, `Win32_Battery`,
power API `GetSystemPowerStatus`, System power status via `GetSystemPowerStatus`),
cross-checked and disagreement preserved.

### 4.3 Storage intelligence
FR-STO-01 S.M.A.R.T. status incl. `PredictFailure`, NVMe health log
(temperature, available spare, percentage used, data units read/written, media
errors, unsafe shutdowns, critical warning).
FR-STO-02 ATA attribute table with manufacturer-aware interpretation notes.
FR-STO-03 Separate reporting of reliability health, wear, and performance health.
FR-STO-04 Non-destructive performance benchmarks (sequential/random, read/write,
IOPS, latency) that create and delete only their own temp files.
FR-STO-05 Critical-warning drives produce an immediate data-backup recommendation.

### 4.4 Interactive hardware tests
FR-TST-01 Display lab: 10 test patterns, full-screen, multi-monitor selection,
keyboard driven, user-recorded findings; states clearly that automatic pixel-defect
detection is not possible by ordinary software.
FR-TST-02 Keyboard lab: live capture of letters, digits, function, navigation,
modifier, numpad, lock-key and media/volume/brightness keys where the OS delivers
them; ANSI/ISO/JIS and multiple layout maps; per-key tested/untested tracking;
rollover/ghosting observation; privacy-scoped capture that is active only while the
lab is open.
FR-TST-03 Audio lab: locally generated tones, left/right/stereo/mono/sweep,
sample-rate sweep, per-device output selection; microphone capture with live level
meter, peak/RMS analysis, local playback, and explicit user-triggered recording.
FR-TST-04 Camera lab: enumerate capture devices, live preview, resolution/frame
rate as exposed, snapshot inspection, device selection.
FR-TST-05 Peripherals: USB tree with VID/PID/class, HID/power/battery where exposed,
network adapters (Wi-Fi/Ethernet/Bluetooth) and link state, sensors via CIM.

### 4.5 Performance & stability
FR-PRF-01 CPU single-core/multi-core benchmark with a defined, documented workload.
FR-PRF-02 Memory bandwidth/latency benchmark.
FR-PRF-03 GPU render benchmark (hardware-accelerated path with CPU fallback).
FR-PRF-04 Storage throughput/IOPS benchmark.
FR-PRF-05 Soak/stability test with duration control and safety governor.
FR-PRF-06 Live monitoring: CPU load/clocks, memory, disk, network, battery,
temperatures where sensors exist; each tile states its source; no synthetic tiles.

### 4.6 Drivers & devices
FR-DRV-01 Enumerate PnP devices with class, vendor, driver version/date/provider,
problem code, status.
FR-DRV-02 Flag error-flagged, disabled and unknown devices with human explanations.
FR-DRV-03 Recommend only official vendor/Windows Update channels; never download or
install automatically.

### 4.7 Intelligence
FR-INT-01 Transparent health score with explicit inputs, excluded inputs and an
insufficient-evidence state.
FR-INT-02 Rule-based recommendation engine; each recommendation carries evidence,
severity, confidence and suggested action.
FR-INT-03 Diagnostic history with timestamps and deltas between runs.

### 4.8 Reports
FR-REP-01 Report types: Full, Buyer Inspection, Technician, Battery, Storage,
Performance, Driver, and single-test.
FR-REP-02 Formats: PDF, HTML, JSON, CSV. Reports embed real data, sources, states,
limitations and app/version metadata. Preview before export.
FR-REP-03 Serial-number redaction option enabled by default.

### 4.9 Application
FR-APP-01 Premium dark/light themes, design-token driven, responsive at
100/125/150 % scaling.
FR-APP-02 Persistent settings; SQLite storage with schema migrations; history.
FR-APP-03 Global command palette, keyboard shortcuts, navigation history.
FR-APP-04 About page with version, build, brand, founder, contact, third-party notices.
FR-APP-05 Privacy centre explaining exactly what is read, what never leaves the machine.

## 5. Non-functional requirements

NFR-01 Cold start to interactive shell under 3 s on a 4-core SSD machine.
NFR-02 Idle CPU < 2 % with monitoring paused; < 5 % with live monitoring.
NFR-03 No UI thread blocking; all providers async with cancellation and timeouts.
NFR-04 Any WMI query ≥ 100 ms cached with a documented TTL.
NFR-05 All public provider results schema-stable; report schemas versioned.
NFR-06 Zero high-severity SCA findings at release; dependencies reviewed.
NFR-07 Deterministic build: `dotnet build -c Release` from a clean clone via CI.
NFR-08 Graceful degradation: a failing provider degrades a single metric, never the app.

## 6. Out of scope for 1.0

- Boot-time/UEFI-level memory or drive self-tests (requires firmware tooling).
- Automatic optical pixel-defect detection (not observable from user mode).
- Fan RPM and VRM power on hardware without an exposed sensor.
- Driver download/install automation.
- Cloud sync, accounts, telemetry.
