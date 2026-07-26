# CLYR — Project Briefing for AI Onboarding

> Purpose of this document: give an AI assistant (or a new engineer) enough grounded context to work on this
> repository correctly on the first try — what CLYR is, why it's built this way, what's actually implemented
> versus merely designed, and where the load-bearing safety logic lives. Everything below was verified against
> the actual source code, not inferred from comments or aspirational docs, as of commit `3356e9d` on `main`
> (working tree clean at time of writing).

---

## 1. What CLYR is

**CLYR is a trustworthy Windows storage-understanding and low-risk cleanup application.** It scans a Windows
drive, explains what is consuming space in plain language, and — for a deliberately tiny, reviewed set of
categories — can safely delete files, with every step gated by explicit user confirmation and typed safety
checks.

The product exists to solve a specific trust problem: most "PC cleaner" tools either (a) lie about how much
space they'll recover, (b) delete things they shouldn't, or (c) hide what they're actually doing behind vague
"Optimize now" buttons. CLYR's entire architecture is built around the opposite instinct — **never claim more
than is true, never mutate anything without an explicit, typed, revalidated authorization, and never let a
finding by itself imply "this is safe to delete."**

The one-sentence safety invariant that governs every design decision in this codebase:

> No storage mutation occurs unless CLYR can identify the exact approved capability, target, consequence, risk,
> and confirmation that authorize it.

This is stated verbatim in `docs/SAFETY_MODEL.md` and is treated as load-bearing, not aspirational — it is
enforced by dozens of automated tests, not just prose.

---

## 2. Technology stack

- **.NET 10 / C# 13**, `net10.0-windows10.0.26100.0`, `win-x64`.
- **WinUI 3** (Windows App SDK 2.2.x) for the desktop app.
- **Microsoft.Data.Sqlite** (+ SQLitePCLRaw) for all local persistence — a single CLYR-owned SQLite database
  (`history.db`) under `%LocalAppData%\CLYR`, with an in-process schema-versioned migration system.
- **xunit** for all automated testing (8 test projects, ~900 tests as of this writing — see §9).
- A standalone **CLI** (`clyr.exe` / `Clyr.Cli`) that mirrors most of the WinUI app's capability as text/JSON
  commands, useful for scripting and for testing without a rendered window.
- No network calls anywhere in the product by design (no telemetry, no update-checker, no cloud features).

---

## 3. Solution layout

```
src/
  Clyr.Contracts/       Pure data contracts (records/enums) shared by every other project. No logic.
  Clyr.Core/             The engine: scanning primitives, classification, cleanup planning, execution,
                          Administrator Retry reconciliation, Developer Mode taxonomy. No Windows API calls,
                          no UI, no SQL — everything here is testable with fakes.
  Clyr.Core/Execution/   Sub-namespace: the Phase 6 execution engine (tokens, executor, receipt lifecycle,
                          elevated-helper IPC types — see §7).
  Clyr.Windows/          The only project allowed to call real Windows APIs (file enumeration, drive discovery,
                          volume identity, elevated-scan retry factory).
  Clyr.Rules/             Loads and validates the built-in YAML rule pack (rules/builtin/rules.yaml) plus
                          community rule-pack schema validation.
  Clyr.Persistence/      SQLite-backed implementations of the storage interfaces Clyr.Core defines
                          (ISnapshotStore, IExecutionReceiptStore's concrete SqliteExecutionReceiptStore, etc.)
                          and the schema migration runner (AppMetadataDatabase).
  Clyr.App/               The WinUI 3 desktop application — pages, view models, DI composition (App.xaml.cs).
  Clyr.Cli/                The command-line front end (Program.cs + CliApplication.cs + per-area partial files).
  Clyr.ElevatedHelper/    A separate, one-shot, UAC-elevated process for a not-yet-production-wired execution
                          path (see §7.5 — this is built and tested but currently unreachable in production).
  Clyr.ElevatedScanner/   A separate elevated process used only by the Administrator Retry read-only permission-
                          limited-root rescan (distinct from the cleanup-execution helper above).

tests/
  Clyr.Contracts.Tests, Clyr.Core.Tests, Clyr.Windows.Tests, Clyr.Rules.Tests, Clyr.Persistence.Tests,
  Clyr.Cli.Tests, Clyr.Integration.Tests, Clyr.Safety.Tests

docs/
  ROADMAP.md, PHASE_STATUS.md            — phase checklist / status table (see caveat in §5)
  PHASE5_CLEANUP_PLANNING.md,
  PHASE6_EXECUTION.md, PHASE7_DEVELOPER_MODE.md   — per-phase implementation notes, kept current by this
                                                     session's work for Phase 6
  SAFETY_MODEL.md, RISK_REGISTER.md, CAPABILITY_MATRIX.md, THREAT_MODEL.md, PRIVILEGE_MODEL.md, SECURE_IPC.md
  adr/000x-*.md                           — architecture decision records (numbered, append-only)

rules/builtin/rules.yaml                  — the built-in detection rule pack (25 rules as of this writing)
```

**Dependency direction** (important, was violated once and had to be fixed): `Clyr.Contracts` has no
dependencies. `Clyr.Core` depends only on `Clyr.Contracts`. `Clyr.Persistence` and `Clyr.Windows` depend on
`Clyr.Core` + `Clyr.Contracts`. `Clyr.App`/`Clyr.Cli` depend on everything. **Interfaces belong in `Clyr.Core`,
concrete storage implementations belong in `Clyr.Persistence`** — e.g. `IExecutionReceiptStore` lives in
`Clyr.Core.Execution` precisely so `Clyr.Core`'s own executor can depend on the abstraction without a circular
reference to `Clyr.Persistence`.

---

## 4. The phase-based development model

CLYR is built one **approved phase** at a time, tracked in `ROADMAP.md` and `PHASE_STATUS.md`. The project's own
stated rule: a checked box in these docs means "the contract/design exists," **not** "the feature is verified
implemented" — every phase report is expected to verify claims against actual code, not just documentation.

| Phase | Name | Status (verified against code, not just docs) |
|---|---|---|
| 0 | Discovery/specification | Complete |
| 1 | Engineering foundation | Complete, approved |
| 2 | Read-only scanner | Complete, approved |
| 3 | Rules/explanations | Complete, approved |
| 4 | Snapshots/history/growth comparison | Complete, approved |
| 4.1 | Polished UI/UX | Complete, approved |
| 5 | Dry-run cleanup planning | Implemented and verified — **awaiting approval** |
| 6 | Low-risk execution + elevated-helper scaffolding | Engine/persistence/CLI/WinUI implemented — **not approved**; see §7 for exactly what's real |
| 7 | Developer Mode (read-only tool detection) | Implemented and verified — **awaiting approval** |
| 8 | Move-to-another-drive workflows | **Not started.** Explicitly gated — must not begin without explicit approval |
| 9 | Public beta (signing/MSIX/SBOM) | Not started |
| 10 | Ecosystem / v1 | Not started |

**Important terminology trap, worth flagging to any AI working on this repo:** the repository's own Phase 7 is
"Developer Mode" (read-only detection of 14 developer-tool families — npm/Gradle/Maven/Docker/WSL/etc. storage
usage, no new deletion capability). A user or task description that says "Phase 7" while actually meaning "the
next cleanup category" or "more things CLYR can delete" is conflating two different things — that
work is really a **continuation of Phase 6** (low-risk execution), not Phase 7. This exact confusion was
identified and explicitly called out during a prior audit in this project's history; don't silently accept a
task's phase-number framing without checking it against `PHASE_STATUS.md`/`ROADMAP.md` first.

`docs/PHASE_STATUS.md`'s own working-tree evidence table is **not kept continuously up to date** — it still
shows Phase 6 as "not complete" without the crash-recovery work landed in this session, and its test counts
(163/163) are stale relative to the current ~900. Treat `PHASE_STATUS.md` as a snapshot from an earlier point,
not a live source of truth; the per-phase docs (`PHASE5_CLEANUP_PLANNING.md`, `PHASE6_EXECUTION.md`,
`PHASE7_DEVELOPER_MODE.md`) and the actual code are more reliable.

---

## 5. The end-to-end pipeline (how a user's session actually flows)

```
 Drive Analysis (Phase 2/3)
   → real-time file/folder enumeration (Clyr.Windows.WindowsFileSystemEnumerator)
   → streaming rule-pack classification (no per-file paths retained — see ADR-0009)
   → ScanResult (aggregate counters, top-N ranked paths, category totals, findings)
        │
        ├─→ optional: Administrator Retry (permission-limited-root rescan via a separate elevated
        │    Clyr.ElevatedScanner process) → reconciled, enriched ScanResult with the SAME ScanId
        │
        ├─→ optional: saved to History as a StorageSnapshot (SQLite, schema-versioned)
        │
        └─→ Review Plan
              → CleanupCandidateFactory turns findings into CleanupCandidate objects with an
                eligibility verdict (Protected / Unsupported / InsufficientEvidence / ManualReviewOnly /
                DryRunEligible / NotEligible) — see §6 for the category table
              → user selects DryRunEligible candidates
              → CleanupPlanBuilder produces an immutable, digest-bound, evidence-state-bound CleanupPlan
              → CleanupPlanValidator can revalidate that plan against current live evidence at any time
              → for the ONE real executable category (see §7), ExecutionEligibilityValidator gates it
              → NonElevatedCleanupExecutor: token validate/consume → durable "Started" record →
                mutation loop (independent TOCTOU re-probe per file) → durable terminal receipt
              → Receipt/history UI, CLI `execution status/receipt/list`
```

---

## 6. Why almost nothing is actually deletable (the aggregate-vs-exact architectural split)

This is the single most important structural fact about this codebase, and the thing most likely to confuse an
AI that hasn't read the code directly.

**ADR-0009 (accepted, Phase 3)** deliberately made the scanner/classifier **aggregate-only**: it performs no
per-rule filesystem traversal beyond the single main enumeration pass, and retains no raw per-file paths — only
counters, top-N rankings, and category totals. Finding IDs are deterministic hashes of
`(rulePackId|rulePackVersion|ruleId|ruleVersion|category)` — **not** of the observed bytes/paths — so the same
rule match always produces the same finding ID regardless of what changed on disk.

The consequence: `CleanupCandidateFactory` (`src/Clyr.Core/CleanupCandidates.cs`), which turns classification
findings into `CleanupCandidate` objects, can **never** attach real per-file `CleanupTarget` data — by design,
not by oversight. Every classification-derived candidate has `Targets = ImmutableArray<CleanupTarget>.Empty`.
Even the six rule IDs marked `DryRunEligible` (npm/pnpm/nuget/gradle/maven/playwright caches) can be
*previewed* in a dry-run plan but can **never** pass `ExecutionEligibilityValidator` — which independently
requires `ActionType == TrustedBuiltInCleanup`, `ExecutionAvailability == Phase6BuiltInExecutable`, and
`Targets.Length > 0`, none of which classification-derived candidates ever satisfy.

**The only category that can actually execute today** is `builtin.clyr-owned-temp-artifacts`, produced by
`ClyrOwnedTempArtifactScanner` (`src/Clyr.Core/Execution/ClyrOwnedTempArtifactScanner.cs`) — a **second,
entirely separate, dedicated filesystem walker**, not part of the rule/classification engine at all. It scans
exactly one root (`%LocalAppData%\Clyr\Temp` — CLYR's own scratch folder), finds files older than 7 days, and
produces one real candidate with genuine per-file `CleanupTarget` entries (real canonical path, volume identity,
size, timestamps, reparse/cloud-placeholder flags).

**The template for any future real-execution category is `ClyrOwnedTempArtifactScanner`, not a change to
`CleanupCandidateFactory`.** Any AI asked to "make category X actually deletable" must build a new dedicated
scanner analogous to this one — changing `Decide()`/`Create()` in `CleanupCandidates.cs` cannot make anything
executable, because `ExecutionEligibilityValidator` and the empty-`Targets` hardcoding form an independent,
structural second gate.

### Category eligibility table (from `CleanupCandidateFactory.Decide`, `src/Clyr.Core/CleanupCandidates.cs`)

| Eligibility | Categories |
|---|---|
| `Protected` (never a cleanup candidate) | WindowsSystemManaged, WindowsUpdateServicing, RestoreRecovery, Containers, VirtualMachines, Wsl, AndroidSdkEmulators, CloudSync |
| `InsufficientEvidence` (forced, extra-cautious) | BrowserCache (Chrome/Edge/Firefox) — always, regardless of confidence |
| `DryRunEligible` (preview-only, structurally never executable — see above) | the 10 developer-cache rule IDs (npm, pnpm, nuget, gradle, maven, playwright, yarn, pip, cargo, flutter) |
| `ManualReviewOnly` | UserDownloads, UserDocuments, UserMedia, ArchivesInstallers, DeveloperDependencies, BuildOutput, Logs, CrashDumpsDiagnostics, TemporaryFiles |
| `NotEligible` | everything else (e.g. RecycleBin today) |
| Real, executable today | **only** `builtin.clyr-owned-temp-artifacts` (not a `rules.yaml` entry — a dedicated scanner) |

---

## 7. The Phase 6 execution and safety machinery (the most heavily engineered part of the codebase)

This is where most of the recent work in this project's history has concentrated, because it's where a bug has
real-world consequences (actual file deletion).

### 7.1 The execution allowlist

`src/Clyr.Core/Execution/BuiltInExecutionActions.cs` — a closed, compile-time list. As of this writing it
contains **exactly one enabled entry**: `builtin.clyr-owned-temp-artifacts` (rooted at
`known-folder:local-app-data/clyr/temp`, 7-day minimum age, 512 items / 512 MiB bound, `RiskLevel.Low`, no
elevation). Nothing outside this list can execute — enforced by `ExecutionEligibilityValidator`.

### 7.2 Multi-layer validation before any mutation

1. **`CleanupPlanValidator.Validate`** — checks schema version, digest integrity (constant-time comparison),
   expiry, and staleness across scan/snapshot/drive/rule-pack/category-registry/app-compat/privacy-mode/**and
   evidence-state** (see §7.3).
2. **`ExecutionEligibilityValidator.ValidateItemForExecution`** — per plan item: eligibility, risk, action type,
   execution availability, no-elevation, allowlist membership, root-identity match, target-count bounds.
3. **`ExecutionTokenService`** — one-time, in-memory-only, 2-minute-lifetime tokens bound to plan ID, plan
   digest, application session, Windows user SID, and drive identity. Consumed atomically exactly once.
4. **`ExecutionTargetProcessor.Process`** — the actual TOCTOU gate: immediately before `File.Delete`, re-probes
   the file's reparse state, cloud-placeholder state, size, and last-write time against what the plan recorded,
   and re-runs `WindowsPathSafetyValidator` (component-aware containment, reparse rejection, protected-path
   denial). Any mismatch → `SkippedChanged`, never forced.
5. **`WindowsPathSafetyValidator`** — the shared protected-path gate (Windows/system directories, WinSxS,
   pagefile/hiberfil/SAM/SYSTEM, credential/password/game-save heuristics, `.vhd(x)`/`.vmdk`/`.ost`/`.pst`/`.db`
   extensions) used by both the scanner and the executor.

### 7.3 Evidence-state binding (fixed in this project's history — see §10)

A plan is bound not just to a `ScanId`, but to an **evidence-state digest** (`Clyr.Core.EvidenceState`) — a
SHA-256 content hash over root contributions, scan coverage, allocation accounting, and classification
findings/categories. This exists because **Administrator Retry deliberately preserves the original `ScanId`**
while changing what was actually observed — so `ScanId` alone was never sufficient to detect "this plan's
evidence changed since it was built." `EvidenceState.ForResult`/`ForSnapshot` are pure functions: identical
content → identical digest, regardless of object reference identity, page navigation, or reformatting.

### 7.4 Durable execution lifecycle / crash recovery (also fixed in this project's history — see §10)

`NonElevatedCleanupExecutor.ExecuteAsync` (the one production executor) writes a durable **"Started"** row
(`ExecutionState.Running`, via `IExecutionReceiptStore.BeginAsync`) — carrying `ExecutionId`, `PlanId`, plan
digest, evidence-state ID, source `ScanId`, drive-identity fingerprint, action IDs, approved item count/byte
estimate, session ID, a privacy-safe hash of the Windows user SID, and the started timestamp, **before any file
mutation can occur** and only after the one-time token is consumed. The same `ExecutionId` is later finalized
via `CompleteAsync`. If the durable write itself fails, execution stops with zero mutation. If the *terminal*
write fails after mutation, the true outcome is still returned (never hidden), with a warning that the durable
trail is incomplete — the Started row is left exactly as an interrupted execution would look.

At every app/CLI launch, `IExecutionReceiptStore.ReconcileInterruptedAsync(TimeSpan.Zero, now)` now actually
runs (previously it existed but nothing called it) and marks any lingering non-terminal row `Interrupted` —
never `Completed`, never auto-resumed, never auto-retried. Durable replay protection
(`HasRecordForPlanAsync`) also means the exact same `PlanId`/digest can never reach the executor twice, even
across a process restart (a genuinely new plan always gets a fresh random ID, so this never blocks unrelated
future plans).

### 7.5 The elevated helper path is fully dormant (confirmed by direct code search, not inference)

`Clyr.ElevatedHelper` and its IPC protocol (`ElevatedHelperLauncher`, `ElevatedHelperIpc`,
`ElevatedHelperRequestHandler`, `Clyr.Contracts.ExecutionIpc`) are built, unit-tested, and would launch a real
UAC prompt if invoked — but **zero production call sites** invoke `ElevatedHelperLauncher.RunAsync` anywhere.
`ExecutionEligibilityValidator` unconditionally rejects any item with `RequiresElevation: true`
(`execution.elevation-unsupported`), so nothing can currently reach that code path. It exists so the
architecture is ready for a future elevation-requiring action without elevating the main app — any such future
action must go through the identical Begin-before-mutate/Complete-after lifecycle gate described in §7.4;
there is no second way to write a receipt row that bypasses it.

**This confirms Phase 6 is not approved and the real UAC smoke test is still an outstanding, separate
checkpoint** — nothing in this session's or prior sessions' work claims otherwise.

---

## 8. Administrator Retry (permission-limited-root rescan)

A completed Deep Analysis may have roots it couldn't fully read (permission-denied). "Administrator Retry" lets
the user re-scan just those roots with an elevated helper process (`Clyr.ElevatedScanner` — distinct from the
cleanup-execution helper in §7.5), then safely **reconcile** the elevated figures into the original result
without double-counting (`ElevatedScanResultReconciler`) and **enrich** the active in-session result
(`ElevatedScanResultEnricher.Build` → `AppSessionViewModel.ApplyEnrichedResult`), which deliberately keeps the
same `ScanId` (this is exactly the behavior that made evidence-state binding in §7.3 necessary). Reconciliation
distinguishes `Additive` (root was 100% unread before) from `Replacement` (root was partially read; the new
figure — which can legitimately be *smaller* than before if files were deleted meanwhile — replaces it, not
adds to it) from `Overlap` (net-zero, no new evidence).

---

## 9. Testing philosophy and current state

- **~900 tests, 0 known failures**, spread across 8 xunit projects, as of commit `3356e9d`.
- **`Clyr.Core.Tests`** (largest, ~500 tests): pure object-level tests against the engine, using fakes/temp
  directories, never touching a real user path.
- **`Clyr.Persistence.Tests`**: real `SqliteExecutionReceiptStore`/`SqliteSnapshotStore` against disposable
  temp `.db` files — including schema-migration tests that manually construct an old-schema database and verify
  upgrade-in-place preserves existing rows with safe defaults for new columns.
- **`Clyr.Cli.Tests`**: full `CliApplication.Run(...)` integration tests. A few tests legitimately touch the
  real `%LocalAppData%\Clyr\Temp` folder (the CLI has no root-override hook, unlike the WinUI path) — these are
  grouped into an xunit collection with `DisableParallelization = true` to prevent them from racing each other.
- **`Clyr.Safety.Tests`**: a distinctive pattern worth understanding — `Clyr.App` is a WinUI project with no
  plain xunit test project referencing it (WinUI SDK friction), so **every App-layer safety property in this
  codebase is verified via source-text assertions** (`File.ReadAllText(...)`; `Assert.Contains(...)`) rather
  than compiling against and instantiating the actual view models/pages. This is deliberate and consistent
  across ~15 files in this project — don't try to "fix" it by adding a WinUI test project unless asked.
- Every automated test uses fakes, in-memory fixtures, or disposable temp directories — `RepositorySafetyTests`
  and repo-wide scans enforce that no test can ever target a real user path (R-026 in `RISK_REGISTER.md`,
  treated as non-waivable).
- `CLYR_UI_FIXTURE=1` is an environment variable that swaps every real service (drive discovery, scan service,
  snapshot store, Administrator Retry service, execution receipt store) for an in-memory/synthetic
  implementation, used for WinUI UI-Automation smoke testing without ever touching real drives or data.

---

## 10. Recent history (this session and its immediate predecessors, chronological)

These are the four most recent substantive changes, each its own commit, each independently verified:

1. **`aa70c3b` / `5fa138e` / `e57bb0e` / `e05baf1`** — a sequence of UI/UX polish and Administrator Retry
   accounting-correctness passes (root-reconciliation mode-scoping, negative-delta handling, retry-summary
   persistence bugs, branding/terminology cleanup, directory/finding presentation).
2. **`34daf09` — "fix: invalidate cleanup plans when analysis evidence changes"**: introduced
   `EvidenceState` (§7.3) and bound it into `PlanBinding`/`PlanValidationContext`/the plan digest; bumped the
   plan schema version 1→2; made `ReviewPlanViewModel`/CLI revalidate against **live** evidence instead of a
   plan echoing its own binding back at itself (which had trivially always "validated"); added a "Rebuild
   required" banner and auto-discard-with-explanation instead of silently keeping a stale plan on screen.
3. **`3356e9d` — "fix: persist a durable execution-start record for crash recovery"**: the durable Started/
   Complete execution lifecycle described in §7.4; moved `IExecutionReceiptStore` from `Clyr.Persistence` to
   `Clyr.Core.Execution` to fix a dependency-direction problem; SQLite schema v3→v4 (5 new columns, existing
   rows preserved with safe defaults); wired `ReconcileInterruptedAsync` into actual app/CLI startup (it existed
   but was never called before); durable plan-replay protection; new receipt/history UI wording for
   Interrupted/UnknownOutcome states with no Resume button and no automatic retry.

**Common thread across all three fixes**: each was found by an explicit read-only audit that verified claims
against actual code rather than trusting prior status reports or doc comments — and each found that some
piece of safety machinery *existed in the code* (a store method, a validator field) but was **never actually
wired up or invoked** at the point that mattered. This is the recurring bug shape in this codebase: not missing
logic, but missing *wiring* between correctly-built pieces. Any AI auditing this repo further should expect to
find more of this shape before assuming a described safety property is actually enforced end-to-end.

---

## 11. Explicit constraints this project consistently operates under

These appear, nearly verbatim, in every task given to an AI working on this repository, and are worth treating
as durable ground rules rather than one-off instructions:

- Never perform Git mutations unless explicitly asked (no commit/reset/checkout/clean) — the user stages and
  commits manually.
- Never run a real cleanup action against real user data; never launch a real UAC prompt.
- Never begin the next unapproved phase (currently: don't start Phase 8) without explicit approval.
- Never add a new cleanup category or execution action as a side effect of an unrelated fix.
- All fixture/test data must be synthetic or in a disposable temp directory — never a real user path.
- Preserve all existing/previously-completed work; don't regress prior phases while fixing something new.
- Verify claims against actual code before reporting completion — this codebase's own prior-session reports
  have been wrong before (features that were "implemented" turned out to be built but not wired in).

---

## 12. Glossary (terms that mean something specific in this codebase)

- **Finding** — an aggregate classification result (category + bytes + count), never proof content is
  disposable, never carrying a raw path.
- **Candidate** (`CleanupCandidate`) — a finding turned into a possible cleanup item, with an eligibility
  verdict; usually has zero real per-file targets (see §6).
- **Plan** (`CleanupPlan`) — an immutable, digest-bound, evidence-state-bound, expiring (≤10 min) set of
  user-selected candidates, built by `CleanupPlanBuilder`.
- **Evidence state** — a content digest over everything a plan's candidates were derived from; changes whenever
  Administrator Retry (or anything else) changes what was actually observed, even if `ScanId` stays the same.
- **Token** (`ExecutionToken`) — one-time, in-memory authorization to run one execution attempt, bound to plan +
  session + user + drive.
- **Receipt** (`ExecutionReceipt`) — the durable, privacy-safe record of an execution attempt; now written
  twice (Started, then Completed) under the same `ExecutionId`, never containing raw paths.
- **Snapshot** (`StorageSnapshot`) — a saved History entry (SQLite), distinct from a live in-session
  `ScanResult`.
- **Administrator Retry** — the permission-limited-root elevated rescan-and-reconcile feature (§8); unrelated
  to the cleanup-execution elevated helper (§7.5), which is a different process for a different purpose.

---

## 13. Where to look first for common tasks

| If asked to... | Start here |
|---|---|
| Understand why something can/can't be deleted | `src/Clyr.Core/CleanupCandidates.cs`, `ExecutionEligibilityValidator.cs`, §6 above |
| Add a new real-execution cleanup category | Model on `ClyrOwnedTempArtifactScanner.cs`; wire into `BuiltInExecutionActions.cs` and both `ReviewPlanViewModel.Candidates`/CLI `CandidatesFor` — **do this only if explicitly asked**, it's a new capability |
| Debug a plan "going stale" unexpectedly | `EvidenceState.cs`, `CleanupPlanValidator.cs`, `ReviewPlanViewModel.ValidateAgainstCurrentEvidence` |
| Debug an execution/receipt issue | `NonElevatedCleanupExecutor.cs`, `IExecutionReceiptStore` (`Clyr.Core.Execution`), `SqliteExecutionReceiptStore` (`Clyr.Persistence`) |
| Change WinUI presentation/wording | `src/Clyr.App/Pages/*.xaml[.cs]`; verify via `Clyr.Safety.Tests` source-text assertions, since there's no compiled WinUI test project |
| Understand current phase truth | Read `docs/PHASE6_EXECUTION.md`/`PHASE7_DEVELOPER_MODE.md` directly, and the actual code — not just `PHASE_STATUS.md` (§4 caveat) |
| Run the full verification a task expects | `dotnet build Clyr.sln -c Release`; `dotnet test Clyr.sln -c Release --no-restore`; `dotnet format Clyr.sln --verify-no-changes` (expect ~16 pre-existing LF-only files to still flag — confirmed harmless/pre-existing via `git diff --stat`, not a regression to fix); `git diff --check`; `git status --short` |
