# Phase 6 execution: core engine slice

Status: partially implemented in the working tree; **not approved, not complete, and not started as a full
Phase 6 delivery**. This document describes only what exists. See "What Phase 6 still requires" below for
everything the full Phase 6 specification calls for that is deliberately deferred.

## Scope of this slice

This slice implements, end to end and under test, the non-elevated execution engine: the one-time token,
per-target TOCTOU revalidation, the exact bounded manifest, cancellation, and the privacy-safe execution
receipt. It does **not** implement the separate elevated helper process, IPC, CLI `plan execute`, or any
WinUI execution surface. No enabled action requires elevation, so the absence of the helper does not block
the one action family that is enabled — but Phase 6 as specified is not complete until the helper, IPC,
fixture-only UAC smoke test, CLI, WinUI, and full documentation sweep also land.

## Execution allowlist

Exactly one action family is enabled, defined in `Clyr.Core.Execution.BuiltInExecutionActions`:

| Field | Value |
|---|---|
| Action ID | `builtin.clyr-owned-temp-artifacts` |
| Trusted root | `known-folder:local-app-data/clyr/temp` (`%LocalAppData%\Clyr\Temp`) |
| Risk | Low |
| Minimum age | 7 days |
| Bounds | 512 items, 512 MiB total |
| Elevation | Not required |

This root is written only by CLYR itself (export staging buffers, diagnostic snapshots). Nothing else is
expected to write there, so the ordinary risks of cache cleanup (browser session state, user-authored
content, unknown application state) do not apply. Every other Phase 5 finding — browser cache, developer
tool caches, user downloads/documents/media, logs, crash dumps — remains report-only or manual-review-only;
none of them gained execution availability in this slice.

`Clyr.Core.Execution.ClyrOwnedTempArtifactScanner` produces the `CleanupCandidate` for this root: it walks
the trusted root non-recursively into reparse points (`AttributesToSkip = ReparsePoint`), applies the age
cutoff, validates each candidate through the existing `WindowsPathSafetyValidator`, and stops at the bound.
The resulting candidate carries real per-file `CleanupTarget` entries (unlike Phase 5 scan-derived
candidates, whose targets are populated only once exact evidence exists) with a new action type,
`CleanupActionType.TrustedBuiltInCleanup`, and a new execution-availability value,
`ExecutionAvailability.Phase6BuiltInExecutable`, added to the existing closed contracts in
`Clyr.Contracts.CleanupPlanning`. `CleanupPlanValidator` was updated to accept plan items carrying either the
Phase 5 or the Phase 6 availability value; everything else about plan immutability, digesting, and staleness
detection is unchanged from Phase 5.

## One-time execution token

`Clyr.Core.Execution.ExecutionTokenService` issues an in-memory, single-process `ExecutionToken` bound to:

- the plan ID and digest,
- an `ExecutionSessionId` representing the current application process run,
- the current Windows user SID (as supplied by the caller — no SID resolution is implemented in this slice),
- the drive identity from the plan binding,
- the requested action IDs,
- a 256-bit random nonce, and
- a 2-minute expiry.

`Validate` checks every one of those fields with fixed-time string comparisons and rejects unknown, expired,
already-consumed, or mismatched tokens. `Consume` is a single atomic dictionary insert — a token can be
consumed exactly once, and the executor consumes it immediately after validation succeeds and before any
target is touched, so a second `Execute` call with the same token is rejected before it can re-run.
Tokens never leave the process and are never persisted; there is no cross-session or cross-restart replay
surface because there is nothing to replay against.

## Pre-execution and per-target revalidation

`NonElevatedCleanupExecutor.Execute` runs, in order:

1. Token validation (session, user, drive, plan ID, plan digest, expiry, consumption).
2. Recomputes the plan digest (`CleanupPlanCanonicalizer.Digest`) and compares it to `plan.Digest` —
   independent of the token check, so a plan object that was mutated in memory after the token was issued
   is also caught.
3. Plan expiry.
4. Token consumption (single use).

Any failure here rejects the whole request (`ExecutionState.Rejected`) before any item is inspected.

For each selected item, `ExecutionEligibilityValidator.ValidateItemForExecution` requires
`CleanupEligibility.DryRunEligible`, `RiskLevel.Low`, `CleanupActionType.TrustedBuiltInCleanup`,
`ExecutionAvailability.Phase6BuiltInExecutable`, no elevation requirement, a matching enabled action in the
allowlist, and a declared root identity that matches the allowlist entry exactly. Any other item (including
every Phase 5 report-only/manual-review item) is rejected without touching its targets.

For each target of an eligible item, the executor re-validates **live from disk**, ignoring everything the
plan recorded except as a comparison baseline:

- `WindowsPathSafetyValidator.Validate` against the actual trusted root path — rejects traversal, outside-root
  paths, and a target that claims to be a reparse point.
- `File.Exists` — a missing file is `NotFound`, not a failure.
- A fresh `FileInfo` probe — re-checks reparse-point and cloud-placeholder attributes live (independent of
  what the plan recorded).
- Identity comparison — current size and last-write time must still match what the plan observed, and the
  file must still be older than the action's age threshold; any mismatch is `SkippedChanged`, never forced.
- Deletion is attempted only after every check passes, and only ever `File.Delete` — no attribute clearing,
  no ownership change, no ACL change. `UnauthorizedAccessException` → `SkippedAccessDenied`;
  `IOException` (locked/in use) → `SkippedLocked`; anything else → `Failed`, never silently swallowed.

## Exact bounded manifest

There is no recursive or wildcard deletion anywhere in this slice. The manifest is exactly the
`CleanupTarget` list captured in the plan at planning time, filtered to caller-selected item IDs, ordered
deterministically by item ID. The trusted root itself is never a deletion candidate — only files under it
are. No directory removal (even of empty directories) is implemented in this slice.

## Cancellation

The executor checks a `CancellationToken` before each item and before each target within an item. Once
cancellation is observed, no further items are started; whatever succeeded before that point is preserved
in the receipt with `ExecutionState.Cancelled` (nothing removed yet) or `ExecutionState.PartiallyCompleted`
(some removed before cancellation landed). There is no automatic resume — a new plan and a new token are
required for another attempt, by construction (the token is already consumed).

## Receipts

`ExecutionReceipt` (`Clyr.Contracts.Execution`) is an immutable record of one execution attempt:
schema version, execution ID, source plan ID/digest, application/rule-pack version, a SHA-256 drive-identity
fingerprint (never the raw identity), start/completion timestamps, final state, cancellation/elevation flags,
a per-outcome summary (removed/skipped/failed counts and logical-byte totals kept separate from each other),
best-effort drive free-space before/after (each independently nullable if unavailable, e.g. an unmounted or
inaccessible root), an outcome-category histogram, warnings, and limitations. `ExecutionReceiptCanonicalizer`
computes a SHA-256 digest over every field except the digest itself, the same pattern Phase 5 uses for plan
digests. The wording follows the spec's accounting language: "removed logical bytes" and "observed
free-space delta" are reported as separate numbers, never as a single "recovered" claim, and the receipt
carries no raw file paths.

Receipts in this slice are constructed in memory only; SQLite persistence, retention, and the
`clyr execution *` CLI surface are not implemented (see below).

## What Phase 6 still requires

Deliberately deferred, in priority order for a follow-up turn:

1. **`Clyr.ElevatedHelper`** — the separate one-shot helper executable, real UAC elevation, and the typed,
   versioned, replay-resistant IPC protocol. Not needed for the one enabled action (it does not require
   elevation) but required for Phase 6 to be complete per the specification, and for any future built-in
   action that does need it.
2. **Fixture-only UAC smoke test** — cannot be completed without the helper above and an interactive Windows
   session; per the spec's own fallback, Phase 6 must be stated as incomplete until this runs for real.
3. **CLI**: `clyr plan execute`, `clyr execution status/receipt/list/export/discard-receipt`.
4. **WinUI**: Review Plan execution controls, the dedicated final-confirmation dialog, progress/cancellation
   UI, and the finished-state view (Completed/PartiallyCompleted/Cancelled/Failed/Interrupted/UnknownOutcome).
5. **SQLite receipt persistence** with migrations, retention, and crash-safe `Interrupted`/`UnknownOutcome`
   finalization for abandoned `Running` records — this slice's receipts are process-local only.
6. **Interruption handling** beyond in-process cancellation: application crash, helper crash, sign-out,
   drive removal mid-execution, and the corresponding recovery rules.
7. Full documentation sweep (ARCHITECTURE, THREAT_MODEL, PRIVILEGE_MODEL, SECURE_IPC, ERROR_HANDLING,
   RECOVERY, RULE_ENGINE, STORAGE_ACCOUNTING, UI_UX, UX_STATE_MACHINE, TESTING_STRATEGY, OPERATIONS,
   EXPORT_FORMAT, ACCESSIBILITY, DECISION_LOG, RISK_REGISTER, CLI docs, ADRs) and `CHANGELOG.md`/`README.md`.
8. The full security test matrix from the specification (IPC fuzzing, helper unauthorized-client tests,
   protocol downgrade/replay, junction/symlink swap races, short-name ambiguity, volume-identity swap,
   forged completion response, etc.) — only the subset reachable without the helper/IPC is implemented today
   (see `tests/Clyr.Core.Tests/ExecutionTests.cs` and the two new checks in
   `tests/Clyr.Safety.Tests/RepositorySafetyTests.cs`).

**Phase 6 is not complete.** What exists is a real, tested, narrow non-elevated execution engine for the one
enabled low-risk action; the elevated helper, IPC, CLI, WinUI, persistence, and the fixture-only UAC smoke
test are not yet implemented.

## Safety invariants verified by test

- `tests/Clyr.Safety.Tests/RepositorySafetyTests.cs` — `File.Delete`/`File.Move`/`Directory.Delete` may
  appear only under `src/Clyr.Core/Execution/`; `Process.Start`, `ProcessStartInfo`, PowerShell/cmd
  invocation, and `requireAdministrator` may not appear anywhere in `src`; the execution boundary itself
  contains no shell, package-manager, Docker, or WSL command text.
- `tests/Clyr.Core.Tests/ExecutionTests.cs` — scanner age/root filtering, happy-path removal with a
  self-verifying receipt digest, token single-use, token expiry, tampered-plan-digest rejection, wrong
  session/user rejection, target-outside-root rejection, plan-time reparse-claim rejection, changed-since-plan
  rejection, missing-target handling, pre-start cancellation, and risk/action-type gating for non-built-in
  items — all against synthetic temporary fixture directories, never real user or Windows paths.
