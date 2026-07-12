# Changelog

All notable project changes are recorded here. The format follows Keep a Changelog; versions will begin when buildable artifacts exist.

## [Unreleased]

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
