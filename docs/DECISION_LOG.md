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
| D-017 | 2026-07-13 | Accepted | Implement Phase 2 as one metadata-only bounded scanner shared by WinUI and CLI. | See ADR-0008; reparse traversal, content reads, cloud hydration, persistence, elevation, and mutation remain absent. |
| D-018 | 2026-07-13 | Accepted | Report logical size only and label it Estimated because hard links are not deduplicated; allocated size is Unavailable. | Prevents false physical-usage precision while keeping the MVP bounded and testable. |
| D-019 | 2026-07-13 | Accepted | Support ready fixed NTFS volumes for Phase 2 and report all other discovered drive capabilities as unsupported. | Avoids inferring safe behavior for ReFS, removable, network, optical, unready, or unverified filesystems. |

## Change protocol

Anyone may propose a decision change. Changes affecting protected resources, privilege, executable adapters, update trust, data retention, license, or product claims require an ADR, threat-model update, tests appropriate to the active phase, and explicit maintainer approval.

## Phase 4.1 decisions — 2026-07-13

- D-020 Accepted: use distinct WinUI pages and page-specific view models while sharing one UI-independent analysis session. This removes control duplication without changing Core, persistence, rules, or Windows scanning behavior.
- D-021 Accepted: expose a process-scoped `CLYR_UI_FIXTURE=1` composition only for deterministic UI Automation. Production composition remains the default, and the fixture performs no drive enumeration or filesystem mutation.
- D-022 Accepted: treat actual Windows DPI/text-scaling checks as an explicit manual release gate; automated window resizing is useful reflow evidence but is not represented as operating-system scaling evidence.
- D-023 Accepted: use a shared `ResponsivePageHost` control with consistent breakpoints (Narrow <760px, Medium 760–1199px, Wide ≥1200px), dynamic gutters (16/24/32px), max content width 1120px, and centered layout for all nine pages. Individual page XAML no longer contains scroll viewers.
- D-024 Accepted: require stable `AutomationProperties.Name` values on all critical interactive elements and page roots; verify with both UI Automation tests and static XAML analysis.
- D-025 Accepted: add a structural responsive layout verifier (`verify-responsive-layout.ps1`) that validates shared host usage, scroll contract, breakpoints, gutters, theme completeness, scan-control isolation, automation names, and safety boundaries without launching the app. The UI Automation verifier (`verify-winui.ps1`) additionally tests viewport bounds at five window sizes (1600×900 through 900×600).
## Phase 4 decisions — 2026-07-13

- Store aggregate history only in local SQLite; never store a file inventory.
- HMAC a transient Windows volume GUID with a per-install 256-bit key; prefer lost comparability over recoverable raw identity.
- Keep at least two snapshots per drive; default retention is 20.
- Use deterministic full-scan comparison. USN remains explicitly unsupported until reset, wrap, privilege, and rename semantics are evidenced.
- Significant means 250 MiB absolute, or both 50 MiB and 10 percent relative. Insights describe observations, never causes.
## Phase 4 decisions — 2026-07-13

- Store aggregate history only in local SQLite; never store a file inventory.
- HMAC a transient Windows volume GUID with a per-install 256-bit key; prefer lost comparability over recoverable raw identity.
- Keep at least two snapshots per drive; default retention is 20.
- Use deterministic full-scan comparison. USN remains unsupported until reset, wrap, privilege, and rename semantics are evidenced.
- Significant means 250 MiB absolute, or both 50 MiB and 10 percent relative. Insights describe observations, never causes.
