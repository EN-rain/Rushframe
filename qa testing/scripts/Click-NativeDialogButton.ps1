param([Parameter(Mandatory=$true)][int]$ControlId)

$ErrorActionPreference = "Stop"
$signature = @"
using System;
using System.Runtime.InteropServices;
public static class QaDialogNativeMethods {
    [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
"@
Add-Type -TypeDefinition $signature -ErrorAction SilentlyContinue
$process = Get-Process -Name "Rushframe.Desktop" -ErrorAction Stop | Select-Object -First 1
$button = [QaDialogNativeMethods]::GetDlgItem($process.MainWindowHandle, $ControlId)
if ($button -eq [IntPtr]::Zero) { throw "Control ID $ControlId was not found." }
[QaDialogNativeMethods]::SendMessage($button, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Milliseconds 500
Write-Output "CLICKED_CONTROL_ID=$ControlId"
