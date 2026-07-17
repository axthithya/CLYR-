[CmdletBinding()]
param(
    [switch]$SkipGitChecks,
    [switch]$SkipUiAutomation
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.tools\\dotnet\\dotnet.exe'
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

Push-Location $root
try {
    $phase41Arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', '.\\scripts\\verify-phase41.ps1')
    if ($SkipUiAutomation) { $phase41Arguments += '-SkipUiAutomation' }
    & powershell @phase41Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Phase 0–4.1 regression verification failed.' }

    Invoke-Gate 'Phase 5 warning-free Release build' @('build', 'Clyr.sln', '--configuration', 'Release', '--no-restore', '-m:1')
    Invoke-Gate 'Phase 5 complete test suite' @('test', 'Clyr.sln', '--configuration', 'Release', '--no-build', '-m:1')
    Invoke-Gate 'Phase 5 formatting check' @('format', 'Clyr.sln', '--verify-no-changes', '--no-restore')
    Invoke-Gate 'Phase 5 core planning and security tests' @('test', 'tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~Cleanup')
    Invoke-Gate 'Phase 5 CLI planning tests' @('test', 'tests/Clyr.Cli.Tests/Clyr.Cli.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1', '--filter', 'FullyQualifiedName~PhaseFive')
    Invoke-Gate 'Phase 5 rule and schema tests' @('test', 'tests/Clyr.Rules.Tests/Clyr.Rules.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1')
    Invoke-Gate 'Phase 5 repository and UI safety tests' @('test', 'tests/Clyr.Safety.Tests/Clyr.Safety.Tests.csproj', '--configuration', 'Release', '--no-build', '-m:1')
    Invoke-Gate 'Dependency vulnerability audit' @('package', 'list', '--project', 'Clyr.sln', '--vulnerable', '--include-transitive', '--no-restore')
    Invoke-Gate 'CLI planning help' @('run', '--project', 'src/Clyr.Cli/Clyr.Cli.csproj', '--configuration', 'Release', '--no-build', '--', '--help')

    foreach ($json in @(
        '.\\rules\\schemas\\cleanup-plan-report.schema.json',
        '.\\rules\\examples\\cleanup-plan-report.valid.json',
        '.\\rules\\examples\\cleanup-plan-report.invalid.json'
    )) { Get-Content -Raw -LiteralPath $json | ConvertFrom-Json | Out-Null }

    $contracts = Get-Content -Raw -LiteralPath '.\\src\\Clyr.Contracts\\CleanupPlanning.cs'
    $core = (Get-ChildItem -LiteralPath '.\\src\\Clyr.Core' -Filter '*Cleanup*.cs' |
        ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join [Environment]::NewLine
    foreach ($required in @(
        'CleanupEligibility', 'DryRunEligible', 'ManualReviewOnly', 'Protected',
        'CleanupPlanId', 'PlanBinding', 'PlanExpiry', 'ProtectedPathViolation',
        'ExecutionNotAvailableInPhase5', 'CleanupPlanCanonicalizer', 'SHA256',
        'WindowsPathSafetyValidator', 'ICleanupExecutor'
    )) {
        if (-not (($contracts + $core).Contains($required))) { throw "Phase 5 contract is missing: $required" }
    }

    # Phase 6 (approved after Phase 5) narrowly permits deletion inside src/Clyr.Core/Execution/** and process
    # launch/elevation only inside ElevatedHelperLauncher.cs and the Clyr.ElevatedHelper project; both exceptions
    # are enforced precisely by Clyr.Safety.Tests.RepositorySafetyTests, which is the authoritative check. This
    # scan is excluded from those two locations so it continues to guard everything else in the repository.
    $forbiddenSourceResult = Find-RepositoryPattern -Pattern 'File\.Delete|File\.Move|Directory\.Delete|RecycleOption|Process\.Start|System\.Diagnostics\.Process|runas|powershell\.exe|cmd\.exe|ShellExecute|SHFileOperation' `
        -Paths @('src') -Include '*.cs' -ExcludeDirs @('src/Clyr.Core/Execution', 'src/Clyr.ElevatedHelper')
    if ($forbiddenSourceResult.Found) { throw ('A forbidden cleanup/process/elevation primitive was found: ' + ($forbiddenSourceResult.Matches -join '; ')) }
    # 'plan execute' is a legitimate Phase 6 CLI command narrowly implemented in these three reviewed files.
    $forbiddenCliResult = Find-RepositoryPattern -Pattern 'plan execute|plan apply|clyr clean|clyr prune' -Paths @('src/Clyr.Cli') -Include '*.cs' `
        -ExcludeDirs @('src/Clyr.Cli/PlanCliCommands.cs', 'src/Clyr.Cli/ExecutionCliCommands.cs', 'src/Clyr.Cli/CliApplication.cs')
    if ($forbiddenCliResult.Found) { throw ('A forbidden cleanup CLI command was found: ' + ($forbiddenCliResult.Matches -join '; ')) }
    $secretsResult = Find-RepositoryPattern -Pattern '(AKIA[0-9A-Z]{16}|gh[pousr]_[A-Za-z0-9]{30,}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----)' -Paths @('src', 'tests', 'scripts', 'docs', '.github', 'rules')
    if ($secretsResult.Found) { throw ('A credential-like value was found: ' + ($secretsResult.Matches -join '; ')) }
    $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    $machinePathsResult = Find-RepositoryPattern -Pattern $userProfile -Literal -Paths @('src', 'tests', 'scripts', 'docs', '.github', 'rules', 'README.md', 'PHASE_STATUS.md', 'ROADMAP.md', 'CHANGELOG.md')
    if ($machinePathsResult.Found) { throw ('A machine-specific user path was found: ' + ($machinePathsResult.Matches -join '; ')) }
    $trailingResult = Find-RepositoryPattern -Pattern '[ \t]+\r?$' -Paths @('src', 'tests', 'scripts', 'docs', '.github', 'rules', 'README.md', 'PHASE_STATUS.md', 'ROADMAP.md', 'CHANGELOG.md')
    if ($trailingResult.Found) { throw ('Trailing whitespace was found: ' + ($trailingResult.Matches -join '; ')) }

    if (-not $SkipUiAutomation) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File .\\scripts\\verify-responsive-layout.ps1
        if ($LASTEXITCODE -ne 0) { throw 'Phase 5 responsive UI Automation failed.' }
    }
    if (-not $SkipGitChecks) {
        & git diff --check
        if ($LASTEXITCODE -ne 0) { throw 'git diff --check failed.' }
    }
    Write-Host 'Phase 5 verification PASSED. Immutable integrity-checked dry-run planning only; no cleanup execution, process execution, elevation, helper, or Phase 6 behavior.' -ForegroundColor Green
}
finally {
    if ($null -eq $previousSkipGit) { Remove-Item Env:CLYR_SKIP_GIT_CHECKS -ErrorAction SilentlyContinue }
    else { $env:CLYR_SKIP_GIT_CHECKS = $previousSkipGit }
    Pop-Location
}

