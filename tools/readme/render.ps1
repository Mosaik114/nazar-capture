<#
.SYNOPSIS
    Renders the README pictures in docs/images with the app's own drawing code (light, 200 %).
.DESCRIPTION
    ReadmeRenders.cs runs inside the test project (built with -p:NazarReadme=true) and draws the windows,
    the editor, the selection overlay, the cards and the recording bar into a folder of raw renders.
    compose.py frames them, demo.py makes the demo GIF and social.py the social preview (1280 x 640).
    Needs the .NET SDK, Python with Pillow, and the fonts of the pixel tests (Segoe UI Variable, Nunito).
.PARAMETER Raw
    Folder for the raw renders (default: a folder in %TEMP%).
.PARAMETER Mark
    A larger rendering of the logo for the social preview (default: engine/NazarUi/Logo/nazar_mark.png).
#>
param(
    [string]$Raw = (Join-Path $env:TEMP 'nazar-readme-raw'),
    [string]$Mark
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$env:NAZAR_README_OUT = $Raw
New-Item -ItemType Directory -Force $Raw | Out-Null

dotnet test (Join-Path $root 'tests\NazarCapture.Engine.Tests\NazarCapture.Engine.Tests.csproj') -c Release `
    -p:NazarReadme=true -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false -p:EnforceCodeStyleInBuild=false `
    --filter 'FullyQualifiedName~ReadmeRenders'
if ($LASTEXITCODE -ne 0) { throw "rendering failed ($LASTEXITCODE)" }
if (-not (Test-Path (Join-Path $Raw 'main-window.png'))) { throw "no renders in $Raw (the fonts of the pixel tests missing?)" }

python (Join-Path $PSScriptRoot 'compose.py'); if ($LASTEXITCODE -ne 0) { throw 'compose.py failed' }
python (Join-Path $PSScriptRoot 'demo.py'); if ($LASTEXITCODE -ne 0) { throw 'demo.py failed' }
if ($Mark) { python (Join-Path $PSScriptRoot 'social.py') $Mark } else { python (Join-Path $PSScriptRoot 'social.py') }
if ($LASTEXITCODE -ne 0) { throw 'social.py failed' }
