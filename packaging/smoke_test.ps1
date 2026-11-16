<#
.SYNOPSIS
    Installs Nazar Capture silently, runs both self-tests, reinstalls without autostart, uninstalls, and checks
    that nothing is left.

.DESCRIPTION
    Meant for CI (a throwaway Windows runner). It creates a real Start menu entry and a real autostart entry for
    the current user and writes autostart=0 into the real settings.ini, so do not run it on your own machine.
    Logs go to build\smoke\.

.EXAMPLE
    .\packaging\smoke_test.ps1 -Setup dist\Nazar-Capture-Setup-1.1.5.exe
#>
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Setup)

$ErrorActionPreference = "Stop"
$Setup = (Resolve-Path $Setup).Path
$AppDir = Join-Path $env:LOCALAPPDATA "Programs\NazarCapture"
$Exe = Join-Path $AppDir "NazarCapture.exe"
$Settings = Join-Path $env:LOCALAPPDATA "NazarCapture\settings.ini"
$RunKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
# Found as "Nazar Capture" (Start menu, Windows "Apps", autostart); folders and the EXE keep the technical name.
$Shortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Nazar Capture.lnk"
$UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{6458E5CD-9A10-4E94-B0ED-57D2DC610D1D}_is1"
$Logs = Join-Path (Get-Location) "build\smoke"
New-Item -ItemType Directory -Force $Logs | Out-Null

function Assert-That([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAILED: $Message" }
    Write-Host "ok   $Message"
}

function Get-Autostart {
    (Get-ItemProperty $RunKey -Name "Nazar Capture" -ErrorAction SilentlyContinue)."Nazar Capture"
}

function Get-FileList([string]$Folder) {
    Get-ChildItem $Folder -Recurse -File | ForEach-Object { $_.FullName.Substring($Folder.Length) } | Sort-Object
}

function Install([string]$Tasks, [string]$Log) {
    $arguments = "/VERYSILENT /SUPPRESSMSGBOXES /CURRENTUSER /NORESTART $Tasks /LOG=`"$Logs\$Log`""
    $process = Start-Process $Setup -ArgumentList $arguments -Wait -PassThru
    Assert-That ($process.ExitCode -eq 0) "setup $Tasks exits with 0 (got $($process.ExitCode))"
}

if (Test-Path $AppDir) { throw "$AppDir exists already; this test needs a clean machine." }

# --- install -----------------------------------------------------------------------------
Write-Host "Installing $Setup"
Install "/TASKS=autostart" "install.log"
Assert-That (Test-Path $Exe) "NazarCapture.exe installed per user in $AppDir"
Assert-That (Test-Path (Join-Path $AppDir "NazarCapture.Engine.exe")) "NazarCapture.Engine.exe installed next to it"
Assert-That (Test-Path $Shortcut) "Start menu shortcut exists"
$displayName = (Get-ItemProperty $UninstallKey -Name DisplayName -ErrorAction SilentlyContinue).DisplayName
Assert-That ($displayName -eq "Nazar Capture") "listed in Windows Apps as 'Nazar Capture' (got '$displayName')"
Assert-That ((Get-Autostart) -eq "`"$Exe`" --background") "autostart entry starts NazarCapture.exe in the tray (--background)"
Assert-That (-not (Test-Path $Settings)) "autostart on (the app's default) needs no settings file"
$installed = Get-FileList $AppDir
Write-Host ("Installed: {0} files, {1:N1} MB" -f $installed.Count, ((Get-ChildItem $AppDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB))

# --- self-tests --------------------------------------------------------------------------
# Under a test-only name and data folder, as in build.ps1 -Test.
$env:NAZAR_CAPTURE_TEST_INSTANCE = "installer-smoke-test"
try {
    foreach ($name in "NazarCapture.exe", "NazarCapture.Engine.exe") {
        $report = Join-Path $Logs "self-test-$name.json"
        $process = [Diagnostics.Process]::Start((Join-Path $AppDir $name), "--self-test `"$report`"")
        $process.WaitForExit()
        Assert-That ($process.ExitCode -eq 0) "$name --self-test exits with 0 (got $($process.ExitCode))"
    }
}
finally {
    Remove-Item Env:\NAZAR_CAPTURE_TEST_INSTANCE
}
$after = Get-FileList $AppDir
$new = Compare-Object $installed $after | Where-Object SideIndicator -eq "=>" | ForEach-Object InputObject
Assert-That (-not $new) "nothing was written into the install folder $(($new | Select-Object -First 5) -join ', ')"

# --- reinstall without autostart -----------------------------------------------------------
Install "/MERGETASKS=!autostart" "reinstall.log"
Assert-That ($null -eq (Get-Autostart)) "reinstall without the task removes the autostart entry"
$stored = (Get-Content $Settings -ErrorAction SilentlyContinue) -match "^autostart=0$"
Assert-That ([bool]$stored) "the choice is stored in settings.ini (autostart=0), so the app keeps it"

# --- uninstall ---------------------------------------------------------------------------
$uninstaller = Join-Path $AppDir "unins000.exe"
Assert-That (Test-Path $uninstaller) "uninstaller exists"
$arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$Logs\uninstall.log`""
Start-Process $uninstaller -ArgumentList $arguments -Wait | Out-Null
# The uninstaller hands over to a copy of itself in %TEMP% so it can delete its own file;
# the original returns before the work is done.
$deadline = (Get-Date).AddSeconds(120)
while ((Test-Path $AppDir) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
Assert-That (-not (Test-Path $AppDir)) "install folder removed"
Assert-That (-not (Test-Path $Shortcut)) "Start menu shortcut removed"
Assert-That ($null -eq (Get-Autostart)) "no autostart entry left"
Assert-That (Test-Path $Settings) "silent uninstall keeps the user's settings (offered, not forced)"
Write-Host "Smoke test passed."
