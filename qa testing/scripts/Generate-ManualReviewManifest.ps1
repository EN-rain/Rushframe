$ErrorActionPreference = "Stop"
$root = Join-Path (Get-Location) "qa testing\manual review"
$files = Get-ChildItem -LiteralPath $root -Recurse -File |
    Where-Object { $_.Extension -in ".mp4", ".rushframe", ".png" } |
    Sort-Object FullName

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Manual Review File Manifest")
$lines.Add("")
$lines.Add("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')")
$lines.Add("")
$lines.Add("| File | Bytes | SHA-256 |")
$lines.Add("|---|---:|---|")
foreach ($file in $files) {
    $relative = $file.FullName.Substring($root.Length + 1).Replace("\\", "/")
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $lines.Add("| ``$relative`` | $($file.Length) | ``$hash`` |")
}
$manifest = Join-Path $root "FILE_MANIFEST.md"
[System.IO.File]::WriteAllLines($manifest, $lines, [System.Text.UTF8Encoding]::new($false))
Write-Output "MANIFEST=$manifest"
Write-Output "FILES=$($files.Count)"
