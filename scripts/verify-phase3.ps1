[CmdletBinding()]
param([switch]$IncludeUiSmoke)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $root ".tools\dotnet\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { "dotnet" }
$env:MSBUILDDISABLENODEREUSE = "1"

function Invoke-Gate([string]$name, [string[]]$arguments) {
    Write-Host "==> $name"
    & $dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "$name failed with exit code $LASTEXITCODE." }
}

Push-Location $root
try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-phase2.ps1
    if ($LASTEXITCODE -ne 0) { throw "Phase 2 regression verification failed." }
    Invoke-Gate "Built-in pack verification" @("run", "--project", "src/Clyr.Cli/Clyr.Cli.csproj", "--configuration", "Release", "--no-build", "--", "rules", "verify")
    Invoke-Gate "Built-in rule inventory" @("run", "--project", "src/Clyr.Cli/Clyr.Cli.csproj", "--configuration", "Release", "--no-build", "--", "rules", "list")
    Invoke-Gate "Rule and report tests" @("test", "tests/Clyr.Rules.Tests/Clyr.Rules.Tests.csproj", "--configuration", "Release", "--no-build", "-m:1")
    Invoke-Gate "CLI classification tests" @("test", "tests/Clyr.Cli.Tests/Clyr.Cli.Tests.csproj", "--configuration", "Release", "--no-build", "-m:1")
    Invoke-Gate "Safety boundary tests" @("test", "tests/Clyr.Safety.Tests/Clyr.Safety.Tests.csproj", "--configuration", "Release", "--no-build", "-m:1")
    if ($IncludeUiSmoke) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-winui.ps1
        if ($LASTEXITCODE -ne 0) { throw "WinUI smoke test failed." }
    }
    Write-Host "Phase 3 verification PASSED. Detection and explanation only; no real-drive scan or mutation was started."
}
finally { Pop-Location }
