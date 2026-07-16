# ADR 0012: Active-session execution authority and per-target TOCTOU revalidation

- Status: Implemented in Phase 6
- Date: 2026-07-17
- Scope: `Clyr.Core.Execution` (non-elevated executor, helper handler share the same target processor)

## Context

Phase 5 established immutable, digest-bound dry-run plans but no execution. Phase 6 must let a narrow,
allowlisted action actually run without ever trusting a plan's original observation at the moment of mutation
— a plan can be minutes old, the underlying files can have changed, and nothing about "the plan was valid when
created" may be assumed at execution time.

## Decision

Execution authority is bound to the *current process*, never to a file or a durable record:

- `ExecutionTokenService` issues a one-time `ExecutionToken` bound to the plan ID, plan digest, an
  `ExecutionSessionId` generated once per process launch, the current Windows user SID, the drive identity, and
  the requested action IDs, with a random 256-bit nonce and a 2-minute expiry. Tokens live only in an in-memory
  dictionary; a token cannot be replayed after `Consume` succeeds, and it cannot be replayed across a process
  restart because nothing persists.
- Immediately before executing, the executor independently recomputes the plan digest
  (`CleanupPlanCanonicalizer.Digest`) and compares it to `plan.Digest` — a second, independent check beyond
  token validation, so an in-memory plan object mutated after the token was issued is still caught.
- `ExecutionEligibilityValidator` re-derives, per item, that it is `DryRunEligible`, `Low` risk,
  `TrustedBuiltInCleanup`, declares `Phase6BuiltInExecutable`, requires no elevation (today), and matches an
  entry in the closed `BuiltInExecutionActions` registry by both action ID and declared root identity.
- `ExecutionTargetProcessor.Process` is the single place, shared by both the non-elevated executor and the
  elevated helper's request handler, that re-probes a target live on disk: canonical path containment via
  `WindowsPathSafetyValidator` against the *actual* trusted root path, existence, reparse-point and
  cloud-placeholder attributes read fresh, size/last-write-time compared against what the plan recorded, and
  the age threshold re-checked against the current clock — not the plan's creation time. Only if every check
  passes does it call `File.Delete`, and only that one call, with no attribute clearing, ownership change, or
  ACL change beforehand.

Both the app process and the elevated helper process call the *same* `ExecutionTargetProcessor` code, but each
does so against its own live filesystem view in its own process — "independent revalidation" means independent
execution, not merely independent code paths that happen to share logic.

## Consequences

- A plan cannot be replayed twice, even accidentally: the second `Execute` call with the same token is
  rejected (`token.consumed`) before any target is touched.
- A target that changed between planning and execution is skipped (`SkippedChanged`), never forced.
- Cancellation is checked between items and between targets; whatever succeeded before cancellation is
  observed is preserved in the receipt.
- There is currently no durable "in-flight" record written before deletion begins (see ADR-0014's Consequences)
  — a real process crash mid-run is not distinguishable from a clean stop in persisted history today, though the
  `Interrupted`/`UnknownOutcome` states and `ReconcileInterruptedAsync` exist for when that gap closes.
- CLI `plan execute` and the WinUI Review Plan execution flow both go through this exact same code path — there
  is no second, less-validated way to execute.
