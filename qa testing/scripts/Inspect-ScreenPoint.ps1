param(
    [Parameter(Mandatory=$true)][int]$X,
    [Parameter(Mandatory=$true)][int]$Y
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName WindowsBase
$point = [System.Windows.Point]::new($X, $Y)
$element = [System.Windows.Automation.AutomationElement]::FromPoint($point)
if ($null -eq $element) { throw "No automation element at $X,$Y" }
for ($depth = 0; $depth -lt 8 -and $null -ne $element; $depth++) {
    $rect = $element.Current.BoundingRectangle
    Write-Output ("ELEMENT|DEPTH={0}|TYPE={1}|CLASS={2}|ID={3}|NAME={4}|RECT={5},{6},{7},{8}" -f $depth,$element.Current.ControlType.ProgrammaticName,$element.Current.ClassName,$element.Current.AutomationId,($element.Current.Name -replace "[\r\n|]"," "),[math]::Round($rect.X),[math]::Round($rect.Y),[math]::Round($rect.Width),[math]::Round($rect.Height))
    $element = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($element)
}
