$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$process = Get-Process -Name "Rushframe.Desktop" -ErrorAction Stop | Select-Object -First 1
$condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
$windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
for ($i = 0; $i -lt $windows.Count; $i++) {
    $window = $windows.Item($i)
    $rect = $window.Current.BoundingRectangle
    Write-Output "WINDOW|INDEX=$i|NAME=$($window.Current.Name)|HANDLE=$($window.Current.NativeWindowHandle)|TYPE=$($window.Current.ControlType.ProgrammaticName)|RECT=$([math]::Round($rect.X)),$([math]::Round($rect.Y)),$([math]::Round($rect.Width)),$([math]::Round($rect.Height))|OFFSCREEN=$($window.Current.IsOffscreen)"
}
