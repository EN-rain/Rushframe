param(
    [string]$Name,
    [string]$AutomationId = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$process = Get-Process -Name "Rushframe.Desktop" -ErrorAction Stop | Select-Object -First 1
$window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
$elements = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
for ($i = 0; $i -lt $elements.Count; $i++) {
    $element = $elements.Item($i)
    $nameMatch = [string]::IsNullOrWhiteSpace($Name) -or $element.Current.Name -eq $Name
    $idMatch = [string]::IsNullOrWhiteSpace($AutomationId) -or $element.Current.AutomationId -eq $AutomationId
    if (-not ($nameMatch -and $idMatch)) { continue }

    Write-Output "MATCH|TYPE=$($element.Current.ControlType.ProgrammaticName)|ID=$($element.Current.AutomationId)|NAME=$($element.Current.Name)"
    foreach ($pattern in $element.GetSupportedPatterns()) {
        Write-Output "PATTERN|$($pattern.ProgrammaticName)"
    }
}
