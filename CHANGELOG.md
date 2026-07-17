# Changelog

All notable project changes are recorded here. The format follows Keep a Changelog; versions will begin when buildable artifacts exist.

## [Unreleased]

### Added — Phase 7 Developer Mode: read-only tool detection (2026-07-17)

- Added a closed, compiled taxonomy (`DeveloperToolTaxonomy`) mapping every developer-related built-in rule to
  one of 14 tool families (Docker, WSL, Node/npm, pnpm, Yarn, .NET/NuGet, Gradle, Maven, Python/pip, Rust/Cargo,
  Flutter/Dart, Android SDK, Playwright, generic build output), and a `DeveloperToolReportBuilder` that groups
  existing `CleanupCandidateFactory` output by tool without any new eligibility logic (ADR-0015).
- Added trusted, name-only executable discovery (`TrustedExecutableLocator`: PATH plus a depth-bounded known-
  folder search, `.exe` only, reject reparse points) and a narrow, closed-argument, timeout- and output-bounded
  read-only probe (`DeveloperToolProbeRunner`: `docker --version` / `wsl --status` only) for the two tools where
  classification alone is insufficient (ADR-0016).
- Added `DeveloperToolRegistry`, the closed list of all 14 tools and the status-derivation orchestrator that
  never reports `NotInstalled` for a tool this phase cannot actually probe (ADR-0017).
- Added 5 new report-only built-in rules (`developer.yarn.cache`, `developer.pip.cache`,
  `developer.cargo.registry`, `developer.flutter.pubcache`, `developer.buildoutput.generic`; manifest bumped to
  `1.2.0`).
- Added CLI `developer tools|scan|show|findings|plan|capabilities|doctor` — deliberately no `developer run`,
  `--command`, `--exe`, `--args`, `--path`, or prune/clean-all subcommand.
- Replaced the Developer Mode static preview page with a real WinUI dashboard: a snapshot picker, a "Detect
  developer tools" action, per-tool cards, a tool-detail panel, and a "Review in plan" action (only for findings
  already `DryRunEligible`) that routes through the existing Phase 5 `CleanupPlanBuilder`/Review Plan pipeline.
- Added ADR-0015, ADR-0016, ADR-0017; added `scripts/verify-phase7.ps1`; extended `scripts/verify-winui.ps1` and
  `scripts/verify-phase0.ps1`'s repository-wide `Process.Start` scan for the one new, narrowly reviewed probe
  call site.
- Verified against a real local machine: actual Docker Desktop 29.1.2 and actual WSL were both correctly
  detected through the trusted-discovery-plus-probe path.

### Known gap — Phase 7 (2026-07-17)

- Visual Studio and VS Code are not covered by a dedicated tool family; their build output is only visible
  through the generic `developer.buildoutput.generic` rule.
- Docker/WSL storage numbers still come from classification (rule-based folder scanning), not from parsing
  tool-reported accounting (`docker system df`/`wsl -l -v`) — judged too fragile to build and verify reliably
  this phase. Only installed/running status comes from the real probe.
- Per-tool version-range binding and locale-independent probe output parsing are not implemented; the version
  extracted from probe output is best-effort and may show as unknown on some locales/versions (observed with
  this session's local WSL install).

### Added — Phase 6 execution engine, elevated helper, IPC, receipts, CLI, WinUI (2026-07-17)

- Added a non-elevated execution engine (`NonElevatedCleanupExecutor`, `ExecutionTargetProcessor`) for one
  narrowly allowlisted, Low-risk built-in action (`builtin.clyr-owned-temp-artifacts`), with a one-time,
  session/user/drive/plan-digest-bound token, per-target TOCTOU revalidation immediately before deletion, an
  exact bounded manifest, cooperative cancellation, and immutable privacy-safe execution receipts.
- Added a separate one-shot elevated helper (`Clyr.ElevatedHelper`, own `requireAdministrator` manifest) with
  independent request revalidation, a typed/bounded/versioned named-pipe IPC protocol
  (`Clyr.Contracts.ExecutionIpc`, `Clyr.Core.Execution.ElevatedHelperIpc`), and a tightly controlled UAC
  launcher (`ElevatedHelperLauncher`) — the only `Process.Start` in production source. No enabled action
  currently requires elevation.
- Added SQLite execution-receipt persistence (schema v3, `SqliteExecutionReceiptStore`) with immutable
  terminal-state rows, bounded retention, and a crash-reconciliation primitive
  (`ReconcileInterruptedAsync`).
- Added CLI `plan execute <plan-id> --confirm-digest <prefix>` and
  `execution status|receipt|list|export|discard-receipt`; extended `plan candidates`/`plan create` to include
  the live-scanned built-in candidate.
- Added a WinUI Review Plan execution flow: no default selection, a gated final-confirmation dialog, live
  progress/counters/cancellation, all terminal states (Completed/PartiallyCompleted/Cancelled/Failed/
  Interrupted/Unknown outcome), and receipt history with view/export/delete.
- Added ADR-0012 (execution authority/TOCTOU), ADR-0013 (typed IPC), ADR-0014 (receipts/accounting), and an
  implementation-note update to ADR-0002; added `scripts/verify-phase6.ps1` and
  `scripts/run-phase6-uac-smoke.ps1` (with a dedicated `tools/Phase6UacSmoke` harness kept outside the shipped
  solution).
- Added 197 passing tests across the solution (up from 163 at Phase 5 exit) covering the engine, helper/IPC
  (including a real named-pipe round trip), receipt persistence, CLI execution, and updated UI/repository
  safety architecture checks.

### Known gap — Phase 6 (2026-07-17)

- The required real fixture-only UAC smoke test has not been run: it needs a person at an interactive desktop
  to approve an actual Windows UAC prompt, which was not available in the environment this work was done in.
  Phase 6 implementation is ready for final approval, but Phase 6 remains incomplete until that test passes.

### Added — Phase 5 cleanup planning and dry-run only (2026-07-16)

- Added explicit eligibility, action, risk, consequence, rollback, binding, expiry, target-metadata, diagnostic, dry-run, and immutable cleanup-plan contracts.
- Added deterministic canonical serialization and SHA-256 integrity digests, stale/expired/source-version validation, component-aware Windows path policy, protected-path override, and target metadata comparison.
- Added bounded memory-only plan storage, privacy-safe versioned report export/schema/examples, CLI plan commands, and a responsive Review Plan WinUI flow with no preselection.
- Added a constrained built-in report-only npm-cache descriptor; invalid descriptors reject the built-in pack atomically.
- Added Phase 5 core/security/rule/schema/CLI/UI/repository tests, verifier, and CI validation.

### Security — Phase 5

- Production execution is deliberately unavailable and returns ExecutionNotAvailableInPhase5; no delete, move, Recycle Bin, quarantine, shell/process, elevation, helper, permission, registry, or Windows-setting implementation exists.
- Browser profile aggregates remain insufficient evidence because current detection is broader than an exact cache root. Potential logical bytes are never described as guaranteed recovered space.
- Actual Windows High Contrast activation, 125%/150% DPI, and Windows text scaling remain documented manual release checks and are not represented as completed.

### Added — Phase 4.1 polished UI/UX (2026-07-13)

- Replaced the shared WinUI content stack with distinct Overview, Scan, Results, History, Developer Mode, Privacy, Licenses, About, and Settings pages backed by page-specific view models.
- Added shared design tokens, reusable page headers and empty states, automatic compact navigation, page-level vertical scrolling, responsive result/mode layouts, polished read-only copy, accessible names, and text alternatives.
- Implemented shared `ResponsivePageHost` with consistent breakpoints (Narrow <760px, Medium 760–1199px, Wide ≥1200px), dynamic gutters (16/24/32px), MaxWidth 1120px centered layout, and vertical-only scrolling.
- Added stable `AutomationProperties.Name` values on all critical interactive elements, page roots, metric cards, empty states, settings controls, and shared scroll viewport.
- Verified all 11 theme-aware brushes in Default, Light, and HighContrast theme dictionaries. Selected analysis cards use three contrast indicators: subtle tint, accent border, and check-mark.
- Added a structural responsive layout verifier (`verify-responsive-layout.ps1`) that validates host usage, scroll contract, breakpoints, gutters, themes, automation names, scan isolation, and safety boundaries without launching the app.
- Added a process-scoped fixture composition for safe UI Automation, UI architecture regression tests, accessibility documentation, a Phase 4.1 verifier, and CI validation.
- Verified fixture scan start/cancellation/completion, result presentation, two-snapshot comparison, every navigation destination, viewport bounds at five window sizes (1600×900, 1366×768, 1280×720, 1000×650, 900×600), vertical scroll advancement, absence of horizontal scrolling, license search, and about version.
- Added 4 new responsive architecture tests: shared host scroll contract, breakpoints/gutters, no unsafe fixed widths, page root automation names, and no cleanup/elevation language. Total test count: 124.

### Security — Phase 4.1

- Production drive, scanner, rule, and SQLite composition remains unchanged. Fixture mode uses only fake/in-memory services and never inspects a real drive.
- No cleanup, dry-run planning, file mutation, process execution, elevation, helper, service, or Phase 5 behavior was added.

### Added — Phase 4 aggregate history and growth comparison (2026-07-13)

- Transactional SQLite schema v2 for aggregate snapshots, category/finding aggregates, warnings, settings, foreign keys, indexes, idempotent saves, and bounded retention.
- Per-install HMAC-SHA-256 volume identity; the raw volume identifier is never persisted or exported.
- Typed lifecycle, compatibility/confidence, drift warnings, saturating signed deltas, significance thresholds, and deterministic non-causal insights.
- Automatic eligible-scan capture; functional CLI and WinUI history, comparison, settings, confirmed delete, and clear.
- Snapshot/comparison schemas, ADR-0010, Phase 4 verifier/CI, and focused tests.

### Security — Phase 4

- No file inventory, raw child path, user/machine identity, SID, serial, content, or content hash is stored. Failed scans are not persisted; corruption never triggers automatic database deletion.
- USN remains unsupported. No cleanup, planning, execution, elevation, helper, hydration, content read, or scanned-file write was added. Phase 5 has not begun.

### Added — Phase 3 detection-only classification (2026-07-13)

- One-pass streaming classification with exclusive category ownership, secondary tags, deterministic protected/priority/specificity precedence, stable privacy-safe findings, explanations, confidence/status, and explicit Unknown/coverage accounting.
- A 36-rule first-party Windows storage catalog with offline manifest/category-registry/SHA-256 verification, compatibility/provenance metadata, transactional failure, and positive/negative fixture coverage.
- Classified report schema v2, CLI rule inspection and explanation commands, inactive external-rule validation, and WinUI cause/rule-status/results surfaces.
- Phase 3 ADR, verifier, CI workflow, tamper/privacy/precedence/report/CLI tests, and a one-million-observation classification fixture.

### Security — Phase 3

- Detection remains metadata-only and report-only. There is no cleanup, deletion, movement, process execution, elevation, service, helper, persistence/history, or Phase 4 implementation.

### Added — Phase 2 read-only scanner (2026-07-13)

- Capability-qualified discovery for local drives; ready fixed NTFS volumes are eligible and unsupported drives retain an explicit reason.
- One UI-independent, single-session streaming scanner with bounded depth-first state, deterministic top-N, structural extension families, grouped issues, overlap rejection, cancellation, partial results, and throttled progress.
- Windows metadata adapter that skips reparse points, recognizes cloud placeholder attributes without hydration, and observes logical file length without opening content.
- Quick Analysis with a three-level bound and Deep Analysis with a defensive deep bound; both disclose logical-only accounting, hard-link double-count uncertainty, and unavailable allocated size.
- CLI `drives` and `scan` commands with stable exit behavior, progress on stderr, human or JSON output, and explicit local export.
- Functional WinUI drive overview, Quick/Deep selection, start/cancel lifecycle, coverage text, and ranked top-level results over the same Core service.
- Versioned support-safe scan export/schema with ranked path tokens and explicit no-path/no-user/no-filename/no-content/no-upload declarations.
- Phase 2 CI/verifier, updated UI Automation smoke, ADR-0008, and 39 additional tests for a current total of 77.

### Security and measured evidence — Phase 2

- No scan path can mutate, elevate, follow reparse points, hydrate cloud content, read file contents, start a process, or persist an implicit snapshot.
- Automated tests use fake streams or isolated operating-system temporary fixtures; verification never starts a real-drive scan.
- Synthetic retained-state measurements passed at 10k, 100k, and 1M observations with 25 retained rankings and retained managed memory far below the 256 MiB provisional budget.
- Phase 3 classification/rule evaluation, persistence/history, cleanup planning, and execution remain unimplemented.

### Added — Phase 1 engineering foundation (2026-07-13)

- Buildable .NET 10 solution with contracts, core, persistence, rules, Windows adapters, restricted CLI, and WinUI projects.
- Deterministic demo-data mode, typed JSON configuration, privacy redaction, structured local logging, typed outcomes, guarded startup errors, and dependency injection.
- Explicit SQLite provider initialization and idempotent app-metadata migrations using native SQLite 3.50.4.5.
- Constrained YAML parsing with Draft 2020-12 JSON Schema validation and malicious-rule rejection.
- Restricted CLI commands: help, version, doctor, demo, and rules validate.
- Branded non-elevated WinUI shell with placeholder navigation and the explicit no-real-scan demo label.
- Thirty-eight behavioral tests covering contracts, configuration, privacy, rules, SQLite, CLI, architecture, safety, and integration, plus a WinUI launch/navigation smoke test.
- Central Package Management, immutable-action CI, dependency/license inventory, and the Phase 1 verification entry point.

### Security — Phase 1

- Replaced the vulnerable SQLite convenience meta-package with Microsoft.Data.Sqlite.Core 10.0.9 plus SQLitePCLRaw.bundle_e_sqlite3 3.0.3.
- Confirmed the resolved SourceGear.sqlite3 native package is 3.50.4.5 and the dependency audit reports no known vulnerable packages.
- No scanner, cleanup, delete, move, shell execution, elevation, service, startup task, or functional helper was added.

### Added — Phase 0 (completed 2026-07-12)

- Product brief, specification, personas, stories, misuse cases, C:-first journey, scope, non-goals, and complete phased plan.
- Native Windows architecture, dependency/process/privilege boundaries, domain/storage concepts, state machines, and Mermaid sources.
- Safety, threat, privacy, retention, secure-IPC, protected-resource, future dry-run/transaction, and update/signing contracts.
- Versioned rule/export schemas with safe and malicious examples.
- Capability/support matrices, research notes, test/fixture/benchmark strategy, risk/decision/assumption logs, and governance.
- Phase 0 verification script and repository hygiene policies.
- GitHub issue/PR templates, provisional sensitive-path ownership, Dependabot policy, documentation CI, and meaningful future-project responsibility notes.

### Validation

- Local Phase 0 verifier passed 478 checks across required files, UTF-8/JSON, schema guardrails, examples, terminology, forbidden implementation patterns, and repository hygiene.
- Draft 2020-12 validation accepted both valid examples and rejected both malicious examples for their intended traversal/unknown-command reasons; all 11 YAML files parsed.
- All 13 Mermaid sources rendered with the pinned Mermaid CLI, and 71 Markdown documents passed link and structural checks.

### Security

- Declarative community rules are detection-only and cannot contain free-form commands.
- Permanent deletion is not an action type and remains prohibited pending a dedicated future review.
- The normal app/CLI are non-elevated; any future helper is short-lived and capability-bound.

### Not implemented

- No .NET solution, application, scanner, persistence, rule runtime, cleanup, file movement, elevation, service, telemetry, installer, updater, or release artifact exists in Phase 0.

Comparison links will be added after maintainers create and authorize a repository remote.
