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
    & powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-phase1.ps1
    if ($LASTEXITCODE -ne 0) { throw "Phase 1 regression verification failed." }
    Invoke-Gate "Windows adapter tests" @("test", "tests/Clyr.Windows.Tests/Clyr.Windows.Tests.csproj", "--configuration", "Release", "--no-build", "-m:1")
    Invoke-Gate "Synthetic scanner performance fixtures" @("test", "tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj", "--configuration", "Release", "--no-build", "-m:1", "--filter", "Category=Performance", "--logger", "console;verbosity=detailed")
    Invoke-Gate "CLI drive discovery" @("run", "--project", "src/Clyr.Cli/Clyr.Cli.csproj", "--configuration", "Release", "--no-build", "--", "drives")
    Invoke-Gate "CLI drive discovery JSON" @("run", "--project", "src/Clyr.Cli/Clyr.Cli.csproj", "--configuration", "Release", "--no-build", "--", "drives", "--json")
    if ($IncludeUiSmoke) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-winui.ps1
        if ($LASTEXITCODE -ne 0) { throw "WinUI smoke test failed." }
    }
    Write-Host "Phase 2 verification PASSED. No real-drive scan was started by this script."
}
finally { Pop-Location }
