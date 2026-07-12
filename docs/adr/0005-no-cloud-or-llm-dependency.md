# ADR 0005: No Cloud or LLM Dependency

- **Status:** Accepted
- **Date:** 2026-07-10
- **Decision owners:** Product, privacy, and security

## Context

Filesystem metadata can expose identities, projects, applications, and sensitive topics. Core storage accounting and protection decisions must work during connectivity failures and be reproducible in tests. An online service adds availability, consent, retention, authentication, cost, and supply-chain risks without being required to answer the product's core question.

## Decision

Drive discovery, scanning, classification, protection, explanation templates, export redaction, snapshot comparison, and future action validation operate locally and deterministically. There is no telemetry or automatic upload by default. CLYR does not require an AI/LLM API. Update delivery, if later approved, is an independent signed and rollback-safe capability; it does not change local safety policy remotely.

## Consequences

- Core use remains available offline and tests can reproduce decisions from fixtures and versioned rules.
- Sensitive metadata is not sent to a vendor by default.
- Explanations are curated and versioned rather than generated dynamically.
- Optional crash reporting can be considered only in Phase 9 as explicit opt-in with preview, redaction, disclosure, and a fully local alternative.
- Rule/catalog updates cannot silently weaken installed protection policy.

## Alternatives

- **Cloud classification:** rejected due to privacy, availability, and non-determinism.
- **Remote health scores/recommendations:** rejected because they encourage opaque policy and urgency marketing.
- **Telemetry on by default:** rejected because local metrics and explicit support exports cover initial quality needs.

## Validation

Release tests must prove an offline install/runtime path, no undeclared network endpoints, no analytics SDK, and no behavior change when disconnected. Any future online capability requires a superseding or scoped ADR and privacy/security review.
