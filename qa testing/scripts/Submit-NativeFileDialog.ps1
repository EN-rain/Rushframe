param(
    [Parameter(Mandatory=$true)][string]$Path,
    [string]$DialogName = "Open",
    [int]$FileNameControlId = 1148,
    [int]$ConfirmControlId = 1,
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$signature = @"
using System;
using System.Runtime.InteropServices;
public static class QaFileDialogNativeMethods {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool SetWindowText(IntPtr hWnd, string text);
    [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
"@
Add-Type -TypeDefinition $signature -ErrorAction SilentlyContinue

$process = Get-Process -Name "Rushframe.Desktop" -ErrorAction Stop | Select-Object -First 1
$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$dialog = $null
while ([DateTime]::UtcNow -lt $deadline -and $null -eq $dialog) {
    $elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $elements.Count; $i++) {
        $candidate = $elements.Item($i)
        if ($candidate.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $candidate.Current.Name -eq $DialogName) {
            $dialog = $candidate
            break
        }
    }
    if ($null -eq $dialog) { Start-Sleep -Milliseconds 200 }
}
if ($null -eq $dialog) { throw "Dialog '$DialogName' not found." }

$dialogHandle = [IntPtr]$dialog.Current.NativeWindowHandle
if ($dialogHandle -eq [IntPtr]::Zero) { throw "Dialog has no native window handle." }
$fileNameControl = [QaFileDialogNativeMethods]::GetDlgItem($dialogHandle, $FileNameControlId)
if ($fileNameControl -eq [IntPtr]::Zero) { throw "File name control ID $FileNameControlId not found." }
$fullPath = [System.IO.Path]::GetFullPath($Path)
if (-not [QaFileDialogNativeMethods]::SetWindowText($fileNameControl, $fullPath)) {
    throw "Failed to set file name."
}
$confirm = [QaFileDialogNativeMethods]::GetDlgItem($dialogHandle, $ConfirmControlId)
if ($confirm -eq [IntPtr]::Zero) { throw "Confirm control ID $ConfirmControlId not found." }
[QaFileDialogNativeMethods]::SendMessage($confirm, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 1
Write-Output "SUBMITTED=$fullPath"
