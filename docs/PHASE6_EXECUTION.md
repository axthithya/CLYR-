# Phase 6 execution: engine, helper, IPC, persistence, CLI, and WinUI

Status: implementation complete across every reviewable surface; **not approved, and not fully complete** —
the real fixture-only UAC smoke test has not been run in this environment (it requires a real person at an
interactive desktop to approve a Windows UAC prompt). See "What remains" below.

**Crash-recovery correction (this pass):** a durable "Started" execution record is now written — schema v4,
`IExecutionReceiptStore.BeginAsync`/`CompleteAsync` — before any file mutation can occur, and finalized with the
same `ExecutionId` once the outcome is known. If CLYR, Windows, or a future elevated helper crashes after a
mutation but before the terminal record, the next launch's startup reconciliation
(`ReconcileInterruptedAsync`, now actually invoked at `App.OnLaunched` and at the start of every CLI invocation,
not merely available) marks that row `Interrupted` — never `Completed`, never silently resumed, never replayed.
See "Durable execution lifecycle" below for exactly what this does and does not protect.

## Scope

CLYR ships one narrowly allowlisted, low-risk, non-elevated cleanup action end to end, plus the full
architecture a future elevation-requiring action would need without elevating the main app:

1. **Execution engine** (`Clyr.Core.Execution`) — one-time tokens, per-target TOCTOU revalidation, exact
   bounded manifests, cancellation, privacy-safe receipts.
2. **Elevated helper** (`Clyr.ElevatedHelper`) — a separate one-shot process with its own `requireAdministrator`
   manifest and independent request validation.
3. **Typed IPC** (`Clyr.Contracts.ExecutionIpc`, `Clyr.Core.Execution.ElevatedHelperIpc`) — a closed, bounded,
   versioned named-pipe protocol.
4. **UAC launcher** (`ElevatedHelperLauncher`) — the one reviewed `Process.Start` in production source.
5. **Receipt persistence** (`Clyr.Persistence.SqliteExecutionReceiptStore`) — schema v4, a durable
   `BeginAsync`/`CompleteAsync` lifecycle (immutable terminal rows, fail-closed on duplicate begin or unknown/
   conflicting completion), durable plan-replay protection (`HasRecordForPlanAsync`), and a crash-reconciliation
   primitive that is now actually invoked at every application and CLI launch, not merely available.
6. **CLI** — `plan execute`, `execution status|receipt|list|export|discard-receipt`.
7. **WinUI** — Review Plan's execution panel: no default selections, a gated confirmation dialog, live progress,
   cancellation, Completed/PartiallyCompleted/Cancelled/Failed/Interrupted/Unknown-outcome display, receipt
   history/view/export/delete.

See ADR-0002 (helper), ADR-0012 (execution authority/TOCTOU), ADR-0013 (IPC), ADR-0014 (receipts/accounting)
for the design decisions behind each of these.

## Execution allowlist (unchanged since the first Phase 6 slice)

Exactly one enabled action: `builtin.clyr-owned-temp-artifacts` (`Clyr.Core.Execution.BuiltInExecutionActions`),
rooted at `%LocalAppData%\Clyr\Temp`, 7-day minimum age, 512 items / 512 MiB bound, Low risk, no elevation
required. `ClyrOwnedTempArtifactScanner` produces its `CleanupCandidate` with real per-file `CleanupTarget`
entries; `PlanCliCommands.CandidatesFor` and `ReviewPlanViewModel.Candidates` both merge this live-scanned
candidate alongside classification-derived ones, so a plan can actually contain an executable item through the
normal `plan candidates`/`plan create` flow or the Review Plan page.

## Durable execution lifecycle (crash-recovery correction)

`NonElevatedCleanupExecutor.ExecuteAsync` — the one production executor — now follows this exact order:

1. Validate the execution token (identity/session/user/drive/digest), the plan's own digest, and its expiry.
2. Check `IExecutionReceiptStore.HasRecordForPlanAsync(plan.Id, plan.Digest)` — durable replay protection: even
   across a restart (which clears every in-memory attempted-plan guard), the exact same plan identity or digest
   can never reach the executor twice. A genuinely new plan (always a fresh random `CleanupPlanId`, and a digest
   that includes that ID) never matches, so unrelated future plans are never blocked.
3. Consume the one-time token.
4. Generate the `ExecutionId` and durably persist a `Running`-state "Started" record —
   schema version, `ExecutionId`, `PlanId`, plan digest, evidence-state identity, source `ScanId`, drive-identity
   fingerprint, action IDs, approved item count and logical-byte estimate, session ID, a privacy-safe Windows-SID
   fingerprint, privacy mode, and the started timestamp. No raw path is added to this record.
5. Only after that durable write succeeds does the mutation loop begin.
6. Build the terminal receipt and call `CompleteAsync` with the same `ExecutionId`.

**If the durable Started write fails:** no mutation occurs, the already-consumed token cannot be reused (a fresh
plan/token is required — never an automatic retry), and the caller sees a safe rejection.
**If the terminal write fails after mutation:** the true, already-determined outcome is still returned (never
relabeled), with an added warning that the durable trail could not be completed; the Started row remains exactly
as an interrupted execution would look, until the next launch's reconciliation resolves it.

`IExecutionReceiptStore.CompleteAsync` fails closed on an unknown `ExecutionId` (`receipt.unknown-execution`),
rejects retargeting another plan/scan/evidence/drive/session/user (`receipt.completion-mismatch`), is idempotent
only when a repeated completion's digest matches what is already stored, and rejects a genuinely conflicting
repeat (`receipt.immutable`). `BeginAsync` fails closed on a duplicate `ExecutionId` (`receipt.duplicate-begin`).

Startup reconciliation (`ReconcileInterruptedAsync(TimeSpan.Zero, now)`) now actually runs — at `App.OnLaunched`
before the main window is created, and at the start of every `CliApplication.Run` before any command dispatches —
marking any row left non-terminal past that boundary as `Interrupted`. Receipt/history UI and CLI output show a
bounded, non-identifying explanation ("CLYR found an execution that started but did not record a final result...
Run a new Drive Analysis before creating another cleanup plan.") with no Resume button and no automatic retry;
`clyr execution status <id>` returns a non-zero exit code for `Interrupted`/`UnknownOutcome`.

The elevated-helper path remains fully dormant in production — confirmed directly against the code this pass,
not merely from prior reports: `ExecutionEligibilityValidator` rejects every item with `RequiresElevation: true`
(`execution.elevation-unsupported`), and no production caller invokes `ElevatedHelperLauncher.RunAsync` anywhere.
If a future elevated action is ever added, it must go through the identical Begin-before-mutate/Complete-after
pattern — there is no second way to write a receipt row that skips it.

## WinUI execution flow

`ReviewPlanPage` (`src/Clyr.App/Pages/ReviewPlanPage.xaml[.cs]`) adds, below the existing dry-run plan preview:

- **Executable items** — one checkbox per plan item that independently passes
  `ExecutionEligibilityValidator`, all unchecked by default (`IsChecked = false` in code, never true); "Run
  selected cleanup" starts disabled and only enables once at least one item is checked.
- **Confirmation** — a `ContentDialog` ("Final cleanup confirmation dialog") whose primary button starts
  disabled and only enables once the "I understand that selected cache or temporary data may be permanently
  removed" acknowledgement checkbox is checked; the dialog states the action may be irreversible and that
  actual free-space change may differ from the estimate.
- **Progress** — state text, live removed/skipped/failed counters fed by a real
  `IProgress<ExecutionItemResult>` callback from `NonElevatedCleanupExecutor.Execute` (added this pass — the
  executor now reports after every target it processes), a redacted current-target line, and a Cancel button
  wired to a real `CancellationTokenSource`.
- **Finished state** — maps every `ExecutionState` (`Completed`, `PartiallyCompleted`, `Cancelled`, `Failed`,
  `Interrupted`, `Rejected`, `UnknownOutcome`) to display text, shows removed/skipped/failed counts, removed
  logical bytes kept separate from observed free-space change, warnings, and "View details" / "Export receipt"
  / "Run a new analysis" (navigates to Scan; never auto-rescans) / "Done".
- **Receipt history** — lists every locally persisted `ExecutionReceiptSummary`, with per-row "View" (shows the
  full JSON receipt) and "Delete receipt" (calls `IExecutionReceiptStore.DiscardAsync` — removes only that one
  CLYR-owned row).

No dangerous one-click phrasing ("Fix everything", "Optimize now", "Delete all", "One-click clean", "Clean
automatically") appears anywhere; `Clyr.Safety.Tests.UiArchitectureTests` enforces this together with the
presence of the required consent/accountability vocabulary. Developer Mode remains a static preview page with
no `Click=` handlers at all — verified by a new test.

`App.xaml.cs` wires `IExecutionTokenService`, an `ExecutionSessionContext` (one `ExecutionSessionId` per app
launch), and `IExecutionReceiptStore` (real `SqliteExecutionReceiptStore` normally; an in-memory-only
`UiFixtureExecutionReceiptStore` when `CLYR_UI_FIXTURE=1`, so receipt history/view/export/delete can be
exercised by UI Automation without ever opening the real CLYR-owned history database). `ExecutionFixtureRoot`
gives fixture launches a private temporary directory seeded with four synthetic stale files instead of the real
`%LocalAppData%\Clyr\Temp` — so `scripts/verify-winui.ps1`'s execution steps run entirely against synthetic data
it creates and cleans up itself. This was verified with a real rendered WinUI window in this session: the full
execution flow (no default selection, gated confirmation requiring the acknowledgement checkbox, a completed
fixture run, receipt history/view/export/delete, and all six responsive sizes with no horizontal overflow) ran
and passed for real — see the completion report for the exact sequence and what it does not prove (a live
mid-run cancellation racing to `PartiallyCompleted` specifically, since local fixture deletes are too fast to
reliably interrupt from an external script; that exact code path is instead proven deterministically by
`Clyr.Core.Tests.ExecutionTests`).

## CLI (unchanged from the previous Phase 6 slice; see prior report for detail)

`plan execute <plan-id> --confirm-digest <prefix> [--json]` and
`execution status|receipt|list|export --output <path>|discard-receipt` — active in-memory plan only, digest
confirmation required, no `--force`/`--path`/`--root`/`--action`/`--command`. Plan replay is rejected both
per-process (in-memory `attemptedPlanIds`) and durably (`HasRecordForPlanAsync` against the receipt store, which
survives a restart even though the in-memory plan store does not). `plan execute` now refuses to run at all if
no execution-receipt store is available, and requires the plan to pass full revalidation (not just its digest)
before selecting executable items.

## What remains

1. **The real fixture-only UAC smoke test.** `scripts/run-phase6-uac-smoke.ps1` and its harness
   (`tools/Phase6UacSmoke`, deliberately outside `Clyr.sln` and outside `src/`) are built, and the harness
   itself builds cleanly against the Release output. Running it triggers a real Windows UAC consent prompt that
   only a person at an interactive desktop can approve or deny — that step was not performed in this
   environment. **Phase 6 implementation is ready for final approval, but Phase 6 remains incomplete until the
   fixture-only UAC smoke test passes.**
2. ~~A "started" receipt placeholder for true crash-mid-run recovery~~ — **implemented this pass**: see "Durable
   execution lifecycle" above. What remains open: this closes the gap for the one production (non-elevated)
   executor only; a real crash was not injected against the live SQLite file in this environment (only simulated
   via an in-memory test double and a directly-seeded pre-existing row), and the broader IPC/helper security
   matrix in point 3 below is unaffected by this change.
3. **Broader IPC/helper security matrix** — protocol downgrade, forged completion response, wrong-client/binary
   identity verification beyond the pipe ACL — not implemented; see ADR-0013's Consequences.
4. A live UI Automation run of the extended `scripts/verify-winui.ps1` (it was written, parse-checked, and its
   logic reviewed against the actual XAML/code-behind, but not executed against a rendered window in this
   environment — see the completion report for exactly what was and wasn't run).

## Verification

`scripts/verify-phase6.ps1` runs the full Phase 0–5 verifier chain, the complete solution build/test/format,
Phase 6–specific test filters, repository safety scans scoped to the reviewed execution boundary, a dependency
vulnerability audit, `git diff --check`, and (unless `-SkipUiAutomation`) `scripts/verify-winui.ps1`. Running it
end to end in this environment surfaced that native Windows PowerShell here has no `rg` (ripgrep) binary on
PATH — a pre-existing gap in the Phase 4/4.1/5 verifier scripts, not a Phase 6 regression (confirmed via
`git log`, predating this session). Those specific scans were re-verified manually with ripgrep through this
session's Bash tool instead; every other gate in the chain (Phase 0–3 fully, and all of Phase 6's own build,
test, format, scoped safety scans, and vulnerability audit) ran and passed for real. See the completion report
for the exact command-by-command results.
