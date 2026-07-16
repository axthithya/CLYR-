# CLYR Error-Handling Model

Status: Phase 0 behavioral contract. It defines future behavior; no scanner, planner, cleanup executor, or helper is implemented.

## Objectives

CLYR handles failure without converting uncertainty into a safety claim. An error may reduce coverage, block an item, stop an operation, or create a partial outcome, but it must never silently broaden scope, retry destructive work, substitute a changed target, suppress a protected-resource denial, or report estimated recovery as actual recovery.

Expected environmental failures—access denied, disappearing files, locked files, unsupported capabilities, UAC denial, cancellation, disk full, version mismatch—are modeled outcomes at process boundaries. Unexpected exceptions are caught at the nearest safe orchestration boundary, normalized, privacy-filtered, and mapped to an honest state.

## Error versus action risk

`RiskLevel` classifies a proposed action and has exactly `Informational`, `Low`, `Medium`, `High`, and `Prohibited`. It is not an exception severity. An `Informational` scan can fail, and a `High` action can be blocked correctly without a system failure. Error codes, operation states, and `RiskLevel` must not be conflated in code or UI.

## Structured error contract

Every error crossing a component or process boundary contains:

- a stable machine-readable `code` from a versioned namespace;
- operation and phase (`scan`, `rule-load`, `plan`, `dry-run`, `ipc`, `execute`, `verify`, `rollback`, `persist`, or `export`);
- correlation ID and, when applicable, plan/item/target token—not an exported full personal path;
- disposition: continued, item skipped, operation stopped, partial result, or outcome unknown;
- whether mutation began and whether user action or a new plan is required;
- a concise localizable user message and optional privacy-filtered technical detail;
- relevant capability, adapter, and schema/protocol versions;
- timestamp and safe retry guidance.

Raw exceptions, stack traces, HRESULT text containing personal paths, command lines, environment blocks, access tokens, raw external-tool output, and arbitrary filenames do not cross into default UI, CLI JSON, receipts, or exports. Local diagnostic mode may retain bounded detail only after the same privacy filter and an explicit user choice.

## Stable error-code families

The exact leaf set will be versioned with contracts; Phase 1 should preserve these families:

| Family | Representative codes | Meaning and default disposition |
| --- | --- | --- |
| `scan.*` | `scan.access-denied`, `scan.entry-changed`, `scan.reparse-skipped`, `scan.resource-limit`, `scan.cancelled` | Continue when safe; record coverage gap; return partial/cancelled scan state rather than zero-size fiction |
| `rule.*` | `rule.schema-invalid`, `rule.pack-untrusted`, `rule.capability-unknown`, `rule.version-unsupported` | Reject the rule or transactional pack; never reinterpret unknown behavior |
| `plan.*` | `plan.protected-resource`, `plan.path-outside-root`, `plan.identity-unavailable`, `plan.overlap`, `plan.stale`, `plan.expired`, `plan.unsupported` | Item cannot enter a confirmable plan; re-observe/re-plan if safe |
| `ipc.*` | `ipc.peer-untrusted`, `ipc.version-incompatible`, `ipc.malformed`, `ipc.limit-exceeded`, `ipc.replay`, `ipc.digest-mismatch`, `ipc.disconnected` | Fail closed; no mutation or no further items; never downgrade/reconnect silently |
| `exec.*` | `exec.precondition-failed`, `exec.target-changed`, `exec.tool-timeout`, `exec.tool-failed`, `exec.cancelled-before-mutation`, `exec.partial` | Zero mutation means `Failed`; any uncertain/partial mutation means `PartiallyCompleted`; no automatic destructive retry |
| `verify.*` | `verify.target-state-unknown`, `verify.free-space-inconclusive`, `verify.receipt-incomplete` | Never claim `Completed` or actual bytes without supporting evidence |
| `rollback.*` | `rollback.unavailable`, `rollback.precondition-failed`, `rollback.partial` | Preserve original receipt, record a linked recovery outcome, and stop on changed destination/source |
| `data.*` | `data.database-busy`, `data.disk-full`, `data.integrity-failed`, `data.migration-failed` | Block mutation if journal durability is affected; preserve last known-good schema/data |
| `export.*` | `export.redaction-failed`, `export.schema-unsupported`, `export.destination-failed` | Produce no export on redaction/schema failure; never fall back to raw detail |
| `internal.*` | `internal.invariant-violated`, `internal.unexpected` | Stop the affected operation at a safe boundary; show correlation ID; no unsafe fallback |

Security-sensitive refusals use enough detail to help the user but do not become an oracle for an unauthenticated IPC peer. Full internal discrimination may be logged locally under a correlation ID while the remote response remains `ipc.peer-untrusted` or `ipc.malformed`.

## Failure behavior by workflow

| Stage | Recoverable example | Continue behavior | Stop/fail-closed condition | User-visible truth |
| --- | --- | --- | --- | --- |
| Drive discovery | One removable drive vanishes | Remove/mark unavailable; other drives remain | System-volume identity cannot be established for a safety-sensitive operation | Drive list changed; no invented capacity |
| Scan | Access denied, locked/disappearing entry | Skip entry, increment reason/count, preserve cancellation | Resource budget, systemic enumerator failure, or invariant violation | Partial coverage; totals are lower bounds/estimates as appropriate |
| Rule loading | One optional community pack is malformed | Reject that entire pack; retain last known-good built-ins | Built-in manifest/policy integrity fails | Rules unavailable or reduced; never silently load permissively |
| Finding/classification | Evidence insufficient | Mark unknown/review/report-only | Protected-policy or overlap invariant cannot be resolved deterministically | Uncertainty is shown; size/age is not called safe |
| Plan generation | Selected target is gone or unsupported | Return per-item block and allow the user to generate a new plan from remaining explicitly selected items | Protected target, path escape, duplicate/overlap, identity ambiguity | No confirmable digest until the displayed target set is rebuilt |
| Dry run | Exact reclaimable bytes unavailable | Keep estimate/unknown label when target/effect is still bounded | Tool lacks a genuine non-mutating preview or target scope cannot be fixed | Action becomes `report-only`/`manual-instructions` if safe preview is impossible |
| Confirmation | Plan expires or any material field changes | Ask for new dry run | Digest mismatch or hidden change | Old confirmation is invalid; no “continue anyway” |
| IPC/elevation | UAC denied or helper not trusted | Return without mutation; user may deliberately start a fresh attempt while plan is still valid if confirmation was not dispatched | Peer/version/replay/schema/digest failure | Elevation unavailable/denied; no instruction to elevate the whole app |
| Pre-execution validation | Any item changed or is invalid | None for that request | One invalid privileged item rejects the entire batch before mutation | `Failed`, zero attempted items, new observation required |
| Execution | Runtime lock/tool failure after prior item | Stop before the next safe item by default; record every item state | Identity/protected state changes, timeout, journal/receipt durability risk | `PartiallyCompleted` if anything changed or is uncertain; otherwise `Failed` |
| Verification | Free-space delta confounded or target state unreadable | Preserve measured observations and qualification | Cannot prove the required postcondition | Never `Completed`; use `PartiallyCompleted`/unknown measurement |
| Rollback | Some quarantine items cannot be restored | Restore only identity-safe items, stop on conflict, retain metadata | Destination changed, capacity/identity uncertain, protected destination | Linked rollback receipt; never overwrite or delete conflict silently |
| Export | Destination unavailable | Allow another destination | Redaction or schema validation fails | No file rather than an unsafe raw fallback |

## Scan and read-only resilience

- Access denied is a coverage gap, not zero bytes and not a fatal whole-drive failure.
- A file disappearing during enumeration is expected churn; count it as changed/skipped and do not retry indefinitely.
- Reparse points are skipped by policy unless a later specialized scanner explicitly handles a tag without recursive traversal.
- Arithmetic is checked. Overflow or impossible metadata marks the affected aggregate invalid/unknown rather than wrapping.
- Cancellation is cooperative and bounded. A cancelled scan can expose a clearly labeled partial result, but cannot be saved or compared as a complete snapshot.
- Error collection is bounded by code/scope counters plus a small representative sample. An attacker cannot force one persisted/logged record per failing entry indefinitely.
- Progress remains monotonic only where the denominator is known; discovery growth must not be hidden behind false percentages.

## Rule and configuration failure

Rule-pack loading is transactional. Schema, manifest, hash/signature, compatibility, or protected-policy failures reject the pack rather than partially applying security-relevant fields. The last known-good built-in pack may remain available if its own integrity is verified. Unknown `ActionType`, `RiskLevel`, adapter ID, schema version, or field with safety meaning is a hard rejection.

Community-rule failure cannot disable protected resources, expand approved roots, lower a compiled risk floor, or make an executable adapter available. A rule description that cannot be safely rendered is escaped and treated as untrusted text.

## Planning, dry-run, and confirmation failure

- A plan is created only from a fully resolved displayed item set. Removing or adding a blocked item creates a new plan ID/digest; it is not an in-place correction.
- Plans expire ten minutes after creation. Clock ambiguity, target change, rule/adapter update, session change, or digest mismatch makes the plan stale.
- A protected target is `Prohibited`; it has no bypass or retry guidance beyond report-only/support information.
- A missing genuine tool preview does not permit executing the tool “to find out.” The action remains `report-only` or `manual-instructions`.
- Estimates preserve their qualification through UI, CLI, persistence, receipt, and export. An error cannot promote estimated to exact.

## Cleanup transaction state and errors

The future state model is:

```text
Planned -> Confirmed -> Validating -> Executing -> Verifying
-> Completed | PartiallyCompleted | Failed | RollbackAvailable | RolledBack
```

Rules for failure transitions:

- Persist and flush the journal before entering `Executing`.
- Validation, expiry, cancellation, UAC denial after dispatch, or protocol failure before mutation ends the attempt as `Failed` with zero attempted items. UAC denial before dispatch may leave the unmodified plan available until it expires.
- Once any target may have changed, failure/cancellation/disconnect cannot return to `Confirmed` or be called `Failed` without qualification; use `PartiallyCompleted` and reconcile.
- A verification failure after an apparently successful action is `PartiallyCompleted`, because outcome or measured recovery is not established.
- `RollbackAvailable` means a verified recovery path is currently available, not that rollback is guaranteed. A later rollback creates a linked transaction and reaches `RolledBack` only after verification.
- A failed/partial rollback remains `PartiallyCompleted`; the original receipt is immutable.
- State transitions are monotonic. Manual database edits, app restart, or receipt arrival cannot skip required validation/reconciliation.

## Retry policy

Automatic retry is deliberately narrow:

- Read-only metadata operations may retry at most twice for a known transient code, with jittered backoff and a total per-entry delay budget of 500 ms. Cancellation interrupts immediately.
- SQLite busy handling may wait/retry for at most two seconds for non-cleanup reads/writes. Failure to durably write pre-action journal state blocks mutation.
- Schema, validation, protected-resource, identity, version, authentication, digest, replay, expiry, and privacy-redaction errors are never retried automatically.
- IPC connection and UAC are never re-prompted automatically.
- Tool actions, file mutations, quarantine source removal, Recycle Bin submission, move cutover, verification, and rollback are never repeated automatically after ambiguous or partial outcome.
- An adapter may define an idempotent read-only probe retry. A mutating recovery retry requires a new observation, new typed recovery plan, and new confirmation.

## External-tool errors

Compiled adapters define supported versions, exact invocation, timeout, child-process policy, exit-code mapping, output limits, parser behavior, and postconditions. CLYR invokes no shell and does not include raw vendor output in a receipt. Output truncation is itself recorded and can make the result unverifiable.

Timeout or cancellation stops new dependent work and applies the adapter's reviewed process-tree policy. CLYR does not kill unrelated processes merely to unlock files. Unknown version, locale-dependent unparseable output, unexpected executable identity, or undocumented exit code makes the capability unavailable or the outcome partial; it never falls back to a guessed command.

## Persistence, disk-full, and crash handling

- SQLite migrations are transactional and backed up/recoverable according to the persistence design. An unrecognized newer schema opens read-only or refuses safely; it is never downgraded in place.
- Database integrity failure preserves the suspect file for diagnostics and creates no cleanup plan from untrusted state.
- Disk-full checks occur before journal/quarantine operations, but concurrent exhaustion is still handled. If journal durability is not certain, no mutation starts.
- App/helper crash recovery begins from journal plus actual filesystem state, not from the intended next state. Missing receipt means unknown outcome.
- Temporary export files are written in the destination directory where possible, flushed, validated/redacted, then atomically replaced. On failure, incomplete artifacts are removed only from CLYR-owned temporary names, never an existing user file.

## Privacy-safe presentation and logging

Default user messages answer: what failed, what remains trustworthy, whether anything changed, and what safe next step exists. They avoid blame and avoid fear language.

Default structured logs use correlation IDs, known-root/category tokens, counts, versions, error codes, and optionally digest prefixes. Personal path segments are redacted/tokenized at the boundary. Messages derived from filenames or tool output are escaped to prevent control-character, terminal, markup, or log injection. Secrets are redacted by value shape and known field classification before serialization.

Default exports contain aggregate errors and coverage counts. An explicit detailed local-only export may include additional path evidence only after warning and schema-level privacy classification; it still excludes credentials, tokens, contents, and transient IPC material.

## CLI outcome contract

The future CLI should use stable process outcomes in addition to structured JSON:

| Exit code | Meaning |
| --- | --- |
| `0` | Requested operation completed and its required verification passed |
| `2` | Valid partial result or `PartiallyCompleted`; inspect structured output |
| `3` | Cancelled before mutation or read-only operation cancelled |
| `4` | Invalid input, schema, or plan |
| `5` | Unsupported or unavailable capability |
| `6` | Access/elevation/identity denied without mutation |
| `7` | Operation failed; structured output states whether mutation may have begun |
| `10` | Unexpected internal failure caught at a safe boundary |

CLI JSON must carry the detailed state and version; callers must not infer “no mutation” from a nonzero code alone. These codes become a compatibility contract when first shipped and require a documented change thereafter.

## Residual risks

- Windows and vendor error codes can be ambiguous; postcondition verification may still yield unknown outcome.
- Concurrent disk activity can make free-space attribution inconclusive even after a successful action.
- A process crash at an OS/vendor boundary can leave work completed without a delivered receipt. Reconciliation reduces but cannot eliminate uncertainty.
- Redaction can miss novel sensitive patterns or over-redact useful data. Fixtures and user review of detailed exports remain necessary.
- Bounded retries and error samples can hide repeated individual details; aggregate counts and coverage must remain accurate.

## Acceptance criteria

- Every public/component error is versioned, machine-readable, privacy-filtered, and maps to one honest disposition/state.
- Scan fixtures prove access denied, locked, disappearing, cyclic/reparse, overflow, cancellation, and resource-limit behavior without writes.
- Invalid rules/packs load transactionally and cannot weaken policy through fallback.
- Plan and IPC failures never mutate, downgrade versions, alter confirmed targets, or auto-elevate.
- Crash tests exercise every cleanup state boundary and prove no silent destructive retry.
- Partial, unknown, and verification-failed outcomes cannot render as `Completed` or claim actual recovered bytes.
- Retry budgets, cancellation latency, output/log limits, disk-full behavior, and database-busy behavior are measurable and tested.
- External-tool fixtures cover unsupported identity/version, injection text, timeout, output flood, localized/unparseable output, and child-process behavior.
- Privacy tests inject usernames, project names, control characters, full paths, account IDs, access tokens, and hostile tool output through every error surface.
- CLI exit codes and JSON states agree, and documentation never claims these contracts are implemented before their delivery phase.
## Phase 5 planning failures

Planning distinguishes invalid selection, missing evidence, protected policy, unsupported source, integrity mismatch, stale binding, expired plan, changed target metadata, unsafe path, report-schema failure, and unavailable execution. Validation is fail-closed and returns typed diagnostics; it never repairs a plan, executes a fallback command, or mutates a target.
