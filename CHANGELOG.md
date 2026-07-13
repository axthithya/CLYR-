# Changelog

All notable project changes are recorded here. The format follows Keep a Changelog; versions will begin when buildable artifacts exist.

## [Unreleased]

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
