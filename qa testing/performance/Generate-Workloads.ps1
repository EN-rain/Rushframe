param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\results\performance\workloads'),
    [string]$VideoPath,
    [string]$AudioPath
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Project = Join-Path $PSScriptRoot 'Rushframe.PerfWorkloads\Rushframe.PerfWorkloads.csproj'
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not $VideoPath) { $VideoPath = Join-Path $RepoRoot 'samplevid.mp4' }
if (-not $AudioPath) { $AudioPath = Join-Path $RepoRoot 'samplevid_audio.wav' }

& dotnet restore $Project --ignore-failed-sources
& dotnet run --project $Project -c Release -- $OutputDirectory $VideoPath $AudioPath
if ($LASTEXITCODE -ne 0) { throw "Performance workload generation failed with exit code $LASTEXITCODE." }

Write-Host "Performance workloads ready: $OutputDirectory"
