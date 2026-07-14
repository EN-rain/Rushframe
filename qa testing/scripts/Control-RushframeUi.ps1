param(
    [Parameter(Mandatory=$true)][ValidateSet("Invoke","Toggle","SetValue","SetRange","Get","Select","Expand","Collapse")][string]$Action,
    [string]$AutomationId = "",
    [string]$Name = "",
    [string]$NameContains = "",
    [string]$Value = "",
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Get-RushframeWindow {
    $process = Get-Process -Name "Rushframe.Desktop" -ErrorAction Stop | Select-Object -First 1
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $process.Id)
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        $condition)
    $window = $windows | Where-Object { $_.Current.Name -eq "Rushframe" } | Select-Object -First 1
    if ($null -eq $window) {
        $window = $windows | Sort-Object { -($_.Current.BoundingRectangle.Width * $_.Current.BoundingRectangle.Height) } | Select-Object -First 1
    }
    if ($null -eq $window) { throw "Rushframe has no top-level editor window." }
    return $window
}

function Find-Element([System.Windows.Automation.AutomationElement]$root) {
    $elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    for ($i = 0; $i -lt $elements.Count; $i++) {
        $element = $elements.Item($i)
        $idMatch = [string]::IsNullOrWhiteSpace($AutomationId) -or $element.Current.AutomationId -eq $AutomationId
        $nameMatch = [string]::IsNullOrWhiteSpace($Name) -or $element.Current.Name -eq $Name
        $containsMatch = [string]::IsNullOrWhiteSpace($NameContains) -or $element.Current.Name.IndexOf($NameContains, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
        if ($idMatch -and $nameMatch -and $containsMatch) { return $element }
    }
    return $null
}

$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$element = $null
while ([DateTime]::UtcNow -lt $deadline -and $null -eq $element) {
    $window = Get-RushframeWindow
    $element = Find-Element $window
    if ($null -eq $element) { Start-Sleep -Milliseconds 200 }
}
if ($null -eq $element) { throw "Element not found. AutomationId='$AutomationId' Name='$Name' NameContains='$NameContains'" }

$rect = $element.Current.BoundingRectangle
Write-Output "ELEMENT|TYPE=$($element.Current.ControlType.ProgrammaticName)|ID=$($element.Current.AutomationId)|NAME=$($element.Current.Name)|ENABLED=$($element.Current.IsEnabled)|OFFSCREEN=$($element.Current.IsOffscreen)|RECT=$([math]::Round($rect.X)),$([math]::Round($rect.Y)),$([math]::Round($rect.Width)),$([math]::Round($rect.Height))"

switch ($Action) {
    "Invoke" {
        $pattern = $null
        if (-not $element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
            throw "InvokePattern is not supported."
        }
        ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
        Write-Output "RESULT=INVOKED"
    }
    "Toggle" {
        $pattern = $null
        if (-not $element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$pattern)) {
            throw "TogglePattern is not supported."
        }
        ([System.Windows.Automation.TogglePattern]$pattern).Toggle()
        Write-Output "RESULT=TOGGLED"
    }
    "SetValue" {
        $element.SetFocus()
        $pattern = $null
        if (-not $element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
            throw "ValuePattern is not supported."
        }
        ([System.Windows.Automation.ValuePattern]$pattern).SetValue($Value)
        Write-Output "RESULT=VALUE_SET"
    }
    "SetRange" {
        $pattern = $null
        if (-not $element.TryGetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern, [ref]$pattern)) {
            throw "RangeValuePattern is not supported."
        }
        ([System.Windows.Automation.RangeValuePattern]$pattern).SetValue([double]::Parse($Value, [System.Globalization.CultureInfo]::InvariantCulture))
        Write-Output "RESULT=RANGE_SET"
    }
    "Get" {
        $valuePattern = $null
        if ($element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valuePattern)) {
            Write-Output "VALUE=$(([System.Windows.Automation.ValuePattern]$valuePattern).Current.Value)"
        }
        $togglePattern = $null
        if ($element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$togglePattern)) {
            Write-Output "TOGGLE=$(([System.Windows.Automation.TogglePattern]$togglePattern).Current.ToggleState)"
        }
        $rangePattern = $null
        if ($element.TryGetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern, [ref]$rangePattern)) {
            $range = ([System.Windows.Automation.RangeValuePattern]$rangePattern).Current
            Write-Output "RANGE_VALUE=$($range.Value)|MIN=$($range.Minimum)|MAX=$($range.Maximum)"
        }
        Write-Output "RESULT=READ"
    }
    "Select" {
        $pattern = $null
        if (-not $element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
            throw "SelectionItemPattern is not supported."
        }
        ([System.Windows.Automation.SelectionItemPattern]$pattern).Select()
        Write-Output "RESULT=SELECTED"
    }
    "Expand" {
        $pattern = $null
        if (-not $element.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pattern)) {
            throw "ExpandCollapsePattern is not supported."
        }
        ([System.Windows.Automation.ExpandCollapsePattern]$pattern).Expand()
        Write-Output "RESULT=EXPANDED"
    }
    "Collapse" {
        $pattern = $null
        if (-not $element.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pattern)) {
            throw "ExpandCollapsePattern is not supported."
        }
        ([System.Windows.Automation.ExpandCollapsePattern]$pattern).Collapse()
        Write-Output "RESULT=COLLAPSED"
    }
}

Start-Sleep -Milliseconds 300
