[CmdletBinding()]
param(
    [int]$TimeoutSeconds = 60
)

<#
Real, interactive fixture-only UAC smoke test for the Phase 6 elevated helper.

This script requires an interactive Windows desktop session — it will not run under a non-interactive/service
session. It builds and runs tools/Phase6UacSmoke (a small, dedicated .NET console harness kept outside
Clyr.sln and outside src/, so it is never part of the shipped product and never seen by RepositorySafetyTests'
scan of src/). That harness:

  1. creates a synthetic temporary fixture root and one synthetic stale file inside it — never a real user,
     system, browser, Docker, WSL, package-cache, or project path;
  2. builds a real HelperRequest naming that fixture root as the trusted root and that one file as the only
     target;
  3. launches the real Clyr.ElevatedHelper.exe through a real Windows UAC prompt — a real person must approve
     it, exactly like the shipped product would;
  4. sends the request over the real named-pipe IPC channel and waits for the real response;
  5. verifies the synthetic file was removed, the fixture root itself still exists, the response reports
     Completed with one Removed item, and the helper process exited;
  6. deletes the fixture root.

This wrapper prints the harness's own explicit PASS/FAIL line and exits with the harness's exit code.

Why a .NET harness instead of pure PowerShell: Clyr.Contracts.dll and Clyr.Core.dll target net10.0, which
Windows PowerShell 5.1 (built on .NET Framework) cannot load via Add-Type; running the real logic as an actual
net10.0 process avoids that mismatch entirely regardless of which PowerShell host invokes this script.
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }

if (-not [Environment]::UserInteractive) {
    Write-Host 'FAILURE DETAIL: This is not an interactive session. The fixture-only UAC smoke test requires a real desktop session where a person can approve the UAC prompt.' -ForegroundColor Red
    Write-Host 'PHASE 6 UAC SMOKE TEST: FAIL' -ForegroundColor Red
    Write-Host 'Phase 6 is not complete because the required fixture-only UAC smoke test has not passed.' -ForegroundColor Red
    exit 1
}
if ($env:SESSIONNAME -eq 'Services') {
    Write-Host 'FAILURE DETAIL: Running in a non-interactive service session. Run this from an interactive desktop logon.' -ForegroundColor Red
    Write-Host 'PHASE 6 UAC SMOKE TEST: FAIL' -ForegroundColor Red
    exit 1
}

$helperPath = Join-Path $root 'src\Clyr.ElevatedHelper\bin\Release\net10.0-windows10.0.26100.0\Clyr.ElevatedHelper.exe'
if (-not (Test-Path -LiteralPath $helperPath -PathType Leaf)) {
    Write-Host "==> Building Clyr.sln (Release) so the elevated helper exists"
    & $dotnet build (Join-Path $root 'Clyr.sln') --configuration Release -m:1
    if ($LASTEXITCODE -ne 0) { Write-Host 'PHASE 6 UAC SMOKE TEST: FAIL (build failed)' -ForegroundColor Red; exit 1 }
}

Write-Host '==> Building the fixture-only UAC smoke harness (tools/Phase6UacSmoke, outside Clyr.sln and outside src/)'
$smokeProject = Join-Path $root 'tools\Phase6UacSmoke\Phase6UacSmoke.csproj'
& $dotnet build $smokeProject --configuration Release -m:1
if ($LASTEXITCODE -ne 0) { Write-Host 'PHASE 6 UAC SMOKE TEST: FAIL (harness build failed)' -ForegroundColor Red; exit 1 }

$smokeExe = Join-Path $root 'tools\Phase6UacSmoke\bin\Release\net10.0-windows10.0.26100.0\Phase6UacSmoke.exe'
Write-Host '==> Running the real fixture-only UAC smoke test — approve the UAC prompt when it appears'
& $smokeExe $TimeoutSeconds
exit $LASTEXITCODE
