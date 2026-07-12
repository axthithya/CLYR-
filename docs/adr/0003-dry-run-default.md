# ADR 0003: Dry Run Before Every Cleanup

- **Status:** Accepted; execution remains out of scope until Phase 6
- **Date:** 2026-07-10
- **Decision owners:** Product safety, UX, and core architecture

## Context

Storage findings are uncertain and contextual. A large or old file is not necessarily disposable, scan coverage may be incomplete, supported tools can have broad effects, and the filesystem can change between observation and action. A generic confirmation such as “clean 12 GB” does not identify the actual targets, consequences, reversibility, privilege, or uncertainty that the user is authorizing.

CLYR's product promise requires explanation and preview before action. Early releases are read-only, and Phase 5 deliberately builds planning and dry-run behavior without executing real cleanup.

## Decision

Every CLYR-controlled storage mutation must be preceded by a genuine non-mutating dry run and an explicit confirmation of the resulting immutable cleanup plan. There is no “skip preview,” “force stale plan,” hidden batch, or CLI bypass.

The plan is versioned, one-time, bound to the initiating user/session, and expires ten minutes after creation. It includes all confirmation-relevant fields: plan/item IDs, exact `ActionType`, `RiskLevel`, rule/adapter/capability versions, resolved target descriptors and stable identity evidence, approved roots, exact/estimated/unknown byte status, consequence, rollback classification, elevation requirement, dependencies/order, typed adapter arguments, creation/expiry, and deterministic digest.

Any material change—including a different target/identity, metadata, risk, action, arguments, order, rule/adapter version, protected-resource decision, user/session, or expiry—invalidates confirmation. CLYR generates and displays a new plan and digest rather than editing a confirmed plan.

Dry run does not mutate, move, rename, recycle, quarantine, hydrate, decrypt, take ownership of, or change permissions on content; run a mutating tool “only to measure”; request elevation to resolve uncertain safety; or claim estimated bytes as recovered. When exact targets or a genuine non-mutating adapter preview cannot be bounded, the action remains `report-only` or `manual-instructions`.

## Canonical action and risk policy

The exact action types are `report-only`, `open-settings`, `recycle-files`, `quarantine-files`, `trusted-tool-command`, `windows-supported-cleanup`, `move-known-folder`, and `manual-instructions`. No generic or permanent-delete action exists.

Risk is exactly `Informational`, `Low`, `Medium`, `High`, or `Prohibited` and is independent of action type and confidence:

- `Informational` performs no CLYR storage mutation.
- `Low` requires explicit selection after preview; beta selections start empty.
- `Medium` requires separate consequence acknowledgement and is not silently batched with `Low`.
- `High` is unavailable until a capability-specific ADR, threat/UX review, fixtures, and release approval succeed.
- `Prohibited` is rejected before confirmation with no override.

UAC consent, if needed later, occurs after CLYR confirmation and does not replace it.

## Preview and confirmation requirements

The preview shows, in understandable form:

- what CLYR detected and the evidence/coverage limitations;
- every exact target where feasible, with large lists summarized but available for local review;
- exact, estimated, lower-bound, or unknown byte semantics next to the number;
- why the data exists, what will happen, likely user/tool impact, and whether it can be regenerated;
- `RiskLevel`, elevation requirement, rollback classification, and rollback limitations;
- tool/Windows mechanism and exact typed effect for adapter actions, never a shell command;
- plan expiry and why a changed/stale target requires another preview.

Confirmation controls start empty in beta. User content, unknown content, profiles, credentials, virtual disks, cloud files, protected resources, and `High`/`Prohibited` actions are never preselected. Confirmation applies to one digest and is consumed by one execution attempt.

## Execution and verification consequence

The future executor revalidates the complete plan and each volatile target immediately before action. One invalid privileged item rejects the entire batch before mutation. After mutation starts, errors stop at the next safe boundary and are recorded honestly.

An action journal is durable before mutation. The receipt distinguishes attempted, skipped, changed, failed, partial, completed, rollback-available, and rolled-back outcomes. Actual recovery is measured from before/after storage evidence and qualified for concurrent activity; the original estimate is preserved rather than overwritten.

## Consequences

- Users can inspect scope, consequences, uncertainty, privilege, and rollback before anything changes.
- Plans become testable security artifacts rather than UI-only summaries.
- Stale/changed targets fail closed and require user attention, reducing unattended throughput.
- Large target sets need careful summarization without hiding exact scope.
- Tool integrations lacking a reliable preview remain informational, even if their cleanup command is popular.
- Two-step preview/confirmation adds friction, but that friction is intentional at the point of possible data loss.
- Estimates can still differ from measured recovery because of hard links, compression, sparse allocation, managed virtual disks, and concurrent disk activity.

## Alternatives considered

- **Immediate action after selecting a finding:** rejected; a finding is not an exact action contract and may be stale or incomplete.
- **One broad “Clean now” confirmation:** rejected; it hides mixed risk, targets, rollback, and privilege and encourages accidental consent.
- **Dry run only for elevated or `High` actions:** rejected; standard-user actions can still destroy valuable data and external tools can have broad effects.
- **Allow `--force` or “continue despite changes”:** rejected; it invalidates target identity and confirmation binding.
- **Trust a tool's reported estimate without CLYR validation:** rejected; preview semantics, versions, and target scope vary. Unsupported or unparseable behavior stays report-only.
- **Long-lived/reusable plans:** rejected; storage state changes too quickly and replay risk increases.

## Validation

Phase 5 must prove entirely with fixtures/fakes that:

- dry run performs zero storage mutation, tool cleanup, elevation, hydration, decryption, ownership, or ACL change;
- identical canonical inputs produce the same digest across app/CLI/helper contract implementations;
- every confirmation-relevant field is digest-bound and any mutation invalidates consent;
- expiry at ten minutes, session/user change, rule/adapter version change, target replacement, reparse swap, overlap, and protected-policy change are rejected;
- exact/estimated/lower-bound/unknown values remain correctly labeled through plan, UI/CLI, persistence, and export;
- no arbitrary command, executable, path wildcard, or permanent-delete action is representable;
- risk-specific selection/acknowledgement rules and empty beta defaults are accessible and testable;
- stale, malformed, duplicate-target, overlapping, or malicious plans fail closed without a real-file executor.

Phase 6 must additionally prove journal durability, immediate revalidation, partial/crash recovery, receipts, rollback semantics, and measured recovery on disposable fixtures before any narrow execution capability ships.

## Revisit triggers

Revisit the ten-minute lifetime only with measured evidence and a security/UX review. Any proposal for unattended cleanup, scheduled execution, reusable confirmation, permanent deletion, implicit selection, or execution without a true preview requires a superseding ADR and cannot be introduced as a convenience flag.
