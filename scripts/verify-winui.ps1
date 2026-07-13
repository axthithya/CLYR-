[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'src\Clyr.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\Clyr.App.exe'
if (-not (Test-Path -LiteralPath $app -PathType Leaf)) { throw "WinUI executable not found: $app" }

$localDotnet = Join-Path $root '.tools\dotnet'
if (Test-Path -LiteralPath (Join-Path $localDotnet 'dotnet.exe')) {
    $env:DOTNET_ROOT = $localDotnet
    $env:PATH = "$localDotnet;$env:PATH"
}

$process = Start-Process -FilePath $app -PassThru
Start-Sleep -Seconds 5
try {
    if ($process.HasExited) { throw "Clyr.App exited with code $($process.ExitCode)." }
    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $process.Id)
    $window = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Children, $processCondition)
    if ($null -eq $window -or $window.Current.Name -ne 'CLYR') { throw 'The CLYR main window did not render.' }

    foreach ($name in @('Overview', 'Scan', 'Results', 'History', 'Developer Mode', 'Privacy', 'Licenses', 'About', 'Settings')) {
        $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $name)
        $candidates = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $nameCondition)
        $item = $null
        for ($index = 0; $index -lt $candidates.Count; $index++) {
            if ($candidates.Item($index).Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
                $item = $candidates.Item($index)
                break
            }
        }
        if ($null -eq $item) { throw "Navigation item not found: $name" }
        $selection = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $selection.Select()
        Start-Sleep -Milliseconds 150
        if (-not $selection.Current.IsSelected) { throw "Navigation item did not select: $name" }
    }

    Start-Sleep -Milliseconds 250
    $expectedDisclosure = "Phase 2 analysis is local and read-only. CLYR reads metadata, never file contents."
    $disclosure = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $expectedDisclosure)
    if ($null -eq $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $disclosure)) {
        throw 'The required read-only Phase 2 disclosure did not render.'
    }
    foreach ($controlName in @('Local volume selector', 'Quick Analysis', 'Deep Analysis', 'Start analysis', 'Cancel analysis')) {
        $controlCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $controlName)
        if ($null -eq $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $controlCondition)) {
            throw "Required Phase 2 control did not render: $controlName"
        }
    }
    Write-Host 'WinUI launch, navigation, drive overview, scan modes, cancellation, and read-only disclosure PASSED.' -ForegroundColor Green
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
