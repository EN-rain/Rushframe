param(
    [Parameter(Mandatory = $true)]
    [int]$ProcessId,
    [int]$DurationSeconds = 60,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\results\performance\traces')
)

$ErrorActionPreference = 'Stop'
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$Timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$Duration = [TimeSpan]::FromSeconds([Math]::Clamp($DurationSeconds, 5, 3600)).ToString('c')
$TracePath = Join-Path $OutputDirectory "rushframe-$Timestamp.nettrace"
$CounterPath = Join-Path $OutputDirectory "rushframe-$Timestamp-counters.csv"

if (-not (Get-Command dotnet-trace -ErrorAction SilentlyContinue)) {
    throw 'dotnet-trace is not installed. Install with: dotnet tool install --global dotnet-trace'
}

Write-Host "Collecting trace from PID $ProcessId for $Duration..."
& dotnet-trace collect `
    --process-id $ProcessId `
    --duration $Duration `
    --providers 'Microsoft-Windows-DotNETRuntime:0x1C000080018:5,System.Runtime' `
    --output $TracePath
if ($LASTEXITCODE -ne 0) { throw "dotnet-trace failed with exit code $LASTEXITCODE." }

if (Get-Command dotnet-counters -ErrorAction SilentlyContinue) {
    & dotnet-counters collect `
        --process-id $ProcessId `
        --duration $Duration `
        --counters 'System.Runtime' `
        --format csv `
        --output $CounterPath
}

Write-Host "Trace: $TracePath"
if (Test-Path $CounterPath) { Write-Host "Counters: $CounterPath" }
