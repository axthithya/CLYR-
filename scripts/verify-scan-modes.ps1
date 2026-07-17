[CmdletBinding()]
param(
    [switch]$SkipGitChecks,
    [switch]$SkipUiAutomation
)

<#
Verifies the scan-mode and scan-engine correction: exclusive/toggleable Quick/Deep selection, the explicit
scan lifecycle, Quick's documented bounded strategy, Deep's unbounded recursive strategy, truthful accounting,
result isolation across rescans, and the strict read-only safety boundary. This is a focused companion to
scripts/verify-phase7.ps1 (which it does not replace or re-run in full) — see docs for the exact requirements
this checks.
#>

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

function Assert-NotFound([string]$description, [hashtable]$result) {
    if ($result.Found) { throw "$description`: $($result.Matches -join '; ')" }
}

Push-Location $root
try {
    Invoke-Gate 'Scan-mode warning-free Release build' @('build', 'Clyr.sln', '--configuration', 'Release', '--no-restore', '-m:1')

    Invoke-Gate 'Scan-mode selection tests (exclusive toggle, never-both-selected)' `
        @('test', 'tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~ScanModeSelectorTests')
    Invoke-Gate 'Scan lifecycle tests (idle/scanning/cancelling/terminal states, Run Again wording)' `
        @('test', 'tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~ScanUiLifecycleTests')
    Invoke-Gate 'Scan accounting tests (quality bands, invariants, documented regression example)' `
        @('test', 'tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~ScanAccountingTests')
    Invoke-Gate 'Quick/Deep bounded-vs-unbounded strategy tests' `
        @('test', 'tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~QuickAndDeepBoundsTests')
    Invoke-Gate 'Existing scan engine regression tests (depth limit, cancellation, reparse, cloud, access-denied)' `
        @('test', 'tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~ScanningTests|FullyQualifiedName~ScanLifecycleTests')
    Invoke-Gate 'CLI scan tests (including --no-persist)' `
        @('test', 'tests/Clyr.Cli.Tests/Clyr.Cli.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~PhaseTwoCliTests')
    Invoke-Gate 'Repository and UI architecture safety tests' `
        @('test', 'tests/Clyr.Safety.Tests/Clyr.Safety.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1')
    Invoke-Gate 'Formatting check' @('format', 'Clyr.sln', '--verify-no-changes', '--no-restore')

    Write-Host '==> Scan-mode repository safety scans'
    $scanCoreFiles = @('src/Clyr.Core/Scanning.cs', 'src/Clyr.Core/ScanUx.cs', 'src/Clyr.Core/ScanAccounting.cs', 'src/Clyr.Windows/WindowsScanning.cs')

    Assert-NotFound 'A mutation API was found in scan production code' `
        (Find-RepositoryPattern -Pattern 'File\.Delete|Directory\.Delete|File\.Move|Directory\.Move|File\.WriteAllText|File\.WriteAllBytes|File\.AppendAllText|File\.Create\(|File\.OpenWrite|File\.SetAttributes|File\.Replace|FileSecurity|DirectorySecurity|SetAccessControl' `
            -Paths $scanCoreFiles)
    Assert-NotFound 'A process-launch or shell primitive was found in scan production code' `
        (Find-RepositoryPattern -Pattern 'Process\.Start|ProcessStartInfo|System\.Diagnostics\.Process|powershell\.exe|cmd\.exe|runas|requireAdministrator' `
            -Paths @('src/Clyr.Core/Scanning.cs', 'src/Clyr.Windows/WindowsScanning.cs'))
    Assert-NotFound 'A Phase 6 execution/cleanup or Phase 8 move reference was found in scan production code' `
        (Find-RepositoryPattern -Pattern 'NonElevatedCleanupExecutor|CleanupPlanBuilder|ExecutionTokenService|ElevatedHelperLauncher|CleanupCandidateFactory|BuiltInExecutionActions|MoveKnownFolder' `
            -Paths @('src/Clyr.Core/Scanning.cs', 'src/Clyr.Windows/WindowsScanning.cs'))
    Assert-NotFound 'A cleanup, deletion, or move-to-drive control was found on the Scan page' `
        (Find-RepositoryPattern -Pattern 'Delete|Clean now|Fix everything|Move to drive|Select destination drive|Recycle Bin' `
            -Paths @('src/Clyr.App/Pages/ScanPage.xaml'))
    Assert-NotFound 'A hard-coded initial-selection literal was found on the Scan page (SelectedScanMode must start at None)' `
        (Find-RepositoryPattern -Pattern 'IsChecked="True"' -Paths @('src/Clyr.App/Pages/ScanPage.xaml') -Literal)
    Write-Host '  Scan-mode safety scans passed: no mutation, no process launch, no Phase 6/8 reference, no cleanup control, no forced initial selection.'

    if (-not $SkipUiAutomation) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-winui.ps1
        if ($LASTEXITCODE -ne 0) { throw 'WinUI Automation (including the scan-mode selection/lifecycle flow) failed.' }
    }
    else {
        Write-Host '  WinUI Automation skipped (-SkipUiAutomation). This requires an interactive desktop session.' -ForegroundColor DarkYellow
    }

    if (-not $SkipGitChecks) {
        & git diff --check
        if ($LASTEXITCODE -ne 0) { throw 'git diff --check failed.' }
    }
    else {
        Write-Host '  git diff --check skipped (-SkipGitChecks).' -ForegroundColor DarkYellow
    }

    Write-Host 'SCAN-MODE CORRECTION VERIFICATION PASSED: exclusive toggleable selection, explicit lifecycle, Quick/Deep bounded-vs-unbounded strategies, truthful accounting, result isolation, and the read-only safety boundary are all built, tested, and safety-scanned.' -ForegroundColor Green
}
finally {
    Pop-Location
}
