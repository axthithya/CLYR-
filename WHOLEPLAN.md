# CLYR Whole Plan

## Vision

CLYR helps a Windows user move from “my C: drive is full” to an evidence-backed explanation and, only after later safety gates, a deliberate recovery plan. It is a diagnostic and recovery tool, not a PC booster. The engine remains usable for another explicitly selected eligible local volume, while onboarding and product language stay C:-first.

The product succeeds by earning trust: no silent mutation, no single misleading junk number, no hidden cloud dependency, no arbitrary elevated commands, and no support claim without build and test evidence.

## Problem and users

Storage pressure combines urgency, incomplete filesystem visibility, misleading logical sizes, and contextual data value. Everyday users need plain consequences; developers need tool-aware semantics for Docker, WSL, packages, and build output; gamers and creators need saves/media separated from caches; support engineers need safe exports; contributors need a way to extend detection without extending code execution.

The differentiated value is an explicit evidence chain:

```text
observed metadata -> storage accounting -> rule evidence -> protection policy
-> confidence and risk -> explanation -> optional future plan -> measured receipt
```

Protection policy wins over rules. Unknown remains unknown. A partial scan remains useful but never appears complete.

## Product principles

1. Read-only first; no mutation in Phases 0–4.
2. Explain before acting; every finding exposes evidence and uncertainty.
3. Dry-run by default; future plans are immutable, short-lived, and explicitly confirmed.
4. Least privilege; the app and CLI remain standard-user processes.
5. No arbitrary commands; executable integrations are compiled, allowlisted adapters.
6. Verify after acting; actual free-space change and per-item outcomes matter.
7. Offline and deterministic; no cloud/LLM dependency or telemetry by default.
8. Privacy by minimization; summary exports redact identities and full paths.
9. Open-source safety; schemas, fixtures, protected-path tests, and sensitive-owner review.
10. Calm UX; no fear language, health score, artificial urgency, or destructive preselection.

## Capability horizon

### Read-only foundation

- Discover drives and the actual system volume.
- Quick and opt-in Deep Analysis using streaming, bounded, cancellable traversal.
- Skip reparse points; expose access-denied, locked, changing, and unsupported entries.
- Separate logical, allocated, exclusive, cloud-local, protected, unknown, and inaccessible measures.
- Explain top causes, categories, findings, and coverage.
- Export versioned privacy-safe reports.
- Persist bounded aggregate snapshots and compare growth.

### Gated recovery

- Generate immutable, digest-bound, expiring dry-run plans with exact targets where feasible.
- Revalidate protected policy, rule version, target identity, containment, and capability immediately before action.
- Begin with a tiny allowlist: selected eligible items through the Recycle Bin, app-owned fixture/quarantine data, and exact supported tool operations.
- Journal, verify, measure, receipt, and expose partial outcomes and rollback limits.
- Add tool adapters and move workflows independently, only with official behavior and isolated tests.

## Non-goals

CLYR does not clean the registry, optimize RAM, update drivers, scan malware, replace antivirus, kill processes, run at startup or in the background, install a service or driver, defragment or resize/repair filesystems, securely erase or recover files, administer machines remotely, integrate cloud accounts, load arbitrary executable plugins, automatically remove unknown/duplicate data, or promise every estimated byte will be recovered. Automatic compaction of WSL, Docker, VM, or database files is excluded.

## Architecture

The planned solution separates:

- `Clyr.App`: WinUI 3 presentation and MVVM only.
- `Clyr.Cli`: versioned command surface using the same application services.
- `Clyr.Core`: use cases, scanning orchestration, aggregation, classification, policy, snapshots, and future planning.
- `Clyr.Contracts`: versioned DTOs and identifiers shared only where contracts require them.
- `Clyr.Persistence`: SQLite migrations and repositories.
- `Clyr.Rules`: YAML parsing, schema validation, compilation, pack integrity, and risk/action constraints.
- `Clyr.Windows`: Windows filesystem, storage, known-folder, tool, packaging, and future elevation adapters.
- `Clyr.ElevatedHelper`: future Phase 6 short-lived typed action host, never a service.

Dependency direction points inward: UI/CLI and Windows infrastructure depend on application contracts; Core does not depend on UI, SQLite, WinUI, process execution, or concrete Windows adapters. Declarative rule data never becomes an executable extension point.

## Major dependencies and adoption policy

The fixed stack is C#, latest patched .NET 10 LTS, stable WinUI 3/Windows App SDK, MVVM, `Microsoft.Data.Sqlite`, YAML plus JSON Schema, xUnit, Microsoft dependency injection/configuration where practical, structured local logging, `dotnet`, GitHub Actions Windows runners, and signed single-project MSIX as the consumer target. Phase 1 verifies and centrally pins exact supported versions. Dependencies require active maintenance, compatible license, security history review, deterministic/offline behavior, bounded parsing, and a removal path.

## Phase plan and exit gates

| Phase | Outcome | Essential exit gate |
|---:|---|---|
| 0 | Discovery, specification, safety, schemas, diagrams, and repository governance. | Cross-document/schema/Mermaid review passes; no implementation or destructive code. |
| 1 | Buildable, testable, non-destructive solution and UI/CLI/demo shells. | Release build and tests pass; app launches; CLI help works; no admin requirement or real scan. |
| 2 | Read-only drive scanner MVP. | Fixture scans prove no writes, bounded traversal, cancellation, partial results, and clear coverage. |
| 3 | Detection-only rule engine and trustworthy explanations. | Malformed/malicious rules rejected; protected paths always win; every built-in rule tested. |
| 4 | Aggregate snapshots and “What grew?” | Migration/diff/retention tests pass, including incomplete scans and journal fallback. |
| 5 | Cleanup decision system in dry-run only. | Immutable/stale/overlap/path protections pass; fake fixtures only; no real execution. |
| 6 | Tiny low-risk execution allowlist and elevated helper. | TOCTOU, IPC, interruption, receipt, and recovery tests pass; actual recovery measured. |
| 7 | Developer Mode adapters, one tool at a time. | Supported-version and fake-process tests; unsupported versions are report-only. |
| 8 | Supported move-to-drive workflows. | Capacity, copy verification, cutover, interruption, and rollback tests pass. |
| 9 | Security/accessibility/performance hardening and signed public beta. | Clean install/upgrade/uninstall evidence, signing/SBOM/privacy gates, traceable artifacts. |
| 10 | Safe community rule ecosystem and v1. | Declarative-only extension boundary, compatibility rollback, privacy-safe feedback, v1 criteria. |

Each phase stops for approval. No phase is complete with a failing applicable gate. [ROADMAP.md](ROADMAP.md) contains the actionable checklist.

## Test and evidence strategy

Tests progress from pure policy/unit tests through malicious-input safety tests, fixture-only integration and contract tests, maintained UI automation or explicit manual checks, performance fixtures, then isolated packaging/release machines. Property/fuzz suites target canonicalization, parsers, contracts, IPC, redaction, and overlap resolution. Real user or system folders are never cleanup test fixtures.

Evidence reports name the working tree, decisions, commands, results, measured values, unverified claims, limitations, risk changes, manual verification, next phase, and suggested commit. Build/package gates are not simulated when their artifacts do not exist.

## Principal risks

- Path confusion, links, mount points, device paths, or TOCTOU could cross a protected boundary.
- Storage accounting can overstate physical or reclaimable bytes.
- Malicious or weak community rules could misclassify private/protected data.
- Elevation or update channels could create a code-execution boundary.
- Reports/logs/snapshots could leak identities or secrets.
- Huge/changing trees could exhaust resources or undermine completeness.
- Tool behavior can vary by version, locale, installation, and package identity.

The default response is report-only, partial/uncertain labeling, bounded work, and fail-closed mutation. The detailed register is in `docs/RISK_REGISTER.md`.

## Success metrics

Metrics are collected locally in controlled tests, not via product telemetry: completion and partial-result rates; cancellation acknowledgement; peak memory and I/O by fixture size; UI responsiveness; high-confidence classified-byte share; curated-fixture false positives; protected-path violations (must stay zero); dry-run versus measured recovery once execution exists; crash/interruption recovery; accessibility findings; and install/upgrade/uninstall reliability. Initial budgets are provisional engineering hypotheses and must be revised from measured evidence.

## Release criteria

An MVP must let a user install CLYR, scan the system volume read-only, understand major causes and uncertainty, and export a privacy-safe report without mutation. A public beta additionally requires signed traceable packages, clean-machine lifecycle tests, a threat/privacy/accessibility review, no silent telemetry, no persistent helper, an SBOM and license inventory, documented limitations, and zero known protected-path violations. V1 additionally requires stable schemas/compatibility, safe rule contribution and rollback, localization foundations, and maintained support evidence.

## Post-v1 direction

Possible work includes better capability-based ReFS/removable read-only support, opt-in duplicate analysis, signed declarative rule packs, localization, and additional first-party tool adapters. Background monitoring, executable third-party plugins, cross-platform ports, and broader destructive actions remain excluded unless separate product and security decisions justify them.
