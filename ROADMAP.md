# Roadmap and Acceptance Checklists

CLYR advances one approved phase at a time. A checked documentation item means the contract exists, not that the feature exists. Every implementation phase must update the threat model, risk register, tests, docs, and changelog and then stop for approval.

## Phase 0 — Discovery, specification, and documentation

- [x] Repository preflight and preservation review.
- [x] Product brief, personas, user stories, misuse cases, journey, scope, and non-goals.
- [x] Architecture, dependency/process/privilege boundaries, domain and storage models.
- [x] Safety, threat, protected-resource, privacy, retention, IPC, and future transaction contracts.
- [x] Quick/Deep scan behavior, state models, accounting, error, UX, accessibility, test, and performance plans.
- [x] Rule/export schemas, valid/invalid examples, diagrams, ADRs, matrices, and research evidence.
- [x] Open-source governance, milestones, labels, issue/PR templates, CODEOWNERS, and local verification.
- [x] Final validation and consistency report.

Exit: applicable Phase 0 checks pass, no destructive or product implementation code exists, and all future capabilities are labeled planned.

## Phase 1 — Solution skeleton and engineering foundation

- [x] Install/verify the latest patched .NET 10 SDK and record the environment blocker resolution.
- [x] Recheck official sources; pin SDK, stable Windows App SDK, NuGet/test packages, and Actions.
- [x] Create `Clyr.sln`, central props/packages, projects, dependency boundaries, nullable/analyzers/warnings-as-errors, and formatting.
- [x] Add DI/configuration, privacy-safe logging, versioned contracts, SQLite migration foundation, deterministic environment/clock fakes, and enforce that no filesystem/process execution adapter exists yet.
- [x] Add WinUI navigation and isolated demo-data shell; CLI help/doctor/rule validation; no real-drive scan.
- [x] Add test projects, architecture tests, fixture-generator responsibility skeleton, CI, vulnerability/license/no-secret checks, and verification scripts.
- [x] Prove Release build/tests, app launch/navigation, CLI commands, schema validation, no admin requirement, and no mutation capability.

Exit: a clean checkout builds and tests; app/CLI demo shells run; no real scan or cleanup exists.

## Phase 2 — Read-only drive scanner MVP

- [x] Capability-based drive discovery and system-volume labeling.
- [x] Streaming bounded enumeration, progress throttling, cancellation, overlap rejection, and partial results.
- [x] Skip/report reparse points and isolate access-denied, disappeared, and enumeration errors.
- [x] Aggregate logical size, bounded top-N folders/files, and structural extension families; allocated size remains unavailable and hard-link double counting is disclosed.
- [x] Quick/Deep contracts, coverage/uncertainty, cloud-placeholder-safe metadata, privacy-safe CLI JSON, and responsive WinUI controls.
- [x] Fake/temporary-fixture integration, lifecycle, cancellation, loop, no-content-read, privacy, schema, and 10k/100k/1M benchmark tests.

Exit: scanner never writes or hydrates content, handles cancellation/partial access, and exposes uncertainty; no cleanup controls.

## Phase 3 — Classification, explanations, and rule engine

- [x] Harden bounded YAML/schema loading, built-in pack manifest/integrity, deterministic overlap, and protected policy.
- [x] Add detection-only first-party built-ins and invalid/tamper corpus; no action execution.
- [x] Present category/tags/confidence/status/evidence, finding details, explanations, and privacy-safe report v2.
- [x] Validate every built-in with positive/negative fixtures, one-million-observation evidence, and CI.

Exit: malicious/malformed rules fail; protected always wins; unknown remains unknown; “safe” requires evidence.

## Phase 4 — Snapshots and “What grew?”

- [x] Implement versioned SQLite aggregate snapshots, transactional migration, integrity/recovery, retention, and user deletion.
- [x] Compare complete/partial/cancelled snapshots and rank aggregate growth without leaking child paths.
- [x] Add functional history UI/CLI and a fallback-first unsupported USN abstraction; full scans remain authoritative.

Exit: migrations/diffs/retention pass; no journal dependency; incomplete comparisons expose uncertainty.

## Phase 4.1 — Polished read-only UI/UX

- [x] Replace the shared content surface with distinct Overview, Scan, Results, History, Developer Mode, Privacy, Licenses, About, and Settings pages.
- [x] Keep full scan controls on Scan only; preserve the Phase 4 read-only service, history, and comparison behavior.
- [x] Add a restrained design-token system, reusable headers/empty states, automatic navigation compaction, page scrolling, responsive card reflow, accessible names, and text alternatives.
- [x] Add fixture-only UI Automation for cancellation, completion, results, comparison, page identity, narrow-window scrolling, and absence of cleanup controls.
- [x] Add UI architecture regression tests, accessibility guidance, a dedicated verifier, and CI.

Exit: the presentation layer is distinct, navigable, scrollable, accessible by automation, and still contains no cleanup, planning, mutation, elevation, or Phase 5 behavior.

## Phase 5 — Cleanup planning and dry-run only

- [x] Implement strongly typed actions, immutable digest-bound plans, ten-minute expiry, rule/version/identity binding, bounded selections, and duplicate rejection.
- [x] Revalidate protected roots and target metadata; show potential logical bytes, uncertainty, consequences, risk, elevation, rollback, staleness, and unavailable execution.
- [x] Add a deliberately disabled production executor plus malicious-plan, path, digest, schema, CLI, safety, and fixture-only UI tests.

Exit: plan changes or staleness require re-plan/re-confirm; no arbitrary command/path; no real mutation.

## Phase 6 — Low-risk execution and elevated helper

- [x] Implement one narrowly allowlisted built-in action (CLYR-owned stale temporary artifacts), a one-time token, per-target TOCTOU revalidation immediately before mutation, an exact bounded manifest, and immutable execution receipts.
- [x] Add a short-lived, one-shot, typed/bounded/versioned-IPC elevated helper (`Clyr.ElevatedHelper`) with independent request revalidation, plus the tightly controlled UAC launcher that is the only `Process.Start` in production source; no enabled action requires it yet.
- [x] Add CLI (`plan execute`, `execution status|receipt|list|export|discard-receipt`) and a WinUI Review Plan execution flow (no default selection, gated confirmation, live progress/cancellation, all terminal states, receipt history/view/export/delete).
- [ ] Run the real fixture-only UAC smoke test (`scripts/run-phase6-uac-smoke.ps1`) with a person at an interactive desktop approving the prompt.
- [ ] Persist a "started" receipt row before deletion begins, for true crash-mid-run recovery (the reconciliation mechanism exists and is tested in isolation, but nothing writes the placeholder row yet).

Exit: no arbitrary commands, protected violations remain zero, partial outcomes and actual recovery are accurate, all actions opt-in — met except the UAC smoke test, which has not been run.

## Phase 7 — Developer Mode

- [x] Add read-only detection for a closed set of 14 tool families (Docker, WSL, Node/npm, pnpm, Yarn,
      .NET/NuGet, Gradle, Maven, Python/pip, Rust/Cargo, Flutter/Dart, Android SDK, Playwright, generic build
      output) via a compiled taxonomy over existing rule findings, reusing `CleanupCandidateFactory` unchanged.
- [x] Add trusted, name-only executable discovery (PATH + bounded known-folder search, `.exe` only, reject
      reparse points) and a narrow, closed-argument, timeout-bounded, output-bounded read-only probe
      (`docker --version`, `wsl --status` only) for the two tools where classification alone is insufficient.
- [x] Add CLI (`developer tools|scan|show|findings|plan|capabilities|doctor`) and a WinUI dashboard (snapshot
      picker, per-tool cards, tool detail with diagnostics, "Review in plan" routed through the existing Phase 5
      plan pipeline for the subset of findings already `DryRunEligible`).
- [ ] Visual Studio and VS Code are not covered by a dedicated tool family this phase (their build output is
      only visible through the generic `developer.buildoutput.generic` rule, not a named adapter).
- [ ] Per-tool version-range binding, signature verification beyond reparse-point rejection, and locale-independent
      output parsing are not implemented — the two probes parse a best-effort version substring from plain-text
      output and are not guaranteed to succeed on every locale/version combination (see ADR-0016's Consequences).

Exit: every tool family is isolated and documented; unsupported/undetected versions are report-only; Docker
volumes, WSL virtual disks, and Android emulator images remain `Protected` and are never automatic removal
targets — met for the tools covered this phase; VS/VS Code adapters and version-range binding are deferred.

## Phase 8 — Move-to-another-drive workflows

- [ ] Begin with Windows-supported known folders; add application-specific adapters only with official workflows.
- [ ] Check capacity/filesystem/conflicts, copy with proportional verification, then cut over; journal, resume, and roll back.

Exit: source survives until destination verification; insufficient capacity fails early; no generic junction shortcut.

## Phase 9 — Hardening, packaging, and public beta

- [ ] Complete security, dependency/license, privacy, performance, and accessibility reviews.
- [ ] Produce signed traceable single-project MSIX, SBOM, reproducibility evidence, demo screenshots, beta limitations, and release automation.
- [ ] Test clean install, upgrade, uninstall, offline operation, settings retention, helper exit, database migration, and signature verification.

Exit: all beta gates pass with no silent telemetry and no persistent helper; artifacts trace to a commit.

## Phase 10 — Community ecosystem and v1

- [ ] Ship rule linter/fixtures, compatibility manifest/rollback, trusted review, optional signed packs, privacy-safe false-positive export, and localization framework.
- [ ] Define executable-plugin prohibition, public roadmap, stable support commitments, and v1 evidence.

Exit: community data cannot execute code; failures are safe and reversible; v1 quality/support criteria pass.

## Current approval gate

Phase 4.1 and Phase 5 are implemented in the current working tree. Phase 6 (execution engine, elevated helper, IPC, receipts, CLI, WinUI) is implemented and stopped at its approval gate pending the real fixture-only UAC smoke test. Phase 7 (Developer Mode read-only detection: Core, CLI, WinUI) is implemented and verified in the current working tree — see `docs/PHASE7_DEVELOPER_MODE.md`. The exact next phase is **Phase 8 — Move-to-another-drive workflows**, and it must not begin without explicit approval.
