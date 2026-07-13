# ADR 0007: Use an explicit SQLite managed and native provider

- Status: Accepted
- Date: 2026-07-13

## Decision

Use `Microsoft.Data.Sqlite.Core` 10.0.9 with `SQLitePCLRaw.bundle_e_sqlite3` 3.0.3. The resolved native package is `SourceGear.sqlite3` 3.50.4.5. `Clyr.Persistence.SqliteRuntime.Initialize` is the only initialization point and calls `SQLitePCL.Batteries_V2.Init()` exactly once with an idempotent lock.

## Rationale

The `Microsoft.Data.Sqlite` convenience package resolved `SQLitePCLRaw.lib.e_sqlite3` 2.1.11, which the SDK audit flagged with a high-severity advisory. The explicit bundle removes that vulnerable closure, makes native provenance reviewable, and remains compatible with .NET 10. All packages come from NuGet.org and use MIT or Apache-2.0 licenses.

## Verification

- Restore and vulnerability audit report no vulnerable packages.
- Runtime tests open SQLite, execute `SELECT sqlite_version()`, and require at least 3.50.2, the documented patched baseline for the advisory decision.
- The observed runtime version is 3.50.4.
- Migration tests prove idempotency, incompatible-schema rejection, concurrent initialization, disposal, and temporary-file release.

## Update policy

Review the managed provider, bundle, native package, licenses, and vulnerability audit together. Do not suppress an advisory or substitute an uncontrolled native DLL.
