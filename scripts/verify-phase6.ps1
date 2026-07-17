[CmdletBinding()]
param(
    [switch]$SkipGitChecks,
    [switch]$SkipUiAutomation,
    [switch]$SkipInteractiveUac
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
$env:MSBUILDDISABLENODEREUSE = '1'
$previousSkipGit = $env:CLYR_SKIP_GIT_CHECKS
if ($SkipGitChecks) { $env:CLYR_SKIP_GIT_CHECKS = '1' }

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
    $phase5Arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', '.\scripts\verify-phase5.ps1')
    if ($SkipUiAutomation) { $phase5Arguments += '-SkipUiAutomation' }
    if ($SkipGitChecks) { $phase5Arguments += '-SkipGitChecks' }
    & powershell @phase5Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Phase 0-5 regression verification failed.' }

    Invoke-Gate 'Phase 6 warning-free Release build (engine, contracts, helper, CLI, App)' @('build', 'Clyr.sln', '--configuration', 'Release', '--no-restore', '-m:1')
    Invoke-Gate 'Phase 6 complete test suite' @('test', 'Clyr.sln', '--configuration', 'Release', '--no-build', '-m:1')
    Invoke-Gate 'Phase 6 formatting check' @('format', 'Clyr.sln', '--verify-no-changes', '--no-restore')
    Invoke-Gate 'Phase 6 non-elevated execution engine tests' @('test', 'tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~ExecutionTests')
    Invoke-Gate 'Phase 6 helper and IPC tests' @('test', 'tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~HelperIpcTests')
    Invoke-Gate 'Phase 6 receipt persistence tests' @('test', 'tests/Clyr.Persistence.Tests/Clyr.Persistence.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~ExecutionReceiptStoreTests')
    Invoke-Gate 'Phase 6 CLI execution tests' @('test', 'tests/Clyr.Cli.Tests/Clyr.Cli.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~Phase6ExecutionCliTests')
    Invoke-Gate 'Phase 6 repository and UI architecture safety tests' @('test', 'tests/Clyr.Safety.Tests/Clyr.Safety.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1')
    Invoke-Gate 'Dependency vulnerability audit' @('package', 'list', '--project', 'Clyr.sln', '--vulnerable', '--include-transitive', '--no-restore')

    # Repository safety scans, scoped exactly like Clyr.Safety.Tests.RepositorySafetyTests: File/Directory
    # mutation only inside src/Clyr.Core/Execution/**, process launch only inside ElevatedHelperLauncher.cs and
    # Clyr.ElevatedHelper, requireAdministrator only in the helper's own manifest, nothing else anywhere.
    # Find-RepositoryPattern (scripts/lib/RepoScan.ps1) uses a repository-pinned or PATH ripgrep when available
    # and transparently falls back to a pure PowerShell Select-String scan otherwise, so this gate never depends
    # on an undocumented globally installed tool.
    Write-Host '==> Phase 6 repository safety scans'
    Assert-NotFound 'A file mutation primitive was found outside the reviewed execution boundary' `
        (Find-RepositoryPattern -Pattern 'File\.Delete|File\.Move|Directory\.Delete' -Paths @('src') -Include '*.cs' -ExcludeDirs @('src/Clyr.Core/Execution'))
    Assert-NotFound 'A process-launch primitive was found outside the reviewed launcher' `
        (Find-RepositoryPattern -Pattern 'Process\.Start|ProcessStartInfo|System\.Diagnostics\.Process' -Paths @('src') -Include '*.cs' -ExcludeDirs @('src/Clyr.Core/Execution/ElevatedHelperLauncher.cs'))
    Assert-NotFound 'A forbidden command primitive was found' `
        (Find-RepositoryPattern -Pattern 'RecycleOption|powershell\.exe|cmd\.exe|SHFileOperation' -Paths @('src') -Include '*.cs')
    # Package-manager/container/OS-admin command text must never appear inside the reviewed execution boundary
    # specifically (elsewhere, e.g. classification rule identifiers like "developer.npm.cache", is legitimate
    # Phase 3/5 vocabulary unrelated to command execution).
    Assert-NotFound 'A forbidden command primitive was found inside the execution boundary' `
        (Find-RepositoryPattern -Pattern 'npm\.exe|pnpm|yarn|pip\.exe|nuget\.exe|gradle|mvn |flutter|cargo |docker|wsl|dism\.exe|reg\.exe|sc\.exe|takeown|icacls|vssadmin' `
            -Paths @('src/Clyr.Core/Execution', 'src/Clyr.ElevatedHelper') -Include '*.cs')
    Assert-NotFound 'requireAdministrator must appear only in src/Clyr.ElevatedHelper/app.manifest, not in source' `
        (Find-RepositoryPattern -Pattern 'requireAdministrator' -Paths @('src') -Include '*.cs')
    if (-not (Get-Content -Raw -LiteralPath '.\src\Clyr.ElevatedHelper\app.manifest').Contains('requireAdministrator')) {
        throw 'The elevated helper manifest no longer requests administrator elevation.'
    }
    if ((Get-Content -Raw -LiteralPath '.\src\Clyr.App\app.manifest').Contains('requireAdministrator')) {
        throw 'The main CLYR application manifest must remain asInvoker.'
    }
    Write-Host '  Repository safety scans passed: mutation and process-launch primitives are confined to the reviewed Phase 6 boundary.'

    Assert-NotFound 'A credential-like value was found' `
        (Find-RepositoryPattern -Pattern '(AKIA[0-9A-Z]{16}|gh[pousr]_[A-Za-z0-9]{30,}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----)' -Paths @('src', 'tests', 'scripts', 'docs', '.github', 'rules'))
    $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    Assert-NotFound 'A machine-specific user path was found' `
        (Find-RepositoryPattern -Pattern $userProfile -Literal -Paths @('src', 'tests', 'scripts', 'docs', '.github', 'rules', 'README.md', 'PHASE_STATUS.md', 'ROADMAP.md', 'CHANGELOG.md'))
    Assert-NotFound 'Trailing whitespace was found' `
        (Find-RepositoryPattern -Pattern '[ \t]+\r?$' -Paths @('src', 'tests', 'scripts', 'docs', '.github', 'rules', 'README.md', 'PHASE_STATUS.md', 'ROADMAP.md', 'CHANGELOG.md'))
    Write-Host '  Credential, machine-path, and whitespace scans passed.'

    if (-not $SkipUiAutomation) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-winui.ps1
        if ($LASTEXITCODE -ne 0) { throw 'Phase 6 WinUI Automation (including fixture-only execution flow) failed.' }
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

    Write-Host 'Phase 6 noninteractive verification PASSED: engine, helper, IPC, receipt persistence, CLI, and WinUI execution flow are built and tested.' -ForegroundColor Green

    $uacStatus = 'PENDING'
    if (-not $SkipInteractiveUac) {
        Write-Host '==> Real fixture-only UAC smoke test (requires an interactive desktop session)'
        & powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-phase6-uac-smoke.ps1
        if ($LASTEXITCODE -eq 0) { $uacStatus = 'PASS' } else { $uacStatus = 'FAIL' }
    }
    else {
        Write-Host '  Interactive UAC smoke test skipped (-SkipInteractiveUac).' -ForegroundColor Yellow
    }

    if ($uacStatus -eq 'PASS') {
        Write-Host 'PHASE 6 STATUS: All noninteractive and interactive requirements pass. Phase 6 is ready for final approval.' -ForegroundColor Green
    }
    elseif ($uacStatus -eq 'FAIL') {
        Write-Host 'PHASE 6 STATUS: The real fixture-only UAC smoke test FAILED. Phase 6 is not complete.' -ForegroundColor Red
        exit 1
    }
    else {
        Write-Host 'Every noninteractive Phase 6 requirement passes. Final approval remains blocked only by the real fixture-only UAC smoke test.' -ForegroundColor Yellow
    }
}
finally {
    if ($null -eq $previousSkipGit) { Remove-Item Env:CLYR_SKIP_GIT_CHECKS -ErrorAction SilentlyContinue }
    else { $env:CLYR_SKIP_GIT_CHECKS = $previousSkipGit }
    Pop-Location
}
