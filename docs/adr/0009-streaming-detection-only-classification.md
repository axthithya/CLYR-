# ADR 0009: Streaming detection-only classification

Status: Accepted for Phase 3.

## Decision

Classification consumes the Phase 2 metadata stream through one bounded session. The scanner performs no per-rule filesystem traversal and retains only aggregate counters. Built-in rules are inert declarative data, indexed by exact segment, filename, or extension. No regular expression, file-content read, shell, process, cleanup action, or elevation surface exists.

The first-party pack is active only after its manifest, compatibility range, closed category registry, and every file SHA-256 digest verify. Loading is transactional. External YAML can be schema-validated for contribution feedback but remains untrusted and inactive.

Every observed file has exactly one structural category owner. Protected matches precede priority, specificity, and ordinal rule identity. Other matches contribute secondary tags only. Unknown observed bytes, inaccessible entries, skipped entries, and drive-used bytes not represented by observations remain separate.

Findings use path-independent deterministic IDs and include pack/rule provenance, confidence, informational status, evidence, limitations, and report-only safety text. Classified report schema v2 preserves Phase 2 report v1 compatibility and excludes raw finding paths.

## Consequences

Classification cost is linear in observations with bounded rule indexes and aggregate state. Logical size remains estimated because allocated size and hard-link identity are unavailable. Exact known-folder identity and richer metadata predicates can be added only through reviewed schema/engine revisions. Phase 4 persistence and every cleanup capability remain out of scope.
