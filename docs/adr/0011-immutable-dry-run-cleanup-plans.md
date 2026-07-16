# ADR-0011: Immutable integrity-checked cleanup plans

- Status: Accepted
- Date: 2026-07-16
- Scope: Phase 5 cleanup planning only

## Context

Cleanup recommendations are unsafe when selection, target identity, rules, or protected policy can change between observation and action. Phase 5 must answer what a future action could affect without changing any user or system file.

## Decision

Use immutable typed plans with stable canonical UTF-8 JSON and a SHA-256 integrity digest. Bind every plan to scan/snapshot, drive identity, rule-pack ID/version/digest, category registry, application compatibility, privacy mode, selection identity, root identities, creation, and a maximum ten-minute expiry. Recreate the plan for every selection change.

Protected policy is deny-wins. Path validation is component-aware and rejects unsupported Windows namespaces, traversal, alternate streams, ambiguous aliases, environment escapes, and reparses. Validation detects changed target identity/metadata and never assumes the filesystem remained unchanged.

Plans are bounded and memory-only by default. Explicit exports are privacy-safe reports and omit raw paths. The digest is an integrity check, not a signature. Production execution returns ExecutionNotAvailableInPhase5.

## Consequences

- Display is deterministic and cannot silently expand targets.
- Edited, stale, expired, incompatible, or protected plans fail closed.
- CLI show/validate/discard work only for plans held by the current process unless a report is exported.
- Browser profile aggregates remain insufficient evidence until narrower exact-root detection exists.
- Phase 6 must define its own immediate revalidation, journal, helper, receipt, and execution authorization; this ADR grants none.

