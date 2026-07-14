param([Parameter(Mandatory=$true)][string]$DialogName)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$process = Get-Process -Name "Rushframe.Desktop" -ErrorAction Stop | Select-Object -First 1
$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$dialog = $null
for ($i = 0; $i -lt $all.Count; $i++) {
    $candidate = $all.Item($i)
    if ($candidate.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $candidate.Current.Name -eq $DialogName) {
        $dialog = $candidate
        break
    }
}
if ($null -eq $dialog) { throw "Dialog '$DialogName' not found." }
Write-Output "DIALOG|HANDLE=$($dialog.Current.NativeWindowHandle)|NAME=$($dialog.Current.Name)"
$elements = $dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
for ($i = 0; $i -lt $elements.Count; $i++) {
    $element = $elements.Item($i)
    $patterns = ($element.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName }) -join ","
    $rect = $element.Current.BoundingRectangle
    Write-Output ("ELEMENT|INDEX={0}|TYPE={1}|ID={2}|NAME={3}|ENABLED={4}|RECT={5},{6},{7},{8}|PATTERNS={9}" -f $i,$element.Current.ControlType.ProgrammaticName,$element.Current.AutomationId,($element.Current.Name -replace "[\r\n|]"," "),$element.Current.IsEnabled,[math]::Round($rect.X),[math]::Round($rect.Y),[math]::Round($rect.Width),[math]::Round($rect.Height),$patterns)
}
