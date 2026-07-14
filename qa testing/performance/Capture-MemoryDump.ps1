param(
    [Parameter(Mandatory = $true)]
    [int]$ProcessId,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\results\performance\dumps')
)

$ErrorActionPreference = 'Stop'
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$Timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$DumpPath = Join-Path $OutputDirectory "rushframe-$Timestamp.dmp"
$GcDumpPath = Join-Path $OutputDirectory "rushframe-$Timestamp.gcdump"

if (Get-Command dotnet-gcdump -ErrorAction SilentlyContinue) {
    & dotnet-gcdump collect --process-id $ProcessId --output $GcDumpPath
    if ($LASTEXITCODE -ne 0) { Write-Warning "dotnet-gcdump failed with exit code $LASTEXITCODE." }
}

if (-not (Get-Command dotnet-dump -ErrorAction SilentlyContinue)) {
    throw 'dotnet-dump is not installed. Install with: dotnet tool install --global dotnet-dump'
}

& dotnet-dump collect --process-id $ProcessId --type Heap --output $DumpPath
if ($LASTEXITCODE -ne 0) { throw "dotnet-dump failed with exit code $LASTEXITCODE." }

Write-Host "Memory dump: $DumpPath"
if (Test-Path $GcDumpPath) { Write-Host "GC dump: $GcDumpPath" }
