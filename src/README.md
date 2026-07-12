# Planned source projects

Phase 0 contains no `.NET` projects or product code. Phase 1 will create the projects listed below only after installing/verifying the latest patched .NET 10 SDK and reconciling the stable Windows App SDK version. The dependency rules in `docs/ARCHITECTURE.md` are authoritative.

- `Clyr.App`: WinUI 3/MVVM presentation; no filesystem mutation, SQL, or process execution.
- `Clyr.Core`: UI-independent use cases, domain policy, aggregation, findings, snapshots, and future planning.
- `Clyr.Contracts`: minimal versioned DTOs, enums, and strong identifiers across surfaces/processes.
- `Clyr.Persistence`: SQLite migrations/repositories and bounded retention.
- `Clyr.Rules`: bounded YAML/schema/manifest validation and deterministic compilation.
- `Clyr.Windows`: narrow Windows metadata, storage, known-folder, tool, and future elevation adapters.
- `Clyr.Cli`: CLI presentation over the same application services.
- `Clyr.ElevatedHelper`: future Phase 6 short-lived typed capability executor; not built in Phase 1.

Directories contain responsibility notes now so the planned tree is reviewable without fake project files or empty placeholders.
