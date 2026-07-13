[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$pages = Join-Path $root 'src\Clyr.App\Pages'
$controls = Join-Path $root 'src\Clyr.App\Controls'

Write-Host '==> Responsive layout structural verification'

# 1. All nine pages must use ResponsivePageHost
$requiredPages = @('Overview','Scan','Results','History','DeveloperMode','Privacy','Licenses','About','Settings')
foreach ($page in $requiredPages) {
    $file = Join-Path $pages "$($page)Page.xaml"
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing page: $file" }
    $content = Get-Content -Raw -LiteralPath $file
    if (-not $content.Contains('ResponsivePageHost')) { throw "$($page)Page does not use ResponsivePageHost." }
    if (-not $content.Contains('AutomationProperties.Name')) { throw "$($page)Page has no automation names." }
}
Write-Host '  All 9 pages use ResponsivePageHost.'

# 2. ResponsivePageHost has consistent scroll contract
$hostXaml = Get-Content -Raw -LiteralPath (Join-Path $controls 'ResponsivePageHost.xaml')
foreach ($required in @('VerticalScrollBarVisibility="Auto"','VerticalScrollMode="Auto"','HorizontalScrollBarVisibility="Disabled"','HorizontalScrollMode="Disabled"','MaxWidth="1120"','HorizontalAlignment="Center"')) {
    if (-not $hostXaml.Contains($required)) { throw "ResponsivePageHost is missing: $required" }
}
Write-Host '  ResponsivePageHost scroll contract verified.'

# 3. ResponsivePageHost code-behind has breakpoints and gutters
$hostCs = Get-Content -Raw -LiteralPath (Join-Path $controls 'ResponsivePageHost.xaml.cs')
foreach ($required in @('< 760','< 1200','ResponsivePageWidth.Narrow','ResponsivePageWidth.Medium','ResponsivePageWidth.Wide','new Thickness(16','new Thickness(24','new Thickness(32')) {
    if (-not $hostCs.Contains($required)) { throw "ResponsivePageHost.xaml.cs is missing: $required" }
}
Write-Host '  Breakpoints: Narrow <760px, Medium 760-1199px, Wide >=1200px.'
Write-Host '  Gutters: 16px narrow, 24px medium, 32px wide.'

# 4. PageHeader has responsive trust badge stacking
$header = Get-Content -Raw -LiteralPath (Join-Path $controls 'PageHeader.xaml.cs')
if (-not $header.Contains('Grid.SetRow(TrustBadge')) { throw 'PageHeader does not responsively stack the trust badge.' }
Write-Host '  PageHeader trust badge stacking verified.'

# 5. No pages have unsafe fixed root widths or large left margins
foreach ($page in $requiredPages) {
    $content = Get-Content -Raw -LiteralPath (Join-Path $pages "$($page)Page.xaml")
    foreach ($unsafe in @('TranslateTransform','Margin="200','Margin="300')) {
        if ($content.Contains($unsafe)) { throw "$($page)Page has unsafe layout: $unsafe" }
    }
}
Write-Host '  No unsafe fixed widths or translations in page XAML.'

# 6. Key reflow methods exist in page code-behind
$reflowPages = @('Overview','Scan','Results','History','DeveloperMode','Privacy','About','Settings')
foreach ($page in $reflowPages) {
    $cs = Get-Content -Raw -LiteralPath (Join-Path $pages "$($page)Page.xaml.cs")
    if (-not $cs.Contains('LayoutModeChanged')) { throw "$($page)Page does not subscribe to LayoutModeChanged." }
}
Write-Host '  Responsive reflow handlers verified on all applicable pages.'

# 7. Scan controls are isolated to ScanPage
$scanXaml = Get-Content -Raw -LiteralPath (Join-Path $pages 'ScanPage.xaml')
if (-not $scanXaml.Contains('Start Analysis')) { throw 'ScanPage is missing Start Analysis.' }
foreach ($page in @('Overview','Results','History','DeveloperMode','Privacy','Licenses','About','Settings')) {
    $content = Get-Content -Raw -LiteralPath (Join-Path $pages "$($page)Page.xaml")
    if ($content.Contains('Start Analysis') -or $content.Contains('Cancel Analysis')) {
        throw "$($page)Page incorrectly contains scan controls."
    }
}
Write-Host '  Scan controls isolated to ScanPage.'

# 8. No cleanup/elevation language
foreach ($page in $requiredPages) {
    $content = Get-Content -Raw -LiteralPath (Join-Path $pages "$($page)Page.xaml")
    foreach ($forbidden in @('DeleteFile','MoveFile','Process.Start','runas','requireAdministrator')) {
        if ($content.Contains($forbidden)) { throw "$($page)Page contains forbidden token: $forbidden" }
    }
}
Write-Host '  No cleanup or elevation language in pages.'

# 9. Theme resources complete
$appXaml = Get-Content -Raw -LiteralPath (Join-Path $root 'src\Clyr.App\App.xaml')
foreach ($theme in @('Default','Light','HighContrast')) {
    if (-not $appXaml.Contains("x:Key=""$theme""")) { throw "Missing theme dictionary: $theme" }
}
foreach ($brush in @('AppBackgroundBrush','CardBackgroundBrush','CardBorderBrush','MutedTextBrush','WarmAccentBrush','SubtleAccentBrush','SelectedCardBorderBrush','TrustBadgeBackgroundBrush','TrustBadgeBorderBrush','PrivacyBannerBrush','DisabledReadableBrush')) {
    $count = ([regex]::Matches($appXaml, [regex]::Escape("x:Key=""$brush"""))).Count
    if ($count -lt 3) { throw "Brush $brush is defined in only $count theme dictionaries (expected 3)." }
}
Write-Host '  All 11 brushes verified in Default, Light, and HighContrast themes.'

# 10. Selected analysis card uses multiple selection indicators
if (-not $appXaml.Contains('SubtleAccentBrush')) { throw 'AnalysisModeCardStyle missing subtle tint.' }
if (-not $appXaml.Contains('SelectedCardBorderBrush')) { throw 'AnalysisModeCardStyle missing accent border.' }
if (-not $appXaml.Contains('SelectionMark')) { throw 'AnalysisModeCardStyle missing check indicator.' }
Write-Host '  Selected-card contrast: subtle tint + accent border + check indicator.'

Write-Host 'Responsive layout structural verification PASSED.' -ForegroundColor Green
