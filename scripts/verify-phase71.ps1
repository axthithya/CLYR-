[CmdletBinding()]
param(
    [switch]$SkipGitChecks
)

<#
Verifies Phase 7.1 — Scan semantics and reliable Quick Analysis: the adaptive, known-root-priority Quick
traversal that replaces the old unconditional 8-second cutoff; the PolicyBoundary vs. real-warning severity
distinction (an intentional Quick policy limit must never read as a scan error); and the CLYR-owned Quick
Analysis checkpoint/continuation capability. This is a focused companion to scripts/verify-scan-modes.ps1 and
scripts/verify-phase7.ps1 (neither of which it replaces or re-runs in full).
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
    Invoke-Gate 'Phase 7.1 warning-free Release build' @('build', 'Clyr.sln', '--configuration', 'Release', '--no-restore', '-m:1')

    Invoke-Gate 'Quick adaptive traversal tests (known-root priority, policy-boundary vs. warning, checkpoint round-trip)' `
        @('test', 'tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~QuickAdaptiveTraversalTests')
    Invoke-Gate 'Quick/Deep bounded-vs-unbounded strategy tests (regression)' `
        @('test', 'tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~QuickAndDeepBoundsTests')
    Invoke-Gate 'Existing scan engine regression tests (depth limit, cancellation, reparse, cloud, access-denied)' `
        @('test', 'tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~ScanningTests|FullyQualifiedName~ScanLifecycleTests')
    Invoke-Gate 'Scan-mode selection and accounting tests (regression)' `
        @('test', 'tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~ScanModeSelectorTests|FullyQualifiedName~ScanUiLifecycleTests|FullyQualifiedName~ScanAccountingTests')
    Invoke-Gate 'Quick Analysis checkpoint store tests (round-trip, root/mode isolation, staleness, corrupt-file safety)' `
        @('test', 'tests/Clyr.Persistence.Tests/Clyr.Persistence.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~ScanCheckpointStoreTests')
    Invoke-Gate 'CLI scan tests (including --no-persist and --continue)' `
        @('test', 'tests/Clyr.Cli.Tests/Clyr.Cli.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~PhaseTwoCliTests')
    Invoke-Gate 'Repository and UI architecture safety tests (including the reviewed checkpoint-store mutation carve-out)' `
        @('test', 'tests/Clyr.Safety.Tests/Clyr.Safety.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1')
    Invoke-Gate 'Formatting check' @('format', 'Clyr.sln', '--verify-no-changes', '--no-restore')

    Write-Host '==> Phase 7.1 repository safety scans'
    $scanCoreFiles = @('src/Clyr.Core/Scanning.cs', 'src/Clyr.Windows/WindowsScanning.cs')

    Assert-NotFound 'A mutation API was found in scan production code (checkpoint persistence must live only in Clyr.Persistence)' `
        (Find-RepositoryPattern -Pattern 'File\.Delete|Directory\.Delete|File\.Move|Directory\.Move|File\.WriteAllText|File\.WriteAllBytes|File\.AppendAllText|File\.Create\(|File\.OpenWrite|File\.SetAttributes|File\.Replace|FileSecurity|DirectorySecurity|SetAccessControl' `
            -Paths $scanCoreFiles)
    Assert-NotFound 'The checkpoint store writes outside a CLYR-owned "checkpoints" application-data path' `
        (Find-RepositoryPattern -Pattern 'Environment\.GetFolderPath\(Environment\.SpecialFolder\.Desktop|Environment\.SpecialFolder\.MyDocuments|Environment\.SpecialFolder\.UserProfile' `
            -Paths @('src/Clyr.Persistence/ScanCheckpointStore.cs'))
    Assert-NotFound 'A process-launch or shell primitive was found in scan production code' `
        (Find-RepositoryPattern -Pattern 'Process\.Start|ProcessStartInfo|System\.Diagnostics\.Process|powershell\.exe|cmd\.exe|runas|requireAdministrator' `
            -Paths @('src/Clyr.Core/Scanning.cs', 'src/Clyr.Windows/WindowsScanning.cs', 'src/Clyr.Persistence/ScanCheckpointStore.cs'))
    Assert-NotFound 'A Phase 6 execution/cleanup or Phase 8 move reference was found in scan production code' `
        (Find-RepositoryPattern -Pattern 'NonElevatedCleanupExecutor|CleanupPlanBuilder|ExecutionTokenService|ElevatedHelperLauncher|CleanupCandidateFactory|BuiltInExecutionActions|MoveKnownFolder' `
            -Paths @('src/Clyr.Core/Scanning.cs', 'src/Clyr.Windows/WindowsScanning.cs', 'src/Clyr.Persistence/ScanCheckpointStore.cs'))
    Write-Host '  Phase 7.1 safety scans passed: no scan-engine mutation, checkpoint mutation confined to its own reviewed file and CLYR app-data path, no process launch, no Phase 6/8 reference.'

    if (-not $SkipGitChecks) {
        & git diff --check
        if ($LASTEXITCODE -ne 0) { throw 'git diff --check failed.' }
    }
    else {
        Write-Host '  git diff --check skipped (-SkipGitChecks).' -ForegroundColor DarkYellow
    }

    Write-Host 'PHASE 7.1 VERIFICATION PASSED: adaptive known-root-priority Quick traversal, honest PolicyBoundary-vs-warning status, and CLYR-owned Quick Analysis checkpoint/continuation are all built, tested, and safety-scanned.' -ForegroundColor Green
}
finally {
    Pop-Location
}
