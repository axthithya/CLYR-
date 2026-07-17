# Testing Strategy

## Safety-first test pyramid

CLYR tests decisions at the lowest deterministic layer and reserves real Windows/package checks for isolated environments. Automated cleanup tests never target the developer or CI machine. All mutation uses temporary generated roots, app-owned data, fake Windows/process adapters, or disposable virtual machines expressly provisioned for the test.

| Layer | Purpose | Typical scope | First phase |
|---|---|---|---:|
| Static/schema | Formatting, analyzers, dependency rules, JSON/YAML/Mermaid/docs, secret/destructive-pattern checks | Entire repository | 0/1 |
| Unit/property | Domain invariants and hostile input spaces | Pure functions and fakes | 1 |
| Safety regression | Prove protected policy/action/adapter boundaries | Malicious paths, rules, plans, IPC | 1–6 |
| Contract | Versioned app/CLI/export/helper compatibility | Serialized DTO fixtures | 1–6 |
| Fixture integration | Orchestration over deterministic trees/databases/fake tools | Temporary roots only | 2–7 |
| UI/accessibility | State/navigation/semantics and demo data | No real-drive dependency | 1/9 |
| Performance/soak | Bounded memory, cancellation, progress, database, adapter timeouts | Generated/disposable fixtures | 2+ |
| Packaging/release | Install, upgrade, sign, offline, uninstall, helper lifecycle | Clean VMs/test machines | 9 |

No lower layer is replaced by an end-to-end test. Failures include seed, fixture version, environment/capability, exact command, and bounded relevant output.

## Unit and property suites

- Windows-aware path parsing, canonicalization, component containment, volume identity, case comparison, and protected-resource precedence.
- Aggregation, top-N bounds, hard-link ownership, accounting qualification, overlap resolution, and no-double-count invariants.
- Scan/action/UX state transitions, cancellation, progress throttle, and idempotent terminal events.
- Risk/confidence/disposition policy and explanation completeness.
- YAML/schema/manifest parsing, limits, compatibility, hash, ordering, and safe fallback.
- Privacy redaction, stable local tokens, log field classification, export validation, and retention selection.
- Snapshot diff with complete/partial/coverage/method/schema mismatch.
- Future plan immutability, digest, expiry, rule binding, overlap, identity, and confirmation.
- Typed external-tool arguments, executable discovery, timeout/output bounds, locale/exit mapping.
- Future IPC framing, version negotiation, caller/session/identity, nonce/replay, request size/batch, and receipt coverage.

Property/fuzz generators emphasize semantically equivalent/adversarial forms rather than random strings alone. Every discovered failure becomes a minimized fixed regression fixture. Seeds are logged. Bounds prevent a fuzz case from exhausting CI.

## Malicious-input corpus

Required cases include `..` traversal; mixed/alternate separators and casing; environment expansion; UNC, extended-length, volume-GUID and device paths; drive-relative paths; ADS; reserved names; trailing dots/spaces; 8.3 aliases; Unicode normalization/confusables; symlinks, junctions, mount points and loops; link/identity swaps; malformed/aliased/deep/oversized YAML; duplicate keys; unknown fields/versions; regex/resource bombs; shell metacharacters; free-form executables/arguments; oversized/truncated/replayed IPC; stale plans; hash/signature mismatch; and privacy tokens/credentials in fields.

The oracle is fail closed: a malformed/ambiguous rule or action is rejected; a scan observation becomes skipped/unknown/partial when safe continuation is possible.

## Fixture catalog

All names and contents are synthetic.

| Fixture | Evidence exercised | Expected safety property |
|---|---|---|
| `basic-mixed-tree` | Nested files/extensions/categories | Stable accounting/top-N without writes |
| `deep-and-wide` | Long/deep/wide enumeration | Bounded queue/stack and long-path handling |
| `million-entry-stream` | Generated observation stream | Bounded memory/progress; no million objects retained |
| `access-denied` | Adapter-generated denial | Partial coverage, scan continues |
| `locked-and-disappearing` | Lock/delete/rename/resize races | Typed isolated errors; no crash |
| `cancel-every-state` | Cancellation injection | Deterministic terminal state and resource release |
| `drive-removed` | Capability loss | DriveRemoved with useful partial evidence |
| `hardlink-sparse-compressed` | Physical/logical distinctions | Qualified totals; no naive exact claim |
| `cloud-placeholder` | Cloud attributes/provider fakes | No hydration/content read |
| `reparse-loop-and-mount` | Junction/symlink/mount tags | No traversal/escape/double count |
| `protected-and-lookalikes` | Real protected components and sibling-prefix names | Protected wins; lookalikes handled component-wise |
| `fake-developer-caches` | npm through WSL/Docker/Android representations | Report-only and tool-semantic distinctions |
| `rules-valid-invalid-malicious` | Schema/parser/policy corpus | Exact accept/reject reasons |
| `snapshot-versions` | Every supported migration and incomplete snapshot | Transactional migration and honest diff |
| `privacy-canaries` | Fake usernames/tokens/secrets/path topics | Summary/log contains none |
| `plan-path-swap` | File/junction/identity replacement after plan | Revalidation rejects, zero unintended mutation |
| `action-interruption` | Crash at every journal/action boundary | Reconcile to exact/unknown, never assumed success |
| `fake-tool-matrix` | Missing/version/locale/hang/output/exit cases | Bounds and report-only fallback |
| `ipc-malicious` | Spoof/replay/truncate/oversize/order/capability cases | Helper rejects and exits |

Fixtures needing NTFS/ReFS/cloud/EFS/package identity are capability-tagged and run only on isolated matching machines; absence is a reported skip, not a pass.

## Phase-specific gates

### Phase 0

Required files/headings; UTF-8/line endings; JSON parse; Draft 2020-12 schemas; valid/invalid examples; internal links; Mermaid parsing/render when available; naming/taxonomy/phase claims; table structure; secret/machine-path/generated-junk/destructive-code scan; `git diff --check`; final status evidence. Build/package tests are inapplicable because no solution exists.

### Phase 1

Implemented Phase 1 coverage contains 38 behavioral tests across eight projects: contracts (3), core/privacy/configuration (4), persistence/SQLite (4), rules (7), Windows adapters/logging (2), CLI (8), safety/architecture (9), and integration (1). Tests use deterministic demo data, in-memory SQLite or random files under the operating-system temporary directory, approved repository rule fixtures, and fake environment values. No test targets a real Windows or application-data directory.

The Release build treats warnings and analyzers as errors. The repository safety suite verifies Central Package Management, exact stable versions, dependency direction, a non-elevated manifest, forbidden mutation/process APIs, and the absence of Phase 2 service types. The persistence suite captures `sqlite_version()` and enforces the documented patched minimum from ADR 0007.

Format; warnings-as-errors Release build; unit/architecture/contract/schema tests; app demo launch; CLI help/doctor; fake filesystem separation; dependency vulnerability/license inventory; no-secret scan; CI on pinned Windows runner/toolchain. No real scan or cleanup.

### Phases 2–4

No-write scanner tests, cancellation and state tests, coverage/error fixtures, memory/I/O benchmarks, privacy export, detection/protection/malicious-rule corpus, migrations/diffs/retention/corruption, and journal fallback.

Phase 2 implements 39 additional behavioral cases, bringing the solution total to 77: hostile drive-root validation; Quick/Deep bounds; nested roll-up; deterministic bounded rankings; structural extension families; reparse and cloud-placeholder handling; access denial; overlap; cancellation lifecycle; progress throttling/redaction; support-safe export/schema; CLI parsing/output separation/export; drive-label privacy; Windows discovery and temporary metadata fixtures; source-level no-content-read assertions; and 10k/100k/1M generated streams. No automated case scans a real drive root.

The final 2026-07-13 local synthetic run measured 10k in 33 ms with 6,088 retained managed bytes/12,288 working-set growth bytes, 100k in 298 ms with 8,928/5,070,848 bytes, and 1M in 584 ms with 3,560/9,302,016 bytes, each with 25 retained top entries. Values vary with host/runtime and are regression evidence, not disk-throughput claims.

### Phases 5–8

Fake-only dry run first; immutability/expiry/overlap/identity; then disposable action roots, TOCTOU/link swap, journal crash matrix, receipt/measurement, IPC fuzz, helper identity/lifecycle, external-tool bounds, and migration copy/verify/rollback. Each adapter/workflow has an independent release flag and evidence.

### Phase 9+

Clean VM install/launch/upgrade/downgrade policy/uninstall, package/signature/provenance/SBOM, offline behavior, no persistent helper, database migration, accessibility, performance/soak, privacy/security review, and release dry run. Publication remains manual/authorized until gates pass.

## UI and accessibility testing

Phase 1 uses deterministic demo data plus `scripts/verify-winui.ps1`, which launches the unpackaged app, finds the `CLYR` window through Windows UI Automation, selects Overview, Scan, Results, History, Developer Mode, Privacy, Licenses, About, and Settings, verifies every selection, checks the exact demo disclosure, and closes the app. Keyboard, Narrator, theme, and scaling certification remain Phase 9 work.

## Quality-gate command

`scripts/verify-phase0.ps1` remains the documentation regression entry point. `scripts/verify-phase1.ps1` extends it with restore, Release build, tests, format, package audit/inventory, CLI, and rule-validation gates. `scripts/verify-phase2.ps1` adds Windows-adapter, detailed synthetic benchmark, and CLI drive-discovery evidence while deliberately starting no real scan. `scripts/verify-winui.ps1` is the interactive-session launch/navigation/read-only scan-control gate.

## Test-data privacy

Never copy a real scan, user path, registry/database, dump, log, cloud item, project, token, or credential into fixtures. Generators use fixed fictional identities and obvious canary secrets solely to assert removal. Failure output caps samples and redacts before logs/reports. Test artifacts are deleted from temporary product-owned roots, never by broad workspace/system cleanup.

## Acceptance criteria

- Protected-path violations remain exactly zero.
- Every safety-critical bug has a minimized regression case.
- No automated mutation test can resolve outside its verified temporary root.
- Skips name missing capability and do not inflate pass counts.
- Results separate measured, inferred, unverified, and manual evidence.
- A failed applicable gate prevents phase completion or release.
## Phase 4 gates

Gates cover migration/idempotency, transactional normalized round-trip, lifecycle eligibility, retention floor, settings, deletion isolation, key stability, compatibility and drift, new/removed groups, overflow saturation, significance boundaries, deterministic CLI JSON, confirmations, corruption translation, and all earlier regressions. Tests use fakes and temporary databases; verification starts no real drive scan.
## Phase 5 verification matrix

Core tests cover eligibility precedence, immutable ordering, digest recomputation/mutation, selection bounds, overflow, expiry, stale drive/rule/category/app/privacy bindings, target metadata, dry-run uncertainty, memory retention, and disabled execution. Security theories cover traversal, sibling prefixes, UNC/device paths, alternate streams, reparses, protected paths, 8.3 ambiguity, and environment escapes. Rule/schema, CLI, repository-safety, and fixture-only UI tests cover plan commands, absent mutation commands, no preselection, preview/export/discard, and ten-page responsive bounds.
## Phase 6 verification matrix

Core tests (`ExecutionTests.cs`, `HelperIpcTests.cs`) cover the happy path, token single-use/expiry/mismatch
rejection, tampered plan digest, wrong session/user, target-outside-root, plan-time reparse claims,
changed-since-plan targets, missing targets, pre-start cancellation, non-built-in items never executing, a real
named-pipe round trip, and a real connection timeout — all against synthetic temporary fixture directories.
Persistence tests (`ExecutionReceiptStoreTests.cs`) cover round-trip accounting, terminal-state immutability,
list ordering, discard, and interrupted-row reconciliation. CLI tests (`Phase6ExecutionCliTests.cs`) exercise
`plan create` → `plan execute` → `execution list` end to end, including a real deletion of a synthetic fixture
file under CLYR's own trusted root, plus wrong-digest and plan-replay rejection. Repository safety tests
(`RepositorySafetyTests.cs`) confine `File.Delete`/`Directory.Delete` to `src/Clyr.Core/Execution/**` and
`Process.Start` to `ElevatedHelperLauncher.cs`, and prove `requireAdministrator` appears only in the helper's
own manifest. UI architecture tests (`UiArchitectureTests.cs`) prove no default selection, the required
consent/accountability vocabulary, absence of one-click phrasing, and that Developer Mode has no interactive
controls. `scripts/verify-winui.ps1`'s execution steps were run against a real rendered window in this session
(not merely parsed) and passed. The one test this repository cannot run in most environments is a real,
interactive fixture-only UAC elevation — `scripts/run-phase6-uac-smoke.ps1` requires a person at the desktop to
approve the prompt; see `docs/PHASE6_EXECUTION.md` for its current status.
