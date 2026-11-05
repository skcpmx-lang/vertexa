# MERQO VERTEXA — Development Roadmap

Phase gates: a phase is closed only when its *completion criteria* are met and
`docs/PROGRESS.md` records the evidence.

| Phase | Scope | Completion criteria |
|---|---|---|
| P0 | Repository & environment audit | Repo/permissions/tooling documented in `docs/PROGRESS.md`; CI proven to run |
| P1 | Planning & architecture | 20 planning documents committed; stack decided with rationale |
| P2 | Foundation | Solution builds warning-clean on Windows CI; DI container, logging, storage, error boundary, app shell, design tokens; unit tests green |
| P3 | Core Windows diagnostics | System/CPU/GPU/RAM/board/BIOS/OS/drivers/devices detected and covered by Windows integration tests |
| P4 | Battery & storage intelligence | Battery engine + storage engine + S.M.A.R.T./NVMe parsing with unit fixtures; safety controls; integration tests |
| P5 | Interactive labs | Display, keyboard, audio, microphone, camera, touch, USB, network, sensors pages functional |
| P6 | Performance & monitoring | CPU/memory/storage/GPU benchmarks, live monitor with tiered polling, `SafetyGovernor` verified |
| P7 | Dashboard & intelligence | Health scoring with evidence, recommendation engine, history, quick actions, command palette |
| P8 | Reports & settings | PDF/HTML/JSON/CSV, 8 report types, preview, settings persistence across restart, migrations |
| P9 | UI/UX finalisation | Tokens applied everywhere, dark+light, responsive at 3 DPI scales, reduced motion, app icon, About page |
| P10 | QA & security review | Unit+integration suites green, vulnerability scan clean, security checklist complete |
| P11 | Release engineering | Installer + portable + checksums + version metadata produced by CI |
| P12 | Executable verification | Published EXE PE-verified (subsystem, icon, VERSIONINFO), launched on Windows runner, exit code 0 |
| P13 | Official release | Tag `v1.0.0`, workflow run green, GitHub Release published with artifacts and checksums |

## Milestones

| M | Gate | Depends on |
|---|---|---|
| M1 | "CI proves a real Windows detection" | P0–P2 |
| M2 | "Full hardware inventory works" | P3 |
| M3 | "Health intelligence works" | P4, P7 |
| M4 | "Every page is functional" | P5, P9 |
| M5 | "Release candidate artifacts exist" | P8, P10–P12 |
| M6 | "Public release published" | P13 |

## Sequencing notes

- `Core` is written before its Windows providers so that the contract is stable and
  the unit tests can run on Linux early.
- Reports depend on the snapshot model, so the snapshot model is finished in P2/P3.
- The installer generator is deliberately last-but-one because it embeds the
  published payload; it must never be able to produce a stale installer.

## Future (post-1.0, not promised here)

WinUI 3 shell option · per-vendor sensor plug-ins (user-supplied, sandboxed) ·
UEFI-level RAM test integration · driver age advisory with curated offline DB ·
French/Chinese localisation.
