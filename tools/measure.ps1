<#
.SYNOPSIS
    Measuring run of the capture engine: time from hotkey to overlay (cold and warm) and private memory
    after 25 screenshots. Writes the medians of several engine starts as JSON.

.DESCRIPTION
    Starts MosaikCapture.Engine.exe --measure <dir> several times under test isolation (its own mutex,
    window title and data folder, never those of an installed copy), reads each measure-report.json and
    reports the median of every value plus its spread between the runs. The engine opens the real
    overlay, so run it on a desktop where that is fine (an invisible desktop or a test machine).

.PARAMETER Engine
    Path of MosaikCapture.Engine.exe (default: bin\ next to this repository).

.PARAMETER Runs
    Engine starts; the first overlay of each start is the cold value.

.PARAMETER Baseline
    Optional JSON of an earlier run (e.g. tests\Measurements\baseline-1.1.2.json): prints the change per value.

.PARAMETER Report
    Where to write the result (default: measure-report.json in the system temp folder).
#>
[CmdletBinding()]
param(
    [string]$Engine,
    [int]$Runs = 5,
    [string]$Baseline,
    [string]$Report = (Join-Path ([IO.Path]::GetTempPath()) 'mosaik-capture-measure-report.json')
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 has no $PSScriptRoot in parameter defaults.
if (-not $Engine) { $Engine = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\bin\MosaikCapture.Engine.exe' }
# Windows PowerShell 5.1 has no $PSScriptRoot in parameter defaults.
if (-not $Engine) { $Engine = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..in\MosaikCapture.Engine.exe' }
Set-StrictMode -Version Latest

$keys = 'coldMs', 'warmMs', 'warmMaxMs', 'privateIdleMb', 'privateAfterMb', 'privateAfterGcMb'
$work = Join-Path ([IO.Path]::GetTempPath()) ('mosaik-capture-measure-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$isolated = $env:MOSAIK_CAPTURE_TEST_INSTANCE
$env:MOSAIK_CAPTURE_TEST_INSTANCE = 'measure'
$results = @()
try {
    for ($i = 1; $i -le $Runs; $i++) {
        $dir = Join-Path $work "run$i"
        $process = [Diagnostics.Process]::Start((Resolve-Path $Engine).Path, "--measure `"$dir`"")
        if (-not $process.WaitForExit(180000)) {
            $process.Kill()
            throw "Measuring run $i did not finish within 3 minutes"
        }
        $json = Get-Content (Join-Path $dir 'measure-report.json') -Raw | ConvertFrom-Json
        if (-not $json.passed) { throw "Measuring run $i failed: $($json.error)" }
        $results += $json
        Write-Host ("run {0}: cold {1} ms, warm {2} ms, private {3} MB after {4} screenshots" -f $i, $json.coldMs, $json.warmMs, $json.privateAfterMb, $json.captures)
    }
}
finally {
    $env:MOSAIK_CAPTURE_TEST_INSTANCE = $isolated
}

function Median([double[]]$values) {
    $sorted = $values | Sort-Object
    return $sorted[[int][Math]::Floor($sorted.Count / 2)]
}

$summary = [ordered]@{ runs = $Runs; screen = $results[0].screen; screenCopy = $results[0].screenCopy; captures = $results[0].captures }
$spread = [ordered]@{}
foreach ($key in $keys) {
    $values = [double[]]($results | ForEach-Object { $_.$key })
    $summary[$key] = Median $values
    $low = ($values | Measure-Object -Minimum).Minimum
    $high = ($values | Measure-Object -Maximum).Maximum
    $spread[$key] = if ($summary[$key] -gt 0) { [Math]::Round(($high - $low) / $summary[$key] * 100, 1) } else { 0 }
}
$summary['spreadPercent'] = $spread
$summary | ConvertTo-Json -Depth 3 | Set-Content -Path $Report -Encoding UTF8
Write-Host "Medians: $Report"
$keys | ForEach-Object { Write-Host ("  {0,-18} {1,8}   spread {2} %" -f $_, $summary[$_], $spread[$_]) }

if ($Baseline) {
    $base = Get-Content $Baseline -Raw | ConvertFrom-Json
    Write-Host "Compared with $Baseline"
    foreach ($key in $keys) {
        $before = [double]$base.$key
        if ($before -le 0) { continue }
        $change = [Math]::Round(($summary[$key] - $before) / $before * 100, 1)
        Write-Host ("  {0,-18} {1,8} -> {2,8}   {3:+0.0;-0.0;0} %" -f $key, $before, $summary[$key], $change)
    }
}
