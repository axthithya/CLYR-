[CmdletBinding()]
param(
    [switch]$SkipGitChecks,
    [switch]$SkipUiAutomation
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
$env:MSBUILDDISABLENODEREUSE = '1'
$previousSkipGit = $env:CLYR_SKIP_GIT_CHECKS
if ($SkipGitChecks) { $env:CLYR_SKIP_GIT_CHECKS = '1' }

function Invoke-Gate([string]$name, [string[]]$arguments) {
    Write-Host "==> $name"
    & $dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "$name failed with exit code $LASTEXITCODE." }
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
    $mutationOutsideBoundary = rg -n 'File\.Delete|File\.Move|Directory\.Delete' src --glob '*.cs' --glob '!src/Clyr.Core/Execution/**'
    if ($LASTEXITCODE -eq 0) { throw ('A file mutation primitive was found outside the reviewed execution boundary: ' + ($mutationOutsideBoundary -join '; ')) }
    $launchOutsideBoundary = rg -n 'Process\.Start|ProcessStartInfo|System\.Diagnostics\.Process' src --glob '*.cs' --glob '!src/Clyr.Core/Execution/ElevatedHelperLauncher.cs'
    if ($LASTEXITCODE -eq 0) { throw ('A process-launch primitive was found outside the reviewed launcher: ' + ($launchOutsideBoundary -join '; ')) }
    $forbiddenAlways = rg -n 'RecycleOption|powershell\.exe|cmd\.exe|SHFileOperation' src --glob '*.cs'
    if ($LASTEXITCODE -eq 0) { throw ('A forbidden command primitive was found: ' + ($forbiddenAlways -join '; ')) }
    # Package-manager/container/OS-admin command text must never appear inside the reviewed execution boundary
    # specifically (elsewhere, e.g. classification rule identifiers like "developer.npm.cache", is legitimate
    # Phase 3/5 vocabulary unrelated to command execution).
    $forbiddenInBoundary = rg -n 'npm\.exe|pnpm|yarn|pip\.exe|nuget\.exe|gradle|mvn |flutter|cargo |docker|wsl|dism\.exe|reg\.exe|sc\.exe|takeown|icacls|vssadmin' `
        src/Clyr.Core/Execution src/Clyr.ElevatedHelper --glob '*.cs'
    if ($LASTEXITCODE -eq 0) { throw ('A forbidden command primitive was found inside the execution boundary: ' + ($forbiddenInBoundary -join '; ')) }
    $requireAdminOutsideManifest = rg -n 'requireAdministrator' src --glob '*.cs'
    if ($LASTEXITCODE -eq 0) { throw ('requireAdministrator must appear only in src/Clyr.ElevatedHelper/app.manifest, not in source: ' + ($requireAdminOutsideManifest -join '; ')) }
    if (-not (Get-Content -Raw -LiteralPath '.\src\Clyr.ElevatedHelper\app.manifest').Contains('requireAdministrator')) {
        throw 'The elevated helper manifest no longer requests administrator elevation.'
    }
    if ((Get-Content -Raw -LiteralPath '.\src\Clyr.App\app.manifest').Contains('requireAdministrator')) {
        throw 'The main CLYR application manifest must remain asInvoker.'
    }
    Write-Host '  Repository safety scans passed: mutation and process-launch primitives are confined to the reviewed Phase 6 boundary.'

    $secrets = rg -n '(AKIA[0-9A-Z]{16}|gh[pousr]_[A-Za-z0-9]{30,}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----)' src tests scripts docs .github rules --glob '!**/bin/**' --glob '!**/obj/**'
    if ($LASTEXITCODE -eq 0) { throw ('A credential-like value was found: ' + ($secrets -join '; ')) }
    $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    $machinePaths = rg -n -F $userProfile src tests scripts docs .github rules README.md PHASE_STATUS.md ROADMAP.md CHANGELOG.md
    if ($LASTEXITCODE -eq 0) { throw ('A machine-specific user path was found: ' + ($machinePaths -join '; ')) }
    $trailing = rg -n '[ \t]+\r?$' src tests scripts docs .github rules README.md PHASE_STATUS.md ROADMAP.md CHANGELOG.md --glob '!**/bin/**' --glob '!**/obj/**'
    if ($LASTEXITCODE -eq 0) { throw ('Trailing whitespace was found: ' + ($trailing -join '; ')) }
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
    Write-Host 'This script does not run the real UAC elevation prompt. See scripts/run-phase6-uac-smoke.ps1 and its own PASS/FAIL output.' -ForegroundColor Yellow
    Write-Host 'Phase 6 implementation is ready for final approval, but Phase 6 remains incomplete until the fixture-only UAC smoke test passes.' -ForegroundColor Yellow
}
finally {
    if ($null -eq $previousSkipGit) { Remove-Item Env:CLYR_SKIP_GIT_CHECKS -ErrorAction SilentlyContinue }
    else { $env:CLYR_SKIP_GIT_CHECKS = $previousSkipGit }
    Pop-Location
}
