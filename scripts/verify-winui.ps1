[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ClyrWindowNative {
  [DllImport("user32.dll", SetLastError=true)]
  public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);
}
'@

$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'src\Clyr.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\Clyr.App.exe'
if (-not (Test-Path -LiteralPath $app -PathType Leaf)) { throw "WinUI executable not found: $app" }
$localDotnet = Join-Path $root '.tools\dotnet'
if (Test-Path -LiteralPath (Join-Path $localDotnet 'dotnet.exe')) { $env:DOTNET_ROOT = $localDotnet; $env:PATH = "$localDotnet;$env:PATH" }

function Find-Named([System.Windows.Automation.AutomationElement]$rootElement, [string]$name) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $rootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Require-Named([System.Windows.Automation.AutomationElement]$rootElement, [string]$name) {
    $item = Find-Named $rootElement $name
    if ($null -eq $item) { throw "Required UI element did not render: $name" }
    return $item
}
function Select-Page([System.Windows.Automation.AutomationElement]$window, [string]$name) {
    $candidates = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name))
    $item = $null
    for ($i=0; $i -lt $candidates.Count; $i++) { if ($candidates.Item($i).Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) { $item=$candidates.Item($i); break } }
    if ($null -eq $item) {
        $toggle = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'TogglePaneButton'))
        if ($null -ne $toggle) {
            $toggle.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Milliseconds 200
            $candidates = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name))
            for ($i=0; $i -lt $candidates.Count; $i++) { if ($candidates.Item($i).Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) { $item=$candidates.Item($i); break } }
        }
    }
    if ($null -eq $item) { throw "Navigation item not found: $name" }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 250
    return $item
}
function Invoke-Named([System.Windows.Automation.AutomationElement]$window, [string]$name) {
    $item = Require-Named $window $name
    $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

$previousFixture = $env:CLYR_UI_FIXTURE
$env:CLYR_UI_FIXTURE = '1'
$process = Start-Process -FilePath $app -PassThru
Start-Sleep -Seconds 4
try {
    if ($process.HasExited) { throw "Clyr.App exited with code $($process.ExitCode)." }
    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $window = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id))
    if ($null -eq $window -or $window.Current.Name -ne 'CLYR') { throw 'The CLYR main window did not render.' }
    [ClyrWindowNative]::MoveWindow($process.MainWindowHandle, 40, 40, 1280, 720, $true) | Out-Null

    Select-Page $window 'Overview' | Out-Null
    Require-Named $window 'Overview page' | Out-Null
    Require-Named $window 'System drive summary' | Out-Null
    if ($null -ne (Find-Named $window 'Start Analysis')) { throw 'Overview incorrectly exposes full scan controls.' }

    Select-Page $window 'Scan' | Out-Null
    Require-Named $window 'Quick Analysis mode card' | Out-Null
    Require-Named $window 'Deep Analysis mode card' | Out-Null
    Invoke-Named $window 'Start Analysis'
    Start-Sleep -Milliseconds 300
    $cancel = Require-Named $window 'Cancel Analysis'
    if (-not $cancel.Current.IsEnabled) { throw 'Cancellation was not enabled during fixture analysis.' }
    $cancel.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    Invoke-Named $window 'Start Analysis'
    Start-Sleep -Seconds 2

    Select-Page $window 'Results' | Out-Null
    Require-Named $window 'Results page' | Out-Null
    Require-Named $window 'Contributor visualization' | Out-Null
    Require-Named $window 'Contributor text alternatives' | Out-Null
    if ($null -ne (Find-Named $window 'Start Analysis')) { throw 'Results incorrectly exposes scan controls.' }

    Select-Page $window 'Review Plan' | Out-Null
    Require-Named $window 'Review Plan page' | Out-Null
    Require-Named $window 'Dry-run only safety banner' | Out-Null
    $candidate = Require-Named $window 'Developer package cache DryRunEligible candidate'
    $candidate.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Invoke-Named $window 'Preview dry-run plan'
    Start-Sleep -Milliseconds 300
    Require-Named $window 'Dry-run plan preview' | Out-Null
    Require-Named $window 'Save dry-run report' | Out-Null
    Require-Named $window 'Discard plan' | Out-Null

    Select-Page $window 'History' | Out-Null
    $history = Require-Named $window 'Local snapshot history'
    $historyItems = $history.FindAll([System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))
    if ($historyItems.Count -lt 2) { throw 'Fixture history did not expose two snapshots.' }
    $historyItems.Item(0).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $historyItems.Item(1).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).AddToSelection()
    Invoke-Named $window 'Compare two selected snapshots'
    Start-Sleep -Milliseconds 300
    Require-Named $window 'Snapshot comparison' | Out-Null

    $expectations = @{
        'Developer Mode'='Developer Mode page'; 'Privacy'='Privacy page'; 'Licenses'='Licenses page';
        'About'='About page'; 'Settings'='Settings page'
    }
    foreach ($entry in $expectations.GetEnumerator()) { Select-Page $window $entry.Key | Out-Null; Require-Named $window $entry.Value | Out-Null; if ($null -ne (Find-Named $window 'Start Analysis')) { throw "$($entry.Key) incorrectly exposes scan controls." } }
    Select-Page $window 'Settings' | Out-Null
    Require-Named $window 'Settings page' | Out-Null
    Require-Named $window 'History settings' | Out-Null
    Require-Named $window 'Appearance settings' | Out-Null

    Select-Page $window 'Licenses' | Out-Null
    Require-Named $window 'Licenses page' | Out-Null
    Require-Named $window 'Search third-party licenses' | Out-Null
    Require-Named $window 'Third-party license inventory' | Out-Null

    Select-Page $window 'About' | Out-Null
    Require-Named $window 'About page' | Out-Null
    Require-Named $window 'About version text' | Out-Null

    # Scroll verification at 1000x650
    Select-Page $window 'Settings' | Out-Null
    [ClyrWindowNative]::MoveWindow($process.MainWindowHandle, 40, 40, 1000, 650, $true) | Out-Null
    Start-Sleep -Milliseconds 400
    $scroller = Require-Named $window 'Page content viewport'
    $scrollPattern = $null
    if (-not $scroller.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$scrollPattern)) { throw 'Settings page did not expose an accessible scroll pattern.' }
    if (-not $scrollPattern.Current.VerticallyScrollable) { throw 'Settings page was not vertically scrollable at 1000x650.' }
    $before = $scrollPattern.Current.VerticalScrollPercent
    $scrollPattern.ScrollVertical([System.Windows.Automation.ScrollAmount]::LargeIncrement)
    Start-Sleep -Milliseconds 250
    if ($scrollPattern.Current.VerticalScrollPercent -le $before) { throw 'Vertical scrolling did not advance.' }
    if ($scrollPattern.Current.HorizontallyScrollable) { throw 'An accidental horizontal scrollbar appeared.' }

    # Multi-size viewport bounds verification
    $sizes = @(@(1600,900), @(1366,768), @(1280,720), @(1000,650), @(900,600), @(800,600))
    $boundsPages = @('Overview','Scan','Results','Review Plan','History','Developer Mode','Privacy','Licenses','About','Settings')
    foreach ($size in $sizes) {
        [ClyrWindowNative]::MoveWindow($process.MainWindowHandle, 20, 20, $size[0], $size[1], $true) | Out-Null
        Start-Sleep -Milliseconds 300
        $observations = @()
        foreach ($page in $boundsPages) {
            Select-Page $window $page | Out-Null
            Start-Sleep -Milliseconds 150
            $pageHost = Require-Named $window "$page responsive page host"
            $contentViewport = Require-Named $pageHost 'Page content viewport'
            $container = Require-Named $pageHost 'Page content container'
            $hostRect = $contentViewport.Current.BoundingRectangle
            $containerRect = $container.Current.BoundingRectangle
            if ($hostRect.Width -le 0 -or $containerRect.Width -le 0) { throw "$page had zero-width content at $($size[0])x$($size[1])." }
            if ($containerRect.Left -lt $hostRect.Left - 1 -or $containerRect.Right -gt $hostRect.Right + 1) {
                throw "$page content overflowed its viewport at $($size[0])x$($size[1])."
            }
            if ($containerRect.Width -gt 1121) { throw "$page exceeded the 1120 px readable width at $($size[0])x$($size[1])." }
            $hostScroll = $null
            if ($contentViewport.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$hostScroll) -and $hostScroll.Current.HorizontallyScrollable) {
                throw "$page had an accidental horizontal scrollbar at $($size[0])x$($size[1])."
            }
            $observations += [pscustomobject]@{ Page=$page; Left=$containerRect.Left; Right=$containerRect.Right }
        }
        $lefts = $observations.Left
        $rights = $observations.Right
        $summary = ($observations | ForEach-Object { "$($_.Page):$([math]::Round($_.Left,0))-$([math]::Round($_.Right,0))" }) -join '; '
        if ((($lefts | Measure-Object -Maximum).Maximum - ($lefts | Measure-Object -Minimum).Minimum) -gt 10) {
            throw "$($size[0])x$($size[1]) page content origins differed by more than 10 px. $summary"
        }
        if ((($rights | Measure-Object -Maximum).Maximum - ($rights | Measure-Object -Minimum).Minimum) -gt 10) {
            throw "$($size[0])x$($size[1]) page content right bounds differed by more than 10 px. $summary"
        }
        Write-Host "BOUNDS $($size[0])x$($size[1]) $summary" -ForegroundColor DarkGreen
    }

    $clean = Find-Named $window 'Clean'
    if ($null -ne $clean) { throw 'A cleanup control appeared in the UI.' }
    Write-Host 'WinUI UI Automation PASSED: ten distinct pages, fixture scan/cancel/complete, dry-run plan selection/preview, history comparison, license/about verification, common bounds at six sizes (1600x900 to 800x600), vertical scroll, no horizontal scroll, and no execution control.' -ForegroundColor Green
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    $env:CLYR_UI_FIXTURE = $previousFixture
}
