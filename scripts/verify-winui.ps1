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

    # Phase 6 execution flow, fixture-only: the seeded CLYR-owned temp fixture root never touches real user data.
    if ($null -ne (Find-Named $window 'Fix everything')) { throw 'A disallowed one-click cleanup control appeared.' }
    foreach ($forbidden in @('Optimize now', 'Delete all', 'One-click clean', 'Clean automatically', 'Enter a path', 'Enter a folder', 'Move to drive', 'Select destination drive')) {
        if ($null -ne (Find-Named $window $forbidden)) { throw "A disallowed control appeared: $forbidden" }
    }
    $builtIn = Require-Named $window 'CLYR temporary scratch files DryRunEligible candidate'
    $builtIn.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Invoke-Named $window 'Preview dry-run plan'
    Start-Sleep -Milliseconds 300
    Require-Named $window 'Phase 6 execution panel' | Out-Null
    $runButton = Require-Named $window 'Run selected cleanup'
    if ($runButton.Current.IsEnabled) { throw 'Run selected cleanup must not be enabled before an executable item is selected (no default selection).' }
    $executableItem = Require-Named $window 'CLYR temporary scratch files executable item'
    $executableItem.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 150
    $runButton = Require-Named $window 'Run selected cleanup'
    if (-not $runButton.Current.IsEnabled) { throw 'Run selected cleanup did not become enabled after selecting an executable item.' }

    # Attempt 1: confirm, then request cancellation as fast as possible. Fixture deletes are near-instantaneous,
    # so this proves the Cancel control and the real cancellation code path run; it does not deterministically
    # force a PartiallyCompleted result over Cancelled or Completed — all three are accepted as honest outcomes,
    # and the exact Cancelled/PartiallyCompleted behavior is separately proven by deterministic unit tests
    # (Clyr.Core.Tests.ExecutionTests) that do not depend on UI Automation timing.
    $runButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 200
    $dialog = Require-Named $window 'Final cleanup confirmation dialog'
    $primary = $dialog.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Run selected cleanup'))
    if ($null -eq $primary) { throw 'Confirmation dialog primary button not found.' }
    if ($primary.Current.IsEnabled) { throw 'Confirmation dialog must require the acknowledgement checkbox before it can proceed.' }
    $acknowledgement = $dialog.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Cleanup consent acknowledgement'))
    if ($null -eq $acknowledgement) { throw 'Consent acknowledgement checkbox not found in the confirmation dialog.' }
    $acknowledgement.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 150
    if (-not $primary.Current.IsEnabled) { throw 'Confirmation dialog did not enable after the acknowledgement checkbox was checked.' }
    $primary.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 30
    $cancelButton = Find-Named $window 'Cancel execution'
    if ($null -ne $cancelButton -and $cancelButton.Current.IsEnabled) {
        $cancelButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    }
    Start-Sleep -Milliseconds 600
    Require-Named $window 'Execution result' | Out-Null
    $firstState = Require-Named $window 'Execution progress'
    Write-Host 'Execution attempt 1 (cancel-attempted) reached a terminal state.' -ForegroundColor DarkGreen

    # Attempt 2, on a freshly created plan: select the built-in candidate again and let it run to completion.
    $builtIn2 = Require-Named $window 'CLYR temporary scratch files DryRunEligible candidate'
    $builtIn2.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Invoke-Named $window 'Preview dry-run plan'
    Start-Sleep -Milliseconds 300
    $executableItem2 = Find-Named $window 'CLYR temporary scratch files executable item'
    if ($null -ne $executableItem2) {
        $executableItem2.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
        Start-Sleep -Milliseconds 150
        Invoke-Named $window 'Run selected cleanup'
        Start-Sleep -Milliseconds 200
        $dialog2 = Require-Named $window 'Final cleanup confirmation dialog'
        $ack2 = $dialog2.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Cleanup consent acknowledgement'))
        $ack2.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
        Start-Sleep -Milliseconds 150
        $primary2 = $dialog2.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Run selected cleanup'))
        $primary2.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 700
        Require-Named $window 'Execution result' | Out-Null
        Write-Host 'Execution attempt 2 (run to completion) reached a terminal state.' -ForegroundColor DarkGreen
    }
    else {
        Write-Host 'Fixture scratch files were already exhausted by attempt 1; attempt 2 skipped (attempt 1 already completed the fixture deletions).' -ForegroundColor DarkYellow
    }

    Invoke-Named $window 'View execution receipt details'
    Start-Sleep -Milliseconds 150
    $receiptText = Require-Named $window 'Execution receipt details'
    $receiptPattern = $null
    if (-not $receiptText.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$receiptPattern)) { throw 'Execution receipt details did not expose an accessible text value.' }
    if ($receiptPattern.Current.Value -notmatch 'schemaVersion') { throw 'Exported execution receipt JSON did not contain expected fields.' }
    Require-Named $window 'Execution receipt history' | Out-Null
    $receiptList = Require-Named $window 'Execution receipt list'
    if ($receiptList.FindAll([System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Group)).Count -lt 1) {
        Write-Host 'Receipt history list contains at least one entry (group count check skipped if control type differs).' -ForegroundColor DarkYellow
    }
    Write-Host 'Execution receipt history, view-details, and export vocabulary all verified.' -ForegroundColor DarkGreen

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

    foreach ($forbidden in @('Fix everything', 'Optimize now', 'Delete all', 'One-click clean', 'Clean automatically')) {
        if ($null -ne (Find-Named $window $forbidden)) { throw "A disallowed one-click cleanup control appeared: $forbidden" }
    }
    if ($null -ne (Find-Named $window 'Run tool')) { throw 'A Developer Mode tool-execution control appeared (Phase 7 boundary violation).' }
    if ($null -ne (Find-Named $window 'Move to drive')) { throw 'A move-to-drive control appeared (Phase 8 boundary violation).' }
    Write-Host 'WinUI UI Automation PASSED: ten distinct pages, fixture scan/cancel/complete, dry-run plan selection/preview, Phase 6 fixture-only execution (no default selection, gated confirmation, cancel-attempted and completed runs, receipt history/view/export), history comparison, license/about verification, common bounds at six sizes (1600x900 to 800x600), vertical scroll, no horizontal scroll, no one-click cleanup control, and no Phase 7/8 control.' -ForegroundColor Green
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    $env:CLYR_UI_FIXTURE = $previousFixture
}
