# Phase 6 execution: engine, helper, IPC, persistence, and CLI

Status: substantially implemented in the working tree; **not approved, not complete**. This document describes
only what exists and is tested. See "What Phase 6 still requires" for the remaining gap — most importantly the
real fixture-only UAC smoke test, which this environment cannot perform.

## Scope of this slice

Building on the core non-elevated execution engine (models, one-time token, per-target TOCTOU revalidation,
exact bounded manifest, cancellation, privacy-safe receipts — unchanged from the prior slice and re-verified
here), this pass adds:

1. A separate one-shot elevated helper process (`Clyr.ElevatedHelper`) with its own independent request handler.
2. A typed, bounded, versioned named-pipe IPC protocol between CLYR and the helper.
3. A tightly controlled UAC launcher for the helper (unused by the current allowlist, since no enabled action
   requires elevation, but implemented and tested).
4. SQLite-backed execution receipt persistence with immutability and crash-recovery reconciliation.
5. CLI commands: `plan execute`, `execution status|receipt|list|export|discard-receipt`.

Not implemented in this pass: the WinUI execution flow (Review Plan confirmation/progress/finished states) and
the exhaustive documentation/ADR sweep listed in the original Phase 6 spec. The real fixture-only UAC smoke
test — launching the helper through an actual interactive UAC prompt — has not been performed; this environment
has no interactive Windows session to click "Yes" on a UAC dialog.

## Execution allowlist (unchanged)

Still exactly one enabled action: `builtin.clyr-owned-temp-artifacts`, rooted at `%LocalAppData%\Clyr\Temp`,
7-day minimum age, 512 items / 512 MiB bound, Low risk, no elevation required. See
`Clyr.Core.Execution.BuiltInExecutionActions`. Nothing was added to or removed from the allowlist this pass.

## Elevated helper (`Clyr.ElevatedHelper`)

A separate executable project, `net10.0-windows10.0.26100.0`, referencing only `Clyr.Contracts` and `Clyr.Core`
— no WinUI, no App SDK. Its `app.manifest` requests `requireAdministrator`; the main `Clyr.App` manifest is
untouched and remains `asInvoker`.

`Program.cs` accepts **exactly one** command-line argument — a pipe name matching `^Clyr\.Helper\.[0-9A-F]{32}$`
— and rejects anything else outright. It calls `ElevatedHelperIpc.RunOneShotAsync`, which accepts one connection,
reads one bounded request, hands it to `ElevatedHelperRequestHandler.Handle`, writes one bounded response, and
returns. The process then exits. There is no listening loop, no second request, no retry, no resident state.

`ElevatedHelperRequestHandler.Handle` independently re-validates every field of the request before touching
anything: protocol version, manifest bounds, nonce shape, token expiry, plan digest shape, action ID against the
closed allowlist (`BuiltInExecutionActions.Find`), declared root identity against the allowlist entry, and
that the trusted root actually exists on this machine. Only then does it call the same
`ExecutionTargetProcessor.Process` used by the non-elevated executor — once per target, live against this
process's own filesystem view — so calling it "independent" describes independent execution, not merely
independent code paths that happen to share logic. Cooperative cancellation is checked between targets.

## Typed IPC (`Clyr.Contracts.ExecutionIpc`, `Clyr.Core.Execution.ElevatedHelperIpc`)

`HelperRequest`/`HelperResponse` are closed sealed records — protocol version, request ID, nonce, session ID,
user SID, drive identity, action ID, trusted root identity/path, plan ID/digest, token expiry, and an exact
`ImmutableArray<HelperTargetManifestItem>` manifest (bounded to `HelperProtocol.MaxManifestItems` = 512). There
is no command field, no script field, no executable-path field, no unrestricted argument list, and no
environment-variable field. `HelperIpcSerializer` uses `System.Text.Json`'s default reflection contract with a
hard 256 KiB frame-size ceiling enforced both before serializing and after reading a length-prefixed frame off
the wire — there is no polymorphic type discriminator anywhere in these contracts, so there is no unsafe
polymorphic deserialization surface to exploit.

Transport is a named pipe (`ElevatedHelperIpc`, `[SupportedOSPlatform("windows")]`) with a random 128-bit hex
pipe name generated fresh per request (`NewPipeName`), a `PipeSecurity` ACL restricted to the current Windows
user, a single server instance (`maxNumberOfServerInstances: 1`), a length-prefixed framing format with the
same 256 KiB bound enforced on both read and write, and an overall request timeout
(`HelperProtocol.RequestTimeout` = 30s). The server accepts one connection, processes one request-response
exchange, and returns — there is no way to send a second request down the same pipe. `HelperIpcTests.cs`
exercises this transport for real (not mocked): a real named pipe, a real background listener, a real client
connection, in `RealNamedPipeRoundTripDeliversTypedRequestAndResponse`, plus a real connection-timeout case.

## UAC launcher (`Clyr.Core.Execution.ElevatedHelperLauncher`)

The single, tightly controlled process launch permitted anywhere in production CLYR. `RunAsync` resolves the
helper path as `Path.Combine(AppContext.BaseDirectory, "Clyr.ElevatedHelper.exe")` — never an arbitrary path —
starts it with `UseShellExecute = true, Verb = "runas"` and exactly one argument (the freshly generated pipe
name), then sends the real request only afterward over the already-established IPC channel. A declined UAC
prompt surfaces as `ElevationOutcome.Denied`, never retried automatically and never treated as an error state
distinct from a normal rejected/cancelled result. No current allowlisted action calls this path — it exists so
the architecture is ready without elevating the main process. `RepositorySafetyTests` proves `Process.Start`
appears nowhere in production source except this one file.

## Receipt persistence (`Clyr.Persistence.SqliteExecutionReceiptStore`)

Schema v3 adds an `ExecutionReceipt` table (migration is additive over the existing schema v2 snapshot tables;
`AppMetadataDatabase.CurrentSchemaVersion` is now 3). `SaveAsync` upserts by execution ID but refuses to
overwrite a row already in a terminal state (`Completed`, `PartiallyCompleted`, `Cancelled`, `Failed`,
`Interrupted`, `UnknownOutcome`, `Rejected`) — throwing `ExecutionReceiptStoreException("receipt.immutable", …)`
— and retains at most the 200 most recent rows. No raw file paths are stored; only the privacy-safe fields
already defined on `ExecutionReceipt` (drive-identity fingerprint, counts, logical-byte totals kept separate
per outcome category, free-space before/after/delta, warnings, limitations, digest). `ReconcileInterruptedAsync`
marks any row still missing `CompletedAtUtc` and older than a caller-supplied staleness threshold as
`Interrupted` — it can only ever produce `Interrupted`, never guess `Completed`. This pass does not yet wire a
"started" placeholder row at the beginning of an execution (see gaps below), so today every row is written once,
at completion, by the CLI; the reconciliation mechanism exists and is tested but has nothing to reconcile until
a future caller starts persisting in-flight rows.

## CLI

`clyr plan execute <plan-id> --confirm-digest <prefix> [--json]` (`PlanCliCommands.PlanExecute`): requires the
plan to still be held in the CLI's in-memory `ICleanupPlanStore` (imported/exported plans have no such record
and are rejected as not-found), requires `--confirm-digest` to be a prefix of the plan's actual digest (no
`--force`, no `--yes` as the sole barrier), rejects a plan ID that has already been attempted once in this
process (`plan.consumed`), rejects an expired plan, auto-selects every plan item that independently passes
`ExecutionEligibilityValidator` (there is no `--action`/`--path`/`--root`/`--command` flag — nothing arbitrary
is ever accepted), issues a one-time token, runs the same `NonElevatedCleanupExecutor` used everywhere else, and
persists the resulting receipt. Exit code is 0 only for `Completed`/`PartiallyCompleted`.

`clyr execution status|receipt|list|export --output <path>|discard-receipt` (`ExecutionCliCommands.cs`) read
from the same receipt store; `export` writes the already-privacy-safe receipt JSON verbatim.

`PlanCliCommands.PlanCandidates`/`PlanCreate` now also include the live-scanned built-in candidate
(`ClyrOwnedTempArtifactScanner.Scan`) alongside the existing scan/snapshot-derived candidates, so a plan can
actually contain an executable item without a separate out-of-band mechanism — `plan candidates --snapshot <id>`
lists it, and `plan create --finding builtin:clyr-owned-temp-artifacts` selects it.

`Phase6ExecutionCliTests.cs` exercises the full path for real: creates a synthetic stale file directly under
the CLI's resolved trusted root (`%LocalAppData%\Clyr\Temp` — the only root these commands ever touch), runs
`plan create` → `plan execute` → `execution list`, and asserts the file is actually gone and a receipt exists;
separate tests assert wrong-digest and plan-replay rejection.

## What Phase 6 still requires

1. **The real fixture-only UAC smoke test.** This environment has no interactive Windows session to approve a
   UAC prompt, so the helper's elevation path has been built and IPC-tested but never actually launched through
   real UAC. Per the spec's own fallback: **Phase 6 is not complete because the required fixture-only UAC smoke
   test has not been performed.**
2. **WinUI execution flow** — Review Plan's before/confirmation/running/finished states, cancellation UI,
   receipt viewer. Not started.
3. **A "started" receipt placeholder** persisted before deletion begins, so a real application crash mid-run
   leaves a genuinely reconcilable `Running` row rather than no row at all. `ReconcileInterruptedAsync` exists
   and is tested against a synthetic in-flight row, but nothing in this pass calls `SaveAsync` before
   completion.
4. **Full documentation/ADR sweep** — README, ROADMAP, ARCHITECTURE, DATA_MODEL, SAFETY_MODEL, THREAT_MODEL,
   PRIVILEGE_MODEL, SECURE_IPC, ERROR_HANDLING, RECOVERY, RULE_ENGINE, STORAGE_ACCOUNTING, UI_UX,
   UX_STATE_MACHINE, TESTING_STRATEGY, OPERATIONS, EXPORT_FORMAT, ACCESSIBILITY, DECISION_LOG, RISK_REGISTER,
   and a dedicated ADR — only this file and `PHASE_STATUS.md` were updated this pass.
5. **Broader IPC/helper security matrix** — protocol downgrade, forged completion response, wrong-client
   detection, helper-crash-mid-request recovery, and the CLI's own `Interrupted`/crash-recovery path are not
   yet covered by a test; the fields and states exist (`ExecutionState.Interrupted`/`UnknownOutcome`,
   `ReconcileInterruptedAsync`) but the end-to-end interruption story is untested.

**Phase 6 is not complete.** The non-elevated engine, the elevated helper, the IPC protocol, receipt
persistence, and the CLI execution surface are real, built, and tested end to end (including a real named-pipe
round trip and a real file deletion through the full CLI plan-execute path). The real UAC smoke test, the WinUI
surface, and the full documentation sweep are not.
