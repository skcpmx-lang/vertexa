<#
  Verifies a published MERQO VERTEXA binary:
    * PE header says it is a Windows GUI (subsystem 2) x64 image
    * VERSIONINFO carries the product name, version and copyright
    * an application icon resource is present (extracted icon contains the MERQO cyan)
    * single-file self-contained publish produced one executable with no loose runtime DLLs
  Exits non-zero on any failed assertion so CI cannot publish an unverified artifact.
#>
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [Parameter(Mandatory = $true)][string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$failures = New-Object System.Collections.Generic.List[string]

function Assert($condition, $message) {
    if ($condition) { Write-Host "PASS  $message" }
    else { Write-Host "FAIL  $message"; $failures.Add($message) }
}

if (-not (Test-Path $ExePath)) { throw "executable not found: $ExePath" }
$exe = Get-Item $ExePath
Write-Host "Inspecting $($exe.FullName) ($([math]::Round($exe.Length / 1MB, 1)) MB)"

Assert ($exe.Length -gt 30MB) "binary is a self-contained publish (>= 30 MB)"

# --- PE header -------------------------------------------------------------
$bytes = [System.IO.File]::ReadAllBytes($exe.FullName)
$peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
$signature = [BitConverter]::ToUInt32($bytes, $peOffset)
Assert ($signature -eq 0x00004550) "PE signature present"

$machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
Assert ($machine -eq 0x8664) "machine type is x64 (0x8664), got 0x{0:X4}" -f $machine

$optionalHeaderStart = $peOffset + 24
$magic = [BitConverter]::ToUInt16($bytes, $optionalHeaderStart)
$subsystem = if ($magic -eq 0x20B) {
    [BitConverter]::ToUInt16($bytes, $optionalHeaderStart + 68)
} else {
    [BitConverter]::ToUInt16($bytes, $optionalHeaderStart + 64)
}
Assert ($subsystem -eq 2) "subsystem is Windows GUI (2), got $subsystem"

# --- VERSIONINFO ----------------------------------------------------------
$info = $exe.VersionInfo
Write-Host "ProductName=$($info.ProductName) FileVersion=$($info.FileVersion) Company=$($info.CompanyName)"
Assert ($info.ProductName -eq 'MERQO VERTEXA') "VERSIONINFO ProductName is 'MERQO VERTEXA'"
Assert ($info.CompanyName -eq 'MERQO') "VERSIONINFO CompanyName is 'MERQO'"
Assert ($info.FileVersion -like "$ExpectedVersion*") "VERSIONINFO FileVersion starts with $ExpectedVersion (got $($info.FileVersion))"
Assert ($info.LegalCopyright -like '*Shohan Khan*') "copyright metadata present"

# --- icon resource -------------------------------------------------------
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Icon]::ExtractAssociatedIcon($exe.FullName)
Assert ($null -ne $icon) "application icon resource extracted"
if ($null -ne $icon) {
    $bitmap = $icon.ToBitmap()
    $cyanPixels = 0
    for ($x = 0; $x -lt $bitmap.Width; $x += 1) {
        for ($y = 0; $y -lt $bitmap.Height; $y += 1) {
            $pixel = $bitmap.GetPixel($x, $y)
            if ($pixel.B -gt 150 -and $pixel.G -gt 130 -and $pixel.R -lt 110) { $cyanPixels++ }
        }
    }
    Assert ($cyanPixels -gt 5) "embedded icon contains the MERQO cyan accent ($cyanPixels pixels)"
    $bitmap.Dispose()
}

# --- publish shape -------------------------------------------------------
$directory = Split-Path -Parent $exe.FullName
$looseRuntime = Get-ChildItem -Path $directory -Filter '*.dll' -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like 'System.*' -or $_.Name -eq 'coreclr.dll' }
Assert ($null -eq $looseRuntime -or $looseRuntime.Count -eq 0) "self-contained runtime is bundled (no loose runtime DLLs)"
Assert (Test-Path (Join-Path $directory 'LICENSE')) "LICENSE shipped beside the executable"
Assert (Test-Path (Join-Path $directory 'THIRD-PARTY-NOTICES.md')) "third-party notices shipped beside the executable"

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "VERIFICATION FAILED: $($failures.Count) check(s)" -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "VERIFICATION PASSED" -ForegroundColor Green
exit 0
