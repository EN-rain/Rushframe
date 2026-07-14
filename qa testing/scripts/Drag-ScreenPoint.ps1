param(
    [Parameter(Mandatory=$true)][int]$StartX,
    [Parameter(Mandatory=$true)][int]$StartY,
    [Parameter(Mandatory=$true)][int]$EndX,
    [Parameter(Mandatory=$true)][int]$EndY,
    [int]$DurationMilliseconds = 600
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$signature = @"
using System;
using System.Runtime.InteropServices;
public static class QaDragNativeMethods {
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
[QaDragNativeMethods]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
Start-Sleep -Milliseconds 250
[QaDragNativeMethods]::SetCursorPos($StartX, $StartY) | Out-Null
Start-Sleep -Milliseconds 150
[QaDragNativeMethods]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
$steps = 20
for ($i = 1; $i -le $steps; $i++) {
    $x = [int][math]::Round($StartX + (($EndX - $StartX) * $i / $steps))
    $y = [int][math]::Round($StartY + (($EndY - $StartY) * $i / $steps))
    [QaDragNativeMethods]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds ([math]::Max(5, [int]($DurationMilliseconds / $steps)))
}
[QaDragNativeMethods]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 600
Write-Output "DRAGGED=$StartX,$StartY->$EndX,$EndY"
