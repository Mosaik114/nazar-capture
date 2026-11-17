<#
.SYNOPSIS
    Builds Nazar Capture: the Rust tray app and the C# capture engine, side by side in bin\.

.DESCRIPTION
    Requires the Rust toolchain (cargo) and the .NET SDK 10 (dotnet). Rust and .NET are
    build-time only; the finished app needs nothing beyond Windows 10/11 x64 with
    .NET Framework 4.8 (built in) and, for screen recording, ffmpeg.

.PARAMETER Test
    Also run the Rust and C# test suites (pixel tests included, they use this machine's fonts).

.PARAMETER Measure
    Also run the measuring run (tools\measure.ps1): hotkey to overlay cold and warm, private memory after
    25 screenshots, compared with tests\Measurementsaseline-1.1.2.json. It opens the real overlay.

.PARAMETER Package
    Also zip bin\ into dist\Nazar-Capture-Zip-<version>-win-x64.zip (by hand: unpack, run NazarCapture.exe --install; the
    in-app update of 1.1.3 to 1.1.5 installs it) and write its SHA-256 next to it (.sha256), which that update checks.

.PARAMETER Installer
    Also build the installer dist\Nazar-Capture-Setup-<version>.exe from bin\ with Inno Setup 6
    (packaging\nazar.iss): double-click, per user, no admin rights. Its SHA-256 goes next to it (.sha256);
    the in-app update downloads the setup and installs it only when it matches.

.PARAMETER InnoCompiler
    Path to ISCC.exe; by default it is looked up on PATH and in the usual Inno Setup 6 folders.

.PARAMETER InnoVersion
    The Inno Setup version the installer must be compiled with, e.g. 6.7.4 (CI). The compiler confirms it itself
    (packaging\nazar.iss stops with #error otherwise). Empty: any version.

.PARAMETER RustToolchain
    Optional rustup toolchain, e.g. stable-x86_64-pc-windows-gnu on machines without the
    MSVC linker. By default the GNU toolchain is chosen automatically when link.exe is missing.

.EXAMPLE
    ./build.ps1 -Test

.EXAMPLE
    ./build.ps1 -Package -Installer
#>
[CmdletBinding()]
param(
    [switch]$Test,
    [switch]$Measure,
    [switch]$Package,
    [switch]$Installer,
    [string]$InnoCompiler,
    [string]$InnoVersion,
    [string]$RustToolchain
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = $PSScriptRoot
$bin = Join-Path $root 'bin'
$engineProject = Join-Path $root 'engine/NazarCapture.Engine.csproj'
$solution = Join-Path $root 'NazarCapture.sln'
$version = (Select-String -Path (Join-Path $root 'Cargo.toml') -Pattern '^version = "(.+)"').Matches[0].Groups[1].Value
$dist = Join-Path $root 'dist'

function Invoke-Step([string]$Name, [scriptblock]$Command) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE" }
}

function Find-InnoCompiler {
    if ($InnoCompiler) { return $InnoCompiler }
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) { return $candidate }
    }
    throw "Inno Setup 6 (ISCC.exe) not found. Install it (winget install JRSoftware.InnoSetup) or pass -InnoCompiler."
}

# Without Visual Studio there is no link.exe; the GNU toolchain links on its own.
if (-not $RustToolchain -and -not (Get-Command link.exe -ErrorAction SilentlyContinue)) {
    $RustToolchain = 'stable-x86_64-pc-windows-gnu'
}
$cargo = @()
if ($RustToolchain) { $cargo += "+$RustToolchain" }

Invoke-Step 'Rust tray app' { cargo @cargo build --release --locked }
Invoke-Step 'C# capture engine' { dotnet build $engineProject -c Release -o $bin }

New-Item -ItemType Directory -Force $bin | Out-Null
$targetDir = if ($env:CARGO_TARGET_DIR) { $env:CARGO_TARGET_DIR } else { Join-Path $root 'target' }
$rustExe = Get-ChildItem $targetDir -Recurse -Filter 'NazarCapture.exe' |
    Where-Object { $_.Directory.Name -eq 'release' } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $rustExe) { throw "NazarCapture.exe not found under $targetDir" }
Copy-Item $rustExe.FullName $bin -Force

if ($Test) {
    Invoke-Step 'Rust tests' { cargo @cargo test --locked }
    Invoke-Step 'C# tests' { dotnet test $solution -c Release }
    # GUI-subsystem executables: wait on the process object to get their exit codes
    # Under test-only names and data folders (see docs/architecture.md), never those of an installed copy.
    # Set only here: the C# tests above must run unisolated.
    $isolated = $env:NAZAR_CAPTURE_TEST_INSTANCE
    $env:NAZAR_CAPTURE_TEST_INSTANCE = 'build-self-test'
    try {
        foreach ($exe in 'NazarCapture.exe', 'NazarCapture.Engine.exe') {
            Write-Host "==> Self-test $exe" -ForegroundColor Cyan
            $report = Join-Path ([IO.Path]::GetTempPath()) 'nazar-capture-self-test.json'
            $process = [Diagnostics.Process]::Start((Join-Path $bin $exe), "--self-test `"$report`"")
            $process.WaitForExit()
            if ($process.ExitCode -ne 0) { throw "Self-test of $exe failed with exit code $($process.ExitCode)" }
        }
    }
    finally {
        $env:NAZAR_CAPTURE_TEST_INSTANCE = $isolated
    }
}

if ($Measure) {
    Write-Host "==> Measuring run" -ForegroundColor Cyan
    & (Join-Path $root 'tools/measure.ps1') -Engine (Join-Path $bin 'NazarCapture.Engine.exe') -Baseline (Join-Path $root 'tests/Measurements/baseline-1.1.2.json')
}

if ($Package) {
    New-Item -ItemType Directory -Force $dist | Out-Null
    # The ZIP installs too: unpack it and run NazarCapture.exe --install (the in-app update of 1.1.3 to 1.1.5, and
    # of later versions for a release without a setup, does exactly that).
    # "Nazar-Capture-Zip-" sorts after "Nazar-Capture-Setup-" (GitHub lists release assets by name): the setup comes
    # first. The update finds the ZIP by its end "-win-x64.zip" (engine\Updates\Updates.cs), whatever comes before.
    $zip = Join-Path $dist "Nazar-Capture-Zip-$version-win-x64.zip"
    Compress-Archive -Path (Join-Path $bin '*.exe') -DestinationPath $zip -Force
    # The update checks the download against this SHA-256 (and GitHub's asset digest) before it installs anything.
    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
    Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path -Leaf $zip)" -Encoding ascii
    Write-Host "Package: $zip ($hash)"
}

if ($Installer) {
    $iscc = Find-InnoCompiler
    Write-Host "==> Installer (Inno Setup: $iscc)" -ForegroundColor Cyan
    $innoArgs = @("/DAppVersion=$version", "/DSourceDir=$bin", "/DOutputDir=$dist")
    # The compiler confirms its own version (#error in nazar.iss if it differs).
    if ($InnoVersion) { $innoArgs += "/DRequiredInnoVersion=$InnoVersion" }
    & $iscc /Qp @innoArgs (Join-Path $root 'packaging/nazar.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }
    if ($InnoVersion) { Write-Host "Compiled with Inno Setup $InnoVersion (confirmed by the compiler)" }
    $setup = Join-Path $dist "Nazar-Capture-Setup-$version.exe"
    # The in-app update (1.1.6 on) installs the setup only if it matches this SHA-256 (a release asset of its own).
    $setupHash = (Get-FileHash -Algorithm SHA256 $setup).Hash.ToLowerInvariant()
    Set-Content -Path "$setup.sha256" -Value "$setupHash  $(Split-Path -Leaf $setup)" -Encoding ascii
    Write-Host ("Installer: {0} ({1:N1} MB, {2})" -f $setup, ((Get-Item $setup).Length / 1MB), $setupHash)
}

Write-Host "Built: $bin" -ForegroundColor Green
