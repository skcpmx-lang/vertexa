<#
  Produces the two release artifacts from a clean checkout:
    dist/MerqoVertexa-Setup-<version>.exe        per-user installer (payload embedded, hash-verified)
    dist/MerqoVertexa-<version>-portable-x64.zip portable package
    dist/SHA256SUMS.txt, dist/VERSION.txt        integrity data published with the release
#>
param(
    [string]$Version = '1.0.0',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$dist = Join-Path $repo 'dist'
$appOut = Join-Path $dist 'app'
$portable = Join-Path $dist 'portable'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist, $appOut, $portable -Force | Out-Null

Write-Host "== publish application ($Version, win-x64, self-contained, single file)" -ForegroundColor Cyan
dotnet publish src/MERQO.VERTEXA.App/MERQO.VERTEXA.App.csproj `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:Version=$Version `
    -o $appOut
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

Copy-Item LICENSE, THIRD-PARTY-NOTICES.md, README.md -Destination $appOut -Force
Copy-Item "docs/KNOWN_LIMITATIONS.md" -Destination (Join-Path $appOut 'KNOWN-LIMITATIONS.md') -Force
Copy-Item assets/icons/MerqoVertexa.ico -Destination $appOut -Force

Write-Host "== portable package" -ForegroundColor Cyan
$portableFolder = Join-Path $portable 'MERQO VERTEXA'
Copy-Item $appOut $portableFolder -Recurse -Force
$zipPath = Join-Path $dist "MerqoVertexa-$Version-portable-x64.zip"
Compress-Archive -Path $portableFolder -DestinationPath $zipPath -CompressionLevel Optimal

$payloadHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant().ToUpperInvariant()
Write-Host "payload sha256: $payloadHash"

Write-Host "== installer (payload embedded, digest baked in)" -ForegroundColor Cyan
$setupOut = Join-Path $dist 'setup'
dotnet publish tools/MerqoVertexa.Installer/MerqoVertexa.Installer.csproj `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -p:Version=$Version -p:PayloadZip=$zipPath -p:PayloadSha256=$payloadHash `
    -o $setupOut
if ($LASTEXITCODE -ne 0) { throw 'installer publish failed' }

$setupExe = Join-Path $setupOut 'MerqoVertexaSetup.exe'
$finalSetup = Join-Path $dist "MerqoVertexa-Setup-$Version.exe"
Move-Item $setupExe $finalSetup -Force

Write-Host "== checksums" -ForegroundColor Cyan
$lines = @()
foreach ($file in @($finalSetup, $zipPath)) {
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines += "$hash  $(Split-Path -Leaf $file)"
}
$lines += "$((Get-FileHash (Join-Path $appOut 'MerqoVertexa.exe') -Algorithm SHA256).Hash.ToLowerInvariant())  MerqoVertexa.exe (inside the portable package)"
$lines | Set-Content -Path (Join-Path $dist 'SHA256SUMS.txt') -Encoding ascii

@"
MERQO VERTEXA
Version: $Version
Build date: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))
Platform: Windows 10 1809+ / Windows 11, x64
Signing: UNSIGNED (no commercial code-signing certificate)
Payload SHA-256: $payloadHash
"@ | Set-Content -Path (Join-Path $dist 'VERSION.txt') -Encoding utf8

Write-Host "== artifacts" -ForegroundColor Cyan
Get-ChildItem $dist -File | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
