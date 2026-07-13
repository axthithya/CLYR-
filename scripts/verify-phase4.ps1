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
    & powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-phase3.ps1
    if ($LASTEXITCODE -ne 0) { throw "Phase 3 regression verification failed." }
    Invoke-Gate "Phase 4 warning-free Release build" @("build", "Clyr.sln", "--configuration", "Release", "--no-restore", "-m:1")
    Invoke-Gate "Snapshot comparison tests" @("test", "tests/Clyr.Core.Tests/Clyr.Core.Tests.csproj", "--configuration", "Release", "--no-build", "-m:1")
    Invoke-Gate "Snapshot persistence tests" @("test", "tests/Clyr.Persistence.Tests/Clyr.Persistence.Tests.csproj", "--configuration", "Release", "--no-build", "-m:1")
    Invoke-Gate "Snapshot CLI tests" @("test", "tests/Clyr.Cli.Tests/Clyr.Cli.Tests.csproj", "--configuration", "Release", "--no-build", "-m:1")
    Get-Content -Raw .\rules\schemas\snapshot.schema.json | ConvertFrom-Json | Out-Null
    Get-Content -Raw .\rules\schemas\comparison-report.schema.json | ConvertFrom-Json | Out-Null
    $forbidden = rg -n "DeleteFile|MoveFile|Process\.Start|runas|FileMode\.Open.*FileAccess\.Write" src --glob "*.cs"
    if ($LASTEXITCODE -eq 0) { throw "A forbidden mutation/elevation primitive was found:`n$forbidden" }
    if ($IncludeUiSmoke) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-winui.ps1
        if ($LASTEXITCODE -ne 0) { throw "WinUI smoke test failed." }
    }
    Write-Host "Phase 4 verification PASSED. Aggregate history only; no real-drive scan, cleanup, planning, elevation, or Phase 5 behavior was started."
}
finally { Pop-Location }
