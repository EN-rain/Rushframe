param(
    [string]$Executable = ".\src\Rushframe.Desktop\bin\Debug\net10.0-windows\Rushframe.Desktop.exe",
    [int]$TimeoutSeconds = 20
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$fullPath = [System.IO.Path]::GetFullPath($Executable)
if (-not (Test-Path -LiteralPath $fullPath)) {
    throw "Rushframe executable not found: $fullPath"
}

$process = Get-Process -Name "Rushframe.Desktop" -ErrorAction SilentlyContinue | Select-Object -First 1
$startedHere = $false
if ($null -eq $process) {
    $process = Start-Process -FilePath $fullPath -PassThru
    $startedHere = $true
}

$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$window = $null
while ([DateTime]::UtcNow -lt $deadline -and $null -eq $window) {
    Start-Sleep -Milliseconds 300
    $process.Refresh()
    $processCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $process.Id)
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        $processCondition)
    $window = $windows | Where-Object { $_.Current.Name -eq "Rushframe" } | Select-Object -First 1
    if ($null -eq $window) {
        $window = $windows | Sort-Object { -($_.Current.BoundingRectangle.Width * $_.Current.BoundingRectangle.Height) } | Select-Object -First 1
    }
}

if ($null -eq $window) {
    throw "Rushframe main window was not available within $TimeoutSeconds seconds."
}

Write-Output "PROCESS_ID=$($process.Id)"
Write-Output "WINDOW_NAME=$($window.Current.Name)"
Write-Output "WINDOW_AUTOMATION_ID=$($window.Current.AutomationId)"

$condition = [System.Windows.Automation.Condition]::TrueCondition
$elements = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
Write-Output "ELEMENT_COUNT=$($elements.Count)"

for ($i = 0; $i -lt $elements.Count; $i++) {
    $element = $elements.Item($i)
    $id = $element.Current.AutomationId
    $name = $element.Current.Name
    $type = $element.Current.ControlType.ProgrammaticName
    if (-not [string]::IsNullOrWhiteSpace($id) -or -not [string]::IsNullOrWhiteSpace($name)) {
        Write-Output ("ELEMENT|{0}|{1}|{2}|ENABLED={3}|OFFSCREEN={4}" -f $type, $id, ($name -replace "[\r\n|]", " "), $element.Current.IsEnabled, $element.Current.IsOffscreen)
    }
}

if ($startedHere) {
    Write-Output "STARTED_HERE=true"
}
