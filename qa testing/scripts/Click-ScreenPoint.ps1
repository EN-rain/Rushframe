param(
    [Parameter(Mandatory=$true)][int]$X,
    [Parameter(Mandatory=$true)][int]$Y,
    [ValidateSet("Left","Right")][string]$Button = "Left"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$signature = @"
using System;
using System.Runtime.InteropServices;
public static class QaMouseNativeMethods {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
"@
Add-Type -TypeDefinition $signature -ErrorAction SilentlyContinue
$process = Get-Process -Name "Rushframe.Desktop" -ErrorAction Stop | Select-Object -First 1
$condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
$windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
$window = $windows | Where-Object { $_.Current.Name -eq "Rushframe" } | Select-Object -First 1
if ($null -eq $window) { throw "Rushframe editor window not found." }
[QaMouseNativeMethods]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
Start-Sleep -Milliseconds 250
[QaMouseNativeMethods]::SetCursorPos($X, $Y) | Out-Null
Start-Sleep -Milliseconds 120
if ($Button -eq "Left") {
    [QaMouseNativeMethods]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [QaMouseNativeMethods]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
} else {
    [QaMouseNativeMethods]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [QaMouseNativeMethods]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero)
}
Start-Sleep -Milliseconds 400
Write-Output "CLICKED=$Button|X=$X|Y=$Y"
