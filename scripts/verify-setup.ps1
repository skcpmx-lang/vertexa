<#
  End-to-end verification of the built installer, on a real Windows machine:
    1. silent install to a temporary per-user directory
    2. assert the payload landed and the uninstall registry entry exists
    3. run the *installed* executable with --selftest (real scan + report export)
    4. silent uninstall and assert the installation folder is gone
  Any failed assertion fails the release.
#>
param(
    [Parameter(Mandatory = $true)][string]$Setup,
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$failures = New-Object System.Collections.Generic.List[string]
function Assert($condition, $message) {
    if ($condition) { Write-Host "PASS  $message" }
    else { Write-Host "FAIL  $message"; $failures.Add($message) }
}

$target = Join-Path $env:TEMP ("VERTEXA-install-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
Write-Host "Installing to $target"

$install = Start-Process -FilePath $Setup -ArgumentList "/S", "/D=$target" -PassThru -Wait
Assert ($install.ExitCode -eq 0) "silent install exit code 0 (got $($install.ExitCode))"
if (Test-Path "$env:TEMP\MerqoVertexaSetup.log") { Get-Content "$env:TEMP\MerqoVertexaSetup.log" }

$exe = Join-Path $target 'MerqoVertexa.exe'
Assert (Test-Path $exe) "application installed"
Assert (Test-Path (Join-Path $target 'Uninstall.exe')) "uninstaller installed"
Assert (Test-Path (Join-Path $target 'LICENSE')) "licence shipped"

$registry = Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MERQOVERTEXA' -ErrorAction SilentlyContinue
Assert ($null -ne $registry) "uninstall registry entry created under HKCU"
if ($null -ne $registry) {
    Assert ($registry.DisplayName -eq 'MERQO VERTEXA') "DisplayName is 'MERQO VERTEXA'"
    Assert ($registry.DisplayVersion -eq $Version) "DisplayVersion is $Version"
    Assert ($registry.Publisher -eq 'MERQO') "Publisher is 'MERQO'"
    Assert ($registry.DisplayIcon -like '*MerqoVertexa.exe*') "DisplayIcon points at the application"
    Assert ($registry.UninstallString -like '*Uninstall.exe*') "UninstallString is present"
}

$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'MERQO VERTEXA\MERQO VERTEXA.lnk'
Assert (Test-Path $shortcut) "Start Menu shortcut created"

Write-Host "Running the installed executable self test..."
$selftest = Start-Process -FilePath $exe -ArgumentList '--selftest' -PassThru -Wait
Assert ($selftest.ExitCode -eq 0) "installed binary self test exit code 0 (got $($selftest.ExitCode))"
if (Test-Path "$env:TEMP\vertexa-selftest.txt") {
    Get-Content "$env:TEMP\vertexa-selftest.txt" | ForEach-Object { Write-Host "    $_" }
    Assert (-not (Select-String -Path "$env:TEMP\vertexa-selftest.txt" -Pattern '^FAIL' -Quiet)) "self test reported no failures"
} else {
    Assert $false "self test report written"
}

Write-Host "Uninstalling..."
$uninstall = Start-Process -FilePath (Join-Path $target 'Uninstall.exe') -ArgumentList '/UNINSTALL', '/S' -PassThru -Wait
Assert ($uninstall.ExitCode -eq 0) "silent uninstall exit code 0 (got $($uninstall.ExitCode))"
$deadline = (Get-Date).AddSeconds(30)
while ((Test-Path $exe) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
Assert (-not (Test-Path $exe)) "application files removed"
Assert ($null -eq (Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MERQOVERTEXA' -ErrorAction SilentlyContinue)) "uninstall registry entry removed"
Assert (-not (Test-Path $shortcut)) "Start Menu shortcut removed"

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "INSTALLER VERIFICATION FAILED: $($failures.Count) check(s)" -ForegroundColor Red
    exit 1
}
Write-Host ""
Write-Host "INSTALLER VERIFICATION PASSED" -ForegroundColor Green
exit 0
