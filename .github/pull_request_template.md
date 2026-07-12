## Outcome and phase

Describe the user or engineering outcome, active phase, issue, and why this does not pull later work forward.

## Safety impact

- RiskLevel impact: Informational / Low / Medium / High / Prohibited / none
- Mutation, privilege, process/tool, network/update, rollback, and failure behavior:
- Why the safest fallback remains available:

## Protected-path and identity impact

- Protected resources, normalization, containment, reparse, or TOCTOU changes:
- New malicious and negative fixtures:
- Confirm no real system or user directory was scanned or cleaned in tests:

## Privacy and data

- New or changed collected, logged, persisted, exported, or retained fields:
- Redaction, classification, retention/deletion, and no-secret evidence:

## Tests and evidence

- [ ] Formatting and static checks
- [ ] Release build and applicable unit, safety, integration, contract, and architecture tests
- [ ] Rule/export schemas and examples
- [ ] Dependency vulnerability, license, and no-secret checks
- [ ] Manual verification, with environment and result below

Exact commands, results, measured values, and unverified claims:

## UI and accessibility

Provide screenshots using labeled demo/synthetic data, or explain why not applicable. Include keyboard, screen reader, high contrast, scaling, reduced motion, and focus behavior where relevant.

## Schema, migration, and compatibility

Describe rule, export, IPC, CLI, or database version changes; unknown-field behavior; upgrade/rollback; and migration notes. Write “none” with rationale when not applicable.

## Documentation and operations

- [ ] Changelog, status, roadmap, decision/risk, and relevant design/runbook docs updated
- [ ] No secrets, real paths/data, generated packages, certificates, databases, or dumps added
- [ ] Diff reviewed for unrelated changes and unsafe scope expansion
