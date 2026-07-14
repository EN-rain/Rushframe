$ErrorActionPreference = "Stop"
$paths = @(
    ".\qa testing\QA_TESTING_PLAN.md",
    ".\qa testing\QA_EXECUTION_RESULTS.md",
    ".\qa testing\DEFECT_REPORT.md",
    ".\qa testing\manual review\REVIEW_CHECKLIST.md",
    ".\qa testing\manual review\FILE_MANIFEST.md"
)
foreach ($path in $paths) {
    $item = Get-Item -LiteralPath $path
    Write-Output "FILE|$path|BYTES=$($item.Length)"
}
Get-ChildItem -LiteralPath ".\qa testing\manual review" -Recurse -Filter "*.mp4" |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring((Get-Location).Path.Length + 1)
        Write-Output "MP4|$relative|BYTES=$($_.Length)"
    }
if (Get-Process -Name "Rushframe.Desktop" -ErrorAction SilentlyContinue) {
    Write-Output "APP_RUNNING=true"
} else {
    Write-Output "APP_RUNNING=false"
}
