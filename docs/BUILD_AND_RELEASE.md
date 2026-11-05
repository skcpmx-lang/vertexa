# MERQO VERTEXA — Build & Release Engineering

## 1. Local build (Windows developer machine)

```powershell
git clone https://github.com/skcpmx-lang/vertexa.git
cd vertexa
dotnet restore MERQO.VERTEXA.sln
dotnet build   MERQO.VERTEXA.sln -c Release
dotnet test    tests/MERQO.VERTEXA.UnitTests/MERQO.VERTEXA.UnitTests.csproj
dotnet test    tests/MERQO.VERTEXA.IntegrationTests/MERQO.VERTEXA.IntegrationTests.csproj
dotnet run --project src/MERQO.VERTEXA.App
```

Requirements: .NET 8 SDK (8.0.4xx or later), Windows 10 1809+ / Windows 11 x64.

## 2. Publish

```powershell
dotnet publish src/MERQO.VERTEXA.App/MERQO.VERTEXA.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none `
  -o publish/win-x64
```

Output: one `MerqoVertexa.exe` (no .NET runtime prerequisite) plus licence/notice
files. Self-contained was chosen over framework-dependent because the target
audience (used-laptop buyers, technicians) frequently meets machines without the
desktop runtime, and the diagnostics product must not fail on a healthy PC.

## 3. Packaging pipeline (`scripts/package.ps1`, run in CI on Windows)

1. `dotnet publish` (above) → `publish/win-x64`.
2. Portable package: `MERQO-VERTEXA-<ver>-win-x64-portable.zip` containing the
   published files + `README-FIRST.txt` + `LICENSE` + `THIRD-PARTY-NOTICES.md`.
3. Installer: `MerqoVertexa-Setup-<ver>-win-x64.exe` — a self-contained installer
   produced by our own `tools/MerqoVertexa.Installer` (C#, single file) which
   embeds the portable ZIP, extracts to
   `%LOCALAPPDATA%\Programs\MERQO VERTEXA`, creates Start-menu + optional desktop
   shortcuts, registers an uninstall entry (HKCU, no admin needed), can be launched
   silently with `/S`, and uninstalls with `/uninstall`.
   Rationale: keeps the pipeline reproducible with no external toolchain (Inno/NSIS),
   needs no code-signing certificate, and installs per-user without UAC.
4. `SHA256SUMS.txt` over every artifact.
5. `VERSION.txt` (version, commit SHA, build timestamp, runner image).

## 4. Versioning

Semantic versioning, single source of truth `Directory.Build.props`
(`<Version>`), overridable at build time with `-p:Version=…`. Tags: `v1.0.0`.
The release pipeline asserts that the tag equals `v$(Version)` and fails otherwise.

## 5. GitHub Actions

`.github/workflows/ci.yml` — push/PR: ubuntu unit job + windows build/test job,
uploads `MERQO-VERTEXA-win-x64-<sha>` artifacts.
`.github/workflows/release.yml` — on `v*` tag (and `workflow_dispatch`):
build → test → package → checksums → create GitHub Release with notes from
`docs/RELEASE_NOTES_<version>.md` → attach installer, portable ZIP, SHA256SUMS,
VERSION.txt.

## 6. Signing policy

No paid code-signing certificate is used. Binaries are **unsigned** and will show
"Unknown publisher" and possibly a SmartScreen prompt on first run. This is stated
in the release notes, README and the About page. SHA-256 hashes are published so a
user can verify they downloaded the artifact this pipeline produced. The project
never instructs anyone to disable SmartScreen, Defender or UAC.

## 7. Rollback

Releases are immutable; a defective release is superseded by a patch version and
the GitHub Release is marked "pre-release"/"superseded" with a note. No force-push
over tags.
