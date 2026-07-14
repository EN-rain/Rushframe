param(
    [Parameter(Mandatory=$true)][string]$DialogName,
    [Parameter(Mandatory=$true)][string]$Keys,
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
$signature = @"
using System;
using System.Runtime.InteropServices;
public static class QaDialogKeysNativeMethods {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
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
$handle = [IntPtr]$dialog.Current.NativeWindowHandle
[QaDialogKeysNativeMethods]::ShowWindow($handle, 5) | Out-Null
[QaDialogKeysNativeMethods]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait($Keys)
Start-Sleep -Seconds 1
Write-Output "SENT_TO_DIALOG=$DialogName"
