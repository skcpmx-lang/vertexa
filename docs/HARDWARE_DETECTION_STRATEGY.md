# MERQO VERTEXA — Hardware Detection Strategy

This document is the engineering map of *where each number comes from*. It is
binding: an implementation that violates this table is a defect.

## 1. Source classes

| Code | Class | Mechanisms |
|---|---|---|
| `WMI` | CIM/WMI repository | `System.Management` → `root\cimv2`, `root\wmi` |
| `WIN32` | Direct Windows API | P/Invoke: `kernel32`, `powrprof`, `setupapi`, `cfgmgr32`, `advapi32`, `pdh` |
| `IOCTL` | Storage device control | `DeviceIoControl` → `IOCTL_STORAGE_QUERY_PROPERTY`, NVMe health log page 0x02, ATA SMART / SMART_RCV_DRIVE_DATA |
| `REG` | Registry | `HKLM\HARDWARE\DESCRIPTION\System\BIOS`, `HKLM\SYSTEM\CurrentControlSet\Control\Nls`, `Microsoft\Windows NT\CurrentVersion` |
| `SMBIOS` | Firmware table | `GetSystemFirmwareTable('RSMB')` parsed (BIOS vendor/version/date, board, memory array) |
| `ELOG` | Event log | `System`, `Microsoft-Windows-Kernel-Power` providers |
| `PDH` | Performance counters | `\Processor Information(_Total)\% Processor Utility`, `\GPU Engine(*)`, `\Memory\*`, `\PhysicalDisk(*)` |
| `USER` | User-recorded | Display-lab findings, keyboard-lab observations, audio listening answers |

## 2. Capability → primary source → fallbacks

### CPU
| Capability | Primary | Fallback | Notes |
|---|---|---|---|
| Name/vendor/arch/cores/threads | `WMI Win32_Processor` | `WIN32 GetLogicalProcessorInformationEx`, `REG HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0` | Registry gives brand string without WMI |
| Base clock | `Win32_Processor.MaxClockSpeed` (nominal) | `SMBIOS` | Windows does not expose true "base" clock in all cases; labelled nominal |
| Current clock | `PDH \Processor Information(_Total)\% Processor Performance` × nominal | `WMI CurrentClockSpeed` (stale, low confidence) | Computed, marked as computed |
| Load | `PDH \Processor Information(_Total)\% Processor Utility` | `WMI LoadPercentage` | |
| Cache | `Win32_Processor` L2/L3 + `GetLogicalProcessorInformation` | — | |
| Virtualisation / SLAT / DEP | `Win32_Processor.VirtualizationFirmwareEnabled` + `IsVM` checks | — | |
| Temperature | `WMI root\wmi MSAcpi_ThermalZoneTemperature` | `Win32_PerfFormattedData_Counters_ThermalZoneInformation` | Frequently absent on modern laptops (EC hides it) → `Unavailable` |
| Package power | `Win32_PerfFormattedData_Counters_ProcessorInformation` (no RAPL in user mode) | — | Usually `Unsupported`; documented, never invented |

### GPU
| Capability | Primary | Fallback |
|---|---|---|
| Adapter identity | `WMI Win32_VideoController` | `REG HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968…}`, DXGI `EnumAdapters` |
| Dedicated VRAM / shared | `Win32_VideoController.AdapterRAM` (may saturate at 4 GB) + DXGI `DedicatedVideoMemory` | DXGI wins when both exist; disagreement recorded |
| Driver version/date | `Win32_PnPSignedDriver` | Registry `DriverVersion`/`DriverDate` |
| Utilisation | `PDH \GPU Engine(*)\Utilization Percentage` (summed per LUID) | — |
| Temperature | `Win32_PerfFormattedData_Counters_ThermalZoneInformation` when exposed | — |

> Known limitation: the NVIDIA/AMD/Intel temperature and power telemetry channels
> are vendor-SDK or privileged; VERTEXA reports GPU temperature only where Windows
> exposes a thermal zone, otherwise `Unavailable`.

### Memory
`Win32_PhysicalMemory` (per-module), `Win32_PhysicalMemoryArray` (slots),
`Win32_OperatingSystem` (total/available), `GlobalMemoryStatusEx` (live), PDH
`\Memory\Available MBytes`, `\Memory\Committed Bytes`.

### Battery
`root\wmi` → `BatteryStaticData` (design capacity, chemistry, manufacture date,
serial), `BatteryFullChargedCapacity`, `BatteryStatus` (voltage, rate, remaining,
charge/discharge state, cycle count), `Win32_Battery` (composite), plus
`GetSystemPowerStatus`. Capacity maths only when `DesignCapacity > 0` and
`FullChargedCapacity > 0` and `FullCharged ≤ DesignCapacity × 1.25` (plausibility).

### Storage
`Win32_DiskDrive` / `MSFT_PhysicalDisk` (bus type, media type, capacity),
`IORCTL` → NVMe SMART/Health log page for NVMe, ATA SMART for SATA HDD/SSD
(`PredictFailure` from `Win32_DiskDrive.Status` + `MSStorageDriver_FailurePredictStatus`),
`MSFT_StorageReliabilityCounter` for NVMe counters.
`Win32_LogicalDisk`/`MSFT_Partition`/`Volume` for partitions and free space.

### Drivers & devices
`Win32_PnPEntity`, `Win32_PnPSignedDriver`, `Win32_SystemDriver`, plus
`CM_Get_DevNode_Status` (PROBLEM_CODE) for authoritative status.

### Networking / Bluetooth / USB / sensors
`Win32_NetworkAdapter` + `Win32_NetworkAdapterConfiguration`,
`MSFT_NetAdapter` for link speed, `Win32_PnPEntity` PNPClass filters for USB,
Bluetooth, Camera (Image), Sensors, `Win32_Battery` for HID battery.

### Windows / firmware
Registry `CurrentVersion` + `WMI Win32_OperatingSystem` + `WIN32 RtlGetVersion`
(via `ntdll`) for the true build; `GetFirmwareType` for UEFI/legacy;
`Confirm-SecureBootUEFI` equivalent via registry/CIM `Win32_DeviceGuard`;
TPM via `Win32_Tpm` (root\cimv2\security\microsofttpm).

## 3. Units and normalisation

- All clocks stored in MHz (`int`), capacities in bytes (`ulong`), temperatures in
  °C (double, source-native, never converted from a guess), percentages in 0–100
  doubles, time in `TimeSpan`/`DateTimeOffset` with UTC offset.
- Ranges are validated per field (`0 < MHz < 10 000`, `0 ≤ % ≤ 100`,
  `−40 ≤ °C ≤ 150`); out-of-range values are quarantined as `Invalid` with the raw
  value preserved in `Notes`, and the fallback chain continues.

## 4. Anti-hallucination controls

1. Fabricated defaults are impossible: `DiagnosticValue` has no default value path;
   construction requires an explicit availability state.
2. Windows VM/CI environments are detected (`Win32_ComputerSystem.Model` contains
   "Virtual", or hypervisor present) and flagged: battery/keyboard/display/fan
   results are then explicitly `RequiresPhysicalHardware`.
3.
   The CI integration tests assert only *shape and provenance*, never a fixed
   hardware value, so a test can never pass by reason of a hard-coded constant.
