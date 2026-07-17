[CmdletBinding()]
param(
    [switch]$SkipGitChecks
)

<#
Verifies Phase 7.2 — Complete Deep Analysis and storage accounting:
  7.2.1 Deep's traversal has no configured depth ceiling (int.MaxValue, not a large-but-finite constant).
  7.2.2 Real, read-only allocated-size accounting (GetCompressedFileSizeW) kept strictly separate from logical
        (namespace) size — never invented, never mixed.
  7.2.3 Real, read-only NTFS hard-link identity accounting (GetFileInformationByHandle) so visible hard-linked
        paths are de-duplicated out of unique allocated bytes rather than double-counted as reclaimable space.
  7.2.5 No silent clamping: AccountingConsistency names exactly why a figure is not directly comparable instead
        of the scan silently flooring a negative remainder to zero or a percentage to 100%.
  7.2.7 The CLI's human-readable summary surfaces allocation, hard-link, and consistency figures honestly.

7.2.4 (deeper volume-level metadata/reserved-storage buckets) and 7.2.6 (the separate elevated read-only
scanner) are verified by their own dedicated sections once implemented — see the Phase 7.2 completion report for
current status; this script does not claim to cover requirements it does not check.
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
    Invoke-Gate 'Phase 7.2 warning-free Release build' @('build', 'Clyr.sln', '--configuration', 'Release', '--no-restore', '-m:1')

    Invoke-Gate 'Deep unbounded-depth regression tests' `
        @('test', 'tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~QuickAndDeepBoundsTests')
    Invoke-Gate 'Allocation and hard-link accounting aggregation tests' `
        @('test', 'tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~AllocationAccountingTests')
    Invoke-Gate 'Accounting-consistency tests (no silent clamping)' `
        @('test', 'tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~ScanAccountingTests')
    Invoke-Gate 'Real Windows allocated-size and hard-link identity tests (real files, real hard link)' `
        @('test', 'tests/Clyr.Windows.Tests/Clyr.Windows.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1')
    Invoke-Gate 'Existing scan engine and scan-mode regression tests' `
        @('test', 'tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~ScanningTests|FullyQualifiedName~ScanLifecycleTests|FullyQualifiedName~ScanModeSelectorTests')
    Invoke-Gate 'Repository and UI architecture safety tests' `
        @('test', 'tests/Clyr.Safety.Tests/Clyr.Safety.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1')
    Invoke-Gate 'Formatting check' @('format', 'Clyr.sln', '--verify-no-changes', '--no-restore')

    Write-Host '==> Phase 7.2 repository safety scans'
    Assert-NotFound 'A mutation or content-read API was found in the Windows metadata layer' `
        (Find-RepositoryPattern -Pattern 'File\.Delete|Directory\.Delete|File\.Move|Directory\.Move|File\.WriteAllText|File\.WriteAllBytes|File\.AppendAllText|File\.OpenWrite|File\.SetAttributes|FileSecurity|DirectorySecurity|SetAccessControl|OpenRead|ReadAllBytes|ReadAllText|FileStream|GENERIC_WRITE' `
            -Paths @('src/Clyr.Windows/WindowsScanning.cs', 'src/Clyr.Windows/WindowsFileIdentity.cs', 'src/Clyr.Core/Scanning.cs'))
    Assert-NotFound 'A process-launch or shell primitive was found in the Windows metadata layer' `
        (Find-RepositoryPattern -Pattern 'Process\.Start|ProcessStartInfo|System\.Diagnostics\.Process|powershell\.exe|cmd\.exe|runas|requireAdministrator' `
            -Paths @('src/Clyr.Windows/WindowsScanning.cs', 'src/Clyr.Windows/WindowsFileIdentity.cs'))
    Write-Host '  Phase 7.2 safety scans passed: allocation/identity metadata layer is read-only, no process launch.'

    if (-not $SkipGitChecks) {
        & git diff --check
        if ($LASTEXITCODE -ne 0) { throw 'git diff --check failed.' }
    }
    else {
        Write-Host '  git diff --check skipped (-SkipGitChecks).' -ForegroundColor DarkYellow
    }

    Write-Host 'PHASE 7.2 (accounting scope) VERIFICATION PASSED: unbounded Deep depth, real allocated-size and hard-link-aware accounting, and no-silent-clamping accounting consistency are all built, tested (including against real files and a real hard link), and safety-scanned. 7.2.4 (deeper volume-level buckets) and 7.2.6 (elevated read-only scanner) are tracked separately.' -ForegroundColor Green
}
finally {
    Pop-Location
}
