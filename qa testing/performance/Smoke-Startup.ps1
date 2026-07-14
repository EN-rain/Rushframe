param(
    [int]$WarmupSeconds = 8
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Executable = Join-Path $RepoRoot 'src\Rushframe.Desktop\bin\Release\net10.0-windows\Rushframe.Desktop.exe'
if (-not (Test-Path $Executable)) {
    throw "Release executable not found: $Executable"
}

$TemporaryLocalAppData = Join-Path $env:TEMP ("rushframe-smoke-{0}" -f [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $TemporaryLocalAppData -Force | Out-Null
$process = $null

try {
    $boundedWarmup = [Math]::Min(60, [Math]::Max(3, $WarmupSeconds))
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $Executable
    $startInfo.UseShellExecute = $false
    $startupLog = Join-Path $TemporaryLocalAppData 'startup.log'
    $startInfo.EnvironmentVariables['LOCALAPPDATA'] = $TemporaryLocalAppData
    $startInfo.EnvironmentVariables['RUSHFRAME_QA_APPDATA'] = (Join-Path $TemporaryLocalAppData 'Rushframe')
    $startInfo.EnvironmentVariables['RUSHFRAME_PERF'] = '1'
    $startInfo.EnvironmentVariables['RUSHFRAME_QA_AUTOCLOSE_MS'] = [string]($boundedWarmup * 1000)
    $startInfo.EnvironmentVariables['RUSHFRAME_STARTUP_LOG'] = $startupLog
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if (-not $process) { throw 'Rushframe process could not be started.' }
    if (-not $process.WaitForExit(($boundedWarmup + 20) * 1000)) {
        if (Test-Path $startupLog) {
            Write-Host 'Startup milestones before timeout:'
            Get-Content $startupLog | ForEach-Object { Write-Host $_ }
        }
        Stop-Process -Id $process.Id -Force
        throw 'Rushframe did not exit after its QA auto-close request.'
    }
    if ($process.ExitCode -ne 0) {
        throw "Rushframe exited with code $($process.ExitCode)."
    }

    $performanceDirectory = Join-Path $TemporaryLocalAppData 'Rushframe\performance'
    $snapshot = Get-ChildItem -Path $performanceDirectory -Filter '*.json' -ErrorAction Stop |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if (-not $snapshot) {
        throw 'No performance telemetry snapshot was written.'
    }

    $payload = Get-Content $snapshot.FullName -Raw | ConvertFrom-Json
    [pscustomobject]@{
        Status = 'PASS'
        ProcessId = $process.Id
        ExitCode = $process.ExitCode
        TelemetryPath = $snapshot.FullName
        ManagedBytes = $payload.ManagedBytes
        Gen0Collections = $payload.Gen0Collections
        Gen1Collections = $payload.Gen1Collections
        Gen2Collections = $payload.Gen2Collections
    } | Format-List
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
}
