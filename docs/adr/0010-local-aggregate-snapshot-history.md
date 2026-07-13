# ADR-0010: Local aggregate snapshot history

Status: Accepted for Phase 4 — 2026-07-13

## Decision

CLYR stores only bounded aggregate scan snapshots in a CLYR-owned SQLite database. A normalized, versioned schema is written transactionally; foreign keys cascade child aggregate rows. Complete, partial, and cancelled results may be visible. Pending, writing, failed, incompatible, and corrupted states are never presented as successful history.

Drive continuity uses the Windows volume GUID only as transient input to HMAC-SHA-256 with a random 256-bit key in CLYR local application storage. The raw identifier is never persisted, exported, logged, or displayed. Key loss/rotation or unavailable identity makes older/newer snapshots incomparable. Reformatting changes the volume identity; cloned identities remain an acknowledged platform limitation and capacity/filesystem/coverage warnings reduce confidence.

No file inventory, raw scanned path, user or machine identity, serial number, SID, file content, content hash, or optional path identity is stored. The drive-letter root is retained as a display label, not an identity.

Retention defaults to the newest 20 snapshots per drive and cannot be configured below two, preserving a prior baseline. Deletion removes database rows only. Database corruption is reported and the file is not automatically deleted or replaced.

## Comparison policy

Compatibility is `FullyComparable`, `ComparableWithWarnings`, `ClassificationAdjusted`, or `NotComparable`. Identity/schema/state mismatches block comparison; rule-pack drift, scan-mode/filesystem differences, and coverage drift are explicit. Signed deltas are saturating and deterministic. A change is significant at 250 MiB absolute, or at least 50 MiB and 10 percent relative. Statements describe observed differences and never claim cause.

## USN decision

Phase 4 includes only an injectable unsupported USN boundary. Correct journal reset/wrap, privilege, filesystem, checkpoint, rename, deletion, and fallback behavior is not sufficiently evidenced for production, so full scans remain the only implementation.

## Consequences

History is offline, local, bounded, explainable, and safe to disable or clear. Key recovery is intentionally impossible; losing it sacrifices comparability rather than privacy. Phase 4 introduces no cleanup, planning, elevation, helper, process execution, or scanned-file mutation.
