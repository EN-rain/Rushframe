param(
    [switch]$SkipBuild,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$ResultDirectory = Join-Path $RepoRoot "qa testing\results\performance\baseline-$Timestamp"
New-Item -ItemType Directory -Path $ResultDirectory -Force | Out-Null

$Machine = [ordered]@{
    CapturedAt = (Get-Date).ToString('o')
    ComputerName = $env:COMPUTERNAME
    OS = (Get-CimInstance Win32_OperatingSystem).Caption
    OSVersion = (Get-CimInstance Win32_OperatingSystem).Version
    CPU = (Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name)
    LogicalProcessors = [Environment]::ProcessorCount
    TotalMemoryBytes = (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory
    Dotnet = (& dotnet --version)
    GitCommit = (& git -C $RepoRoot rev-parse HEAD 2>$null)
    WorkingTreeDirty = [bool](& git -C $RepoRoot status --short)
}
$Machine | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $ResultDirectory 'machine.json') -Encoding UTF8

& (Join-Path $PSScriptRoot 'Generate-Workloads.ps1') -OutputDirectory (Join-Path $ResultDirectory 'workloads')

Push-Location $RepoRoot
try {
    if (-not $SkipBuild) {
        & dotnet build Rushframe.slnx -c Release --no-restore 2>&1 |
            Tee-Object -FilePath (Join-Path $ResultDirectory 'build.log')
        if ($LASTEXITCODE -ne 0) { throw "Release build failed with exit code $LASTEXITCODE." }
    }
    if (-not $SkipTests) {
        & dotnet test Rushframe.slnx -c Release --no-build --no-restore 2>&1 |
            Tee-Object -FilePath (Join-Path $ResultDirectory 'tests.log')
        if ($LASTEXITCODE -ne 0) { throw "Test run failed with exit code $LASTEXITCODE." }
        & python -m pytest tests/test_media_intelligence_v2.py -q 2>&1 |
            Tee-Object -FilePath (Join-Path $ResultDirectory 'python-tests.log')
        if ($LASTEXITCODE -ne 0) { throw "Python tests failed with exit code $LASTEXITCODE." }
    }
}
finally {
    Pop-Location
}

@"
# Rushframe performance baseline session

Result directory: `$ResultDirectory`

1. Set `RUSHFRAME_PERF=1` before launching Rushframe.
2. Open each generated workload project.
3. Capture startup, timeline pan/zoom, drag/trim, 60-second playback, search, exact-preview seek, and ten-minute loop observations.
4. Use `Collect-PerformanceTrace.ps1` with the Rushframe PID.
5. Use `Capture-MemoryDump.ps1` after the ten-minute loop.
6. Record P50/P95/P99 and visual observations in `results.md`.
"@ | Set-Content (Join-Path $ResultDirectory 'SESSION.md') -Encoding UTF8

Write-Host "Baseline package ready: $ResultDirectory"
