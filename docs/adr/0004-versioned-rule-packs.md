# ADR 0004: Versioned Declarative Rule Packs

- **Status:** Accepted
- **Date:** 2026-07-10
- **Decision owners:** Rules and security

## Context

Storage knowledge changes and benefits from community contributions, but an extensibility mechanism that can execute code or nominate arbitrary paths would cross CLYR's central safety boundary.

## Decision

Represent detection knowledge as YAML documents with an explicit integer `schemaVersion`, validated against a bundled JSON Schema Draft 2020-12 document. Schema v1 rejects unknown fields, free-form executable/command data, unsupported action types, and reparse traversal. Rule packs carry a manifest with pack ID/version, compatible app/schema ranges, ordered file hashes, and provenance. Community rules remain `report-only`; later action references can name only a compiled first-party adapter ID approved in code.

Local validation, protected-resource policy, root-token expansion, canonical containment, and deterministic overlap resolution apply even to signed/built-in packs. Pack signatures may support distribution trust later but never bypass content validation.

## Consequences

- Rules can be reviewed, diffed, tested, and rolled back without becoming executable plugins.
- Strict v1 unknown-field rejection catches typos but additive fields require a schema-version change or explicitly compatible extension point.
- YAML parsers and schema validators require depth, alias, size, regex, and time limits.
- Built-in rules need fixtures, evidence, pack integrity, and false-positive ownership.
- Permanent deletion is not represented.

## Alternatives

- **Code plugins or scripts:** rejected as arbitrary code execution.
- **Unsigned unversioned path lists:** rejected because provenance, compatibility, semantics, and rollback are undefined.
- **Signature-only trust:** rejected because a valid publisher can still make an unsafe rule.
- **Online rules required at runtime:** rejected because offline determinism and safe rollback are product requirements.

## Validation

Phase 3 must test valid, malformed, oversized, aliased, traversal, protected-root, confusable, unknown-field, command-injection, overlap, version, hash, and rollback cases. Every built-in rule has positive/negative fixtures and remains report-only until a separately approved phase.
