# CLYR Safety Model

Status: Phase 0 design baseline. As of Phase 6, one narrowly allowlisted, low-risk, non-elevated cleanup
capability is implemented exactly as this baseline requires: exact target, consequence, risk, and explicit
confirmation before any mutation. See `docs/PHASE6_EXECUTION.md` and ADR-0012 for what is now real; every other
finding remains dry-run/report-only. This pass closes the "Audit and verification" section's requirement below ("persist an action-journal entry ...
before mutation") for that one capability: a durable `Running`-state record is written, using the final
receipt's own `ExecutionId`, before any file is touched, and is only ever finalized or marked `Interrupted` by
startup reconciliation — never silently resumed or replayed. As of Phase 7, Developer Mode adds read-only
detection for 14 developer tool families plus one additional narrow, closed-argument, read-only process probe
(Docker/WSL status only) — no developer-tool finding is added to the execution allowlist; see
`docs/PHASE7_DEVELOPER_MODE.md` and ADR-0015–0017.
Applies to: the CLYR desktop app, CLI, core engine, rule packs, adapters, and elevated helper.

## Safety objective

CLYR must help a user understand storage pressure without turning uncertain evidence into destructive advice. The primary invariant is:

> No storage mutation occurs unless CLYR can identify the exact approved capability, target, consequence, risk, and confirmation that authorize it.

Read-only analysis is the only executable product scope before Phase 5. Phase 5 may generate plans and dry runs against fixtures, but still may not mutate real files. A deliberately small execution allowlist is not eligible until Phase 6 and its security gates pass.

## Terms and precedence

- A **finding** explains observed storage. It is not proof that content is disposable.
- An **action definition** is a versioned, typed capability. It is not an executable command string.
- A **cleanup plan** is an immutable, expiring set of resolved plan items produced from current evidence.
- A **dry run** validates and previews a cleanup plan without storage mutation, tool cleanup, elevation, hydration, decryption, or ACL changes.
- A **confirmation** is one user's consent to one displayed plan digest. It is invalid after any material plan change.
- An **execution receipt** records what was attempted and verified; it is not proof of recovery unless free-space verification supports the claim.
- A **protected resource** is content for which the protected-resource policy overrides rules, adapters, user-interface defaults, and size-based recommendations.

Safety precedence is: protected-resource policy, capability allowlist, path and identity validation, plan validity, risk policy, user confirmation, then execution policy. A lower-precedence decision cannot override a denial at a higher level.

## Threat assumptions

CLYR assumes that:

- file names, directory contents, reparse points, metadata, rule packs, reports, and external-tool output are untrusted input;
- a local standard-user process may race, replay, or tamper with files and local IPC;
- a user may misunderstand a technically accurate consequence or select the wrong item;
- the filesystem can change between scan, preview, confirmation, and execution;
- access can be denied and reported sizes can be incomplete or estimated;
- community rules can be malicious even when schema-valid;
- a supported tool can change behavior across versions or locales;
- dependencies, update metadata, or packaged binaries can be compromised;
- an attacker already controlling the Windows kernel or an administrator can bypass application-level controls. Defending a host already under that level of control is outside CLYR's security boundary.

The detailed STRIDE analysis and residual risks are in `THREAT_MODEL.md`.

## Canonical risk levels

`RiskLevel` has exactly these values, ordered by increasing restriction:

| RiskLevel | Meaning | Policy |
| --- | --- | --- |
| `Informational` | No CLYR storage mutation. The user is being informed or directed to a settings surface. | May be shown without cleanup confirmation. It must not be represented as recovered space. |
| `Low` | Narrow, well-understood scope with strong evidence and either reliable regeneration or a credible recovery path. | Explicit opt-in; beta selections start empty; dry run, confirmation, and verification are required. |
| `Medium` | Meaningful loss of local state, rebuild/redownload cost, limited rollback, or material external-tool behavior is possible. | Isolated consequence acknowledgement; never preselected or silently batched with `Low` items. |
| `High` | Severe application, user-data, or system impact is plausible, or recovery is uncertain. | Not executable without a capability-specific ADR, threat review, UX review, fixtures, and explicit release approval. Never preselected. |
| `Prohibited` | The action violates a hard safety boundary or cannot be bounded and validated. | Must be rejected before confirmation. No user override, rule override, CLI flag, or elevation override exists. |

Risk and confidence are independent. High-confidence detection can still produce a `High` or `Prohibited` action. Size, age, location, popularity of online cleanup advice, and administrator access never lower risk by themselves.

### Risk floors and hard stops

- An arbitrary command, shell text, wildcard deletion, unknown action type, or unknown rule ID is `Prohibited`.
- Permanent deletion is `Prohibited` in the approved architecture until a dedicated future ADR, threat review, UX review, and test gate explicitly change that boundary.
- Direct file mutation under a protected system resource, following an unapproved reparse point, or escaping an approved root is `Prohibited`.
- Unknown content in a broad application-data root is `Prohibited` for cleanup; it remains report-only.
- Lack of rollback or reliable regeneration sets a minimum of `Medium` and can make an action `High` or `Prohibited` depending on impact.
- Elevation is independent of risk and never reduces it. An elevated action must satisfy both the risk policy and the privilege policy.
- An action type does not imply a fixed risk. For example, `trusted-tool-command` can be `Low`, `Medium`, `High`, or `Prohibited` based on the compiled adapter, tool version, exact arguments, and target state.

## Canonical action types

`ActionType` has exactly these Phase 0 design values:

- `report-only`
- `open-settings`
- `recycle-files`
- `quarantine-files`
- `trusted-tool-command`
- `windows-supported-cleanup`
- `move-known-folder`
- `manual-instructions`

No generic delete, execute, script, or path operation is part of the action model. Community rules may use `report-only`, `open-settings`, or `manual-instructions`, and may reference an already approved adapter ID; they cannot create an executable adapter or expand its arguments.

## Protected-resource taxonomy

The following policy is about direct mutation based on detection. Some resources may later be handled only through a documented Windows or vendor-supported adapter after a separate capability review.

| Class | Representative resources | Allowed Phase 0 disposition | Future mutation constraint |
| --- | --- | --- | --- |
| Windows and boot critical | `%SystemRoot%`, `System32`, `WinSxS`, `Installer`, boot files, EFI partitions, registry hives | Report with uncertainty or suppress sensitive detail | No direct file mutation; only a reviewed Windows-supported mechanism where one exists |
| Windows-managed runtime | `pagefile.sys`, `swapfile.sys`, `hiberfil.sys`, reserved storage, restore points, shadow copies | Report through supported system information where feasible | No direct file mutation; settings or a reviewed Windows-supported mechanism only |
| Unknown application state | unknown `ProgramData`, unknown application databases | Report-only and classify as unknown | Specialized first-party adapter plus evidence and tests required |
| User content | documents, source repositories, photos, videos, archives, Downloads, Desktop files | Review or move candidate; never infer disposability from size or age | No rule-driven deletion; an explicit user-selected workflow must preserve consequence and rollback information |
| Virtualized and managed data | Docker volumes, WSL virtual disks, virtual-machine disks, Android emulators | Report-only | Supported product workflow only; never direct deletion or automatic compaction |
| Identity and personal state | game saves, browser profiles, password-manager data, email databases, encryption keys, credential stores | Protected/report-only | Direct cleanup is `Prohibited` |
| Cloud and encrypted state | cloud-sync placeholders, EFS or otherwise encrypted content | Report metadata without hydration or decryption | No automatic hydration, decryption, quarantine, or cleanup |

The canonical paths are resolved from the running system rather than assuming that Windows is installed on `C:`. Marketing may say “C: drive”; safety logic uses the discovered system volume and known-folder APIs.

## Path and target validation

The same validation policy applies in plan generation, dry run, normal execution, and elevated execution. The final executor must repeat validation immediately before each mutation.

1. Require a known `ActionType`, approved rule or compiled adapter ID, schema version, and capability-specific target contract.
2. Expand known folders and environment-derived roots during plan generation. Persist resolved absolute targets in the plan; reject unresolved variables, relative paths, globs, wildcards, alternate data streams, and device-path aliases not explicitly supported by that capability.
3. Resolve the target with Windows filesystem APIs. String normalization alone is insufficient.
4. Perform case-appropriate, component-aware containment against the capability's approved roots. A textual prefix such as `C:\Cache` must not accept `C:\Cache-Escape`.
5. Inspect every existing path component for reparse behavior. Junctions, symbolic links, mount points, and other reparse points are not followed by default.
6. Query the final opened object where practical and record stable evidence: volume identity, file ID, final canonical path, entry type, reparse state, size, and relevant timestamps. Metadata inspection must not hydrate cloud placeholders.
7. Apply the protected-resource policy to the target and its ancestors. Denial wins over every allowlist.
8. Bind the evidence, rule-pack version, adapter version, and target set into the cleanup-plan digest.
9. Immediately before action, reopen and compare identity, path, type, link state, size, and timestamps. A material change makes the item stale and blocks mutation.

Hard links and sparse/compressed files require storage-accounting evidence; removing one name must not be advertised as recovering its logical size. Inaccessible or ambiguous targets fail closed for cleanup while remaining visible as incomplete scan coverage.

## Dry-run contract

A valid cleanup plan contains at least:

- plan ID, schema version, creation time, and expiry time;
- initiating user/session binding and applicable product/rule/adapter versions;
- every item ID, `ActionType`, `RiskLevel`, evidence, consequence, rollback classification, elevation requirement, and dependencies;
- exact resolved targets where feasible, plus stable identity evidence and whether the byte estimate is exact, estimated, or unknown;
- exact approved tool ID and typed argument values for adapter actions—never a command line;
- a deterministic digest over all confirmation-relevant fields.

Plans expire ten minutes after creation. The implementation may shorten this for a volatile capability but may not extend it without an ADR. A plan is also stale when the target identity or material metadata changes, its rule/adapter version changes, its protected-resource decision changes, or the user/session binding no longer matches.

Dry run must not:

- mutate, rename, move, recycle, quarantine, hydrate, decrypt, take ownership of, or change permissions on a target;
- launch a tool mode that can clean when a genuine non-mutating preview is unavailable;
- request elevation merely to make an uncertain plan appear valid;
- claim that estimated bytes will be recovered.

If exact targets or bounded adapter arguments cannot be previewed, the action remains `report-only` or `manual-instructions`.

## Confirmation contract

- The UI and CLI display the plan digest, exact/estimated status, target summary, consequences, rollback classification, elevation need, and expiry before consent.
- Beta cleanup selections start empty. Protected, unknown, user-content, virtual-disk, profile, credential, and cloud categories are never preselected.
- `Low` items require an explicit selection after preview.
- `Medium` items require a separate consequence acknowledgement and cannot be hidden in a `Low` batch.
- `High` items are unavailable until their capability-specific gate is approved; if later enabled they require isolated confirmation.
- `Prohibited` items never reach a confirmation control.
- Confirmation is one-time and digest-bound. Editing targets, arguments, risk, order, versions, or dependencies invalidates it and requires a new dry run.
- Windows UAC consent, when eventually used, is additional to CLYR confirmation; it is not a substitute.

## Rollback classifications

| Classification | Meaning | Required presentation |
| --- | --- | --- |
| `not-applicable` | No CLYR mutation occurs | State that there is nothing for CLYR to roll back |
| `windows-controlled` | Windows owns recovery, such as Recycle Bin behavior | State eligibility limits and that restoration is not guaranteed by CLYR |
| `clyr-managed` | CLYR retains verified quarantine metadata/content | Show location class, expiry policy, capacity requirement, and recovery steps |
| `none` | The approved mechanism has no rollback | Minimum `Medium`; display before confirmation and never imply recovery |

Cross-volume quarantine is copy, verify, then remove; it is not an atomic move. Destination space plus a safety margin is checked first. Expiry never causes silent deletion: expired quarantine becomes a new explicit plan. Credential stores, encrypted content, protected system content, cloud placeholders requiring hydration, and application databases are ineligible without an approved specialized adapter.

## Audit and verification

Before mutation, persist an action-journal entry that binds the confirmed plan digest. After attempted execution, issue a structured receipt containing request and plan IDs, item outcomes, validated identities, timestamps, error codes, skipped/changed targets, rollback status, helper identity when used, and free-space measurements.

Audit records use privacy-safe path tokens and product/category roots by default. Exact quarantine recovery metadata is stored separately with restricted local access and is never included in a default export. Logs must not contain access tokens, personal filenames, complete personal paths, command-line secrets, or raw external-tool output.

Recovered bytes are measured from storage state before and after action and labeled with accounting caveats. A plan estimate is never silently relabeled as an actual result. Partial completion, locked files, disappearing files, and verification failures remain visible.

## Elevated-helper restrictions

The future helper is a short-lived, separately elevated process; the UI and CLI remain standard-user processes. The helper has no background listener and no service installation. It accepts exactly one authenticated, expiring, confirmed batch through the versioned protocol defined in `SECURE_IPC.md`, revalidates the complete batch before mutation, executes only compiled allowlisted capabilities, returns a receipt, and exits.

It rejects arbitrary commands, shell text, executable paths, unknown adapter/rule IDs, unknown schema fields where strict validation requires rejection, free-form paths outside typed capabilities, replayed requests, changed targets, and protected resources. Elevation is disabled when same-product executable identity cannot be established.

## Community-rule restrictions

- YAML is declarative data, never executable code.
- Unknown schema versions, action types, fields with security meaning, and malformed encodings are rejected.
- Detection roots are bounded and protected-resource policy always wins.
- Community rules cannot supply programs, command lines, shell fragments, environment assignments, or arbitrary adapter arguments.
- A rule can reference only a compiled, first-party adapter ID whose own code fixes executable discovery, supported versions, subcommands, typed arguments, limits, and privilege requirement.
- New executable behavior requires code review, malicious fixtures, safety tests, and a signed/versioned built-in release; rule-pack signatures alone do not grant privilege.

## Residual risks and decisions

- Stable file identity support varies by filesystem; unsupported identity evidence keeps mutation unavailable rather than trusting a path alone.
- Windows and third-party behavior can change after validation. Unsupported versions become report-only, but a vendor regression can still cause unexpected results in a previously approved range.
- Recycle Bin and external-tool rollback are controlled outside CLYR and cannot be guaranteed.
- Free-space deltas can be affected by concurrent system activity. Receipts must explain uncertainty rather than promise exact attribution.
- A user can still approve an unwanted but accurately described action. Empty defaults, consequence-first copy, risk separation, and rollback information reduce but cannot eliminate consent mistakes.

These risks are tracked in `RISK_REGISTER.md`; none permit bypassing a `Prohibited` decision.

## Acceptance criteria

This model is satisfied only when tests and reviews demonstrate that:

- every action and risk value uses the canonical enumerations in this document;
- protected-resource denials override rules, UI state, CLI options, and elevation;
- canonical containment rejects traversal, sibling-prefix, case, device-path, alternate-stream, and reparse-point escapes;
- plans are immutable, digest-bound, one-time, expire after ten minutes, and fail closed on target change;
- dry run causes no storage mutation or tool cleanup and labels uncertainty accurately;
- no arbitrary command or path contract reaches an executor;
- confirmation is explicit, risk-appropriate, and invalidated by material plan changes;
- execution is journaled before mutation and produces a privacy-safe receipt with verified outcomes;
- all automated execution tests use disposable fixtures or mocks, never a developer's real cleanup targets;
- implementation status is never inferred from this design document: cleanup remains planned until its phase gates pass.
## Phase 5 enforced safety boundary

Phase 5 planning is implemented, but execution is not. Protected policy overrides eligibility; user-created and broad browser-profile aggregates cannot become dry-run items. Canonical digest/binding/expiry/path checks fail closed, plans are memory-only, and the production executor can only return ExecutionNotAvailableInPhase5. No target-file mutation, Recycle Bin, quarantine, process, elevation, registry, permission, Windows-setting, or helper capability exists.
