# Phase 1 dependency inventory

Recorded 2026-07-13 from `dotnet package list --project Clyr.sln --include-transitive --no-restore` using SDK 10.0.301.

## Direct runtime and build dependencies

- Microsoft.Data.Sqlite.Core 10.0.9 — MIT
- SQLitePCLRaw.bundle_e_sqlite3 3.0.3 — Apache-2.0
- Microsoft.Extensions.Configuration.Json 10.0.9 — MIT
- Microsoft.Extensions.DependencyInjection 10.0.9 — MIT
- Microsoft.Extensions.Logging 10.0.9 — MIT
- Microsoft.Extensions.Logging.Abstractions 10.0.9 — MIT
- Microsoft.Windows.SDK.BuildTools 10.0.28000.2270 — Microsoft Windows SDK terms
- Microsoft.WindowsAppSDK 2.2.0 — Microsoft Windows App SDK terms
- YamlDotNet 18.1.0 — MIT
- JsonSchema.Net 7.4.0 — MIT; deliberately retained on the last reviewed permissive line because newer binary releases add a maintenance-fee agreement

## Direct test dependencies

- Microsoft.NET.Test.Sdk 18.7.0 — MIT
- xunit 2.9.3 — Apache-2.0
- xunit.runner.visualstudio 3.1.5 — Apache-2.0
- coverlet.collector 10.0.1 — MIT

## Important transitive dependencies

- SourceGear.sqlite3 3.50.4.5 — SQLite native binary provenance supplied by the explicit SQLitePCLRaw bundle; runtime `sqlite_version()` is 3.50.4
- SQLitePCLRaw.core, config.e_sqlite3, and provider.e_sqlite3 3.0.3
- JsonPointer.Net 5.3.1 and Json.More.Net 2.1.1
- Microsoft Windows App SDK runtime 2.2.0 and WinUI package 2.2.1 resolved by Microsoft.WindowsAppSDK 2.2.0
- Microsoft Test Platform, xUnit analyzers/runtime, Newtonsoft.Json, and Microsoft.CodeCoverage resolved by the test SDK

The vulnerability audit reported no known vulnerable packages. The outdated audit is informational: JsonSchema.Net remains intentionally pinned for licensing and all other direct runtime packages were current when recorded.
