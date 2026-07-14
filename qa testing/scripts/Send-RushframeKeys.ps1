param([Parameter(Mandatory=$true)][string]$Keys)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$process = Get-Process -Name "Rushframe.Desktop" -ErrorAction Stop | Select-Object -First 1
$condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
$windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
$window = $windows | Where-Object { $_.Current.Name -eq "Rushframe" } | Select-Object -First 1
if ($null -eq $window) { throw "Rushframe editor window not found." }
$signature = @"
using System;
using System.Runtime.InteropServices;
public static class QaNativeMethods {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@
Add-Type -TypeDefinition $signature -ErrorAction SilentlyContinue
[QaNativeMethods]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
Start-Sleep -Milliseconds 200
[System.Windows.Forms.SendKeys]::SendWait($Keys)
Start-Sleep -Milliseconds 500
Write-Output "SENT=$Keys"
