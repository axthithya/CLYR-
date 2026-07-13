[CmdletBinding()]
param([switch]$SkipUiAutomation)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
$env:MSBUILDDISABLENODEREUSE = '1'

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

    $pages = @('Overview', 'Scan', 'Results', 'History', 'DeveloperMode', 'Privacy', 'Licenses', 'About', 'Settings')
    foreach ($page in $pages) {
        $xaml = Join-Path $root "src\Clyr.App\Pages\$($page)Page.xaml"
        if (-not (Test-Path -LiteralPath $xaml -PathType Leaf)) { throw "Missing distinct page: $xaml" }
        $content = Get-Content -Raw -LiteralPath $xaml
        foreach ($required in @('VerticalScrollBarVisibility="Auto"', 'VerticalScrollMode="Auto"', 'HorizontalScrollBarVisibility="Disabled"')) {
            if (-not $content.Contains($required)) { throw "$($page)Page is missing scrolling contract: $required" }
        }
    }

    [string[]]$scanControls = @(rg -l 'Start Analysis|Cancel Analysis' src/Clyr.App/Pages --glob '*.xaml')
    if ($LASTEXITCODE -ne 0 -or $scanControls.Count -ne 1 -or -not $scanControls[0].EndsWith('ScanPage.xaml', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Full scan controls must exist only on ScanPage.xaml.'
    }

    & powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-responsive-layout.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Responsive layout structural verification failed.' }

    $forbidden = rg -n 'DeleteFile|MoveFile|Process\.Start|runas|FileMode\.Open.*FileAccess\.Write' src --glob '*.cs'
    if ($LASTEXITCODE -eq 0) { throw "A forbidden mutation/elevation primitive was found:`n$forbidden" }

    if (-not $SkipUiAutomation) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-winui.ps1
        if ($LASTEXITCODE -ne 0) { throw 'Phase 4.1 UI Automation failed.' }
    }

    Write-Host 'Phase 4.1 verification PASSED. Polished read-only UI only; no cleanup, planning, elevation, filesystem mutation, Phase 5 behavior, or Git mutation.' -ForegroundColor Green
}
finally { Pop-Location }
