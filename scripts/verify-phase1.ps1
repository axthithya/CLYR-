[CmdletBinding()]
param([switch]$IncludeUiSmoke)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $root ".tools\\dotnet\\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { "dotnet" }
$env:MSBUILDDISABLENODEREUSE = '1'

function Invoke-Gate([string]$name, [string[]]$arguments) {
    Write-Host "==> $name"
    & $dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "$name failed with exit code $LASTEXITCODE." }
}

Push-Location $root
try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-phase0.ps1
    if ($LASTEXITCODE -ne 0) { throw "Phase 0 regression verification failed." }
    Invoke-Gate "Restore" @("restore", "Clyr.sln", "--disable-parallel", "-m:1")
    Invoke-Gate "Release build" @("build", "Clyr.sln", "--configuration", "Release", "--no-restore", "-m:1")
    Invoke-Gate "Tests" @("test", "Clyr.sln", "--configuration", "Release", "--no-build", "-m:1")
    Invoke-Gate "Formatting" @("format", "Clyr.sln", "--verify-no-changes", "--no-restore")
    Invoke-Gate "Vulnerability audit" @("package", "list", "--project", "Clyr.sln", "--vulnerable", "--include-transitive", "--no-restore")
    Invoke-Gate "Dependency inventory" @("package", "list", "--project", "Clyr.sln", "--include-transitive", "--no-restore")
    Invoke-Gate "Outdated dependency report" @("package", "list", "--project", "Clyr.sln", "--outdated", "--no-restore")
    Invoke-Gate "CLI help" @("run", "--project", "src/Clyr.Cli/Clyr.Cli.csproj", "--configuration", "Release", "--no-build", "--", "--help")
    Invoke-Gate "CLI version" @("run", "--project", "src/Clyr.Cli/Clyr.Cli.csproj", "--configuration", "Release", "--no-build", "--", "--version")
    Invoke-Gate "CLI doctor" @("run", "--project", "src/Clyr.Cli/Clyr.Cli.csproj", "--configuration", "Release", "--no-build", "--", "doctor")
    Invoke-Gate "CLI demo" @("run", "--project", "src/Clyr.Cli/Clyr.Cli.csproj", "--configuration", "Release", "--no-build", "--", "demo")
    Invoke-Gate "Rule validation" @("run", "--project", "src/Clyr.Cli/Clyr.Cli.csproj", "--configuration", "Release", "--no-build", "--", "rules", "validate", "rules/examples/npm-cache.valid.yaml")
    if ($IncludeUiSmoke) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-winui.ps1
        if ($LASTEXITCODE -ne 0) { throw "WinUI smoke test failed." }
    }
    Write-Host "Phase 1 verification PASSED."
}
finally {
    Pop-Location
}
