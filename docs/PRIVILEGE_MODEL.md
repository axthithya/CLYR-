# CLYR Privilege Model

Status: Phase 0 architecture decision. No elevated helper or cleanup executor is implemented.
Related decisions: `adr/0002-separate-elevated-helper.md`, `SECURE_IPC.md`, and `diagrams/privilege-boundary.mmd`.

## Decision

The CLYR app and CLI run at the invoking user's standard integrity level with an `asInvoker` execution manifest. They do not request administrator rights at startup. A separately built helper may be introduced in Phase 6 for an explicitly approved capability that genuinely requires elevation. The helper is launched through Windows UAC only after dry run and CLYR confirmation, handles one bounded batch, returns a receipt, and exits.

There is no CLYR Windows service, scheduled task, startup entry, persistent broker, generic privileged file API, or always-listening endpoint.

## Why separation is mandatory

Scanning parses attacker-controlled paths, metadata, rule data, database state, and external-tool output. Running that broad surface at high integrity would turn ordinary parsing or path bugs into privilege-escalation paths. Separating elevation keeps UI, rule loading, reporting, persistence, and most adapters outside the privileged boundary and makes the future privileged surface small enough to allowlist and test.

UAC consent answers whether Windows may start a process at higher integrity. It does not prove that a CLYR cleanup plan is safe, current, or understood. CLYR confirmation therefore occurs first and is independently bound to the plan digest.

## Process and token matrix

| Component | Normal token/integrity | May elevate itself? | Permitted responsibilities | Explicitly forbidden |
| --- | --- | --- | --- | --- |
| `Clyr.App` | Invoking user, standard/as-invoker | No | UI, confirmation, read-only analysis orchestration, plan display, privacy-safe export | Direct cleanup, arbitrary process execution, helper behavior in-process |
| `Clyr.Cli` | Invoking user, standard/as-invoker | No | Read-only commands; later plan/confirmation client with explicit switches | Administrator-required startup, hidden confirmation, generic elevated command/path options |
| Core/rules/persistence libraries | In their standard-user host process | No | Classification, protected-policy decisions, dry run, local state | Loading executable rule code, granting privilege, bypassing a denial |
| Standard adapter process | Same standard-user context as its host | No | One compiled adapter's exact executable/subcommand/typed arguments | Shell invocation, current-directory executable discovery, privilege escalation |
| `Clyr.ElevatedHelper` (future) | UAC-approved high-integrity token for the initiating administrative account | Already brokered by Windows; cannot spawn another elevation chain | Authenticate one client; validate one confirmed batch; invoke only approved privileged capability; return receipt; exit | UI, scanning, rule-pack loading, arbitrary paths/commands, network access, background listening, self-update |
| Privileged external mechanism (future) | Only what a reviewed adapter requires | No generic delegation | Exact Windows-supported cleanup or vendor operation in its approved contract | Interactive shell, user-supplied executable/arguments, unbounded child processes |

If the app or CLI detects that it was launched elevated outside this broker flow, read-only analysis may continue but cleanup execution is unavailable. This avoids making manual “Run as administrator” a bypass around the intended boundary.

## Capability eligibility

An action's `RiskLevel` and elevation requirement are separate. Administrator rights never lower risk and cannot override `Prohibited`.

| `ActionType` | Expected host | Elevation policy |
| --- | --- | --- |
| `report-only` | Standard process | Never elevated |
| `open-settings` | Standard process | CLYR does not elevate; Windows may present its own consent for a settings operation |
| `manual-instructions` | Standard process | CLYR does not execute or elevate the described steps |
| `recycle-files` | Standard process for explicitly selected eligible user-owned targets | Privileged variant requires a separate approved capability; protected resources remain blocked |
| `quarantine-files` | Standard process for eligible user/app-owned targets | Privileged variant requires dedicated tests and cannot handle protected, encrypted, credential, database, or hydrating cloud content |
| `trusted-tool-command` | Standard process by default | Elevated only when the compiled adapter fixes executable identity, version, arguments, effects, limits, and privilege need |
| `windows-supported-cleanup` | Standard or helper as the documented Windows API requires | Only a capability-specific allowlist; never direct deletion of protected Windows paths |
| `move-known-folder` | Standard process for the current user's known folder | No helper merely to bypass access; use Windows-supported known-folder behavior and fail on ownership/ACL problems |

The first Phase 6 execution allowlist is constrained to eligible, explicitly selected Recycle Bin operations in test/user-owned locations; app-owned test-data deletion or quarantine; and selected supported cache-clean adapters with exact arguments. Anything else remains unavailable until separately approved.

## Privilege decision procedure

A plan item is eligible for the helper only if all answers are yes:

1. Is its `ActionType`, rule/adapter ID, version, and risk explicitly allowlisted in compiled first-party code?
2. Does authoritative API/vendor evidence establish that the operation requires elevation?
3. Did protected-resource and path/identity validation return allow rather than protected, unknown, stale, or ambiguous?
4. Did a non-mutating dry run resolve a bounded target/effect and display consequence, rollback, and uncertainty?
5. Is the plan unexpired, immutable, digest-bound, and confirmed by the same user/session?
6. Are app, helper, installation location, package/signature identity, and protocol compatible and trustworthy?
7. Can the helper perform the operation without enabling generic backup, restore, ownership, debugging, or arbitrary execution privileges?
8. Is durable pre-action journal state present and sufficient free/destination space available?

A “no” produces a structured refusal. It never triggers a broader retry, an ACL change, ownership takeover, shell fallback, or instructions to run the whole app as administrator.

## Launch and lifetime

The planned broker sequence is:

1. The standard process completes dry run, displays the exact plan, and records one digest-bound confirmation.
2. It writes and flushes a `Confirmed` journal state before any mutation.
3. It verifies that both product executables are from the same approved product identity and an installation root not writable by a standard user. Unsigned, portable, user-writable, or identity-ambiguous builds cannot elevate.
4. It creates a unique local named-pipe endpoint with a restrictive ACL and a fresh nonce, then requests UAC launch of the installed helper. No target path, command, personal data, or reusable secret is put on the command line.
5. UAC denial or launch failure returns safely to the confirmed/failed UI state with zero mutation.
6. The standard process captures the helper PID; both peers validate PID, SID, session, integrity, executable image, same-product signer/package identity, and protocol challenge before the plan is transmitted.
7. The helper accepts exactly one request, revalidates the complete batch, performs the minimum approved work, returns a structured receipt, clears transient data, and exits.
8. The standard process persists the receipt, verifies postconditions/free-space change, and reconciles the journal. It never automatically resends a destructive request after disconnect or timeout.

The connection deadline is 30 seconds. The helper has an inactivity timeout and no keep-alive mode. One process lifetime permits at most one execution request containing at most 128 plan items and 1,024 target descriptors in a 4 MiB envelope. Adjusting these limits requires a documented security and performance review.

## Identity requirements

### Initiating user and session

- Bind plan, confirmation, endpoint ACL, and request to the initiating Windows user SID and logon session.
- Reject fast-user-switching, Remote Desktop, or session changes that break the binding; create a new plan in the active session instead.
- Validate both token identity and named-pipe peer identity. A shared username, pipe name, or possession of a plan file is insufficient.
- Do not impersonate another user to gain filesystem access.

### Executable and installation identity

- App and helper must share an approved signed package/publisher identity or an equivalent signed manifest rooted in an administrator-protected install directory.
- Each side resolves the peer's actual image from its process handle and validates the launched PID. Claimed image paths in protocol data are untrusted.
- The helper loads no community assembly, YAML rule pack, plugin, or executable from a user-writable path.
- Executable and DLL discovery use explicit protected paths and safe search rules; the current directory and user `PATH` do not select privileged code.
- A signer match alone is not enough when that signer covers multiple products; product/package identity and compatible version must also match.

Developer builds that cannot meet these identity requirements may use mocks and disposable integration fixtures, but real elevation stays disabled.

## Token minimization and helper restrictions

- Do not enable `SeTakeOwnershipPrivilege`, generic backup/restore privileges, debug privilege, or other broad token privileges as a shortcut. Any capability needing an additional privilege requires its own ADR and security tests.
- Never take ownership, change ACLs, decrypt, hydrate, unlock, terminate a user process, load a driver, modify security software, or bypass Windows Resource Protection.
- Do not scan broad directories at high integrity. The helper receives only typed target descriptors already resolved by the confirmed plan and reopens only those objects needed for validation/action.
- Do not access network resources, mapped drives, UNC paths, or the internet from the helper. The initial privileged capability set is local-volume only.
- Do not read rules or user preferences to decide privilege. The compiled capability registry and the confirmed request contain all security-relevant choices.
- Do not persist reusable secrets or accept a second client. Pipe disconnect ends authorization to start further items.
- Child processes, if a reviewed adapter needs one, use the narrowest available token and explicit executable/argument list, inherit no unnecessary handles, have bounded output/time, and are included in the adapter's process-tree policy.

## Validation inside the helper

After peer authentication but before mutation, the helper validates the entire request rather than trusting the standard process:

- protocol and schema versions, required fields, size/count limits, request ID, nonce, sequence, timestamps, and ten-minute plan expiry;
- same user/session and confirmed plan digest;
- known `ActionType`, compiled capability ID, supported adapter/rule versions, `RiskLevel`, elevation flag, dependency/order constraints, and one-time status;
- absolute canonical local targets, component-aware containment, final handle path/volume, stable identity, object type, reparse state, material metadata, and protected-resource decision;
- executable identity and exact typed arguments for a tool action;
- durable pre-action state, destination capacity where applicable, and rollback classification.

One invalid item rejects the whole batch before mutation. If state changes only after execution begins, the helper stops before the next safe item boundary, does not retry the changed item, marks unattempted items skipped, and returns `PartiallyCompleted` or `Failed` with structured codes.

## Denial, cancellation, crash, and recovery

- UAC denial is a normal, non-error user outcome and performs zero mutation.
- Cancellation before mutation rejects the batch. During execution it is cooperative at capability-defined safe points; it never abandons a copy or vendor transaction at an unsafe instruction boundary.
- Pipe loss means “start no further work,” not “roll back blindly” and not “retry.” The helper completes only the currently documented safe boundary, records what it can, and exits.
- On restart, the standard process enters reconciliation. It inspects journal/receipt and actual target state; it does not infer success from absence or repeat an action from the old confirmation.
- Missing or corrupt journal state blocks a new mutation. Recovery may remain report-only until the user reviews a newly generated plan.

## Residual risks

- UAC presents operating-system publisher and program information, not the full CLYR consequence summary; the in-app confirmation remains essential.
- Same-user persistence can be modified by local malware. Helper authorization therefore depends on authenticated process/product identity and live request validation, not the database alone.
- An administrator or kernel attacker can replace trusted runtime state despite application checks; this is outside the app boundary.
- Windows identity, package, and multi-session behavior can differ across deployment modes. A distribution mode without verified behavior cannot ship elevation.
- A bug in a small compiled helper or adapter can still be severe. The separation reduces attack surface but does not make privileged code inherently safe.

## Acceptance criteria

Privilege-bearing execution remains disabled until evidence shows that:

- app and CLI manifests run as invoker and an unexpectedly elevated host cannot execute cleanup;
- no service, scheduled task, persistent listener, generic shell, or generic privileged file API is installed;
- wrong PID, SID, session, integrity, image path, signer/package, version, nonce, digest, or expiry is rejected before plan disclosure or mutation;
- the standard user cannot replace the installed app, helper, or privileged dependencies;
- every helper capability is compiled, typed, versioned, bounded, risk-reviewed, and independently tested;
- all batch items are revalidated before the journal advances to execution;
- UAC denial, cancellation, disconnect, crash, disk-full, partial completion, and stale target cases produce no silent retry;
- protected-resource and `Prohibited` decisions remain final even with a valid administrator token;
- test cleanup uses disposable fixtures/mocks and manual elevation testing uses an isolated environment;
- protocol and privilege documentation matches the actual manifests, packaging, and code before release.
## Phase 5 privilege status

The app and CLI remain asInvoker. Planning records metadata only; no built-in Phase 5 action requests elevation and no launch, token, helper, IPC, service, task, permission, ownership, or privileged filesystem implementation exists. ExecutionNotAvailableInPhase5 is the only production executor result.
