# Phase 0 Checklist

Status values are **Done**, **Deferred by phase**, or **Blocked with evidence**. “Done” means the artifact exists and passed the Phase 0 documentation checks; it does not imply runtime implementation.

## Preflight and governance

- [x] Record current directory, repository root, branch, status, remotes, and existing files.
- [x] Classify the repository and record preservation/conflict needs.
- [x] Establish naming, line-ending, ignore, contribution, privacy, security, and licensing policies.
- [x] Record assumptions and decisions rather than silently filling material gaps.
- [x] Define a local Phase 0 verification command.

Preflight result on 2026-07-10: the local workspace root was the Git root; branch `master`; no commits; no remotes; no tracked or working-tree project files; no solution or documentation existed. The repository was an empty CLYR bootstrap, not another project. There was no prior work to migrate or overwrite.

## Product and UX

- [x] Executive brief, problem, value proposition, personas, stories, misuse cases, scope, and non-goals.
- [x] C:-first journey without assuming C: is always the system volume.
- [x] Screen inventory, finding presentation, accessibility checklist, and trust language.
- [x] Scan, finding, and UX state models with exact/estimated/partial distinctions.
- [x] Product-local success metrics and provisional performance budgets with rationale.

## Architecture and data

- [x] Context, container/component, process, privilege, and data-flow documentation.
- [x] Dependency rules and rejected alternatives.
- [x] Domain model and SQLite conceptual model without implementation.
- [x] Quick and Deep Analysis contracts and Windows correctness requirements.
- [x] Storage-accounting definitions, uncertainty, and overlap policy.
- [x] Versioned rule and export JSON Schemas with valid and invalid examples.
- [x] Capability and support matrix drafts.

## Safety, security, and operations

- [x] Protected-resource taxonomy, risk levels, threat model, and residual risks.
- [x] Dry-run, confirmation, quarantine, rollback, receipt, and future execution state contracts.
- [x] Least-privilege and secure-IPC proposal for a future short-lived helper.
- [x] Privacy classification, retention, redaction, export, and user-deletion policies.
- [x] Error taxonomy, recovery behavior, release/signing/update strategy, and operational runbooks.
- [x] Risk register with owners, status, triggers, mitigations, and residual exposure.

## Engineering readiness

- [x] Test pyramid, fixture catalog, property/fuzz plans, benchmarks, quality gates, and manual checks.
- [x] Third-party selection and license criteria.
- [x] ADRs for native Windows, helper isolation, dry-run, versioned rules, and offline operation.
- [x] Proposed milestones, labels, issue forms, PR template, CODEOWNERS, and dependency-update policy.
- [x] Phase-by-phase acceptance checklist and exact Phase 1 handoff.

## Verification

- [x] Parse every JSON file.
- [x] Validate rule and export examples against their schemas.
- [x] Validate required files, headings, internal Markdown links, and Mermaid source structure.
- [x] Check naming, namespace, action types, risk/confidence terms, phase claims, and supported-platform claims.
- [x] Scan for obvious secrets, machine-specific paths, generated junk, destructive code, and forbidden command endpoints.
- [x] Inspect the final Git diff/status and record limitations.

Applicable Phase 0 gates are documentation and schema gates. Build, compiler, unit, safety, integration, architecture-binary, vulnerability, package, and license-inventory execution gates are **deferred by phase** because Phase 0 intentionally contains no .NET solution or dependencies. The local machine also has no .NET SDK installed; Phase 1 must install and verify the latest patched .NET 10 SDK before scaffolding.

## Closeout evidence — 2026-07-12

- `scripts/verify-phase0.ps1`: passed 478 checks, including 64 required-file contracts, 13 Mermaid source structures, three JSON documents, UTF-8/final-newline checks, strict Phase 0 rule-action guardrails, malicious fixtures, canonical terminology, no product-code boundary, forbidden executable-pattern scan, and Git whitespace check.
- Draft 2020-12 conformance with pinned Ajv 8.17.1, `ajv-formats` 3.0.1, and YAML 2.8.1: 11 YAML files parsed; valid rule and export accepted; traversal and command fixtures rejected for the intended schema paths.
- Mermaid CLI 11.12.0 rendered all 13 authoritative `.mmd` files to temporary, uncommitted SVG output using an installed browser.
- Relative-link audit passed all 71 Markdown documents; duplicate-heading and table-structure audit passed all 71.
- Final search found no committed user path, credential-shaped value, generated package/database/dump, `.NET` product file, destructive implementation, or obsolete product-name occurrence outside the verifier's split negative-test literal.
- Working tree remains uncommitted on `master`; no remote was added or modified.
