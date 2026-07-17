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
    $phase6Arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', '.\scripts\verify-phase6.ps1', '-SkipInteractiveUac')
    if ($SkipUiAutomation) { $phase6Arguments += '-SkipUiAutomation' }
    if ($SkipGitChecks) { $phase6Arguments += '-SkipGitChecks' }
    & powershell @phase6Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Phase 0-6 regression verification failed.' }

    Invoke-Gate 'Phase 7 warning-free Release build (Developer Mode: Core, CLI, App)' @('build', 'Clyr.sln', '--configuration', 'Release', '--no-restore', '-m:1')
    Invoke-Gate 'Phase 7 complete test suite' @('test', 'Clyr.sln', '--configuration', 'Release', '--no-build', '-m:1')
    Invoke-Gate 'Phase 7 formatting check' @('format', 'Clyr.sln', '--verify-no-changes', '--no-restore')
    Invoke-Gate 'Phase 7 Developer Mode taxonomy/report-builder/locator/probe/registry tests' @('test', 'tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~DeveloperMode')
    Invoke-Gate 'Phase 7 Developer Mode CLI tests' @('test', 'tests/Clyr.Cli.Tests/Clyr.Cli.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~Phase7DeveloperModeCliTests')
    Invoke-Gate 'Phase 7 repository and UI architecture safety tests' @('test', 'tests/Clyr.Safety.Tests/Clyr.Safety.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1')
    Invoke-Gate 'Dependency vulnerability audit' @('package', 'list', '--project', 'Clyr.sln', '--vulnerable', '--include-transitive', '--no-restore')

    # Repository safety scans specific to the Phase 7 developer-tool detection boundary. The narrow read-only
    # probe runner (docker --version / wsl --status only) is the one new, reviewed Process.Start call site
    # outside the Phase 6 elevated-helper launcher; nothing else may launch a process, and no developer-tool
    # mutation, install, update, or delete-all verb may appear anywhere in source.
    Write-Host '==> Phase 7 repository safety scans'
    Assert-NotFound 'A process-launch primitive was found outside the reviewed launcher/probe boundary' `
        (Find-RepositoryPattern -Pattern 'Process\.Start|ProcessStartInfo|System\.Diagnostics\.Process' -Paths @('src') -Include '*.cs' `
            -ExcludeDirs @('src/Clyr.Core/Execution/ElevatedHelperLauncher.cs', 'src/Clyr.Core/DeveloperMode/DeveloperToolProbeRunner.cs'))
    Assert-NotFound 'A developer-tool mutation or generic-shell primitive was found' `
        (Find-RepositoryPattern -Pattern 'system prune|volume rm|--unregister|--force-remove|npm install|npm update|pip install|cargo install|docker rmi|docker rm |powershell\.exe|cmd\.exe|/c "' -Paths @('src') -Include '*.cs')
    Assert-NotFound 'A generic command-execution CLI surface was found (developer run/--command/--exe/--args/--path)' `
        (Find-RepositoryPattern -Pattern '"developer",\s*"run"|--command|--exe\b|--args\b' -Paths @('src/Clyr.Cli') -Include '*.cs')
    Write-Host '  Repository safety scans passed: the narrow read-only probe boundary is intact and no mutation verb exists anywhere.'

    Assert-NotFound 'A credential-like value was found' `
        (Find-RepositoryPattern -Pattern '(AKIA[0-9A-Z]{16}|gh[pousr]_[A-Za-z0-9]{30,}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----)' -Paths @('src', 'tests', 'scripts', 'docs', '.github', 'rules'))
    $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    Assert-NotFound 'A machine-specific user path was found' `
        (Find-RepositoryPattern -Pattern $userProfile -Literal -Paths @('src', 'tests', 'scripts', 'docs', '.github', 'rules', 'README.md', 'PHASE_STATUS.md', 'ROADMAP.md', 'CHANGELOG.md'))
    Assert-NotFound 'Trailing whitespace was found' `
        (Find-RepositoryPattern -Pattern '[ \t]+\r?$' -Paths @('src', 'tests', 'scripts', 'docs', '.github', 'rules', 'README.md', 'PHASE_STATUS.md', 'ROADMAP.md', 'CHANGELOG.md'))
    Write-Host '  Credential, machine-path, and whitespace scans passed.'

    if (-not $SkipGitChecks) {
        & git diff --check
        if ($LASTEXITCODE -ne 0) { throw 'git diff --check failed.' }
    }
    else {
        Write-Host '  git diff --check skipped (-SkipGitChecks).' -ForegroundColor DarkYellow
    }

    Write-Host 'PHASE 7 STATUS: Developer Mode detection (Core taxonomy/registry/probe, CLI, WinUI) is built, tested, and safety-scanned. Ready for review.' -ForegroundColor Green
}
finally {
    if ($null -eq $previousSkipGit) { Remove-Item Env:CLYR_SKIP_GIT_CHECKS -ErrorAction SilentlyContinue }
    else { $env:CLYR_SKIP_GIT_CHECKS = $previousSkipGit }
    Pop-Location
}
