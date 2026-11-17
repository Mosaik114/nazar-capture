<#
.SYNOPSIS
    Installs Nazar Capture silently, runs both self-tests, reinstalls without autostart, runs the silent in-app update
    (the app must start again), uninstalls, and checks that nothing is left.

.DESCRIPTION
    Meant for CI (a throwaway Windows runner). With the defaults it creates a real Start menu entry and a real
    autostart entry for the current user and writes autostart=0 into the real settings.ini, so do not run it that way
    on your own machine. On a developer machine, test a setup built with its own names instead (see the test defines
    in packaging\nazar.iss) and pass the same names here: -AppDir, -DisplayName, -AppIdGuid, -SettingsFile. Every
    program the test starts runs as an isolated test instance (NAZAR_CAPTURE_TEST_INSTANCE). Logs go to build\smoke\.

.EXAMPLE
    .\packaging\smoke_test.ps1 -Setup dist\Nazar-Capture-Setup-1.1.7.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Setup,
    [string]$AppDir = (Join-Path $env:LOCALAPPDATA "Programs\NazarCapture"),
    [string]$DisplayName = "Nazar Capture",
    [string]$AppIdGuid = "6458E5CD-9A10-4E94-B0ED-57D2DC610D1D",
    [string]$SettingsFile = (Join-Path $env:LOCALAPPDATA "NazarCapture\settings.ini"),
    [string]$Logs = (Join-Path (Get-Location) "build\smoke")
)

$ErrorActionPreference = "Stop"
$Setup = (Resolve-Path $Setup).Path
$Exe = Join-Path $AppDir "NazarCapture.exe"
$Settings = $SettingsFile
$RunKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
# Found as "Nazar Capture" (Start menu, Windows "Apps", autostart); folders and the EXE keep the technical name.
$Shortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$DisplayName.lnk"
$UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{$AppIdGuid}_is1"
New-Item -ItemType Directory -Force $Logs | Out-Null

function Assert-That([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAILED: $Message" }
    Write-Host "ok   $Message"
}

function Get-Autostart {
    (Get-ItemProperty $RunKey -Name $DisplayName -ErrorAction SilentlyContinue).$DisplayName
}

function Get-FileList([string]$Folder) {
    Get-ChildItem $Folder -Recurse -File | ForEach-Object { $_.FullName.Substring($Folder.Length) } | Sort-Object
}

# CI runners are elevated (admin, UAC off). A silent setup with admin rights stops unless /ALLOWADMIN=1 says it is
# meant (packaging\nazar.iss, InitializeSetup): checked once below, then every install here passes it.
$Elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
$AllowAdmin = if ($Elevated) { "/ALLOWADMIN=1" } else { "" }

function Start-Setup([string]$Arguments, [string]$Log) {
    $all = "/VERYSILENT /SUPPRESSMSGBOXES /CURRENTUSER /NORESTART /DIR=`"$AppDir`" $Arguments /LOG=`"$Logs\$Log`""
    # Not -Wait: that would also wait for the app the update starts again (a child of the setup).
    $process = Start-Process $Setup -ArgumentList $all -PassThru
    $process.WaitForExit()
    $process.ExitCode
}

function Install([string]$Arguments, [string]$Log) {
    $code = Start-Setup "$AllowAdmin $Arguments" $Log
    Assert-That ($code -eq 0) "setup $Arguments exits with 0 (got $code)"
}

# Whether a process runs with Windows' RedirectionGuard enforced (ProcessRedirectionTrustPolicy = 16): then it cannot
# follow links a user made without admin rights (error 448). Null where Windows cannot tell (before 10 22H2).
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class NazarSmokeMitigation {
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessMitigationPolicy(IntPtr process, int policy, out uint flags, UIntPtr size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    public static bool? RedirectionGuard(int id) {
        IntPtr process = OpenProcess(0x0400, false, (uint)id);   // PROCESS_QUERY_INFORMATION
        if (process == IntPtr.Zero) return null;
        try { uint flags; return GetProcessMitigationPolicy(process, 16, out flags, (UIntPtr)4) ? (bool?)((flags & 1) != 0) : null; }
        catch (EntryPointNotFoundException) { return null; }
        finally { CloseHandle(process); }
    }
}
"@

# The host process started from the install folder, if one runs.
function Get-InstalledHost {
    Get-CimInstance Win32_Process -Filter "Name = 'NazarCapture.exe'" |
        Where-Object { $_.ExecutablePath -and ([IO.Path]::GetFullPath($_.ExecutablePath) -ieq [IO.Path]::GetFullPath($Exe)) }
}

if (Test-Path $AppDir) { throw "$AppDir exists already; this test needs a clean machine." }
# A run on a developer machine may already be isolated as a whole; each step below restores that.
$outerInstance = $env:NAZAR_CAPTURE_TEST_INSTANCE

# --- with admin rights, silently: stops ----------------------------------------------------
if ($Elevated) {
    $code = Start-Setup "/TASKS=autostart" "elevated.log"
    Assert-That ($code -ne 0) "a silent setup with admin rights stops without /ALLOWADMIN=1 (got $code)"
    Assert-That (-not (Test-Path $AppDir)) "and installs nothing"
}
else {
    Write-Host "skip a silent setup with admin rights (this run is not elevated)"
}

# --- install -----------------------------------------------------------------------------
Write-Host "Installing $Setup"
Install "/TASKS=autostart" "install.log"
Assert-That (Test-Path $Exe) "NazarCapture.exe installed per user in $AppDir"
Assert-That (Test-Path (Join-Path $AppDir "NazarCapture.Engine.exe")) "NazarCapture.Engine.exe installed next to it"
Assert-That (Test-Path $Shortcut) "Start menu shortcut exists"
$displayed = (Get-ItemProperty $UninstallKey -Name DisplayName -ErrorAction SilentlyContinue).DisplayName
Assert-That ($displayed -eq $DisplayName) "listed in Windows Apps as '$DisplayName' (got '$displayed')"
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
    $env:NAZAR_CAPTURE_TEST_INSTANCE = $outerInstance
}
$after = Get-FileList $AppDir
$new = Compare-Object $installed $after | Where-Object SideIndicator -eq "=>" | ForEach-Object InputObject
Assert-That (-not $new) "nothing was written into the install folder $(($new | Select-Object -First 5) -join ', ')"

# --- reinstall without autostart -----------------------------------------------------------
Install "/MERGETASKS=!autostart" "reinstall.log"
Assert-That ($null -eq (Get-Autostart)) "reinstall without the task removes the autostart entry"
$stored = (Get-Content $Settings -ErrorAction SilentlyContinue) -match "^autostart=0$"
Assert-That ([bool]$stored) "the choice is stored in settings.ini (autostart=0), so the app keeps it"

# --- the in-app update: silent, the app starts again -----------------------------------------
# What engine\Updates\Updates.cs passes (UpdateInstaller.SetupArguments). The app it starts again inherits the test
# instance, so it never meets a real Nazar Capture.
$env:NAZAR_CAPTURE_TEST_INSTANCE = "installer-smoke-update"
try {
    Install "/RESTARTAPP=1" "update.log"
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Get-InstalledHost) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
    Assert-That ([bool](Get-InstalledHost)) "after the update the app runs again from $AppDir"
    # Inno Setup 6.7+ would hand its RedirectionGuard to the app it starts (packaging\nazar.iss switches it off).
    $guard = [NazarSmokeMitigation]::RedirectionGuard([int](Get-InstalledHost | Select-Object -First 1).ProcessId)
    Assert-That ($guard -ne $true) "the app started by the setup runs without RedirectionGuard (got $guard)"
    Assert-That ($null -eq (Get-Autostart)) "the update keeps autostart as the app's setting says (off)"
    $stored = (Get-Content $Settings -ErrorAction SilentlyContinue) -match "^autostart=0$"
    Assert-That ([bool]$stored) "the update leaves the app's settings alone"
    $quit = Start-Process $Exe -ArgumentList "--quit" -Wait -PassThru
    Assert-That ($quit.ExitCode -eq 0) "the restarted app quits through its own --quit (got $($quit.ExitCode))"
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-InstalledHost) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
    Assert-That (-not (Get-InstalledHost)) "the restarted app has ended"
}
finally {
    $env:NAZAR_CAPTURE_TEST_INSTANCE = $outerInstance
}

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
Assert-That (-not (Test-Path $UninstallKey)) "no entry left in Windows Apps"
Assert-That (Test-Path $Settings) "silent uninstall keeps the user's settings (offered, not forced)"
Write-Host "Smoke test passed."
