# MERQO VERTEXA — Known Limitations (honest register)

Every entry states what is impossible, why, and what the product does instead.
Nothing on this list is hidden inside the UI: each maps to a user-visible state.

## Platform limits (not fixable in user-mode software)

| # | Limitation | Why | Product behaviour |
|---|---|---|---|
| L-01 | CPU/GPU/power telemetry (package power, per-core power, VRM current) | RAPL and vendor power telemetry require privileged MSR/driver access | Reports `Unsupported` with explanation; never estimates |
| L-02 | Fan speed | Firmware/EC usually exposes only via vendor driver | `Unsupported` unless an OS-exposed counter exists |
| L-03 | CPU temperature on most modern laptops | ACPI thermal zones often absent/hidden by EC | `Unavailable` + suggestion to use vendor tool |
| L-04 | Automatic pixel-defect detection | Pixels are not observable from user mode | Display lab gives patterns + user-recorded findings, clearly labelled |
| L-05 | Full physical RAM test | Requires boot-time/exclusive access | Process-level stress + integrity counters; labelled as *not* a boot-time RAM test |
| L-06 | Battery cycle count | Only where firmware exposes it | `Unavailable` where absent; design/FCC always attempted |
| L-07 | S.M.A.R.T. behind USB bridges / RAID controllers | Bridges often block ATA pass-through | Falls back to WMI status; explains the bridge limitation |
| L-08 | Fn keys, Windows-key combos, Secure Attention Sequence | Handled by firmware/OS, not delivered to apps | Keyboard lab marks them `NotDeliverable` with reason |
| L-09 | Physical port function (HDMI/USB-C/Thunderbolt) | Requires a device on the other end | Ports listed by controller capability only, with an explicit "physical test required" note |
| L-10 | Activation/licence details | Only coarse, legitimate status | Reported as "Licensed"/"Not licensed" when readable; no key discovery |
| L-11 | Uptime-exact boot time | WMI gives last boot time, subject to fast-startup semantics | Reported with a note about fast startup |
| L-12 | CPU "base" clock | Windows exposes nominal/max, not vendor base | Labelled `nominal/max` precisely |

## Environment limits of this project's verification

| # | Limitation |
|---|---|
| V-01 | Development and automated builds run on a **Linux sandbox + GitHub Windows VMs**. The Windows binary is compiled, PE-inspected and smoke-launched on the runner, but the full GUI has not been exercised interactively by a human on physical hardware as part of automation. |
| V-02 | Battery, thermal, panel, keyboard, camera, audio and sensor paths are **PHYS-REQUIRED** rows; they ship implemented but are unverified on real hardware here. See `docs/HARDWARE_COMPATIBILITY.md`. |
| V-03 | No code-signing certificate: the installer/EXE are unsigned by design (see `docs/BUILD_AND_RELEASE.md` §6). |
| V-04 | Automated UI automation of WPF is not part of CI in 1.0; UI verification is a scripted launch/exit smoke test plus a documented manual checklist. |

## Deliberate product decisions that look like limitations

| # | Decision |
|---|---|
| D-01 | Health score can be `Insufficient evidence` — by design, instead of a flattering number. |
| D-02 | Conflicting provider values are shown side by side rather than reconciled. |
| D-03 | No driver download/installation, even though it would be convenient: security and licensing. |
| D-04 | No cloud report sharing, no accounts. |
