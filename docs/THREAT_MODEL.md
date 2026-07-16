# CLYR Threat Model

Status: Phase 0 design baseline. As of Phase 6, the authorization and integrity controls for one narrowly
allowlisted action are implemented and tested (one-time session/user/drive/digest-bound tokens, per-target
TOCTOU revalidation, typed bounded IPC with no polymorphic deserialization surface) — see
`docs/PHASE6_EXECUTION.md` and ADR-0012/0013. The real fixture-only UAC smoke test proving the helper's
elevation path end to end has not yet been run. Controls for anything beyond this one action remain
requirements for later phases, not claims about implemented code.
Method: STRIDE, supplemented by accidental-misuse and supply-chain analysis.

## Security objectives

1. **Safety:** no unapproved user or system content is mutated.
2. **Authorization:** a privileged action is performed only for the same user, session, confirmed plan, product identity, and typed capability that requested it.
3. **Integrity:** rules, plans, targets, IPC messages, adapters, receipts, and update artifacts cannot change unnoticed across their trust boundaries.
4. **Privacy:** scans, logs, persistence, IPC, and exports reveal no more personal information than their stated purpose requires.
5. **Availability:** hostile or pathological filesystem input cannot make the host unusable; cancellation and bounded resource use remain effective.
6. **Accountability:** CLYR distinguishes planned, attempted, completed, partial, failed, and verified outcomes without exaggerating recovered space.

## Scope and assumptions

In scope are the standard-user app and CLI, shared core, local persistence, built-in and community rule data, scan/export data, compiled external-tool adapters, the future elevated helper, local IPC, package/update provenance, and all filesystem objects inspected or targeted by CLYR.

Assumed hostile inputs include path text, filenames, directory depth and width, metadata, reparse points, hard links, cloud placeholders, rule packs, database contents, external-tool output, IPC bytes, reports imported for viewing, and changes made concurrently by another process.

The following adversaries are considered:

- a local standard-user process attempting to impersonate CLYR or race its targets;
- a malicious rule author or compromised rule distribution channel;
- a malicious file tree prepared for CLYR to scan or plan against;
- a compromised or impersonated third-party executable;
- a supply-chain attacker controlling a dependency, build, package, signature, or update channel;
- a well-intentioned user who misunderstands risk or chooses the wrong content.

A process already running as the same administrator as the helper, a compromised Windows kernel, and offline physical tampering are outside the enforceable application boundary. CLYR must not weaken Windows protections in an attempt to defend against them.

## Assets and trust boundaries

| Asset | Required property |
| --- | --- |
| User and system content | No mutation outside an exact confirmed capability and target set |
| Protected-resource policy | Deny decisions cannot be overridden by rules, UI, CLI, or helper |
| Rule packs and adapter registry | Origin/version known; declarative rules cannot introduce code or privilege |
| Cleanup plan and confirmation | Immutable, expiring, digest-bound, one-time, user/session-bound |
| Elevated-helper protocol | Mutually identified peers, isolated local channel, replay resistance, bounded messages |
| Action journal and receipts | Durable before mutation, tamper-evident enough to detect inconsistency, privacy-safe |
| Scan snapshots and reports | Accurate uncertainty labels and privacy classification |
| Installed binaries and updates | Same-product identity, trusted provenance, downgrade/rollback policy |

Principal trust boundaries are:

1. untrusted filesystem and tool output into the standard-user process;
2. community rule data into the validator and rule engine;
3. the UI/CLI into the plan and confirmation boundary;
4. standard integrity into the UAC and elevated-helper boundary;
5. CLYR adapters into third-party processes and Windows-supported cleanup mechanisms;
6. local detailed data into privacy-safe exports;
7. build/update infrastructure into installed product binaries.

`docs/diagrams/privilege-boundary.mmd` is the authoritative Phase 0 boundary diagram.

## Risk method

Threat ratings use the same canonical values as `RiskLevel`: `Informational`, `Low`, `Medium`, `High`, and `Prohibited`. Here the value expresses the product risk if the threat is not adequately controlled. `Prohibited` is reserved for behavior the architecture must never expose, not merely a very likely threat. No additional hidden tier exists.

## STRIDE analysis

### Spoofing

| ID | Threat and attack path | Risk | Required controls | Residual risk |
| --- | --- | --- | --- | --- |
| S-01 | A same-user process connects to the helper pipe first or impersonates the initiating app. | `High` | Unpredictable single-use pipe; restrictive DACL; local-only endpoint; verify peer PID, user SID, session, integrity level, executable path, signer/package identity, and protected install location; bind the request to the launched helper PID and confirmed plan digest. | OS identity APIs or packaging behavior can vary. Elevation stays disabled where same-product identity cannot be proven. |
| S-02 | A fake helper receives a plan from the app. | `High` | Launch only the helper from the verified installed product location; capture the launched PID; validate image identity/signature and protocol challenge before sending plan data. | A compromised trusted signing identity remains a supply-chain risk. |
| S-03 | An executable with a familiar name impersonates a supported tool. | `High` | Compiled adapter controls discovery; validate canonical executable location, supported version, and publisher/signature where practical; never search the current directory first; direct process invocation only. | Some vendor tools are unsigned or mutable per-user. Such configurations remain report-only unless equivalent identity evidence is approved. |

### Tampering

| ID | Threat and attack path | Risk | Required controls | Residual risk |
| --- | --- | --- | --- | --- |
| T-01 | A malicious rule pack declares a broad root, privileged action, script, or misleading risk. | `High` | Strict versioned schema; unknown security fields rejected; protected-resource policy and risk floor applied outside the rule; community rules are declarative and cannot add executables or typed arguments; signed/versioned built-ins and malicious-rule corpus. | A semantically misleading description can pass syntax checks; maintainer review and fixtures remain necessary. |
| T-02 | Traversal, device aliases, alternate streams, case tricks, or sibling-prefix paths escape an approved root. | `High` | Absolute resolved targets; reject unresolved variables, wildcards, unsupported device paths, and alternate streams; OS canonicalization; component-aware containment; final-handle path and volume checks; property/fuzz tests. | Filesystem-specific canonicalization differences can remain; unsupported filesystems fail closed for mutation. |
| T-03 | A request or response is modified, truncated, duplicated, or has security-relevant unknown fields. | `High` | Message framing; strict schema and length checks; OS-isolated named-pipe channel; peer validation; per-session nonces and sequence numbers; plan digest and transcript/request digest; one request per helper; fail closed. | Kernel/admin attackers can observe or alter the channel and are outside the boundary. |
| T-04 | A stale plan is edited or replayed after target, rule, adapter, user, or session state changes. | `High` | Immutable digest-bound plan; ten-minute maximum lifetime; one-time request ID/nonce; user/session and version binding; persistent replay cache for the active transaction; complete revalidation before mutation. | A target can still change after revalidation; T-05 controls the remaining race. |
| T-05 | A file is replaced between validation and action, including same-path replacement. | `High` | Stable volume/file identity, type, reparse state, size, timestamps, and final path; open handles where practical; immediate per-item revalidation; reject material changes; no silent destructive retry. | Filesystems lacking stable IDs keep execution unavailable; vendor-managed operations can have an unavoidable internal race documented per adapter. |
| T-06 | A junction, symbolic link, mount point, or other reparse point redirects traversal into protected content. | `High` | Do not follow reparse points by default; inspect every component; open reparse points without traversal when inspecting; compare final volume/path; protected-resource denial wins; path-swap fixtures. | New reparse tags or races may evade path-only checks, so stable handle/identity checks remain mandatory. |
| T-07 | Local database or journal edits make an unconfirmed plan appear confirmed or a failed action appear successful. | `High` | Confirmation token derives from the displayed plan digest and current session; journal written before mutation; constrained state machine; receipt/request digests; reconciliation with actual filesystem/free-space state; database ACLs and integrity checks. | A same-user attacker can edit user-owned persistence. The helper must not treat database presence alone as authorization. |
| T-08 | A compromised update or downgrade replaces policies, rules, app, or helper. | `High` | No updater without an ADR; signed packages and metadata; identity and version binding; rollback/downgrade policy; protected install root; SBOM and release provenance; helper/app same-product validation. | Signing-key or build-system compromise remains possible and requires release operations controls. |

### Repudiation

| ID | Threat and attack path | Risk | Required controls | Residual risk |
| --- | --- | --- | --- | --- |
| R-01 | CLYR or the user cannot distinguish consent, attempt, partial completion, rollback, and verified completion. | `Medium` | Persist journal before mutation; one-time confirmation record; structured item receipts; monotonic state transitions; helper identity and timestamps; explicit `PartiallyCompleted`; free-space verification. | Local time can change and receipts are not a remote notarization. They provide local accountability, not non-repudiation against an administrator. |
| R-02 | External-tool output is ambiguous, localized, or forged and CLYR reports success. | `Medium` | Prefer structured output; bounded capture; known exit-code contract; postcondition and free-space checks; unsupported versions report-only; retain normalized evidence/error code, not raw sensitive output. | Some supported tools cannot attribute recovered bytes precisely; results remain qualified. |

### Information disclosure

| ID | Threat and attack path | Risk | Required controls | Residual risk |
| --- | --- | --- | --- | --- |
| I-01 | Logs, snapshots, crash data, receipts, or exports reveal usernames, filenames, project names, tokens, account IDs, or full paths. | `High` | Data classification; minimization; path tokenization/root labels; bounded retention; privacy-safe default export; explicit local-only detailed export; secret/redaction tests; no telemetry by default. | Category and size combinations can still identify a user indirectly; the detailed export requires a clear warning. |
| I-02 | Pipe names, command lines, environment variables, or error messages reveal a nonce or sensitive target. | `Medium` | No secret or personal path on command line; opaque pipe/session IDs; payload only after peer authentication; sanitize UI/log errors; erase transient secret material; restrict endpoint ACL. | Same-user process inspection is difficult to prevent completely; authentication does not rely on pipe-name secrecy alone. |
| I-03 | Scanning a cloud placeholder hydrates it or decrypting/accessing protected data reveals content. | `Medium` | Metadata-only APIs where practical; never automatically hydrate, decrypt, take ownership, or alter ACLs; represent inaccessible/placeholder state and skip content reads. | Provider behavior can change; uncertain providers remain partial/report-only. |

### Denial of service

| ID | Threat and attack path | Risk | Required controls | Residual risk |
| --- | --- | --- | --- | --- |
| D-01 | A huge, deep, cyclic, or rapidly changing tree exhausts memory, CPU, handles, log space, or UI responsiveness. | `Medium` | Streaming enumeration; bounded queues/top-N; depth/work budgets; cancellation; reparse avoidance; rate-limited progress; bounded errors/logs; partial-result state; performance fixtures. | A legitimate deep scan can remain slow and incomplete; the UI must show coverage rather than imply failure-free exactness. |
| D-02 | An external tool hangs, spawns children, floods output, or ignores cancellation. | `Medium` | Per-adapter timeout; process-tree policy; direct invocation; stdout/stderr limits; cancellation; fake-process tests; no automatic retries. | Terminating a vendor process can leave vendor-owned state uncertain; report partial/unknown and stop dependent work. |
| D-03 | IPC flooding or an oversized batch holds an elevated helper open. | `Medium` | One local client, one execution request, 4 MiB envelope, at most 128 plan items and 1,024 target descriptors, bounded decode time, 30-second connect deadline, inactivity timeout, then exit. | A user can repeatedly approve new UAC prompts; CLYR cannot prevent deliberate consent but never auto-prompts. |
| D-04 | Disk-full conditions prevent journal, quarantine, database, or receipt writes during cleanup. | `High` | Preflight space and journal durability; no mutation unless required journal state is durable; quarantine safety margin; reserve/flush strategy evaluated in Phase 6; recovery reconciliation. | The disk can fill concurrently after preflight; stop at a safe item boundary and surface recovery instructions. |

### Elevation of privilege

| ID | Threat and attack path | Risk | Required controls | Residual risk |
| --- | --- | --- | --- | --- |
| E-01 | A rule or request injects a shell command, executable, option, or metacharacter into an elevated operation. | `Prohibited` | No generic command action; no shell invocation/interpolation; compiled first-party adapter with immutable executable/subcommand and typed validated arguments; unknown IDs rejected; direct argument-list invocation. | Adapter implementation bugs remain possible and require code review, fuzzing, and fake-runner tests. |
| E-02 | Running the UI/CLI as administrator broadens every parser and filesystem bug into an elevated bug. | `Prohibited` | Standard-user manifest; detect and warn/refuse cleanup if whole app is elevated unexpectedly; separate short-lived helper; no background service. | A user can manually launch software with unusual compatibility settings; privileged cleanup remains unavailable unless the intended broker flow is established. |
| E-03 | A writable install directory, DLL search path, or dependency load lets a standard user replace code used by the helper. | `High` | Signed same-product binaries; administrator-protected install root; safe DLL loading/search configuration; no current-directory discovery; image/dependency review; package integrity checks. | Platform/package defects and signing compromise remain supply-chain risks. |
| E-04 | A valid low-scope capability is confused into operating on another user/session or broader target set. | `High` | Caller SID/session binding; capability ID, rule/adapter version, approved root, targets, risk, and confirmation all bound into digest; complete batch revalidation; minimum token privileges; no impersonation shortcuts. | Multi-session and fast-user-switching behavior requires dedicated integration tests before elevation ships. |

## Misuse and safety-abuse cases

| ID | Misuse | Policy response |
| --- | --- | --- |
| M-01 | User assumes every large item is “junk.” | Neutral finding language; size never lowers risk; protected/unknown/review labels; explain why content exists and consequence of removal. |
| M-02 | User clicks a single broad “clean now” control. | No such control; beta selections start empty; `Medium` items are separated; `High` is gated; `Prohibited` has no override. |
| M-03 | User follows manual instructions and causes damage outside CLYR. | `manual-instructions` is `Informational`, clearly leaves CLYR's controlled workflow, cites authoritative guidance, and never claims verified recovery. |
| M-04 | User mistakes a dry-run estimate for guaranteed recovery. | Exact/estimated/unknown labels remain adjacent to values; execution receipt reports measured change and concurrent-activity caveats. |
| M-05 | User or support staff shares a detailed report publicly. | Default export is privacy-safe; detailed local-only export requires an explicit warning and is never the default. |

## Security invariants

- Protected-resource denial, unknown action type, arbitrary command, broad wildcard mutation, and permanent deletion are `Prohibited` and cannot be overridden.
- The standard-user app and CLI never gain administrator rights as a whole.
- The helper performs zero mutation before authenticating peers, validating the exact schema, validating all batch items, and finding a durable pre-action journal state.
- A changed target is skipped/rejected, never silently substituted by a same-path object.
- Disconnect, timeout, cancellation, or crash never causes an automatic destructive retry.
- Report/export privacy filtering is applied at the output boundary even when local detailed data exists.
- Scan incompleteness is a valid partial result, not an excuse to invent exact totals.

## Validation plan and exit criteria

Before Phase 6 can expose any real mutation, all applicable checks must pass:

- property/fuzz tests for canonicalization, component containment, device aliases, alternate streams, and reparse components;
- malicious rule fixtures covering scripts, unknown fields, misleading risk, broad roots, path escape, and adapter argument expansion;
- deterministic TOCTOU fixtures for same-path replacement, file-ID change, junction swap, mount change, and disappearing targets;
- IPC schema/fuzz tests for truncation, oversized fields, duplicate IDs, unknown versions, replay, expiry, sequence error, wrong SID/session/PID, and peer-image mismatch;
- adapter tests using a fake process runner for injection strings, unsupported versions, output flood, timeout, cancellation, and child processes;
- privacy tests for usernames, personal paths, project names, account IDs, secrets, and maliciously crafted filenames in every output class;
- crash tests at every cleanup state transition, including disk-full before and after journal persistence;
- installer/signing checks proving standard users cannot replace either peer and each peer rejects the wrong product identity;
- manual multi-session, UAC-denial, accessibility, and consequence-comprehension review;
- independent review that documentation, protocol schemas, tests, and shipped capability allowlists agree.

Any failed identity, protected-path, plan-integrity, journal-durability, or injection gate keeps execution disabled. Remaining accepted risks must have an owner and acceptance rationale in `RISK_REGISTER.md`.
## Phase 5 threat treatment

Implemented controls reject traversal, sibling-prefix confusion, UNC/device namespaces, alternate streams, environment escapes, ambiguous aliases, reparses, protected components, duplicate selections, oversized plans, negative/overflowing accounting, stale source bindings, expired plans, changed target metadata, digest edits, incompatible rule packs, and unsafe reports. SHA-256 detects edits but does not authenticate a signer. Residual execution TOCTOU remains intentionally unresolved and blocks Phase 6.
