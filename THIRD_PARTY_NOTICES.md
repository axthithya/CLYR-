# Third-party notices

CLYR Phase 1 consumes the packages listed in `docs/DEPENDENCY_INVENTORY.md`. The repository license does not replace their respective terms.

- MIT: Microsoft.Data.Sqlite.Core, Microsoft.Extensions packages (including Configuration.Json, dependency injection, and logging), YamlDotNet, JsonSchema.Net 7.4.0, Microsoft.NET.Test.Sdk, and coverlet.collector.
- Apache-2.0: SQLitePCLRaw.bundle_e_sqlite3, xUnit, and xunit.runner.visualstudio.
- Microsoft license terms: Windows App SDK and Windows SDK Build Tools.
- SQLite components retain their upstream public-domain and SourceGear package notices as distributed by the signed NuGet packages.

Package license metadata was read from the exact restored `.nuspec` files. Public distribution remains deferred; Phase 9 must generate a full transitive SBOM and reproduce all notices from the release closure.
