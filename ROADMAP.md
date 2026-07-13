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

- [ ] Harden YAML/schema loader, built-in pack manifest/integrity, deterministic overlap, and protected policy.
- [ ] Add detection-only built-ins and malicious/invalid corpus; no action execution.
- [ ] Present risk/confidence/evidence, finding details, “Why is this drive full?”, and privacy-safe report.
- [ ] Validate every built-in with fixtures, official evidence, community guidance, and CI.

Exit: malicious/malformed rules fail; protected always wins; unknown remains unknown; “safe” requires evidence.

## Phase 4 — Snapshots and “What grew?”

- [ ] Implement versioned SQLite aggregate snapshots, transactional migration, integrity/recovery, retention, and user deletion.
- [ ] Compare complete/partial snapshots and rank growth without leaking paths.
- [ ] Add history UI/CLI diff and a fallback-first optional USN abstraction with reset/wrap behavior.

Exit: migrations/diffs/retention pass; no journal dependency; incomplete comparisons expose uncertainty.

## Phase 5 — Cleanup planning and dry-run only

- [ ] Implement strongly typed actions, immutable digest-bound plans, ten-minute expiry, rule/version/identity binding, and overlap rejection.
- [ ] Revalidate protected roots and exact targets; show bytes, evidence, consequences, risk, elevation, and rollback.
- [ ] Add fake execution interfaces and malicious-plan/property tests against fixtures only.

Exit: plan changes or staleness require re-plan/re-confirm; no arbitrary command/path; no real mutation.

## Phase 6 — Low-risk execution and elevated helper

- [ ] Implement only approved Recycle Bin operations, app-owned test/quarantine data, and exact allowlisted tool commands.
- [ ] Journal before mutation; revalidate identity immediately; verify copy/action/free-space; create immutable receipts.
- [ ] Add short-lived authenticated typed helper, denial/timeout/output limits, replay/TOCTOU/path-swap/fuzz tests, and crash recovery.

Exit: no arbitrary commands, protected violations remain zero, partial outcomes and actual recovery are accurate, all actions opt-in.

## Phase 7 — Developer Mode

- [ ] Add read-only detection, then individually reviewed adapters for npm/pnpm/Yarn, NuGet, pip, Gradle/Maven, Flutter/Dart, Rust, Playwright, Docker, WSL, Android, Visual Studio, and VS Code.
- [ ] Bind version ranges, discovery/signature/location checks, exact arguments, timeouts, output limits, locale-independent parsing, and fake runners.

Exit: every adapter is isolated and documented; unsupported versions are report-only; volumes/disks/emulators/dependencies are never automatic removal targets.

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

## Exact next phase

Phase 2 is complete in the current working tree. Stop here. The exact next phase is **Phase 3 — Classification, explanations, and rule engine**, and it must not begin without explicit approval.
