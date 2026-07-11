param(
    [switch]$Advanced,
    [switch]$Upgrade
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Venv = Join-Path $RepoRoot '.tools\intelligence-venv'
$Python = Join-Path $Venv 'Scripts\python.exe'

if (-not (Test-Path $Python)) {
    py -3 -m venv $Venv
}

& $Python -m pip install --upgrade pip
$Requirements = if ($Advanced) {
    Join-Path $RepoRoot 'requirements-intelligence-advanced.txt'
} else {
    Join-Path $RepoRoot 'requirements-intelligence.txt'
}

$Arguments = @('-m', 'pip', 'install', '-r', $Requirements)
if ($Upgrade) { $Arguments += '--upgrade' }
& $Python @Arguments

Write-Host "Rushframe intelligence environment ready: $Python"
Write-Host "Core analysis: & '$Python' -m rushframe_intelligence --help"
if (-not $Advanced) {
    Write-Host "Run again with -Advanced to install OCR, diarization, CLAP and local Qwen support."
}
