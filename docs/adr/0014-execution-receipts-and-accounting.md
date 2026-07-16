# ADR 0014: Execution receipts, persistence, and truthful accounting

- Status: Implemented in Phase 6
- Date: 2026-07-17
- Scope: `Clyr.Contracts.ExecutionReceipt`, `Clyr.Persistence.SqliteExecutionReceiptStore`

## Context

An execution that deletes files must leave a durable, honest record of exactly what happened — not a guess,
and not a single "recovered N bytes" number that conflates logical size, actual disk allocation, and free-space
change three different quantities that can legitimately diverge.

## Decision

- `ExecutionReceipt` is an immutable record with a schema version, execution ID, source plan ID/digest,
  application/rule-pack version, a SHA-256 drive-identity fingerprint (never the raw identity), start/completion
  timestamps, a final `ExecutionState`, cancellation/elevation flags, an `ExecutionSummary` that keeps removed,
  skipped, and failed *counts and logical-byte totals* in separate fields (never merged into one number),
  best-effort drive free-space before/after (each independently nullable — an unmounted or inaccessible root
  yields `null`, not a fabricated value), an outcome-category histogram, warnings, and limitations. No raw file
  paths are ever included.
- `ExecutionReceiptCanonicalizer` computes a SHA-256 digest over every field except the digest itself, the same
  pattern `CleanupPlanCanonicalizer` uses for plan digests — a receipt cannot be silently edited after the fact
  without detection.
- Persistence is CLYR-owned SQLite (schema v3, additive over the existing snapshot schema). `SaveAsync` upserts
  by execution ID but **refuses to overwrite a row already in a terminal state**
  (`Completed`/`PartiallyCompleted`/`Cancelled`/`Failed`/`Interrupted`/`UnknownOutcome`/`Rejected`), throwing
  `ExecutionReceiptStoreException("receipt.immutable", …)` — a completed receipt is as immutable as a plan's
  digest suggests it should be. Retention is bounded to the 200 most recent rows.
- `ReconcileInterruptedAsync(staleAfter, nowUtc)` marks any row still missing `CompletedAtUtc` and older than a
  caller-supplied threshold as `Interrupted`. It can only ever produce `Interrupted`, never guess `Completed` —
  an execution the system cannot account for is reported as uncertain, not as successful.
- User-facing wording (CLI and WinUI) says "removed logical bytes" and "observed free-space change" as two
  separate numbers, and states that other processes may change free space concurrently — never "recovered N GB."

## Consequences

- A receipt is trustworthy evidence of what happened, independent of whether the process that produced it is
  still running — the digest lets anyone detect tampering, and the terminal-state immutability means a bug
  elsewhere in the app cannot silently rewrite history.
- The `Interrupted`/`UnknownOutcome` reconciliation mechanism exists and is unit-tested against a synthetic
  in-flight row, but **nothing in this pass writes a "started" placeholder row before deletion begins** — every
  receipt today is written once, at completion, by whichever caller (CLI or WinUI) invoked the executor. This
  means a real process crash mid-execution today leaves no row at all in history, rather than a genuinely
  reconcilable `Interrupted` one. Closing this gap (persisting a `Running` row at the start of `Execute`, then
  updating it at completion) is the clearest remaining item toward full crash-safety and is called out
  explicitly in `docs/PHASE6_EXECUTION.md`.
- Because free-space-before/after are independently nullable and the delta is computed only when both are
  present, a receipt never fabricates a free-space claim it cannot support.
