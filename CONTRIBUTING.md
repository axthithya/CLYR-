# Contributing to CLYR

CLYR can eventually affect user files, so contributions are reviewed for safety before convenience. During Phase 0 this repository contains planning artifacts only; do not add product implementation until Phase 1 is explicitly approved.

## Before proposing a change

1. Read `README.md`, `docs/SAFETY_MODEL.md`, `docs/ARCHITECTURE.md`, `docs/THREAT_MODEL.md`, and the active phase in `ROADMAP.md`.
2. Keep the change inside the approved phase. A useful idea for a later phase belongs in an issue or roadmap note, not an unreviewed implementation.
3. Inspect the working tree and preserve unrelated changes. Never use destructive history or cleanup commands to simplify a contribution.
4. For behavior tied to Windows or an external tool, cite official documentation and record version, failure, fallback, and access date in `docs/RESEARCH_NOTES.md`.

## Safety rules

- Never test cleanup on a real system or user directory. Use generated temporary fixtures and fake adapters.
- Protected resources always override rule or user selection.
- Rules are declarative, versioned, schema-validated, and detection-only until an action phase explicitly authorizes more.
- YAML must not contain shell, PowerShell, CMD, executable paths, or free-form argument strings.
- A new executable integration requires a compiled first-party adapter, threat review, exact typed arguments, timeouts/output limits, and fake-runner tests.
- Reparse points are not followed by default. String-prefix containment is insufficient.
- Do not include real usernames, personal paths, reports, logs, databases, dumps, secrets, certificates, signing keys, or user-derived fixtures.
- Never weaken a safety check merely to make a fixture or build pass.

## Rule proposals

A detection rule proposal must include:

- unique stable ID, schema version, category, risk, confidence, and `report-only` action;
- narrowly scoped documented roots and `followReparsePoints: false`;
- plain-language origin, evidence, impact, and regenerability explanation;
- official source evidence or an explicit report-only uncertainty;
- valid fixture, negative fixture, protected-path and traversal cases;
- overlap priority/ownership rationale and privacy review.

Run the Phase 0 verifier for schema examples. Phase 1 will add `clyr rules validate` and test commands.

## Pull requests

Complete every relevant section of the PR template, especially safety impact, protected-path impact, tests, schema/migration changes, and screenshots or a reason they do not apply. Security-sensitive paths listed in `CODEOWNERS` require designated review. Release, elevated-helper, action, protection, schema, and update changes should be small and independently reviewable.

## Commit style

Use an imperative Conventional Commit subject:

```text
docs: define storage accounting overlap policy
test(safety): reject device-path traversal
feat(cli): add read-only drive listing
```

Use `!` and a `BREAKING CHANGE:` footer for a deliberate externally visible schema/contract break. Reference the phase and issue when available. Do not commit generated packages or sign/publish from a contribution branch.

## Verification

For Phase 0:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-phase0.ps1
```

Later phases will add one authoritative verification script covering format, Release build, unit/safety/integration/architecture tests, schema validation, dependency vulnerability/license review, no-secret checks, and packaging gates when applicable. Report exact failures; never claim an unrun check passed.

## Reporting security findings

Do not disclose vulnerabilities through a normal issue. Follow `SECURITY.md`.
