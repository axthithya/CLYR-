# Decision Log

This is the concise cross-project index. Architecture details live in the linked ADRs; future entries must record supersession rather than rewriting history.

| ID | Date | Status | Decision | Consequence / evidence |
|---|---|---|---|---|
| D-001 | 2026-07-10 | Accepted | Use CLYR / `clyr` / `Clyr`; reject obsolete CLI naming found in bootstrap examples. | One consistent product and CLI name. |
| D-002 | 2026-07-10 | Accepted | Execute Phase 0 only in this bootstrap. | No solution, scanner, cleanup, elevation, service, installer, updater, or telemetry code is created. |
| D-003 | 2026-07-10 | Accepted | Adopt Apache-2.0 because the repository was empty and unlicensed. | Dependencies need compatible licenses and an inventory before release. |
| D-004 | 2026-07-10 | Accepted | Use native C#/.NET/WinUI 3 with a UI-independent core. | See ADR-0001; no Electron or browser server. |
| D-005 | 2026-07-10 | Accepted | Isolate any future privileged mutation in a short-lived helper. | See ADR-0002; the normal app remains non-elevated. |
| D-006 | 2026-07-10 | Accepted | Require immutable dry-run plans and explicit confirmation before later actions. | See ADR-0003; execution does not exist before Phase 6. |
| D-007 | 2026-07-10 | Accepted | Version YAML rules and validate them with a JSON Schema; community rules are detection-only. | See ADR-0004; executable integration requires compiled adapters. |
| D-008 | 2026-07-10 | Accepted | Keep core safety and detection offline and deterministic with no telemetry by default. | See ADR-0005; updates remain a separate signed capability. |
| D-009 | 2026-07-10 | Accepted | Keep physical usage, reclaimability, review, movement, protection, and uncertainty as separate quantities. | Prevents misleading “junk” totals; see storage accounting. |
| D-010 | 2026-07-10 | Accepted | Do not create empty project directories or fake build artifacts during Phase 0. | Planned structure is documented; projects are Phase 1 deliverables. |
| D-011 | 2026-07-10 | Accepted | Defer exact SDK/package pins to a fresh authoritative check at Phase 1 start. | Avoids stale pins; the local host currently has no .NET SDK. |
| D-012 | 2026-07-12 | Accepted | Close Phase 0 after the applicable documentation, schema, Mermaid, link, terminology, privacy, and repository checks passed. | Phase 1 remains separately approval-gated; no runtime/build capability is implied by Phase 0 completion. |
| D-013 | 2026-07-13 | Accepted | Pin .NET SDK 10.0.301, Windows App SDK 2.2.0, and all packages through Central Package Management. | Stable reproducible Phase 1 builds; no floating or project-level versions. |
| D-014 | 2026-07-13 | Accepted | Use Microsoft.Data.Sqlite.Core 10.0.9 with SQLitePCLRaw.bundle_e_sqlite3 3.0.3. | Removes the vulnerable convenience-package native closure and resolves SourceGear.sqlite3 3.50.4.5 explicitly. |
| D-015 | 2026-07-13 | Accepted | Use JsonSchema.Net 7.4.0, the last reviewed MIT binary line, for Draft 2020-12 validation. | Corvus 5.2.6 runtime compilation failed under .NET 10; newer JsonSchema.Net binaries carry additional fee terms. |
| D-016 | 2026-07-13 | Accepted | Use an unpackaged framework-dependent WinUI developer build in Phase 1. | Proves stable WinUI without prematurely selecting production identity, installer, signing, or future helper topology. |

## Change protocol

Anyone may propose a decision change. Changes affecting protected resources, privilege, executable adapters, update trust, data retention, license, or product claims require an ADR, threat-model update, tests appropriate to the active phase, and explicit maintainer approval.
