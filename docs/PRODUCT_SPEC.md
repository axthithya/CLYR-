# Product Specification

## Product contract

CLYR is a native Windows desktop application and reusable command-line engine that explains storage pressure before it offers any response. The primary journey is a full C: drive; the technical engine discovers and labels the system volume and can later analyze another explicitly selected eligible local volume.

The first useful release is read-only. It must not imply that detected content is junk, that all bytes are locally allocated, or that an inaccessible region is empty. Cleanup planning and execution are later, separately gated capabilities.

## Problem and value proposition

Users can observe low free space but cannot safely interpret overlapping categories, system-managed content, application data, virtual disks, cloud placeholders, hard links, sparse files, or access-denied paths. A list of large paths transfers risk to the user. CLYR combines storage accounting, evidence-based classification, protection policy, explanations, coverage, and privacy-safe export in one deterministic workflow.

CLYR differs by making uncertainty first-class and by separating detection from action. It prefers supported Windows or tool mechanisms, fails closed on ambiguous targets, and measures actual recovery after future actions.

## Personas and needs

| Persona | Goal | Constraints | CLYR response |
|---|---|---|---|
| Maya, everyday user | Recover enough space for an update without breaking Windows or losing photos. | Low filesystem knowledge; urgency; accessibility needs. | Calm C:-first summary, protected labels, plain consequences, no preselected personal content. |
| Arun, developer | Understand Docker/WSL/package/build growth. | Virtual disks and caches have tool-specific semantics. | Developer Mode reports host files separately from guest/tool reclaimability; supported adapters only. |
| Lee, gamer/creator | Find recordings, installers, launcher storage, and movable libraries. | Saves and media are valuable; launchers own migrations. | Review/move classifications, launcher guidance, never automatic save/media removal. |
| Sam, support engineer | Diagnose a user's machine remotely. | Report must not leak usernames, projects, tokens, or personal paths. | Versioned summary export with redaction and coverage. |
| Alex, contributor | Add detection safely. | Community knowledge varies; executable rules are dangerous. | Schema, fixtures, protected-path tests, report-only default, maintainer review. |

## User stories

- As a user with a full drive, I can see the largest causes and the reliability of each total before considering an action.
- As a user, I can cancel a scan and still receive an explicitly partial result.
- As a user, I can see why a location was skipped or inaccessible.
- As a user with cloud files, I can analyze metadata without CLYR hydrating content.
- As a developer, I can distinguish a WSL virtual-disk file from space that a guest cleanup might or might not return to Windows.
- As a support engineer, I can export a redacted summary with no raw username or full personal path.
- As a contributor, I can validate a detection-only rule and malicious examples locally.
- As a future cleanup user, I can inspect an immutable plan, consequences, rollback status, and elevation need before explicit confirmation.

## Misuse cases and required response

| Misuse / failure attempt | Required response |
|---|---|
| Treat an old or large file as automatically safe | Classify based on rule evidence; otherwise Review required or Unknown. |
| Add `C:\Windows` as a broad rule root | Schema/policy rejection; Protected takes precedence over every rule. |
| Insert PowerShell, CMD, or executable text into YAML | Schema rejection; no generic command field exists. |
| Traverse `..`, a junction, symlink, mount point, UNC, or device path | Canonicalize with Windows-aware rules, inspect reparse/file identity, and fail closed. |
| Share a summary report containing a username or token-like value | Redaction and no-secret validation fail; export is not produced as share-safe. |
| Replace a target after plan confirmation | Immediate identity revalidation rejects the item or batch according to atomicity policy. |
| Replay or forge an elevated-helper request | Caller/session/identity, nonce, expiry, capability, and plan validation reject it. |
| Scan a network/removable/unsupported filesystem by default | Exclude it; show unsupported capability and require explicit eligible selection for read-only use. |
| Claim exact recovery from a logical-size estimate | UI/schema prevent exact label and receipt compares free space before/after. |

## Scope by release horizon

### MVP read-only scope

- Drive discovery with capacity, filesystem, eligibility, and status.
- Cancellable Quick and opt-in Deep Analysis with progressive/partial results.
- Top-N folder/file, extension, category, profile, known-cache, developer, Windows-managed, protected, and unknown views.
- Explanations, evidence, confidence, risk, last-modified/last-used caveats, and action availability.
- Privacy-safe export, aggregate snapshots, and “What grew?” comparison.

### Later gated scope

- Immutable dry-run plans; Recycle Bin/quarantine and allowlisted supported tool actions.
- Post-action measurement, audit receipts, partial recovery, and limited rollback.
- Developer adapters one tool/version at a time.
- Supported known-folder and application migration workflows.

### Explicit non-goals

Registry cleaning; RAM optimization; drivers; malware/antivirus; process killing; automatic background/startup monitoring; services; scheduled tasks; kernel/filter drivers; defragmentation; partitions; filesystem repair; secure erase; undelete; cloud accounts; remote administration; cross-platform support; arbitrary executable plugins; automatic unknown/duplicate removal; automatic WSL/Docker/VM/database compaction; and guaranteed recovery of every estimated byte.

## C:-first journey

1. Welcome names the diagnostic goal and privacy posture.
2. Home discovers the system volume. If it is C:, **Analyze C:** is primary; otherwise the actual system volume is clearly named and C: is not misrepresented.
3. Scan configuration defaults to Quick Analysis, no content hashing, no reparse traversal, no cloud hydration, and conservative I/O.
4. Progress shows current state, coverage, skipped/inaccessible counts, and cancellation; results become visible progressively.
5. Results show Currently used, Safely reviewable, and Potentially movable as separate numbers, each with exact/estimated state.
6. “Why is this drive full?” ranks evidence-backed causes without danger colors based on size alone.
7. Finding details explain origin, evidence, confidence, risk, regenerability, consequence, privilege, rollback, and why no action exists.
8. Export defaults to a share-safe summary. Detailed local export is warned and never uploaded.

## Finding vocabulary

Disposition: **Safe candidate**, **Review required**, **Move candidate**, **Protected**, or **Unknown**. Risk: **Informational**, **Low**, **Medium**, **High**, or **Prohibited**. Confidence: **Confirmed**, **High**, **Medium**, **Low**, or **Unknown**. Action types: `report-only`, `open-settings`, `recycle-files`, `quarantine-files`, `trusted-tool-command`, `windows-supported-cleanup`, `move-known-folder`, and `manual-instructions`. All Phase 0 example rules use `report-only`.

## Acceptance criteria

The documentation is internally consistent; every unsupported or future capability is labeled; schemas encode the safety boundary; protected resources always win; diagrams and state models agree; no implementation or destructive behavior exists; and Phase 1 can scaffold the solution without revisiting product direction.
## Phase 5 implemented scope

Users may review eligibility, select nothing-by-default candidates, preview an immutable integrity-checked dry-run, inspect risk/confidence/consequences/rollback/expiry, export a privacy-safe report, or discard the in-memory plan. They cannot execute, apply, clean, delete, recycle, quarantine, elevate, or invoke an external tool.
