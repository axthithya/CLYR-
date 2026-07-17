[CmdletBinding()]
param([switch]$SkipUiAutomation)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
$env:MSBUILDDISABLENODEREUSE = '1'

. (Join-Path $PSScriptRoot 'lib\RepoScan.ps1')

function Invoke-Gate([string]$name, [string[]]$arguments) {
    Write-Host "==> $name"
    & $dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "$name failed with exit code $LASTEXITCODE." }
}

Push-Location $root
try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-phase4.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Phase 4 regression verification failed.' }

    Invoke-Gate 'Phase 4.1 warning-free Release build' @('build', 'Clyr.sln', '--configuration', 'Release', '--no-restore', '-m:1')
    Invoke-Gate 'Phase 4.1 safety and UI architecture tests' @('test', 'tests/Clyr.Safety.Tests/Clyr.Safety.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1')
    Invoke-Gate 'Phase 4.1 formatting check' @('format', 'Clyr.sln', '--verify-no-changes', '--no-restore')

    $pages = @('Overview', 'Scan', 'Results', 'ReviewPlan', 'History', 'DeveloperMode', 'Privacy', 'Licenses', 'About', 'Settings')
    $hostXaml = Get-Content -Raw -LiteralPath .\src\Clyr.App\Controls\ResponsivePageHost.xaml
    foreach ($required in @('VerticalScrollBarVisibility="Auto"', 'VerticalScrollMode="Auto"', 'HorizontalScrollBarVisibility="Disabled"', 'HorizontalScrollMode="Disabled"', 'MaxWidth="1120"')) {
        if (-not $hostXaml.Contains($required)) { throw "ResponsivePageHost is missing layout contract: $required" }
    }
    foreach ($page in $pages) {
        $xaml = Join-Path $root "src\Clyr.App\Pages\$($page)Page.xaml"
        if (-not (Test-Path -LiteralPath $xaml -PathType Leaf)) { throw "Missing distinct page: $xaml" }
        $content = Get-Content -Raw -LiteralPath $xaml
        if (-not $content.Contains('<controls:ResponsivePageHost')) { throw "$($page)Page does not use ResponsivePageHost." }
        if ($content.Contains('<ScrollViewer')) { throw "$($page)Page defines an independent ScrollViewer." }
        if ($content.Contains('MaxWidth=')) { throw "$($page)Page defines an independent maximum content width." }
    }

    $scanControlResult = Find-RepositoryPattern -Pattern 'Start Analysis|Cancel Analysis' -Paths @('src/Clyr.App/Pages') -Include '*.xaml'
    [string[]]$scanControlFiles = @($scanControlResult.Matches | ForEach-Object { ($_ -split ':', 2)[0] } | Sort-Object -Unique)
    if ($scanControlFiles.Count -ne 1 -or -not $scanControlFiles[0].EndsWith('ScanPage.xaml', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Full scan controls must exist only on ScanPage.xaml.'
    }

    & powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-responsive-layout.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Responsive layout structural verification failed.' }

    # Phase 6 (approved after Phase 4.1) narrowly permits process launch/elevation inside ElevatedHelperLauncher.cs
    # and the Clyr.ElevatedHelper project only; Clyr.Safety.Tests.RepositorySafetyTests enforces this precisely.
    $forbiddenResult = Find-RepositoryPattern -Pattern 'DeleteFile|MoveFile|Process\.Start|runas|FileMode\.Open.*FileAccess\.Write' `
        -Paths @('src') -Include '*.cs' -ExcludeDirs @('src/Clyr.Core/Execution', 'src/Clyr.ElevatedHelper')
    if ($forbiddenResult.Found) { throw "A forbidden mutation/elevation primitive was found:`n$($forbiddenResult.Matches -join "`n")" }

    if (-not $SkipUiAutomation) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-winui.ps1
        if ($LASTEXITCODE -ne 0) { throw 'Phase 4.1 UI Automation failed.' }
    }

    Write-Host 'Phase 4.1 regression verification PASSED. The committed responsive architecture remains intact; no mutation, elevation, helper, or Git mutation.' -ForegroundColor Green
}
finally { Pop-Location }
