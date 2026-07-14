param(
    [Parameter(Mandatory=$true)][string]$DialogName,
    [Parameter(Mandatory=$true)][ValidateSet("Invoke","SetValue","SetRange","Toggle","Get")][string]$Action,
    [string]$ControlType = "",
    [string]$Name = "",
    [int]$Occurrence = 0,
    [string]$Value = ""
)

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

$elements = $dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$matches = @()
for ($i = 0; $i -lt $elements.Count; $i++) {
    $element = $elements.Item($i)
    $typeMatch = [string]::IsNullOrWhiteSpace($ControlType) -or $element.Current.ControlType.ProgrammaticName -eq "ControlType.$ControlType"
    $nameMatch = [string]::IsNullOrWhiteSpace($Name) -or $element.Current.Name -eq $Name
    if ($typeMatch -and $nameMatch) { $matches += $element }
}
if ($Occurrence -lt 0 -or $Occurrence -ge $matches.Count) {
    throw "Matching control not found. Type='$ControlType' Name='$Name' Occurrence=$Occurrence Matches=$($matches.Count)"
}
$target = $matches[$Occurrence]
Write-Output "ELEMENT|TYPE=$($target.Current.ControlType.ProgrammaticName)|NAME=$($target.Current.Name)|ID=$($target.Current.AutomationId)"

switch ($Action) {
    "Invoke" {
        $pattern = $null
        if (-not $target.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { throw "InvokePattern unavailable." }
        ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
        Write-Output "RESULT=INVOKED"
    }
    "SetValue" {
        $target.SetFocus()
        $pattern = $null
        if (-not $target.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) { throw "ValuePattern unavailable." }
        ([System.Windows.Automation.ValuePattern]$pattern).SetValue($Value)
        Write-Output "RESULT=VALUE_SET"
    }
    "SetRange" {
        $pattern = $null
        if (-not $target.TryGetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern, [ref]$pattern)) { throw "RangeValuePattern unavailable." }
        ([System.Windows.Automation.RangeValuePattern]$pattern).SetValue([double]::Parse($Value, [System.Globalization.CultureInfo]::InvariantCulture))
        Write-Output "RESULT=RANGE_SET"
    }
    "Toggle" {
        $pattern = $null
        if (-not $target.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$pattern)) { throw "TogglePattern unavailable." }
        ([System.Windows.Automation.TogglePattern]$pattern).Toggle()
        Write-Output "RESULT=TOGGLED"
    }
    "Get" {
        $pattern = $null
        if ($target.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
            Write-Output "VALUE=$(([System.Windows.Automation.ValuePattern]$pattern).Current.Value)"
        }
        $rangePattern = $null
        if ($target.TryGetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern, [ref]$rangePattern)) {
            $range = ([System.Windows.Automation.RangeValuePattern]$rangePattern).Current
            Write-Output "RANGE_VALUE=$($range.Value)|MIN=$($range.Minimum)|MAX=$($range.Maximum)"
        }
        $togglePattern = $null
        if ($target.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$togglePattern)) {
            Write-Output "TOGGLE=$(([System.Windows.Automation.TogglePattern]$togglePattern).Current.ToggleState)"
        }
        Write-Output "RESULT=READ"
    }
}
Start-Sleep -Milliseconds 300
