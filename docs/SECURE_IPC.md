# CLYR Secure IPC Protocol

Status: Phase 0 protocol proposal for a future Phase 6 elevated helper. No endpoint or helper is implemented.
Security boundary: standard-user CLYR process to short-lived high-integrity `Clyr.ElevatedHelper`.

## Goals and non-goals

The protocol carries one already previewed and confirmed cleanup batch across the Windows integrity boundary. It must authenticate both local processes, bind every action to one immutable plan, resist tampering/replay/confused-deputy use, bound parser and work cost, report partial outcomes, and terminate cleanly.

It is not:

- a general RPC, filesystem, shell, process-launch, plugin, or administration API;
- a background service or reusable broker;
- authenticated merely because it uses `localhost`, a named pipe, an obscure name, or UAC;
- a way for community YAML to introduce elevated behavior;
- a transport for arbitrary executable names, command lines, environment blocks, wildcard targets, or unvalidated paths.

## Process launch and endpoint

The standard-user app or CLI is the named-pipe server; the elevated helper connects as the sole client. This lets the initiator create and lock down the endpoint before UAC launch and lets it compare the connecting client PID with the exact process returned by the UAC launch operation.

1. After confirmation and durable journal state, the initiator generates a cryptographically random 256-bit session ID and a fresh 256-bit app nonce.
2. It creates a first-instance, local-only named pipe whose opaque name includes only the session ID. The security descriptor permits the initiating user SID and required Windows system principals, rejects remote clients, and allows one connection. Pipe-name secrecy is defense in depth, not authentication.
3. It verifies the installed helper path and same-product identity, then launches that exact helper with Windows UAC. Command-line fields are limited to pipe/session identifier, initiator PID, expected session ID, and supported protocol range. No target path, personal data, plan body, reusable secret, or command appears there.
4. The initiator captures the returned helper process handle/PID. It accepts messages only from that pipe client's OS-reported PID.
5. Before sending plan metadata, each peer resolves the other peer's actual image and token from OS handles and validates PID, user SID, logon session, integrity level, image path, protected installation root, signer/package product identity, and compatible version.
6. The endpoint accepts one authenticated conversation and one execution request, then is destroyed. The helper exits after receipt or terminal error.

If the main app or helper is unsigned, portable, in a standard-user-writable installation, has ambiguous product identity, or cannot query peer identity reliably, the handshake fails and elevation is unavailable. Development uses a fake transport or isolated signed test installation.

Local TCP, HTTP, COM elevation monikers, a globally discoverable pipe, and an always-running service are not approved alternatives in this design.

## Transport and encoding

- Transport: Windows named pipe in byte mode, local clients only, one server instance, one client.
- Framing: unsigned 32-bit big-endian payload length followed by exactly that many bytes.
- Payload: UTF-8 JSON with no byte-order mark.
- Maximum frame/envelope: 4 MiB. Zero length, trailing bytes, multiple JSON roots, and frames above the maximum are rejected before allocation of the claimed size.
- Parsing: maximum depth 32; duplicate property names, invalid UTF-8/Unicode, non-integer numeric fields, non-finite numbers, unknown security-relevant fields, unknown enum values, and wrong JSON types are rejected.
- Sizes and counters: non-negative 64-bit integers with checked arithmetic. Timestamps: UTC RFC 3339 with explicit `Z`. IDs/nonces: constrained canonical encodings.
- Canonical digest: SHA-256 over the versioned canonical UTF-8 representation of all confirmation-relevant plan fields. Phase 5 must pin canonical property ordering, string escaping, and number encoding with golden vectors before implementation; no runtime-dependent object serialization may define identity implicitly.

The pipe's OS isolation and verified peer process identities provide channel access control. Message sequence, nonces, request IDs, canonical digests, and strict framing protect protocol integrity and detect replay/mix-up. A plain unkeyed hash is not represented as authenticating an untrusted peer.

## Version negotiation

Every envelope contains `envelopeVersion`, `messageType`, `sessionId`, `sequence`, and `sentAtUtc`. The handshake advertises a closed set or range of protocol versions. The helper selects the highest mutually supported stable version; no overlap terminates before plan disclosure.

Compatibility rules:

- the selected version fixes required fields, limits, enum spellings, digest rules, and unknown-field behavior;
- security-relevant unknown fields are rejected, never ignored;
- an older helper cannot receive a newer action capability merely because the outer envelope parses;
- capability version, action-contract version, rule/adapter version, and protocol version are checked independently;
- downgrade below the minimum bound into the confirmed plan is rejected;
- version negotiation occurs once and cannot change mid-session.

## Mutual-authentication handshake

Messages use monotonically increasing sequence numbers beginning at zero. The plan is not sent until the following handshake completes:

1. **`helper-hello`** — selected helper PID, supported protocol versions, session ID, fresh 256-bit helper nonce, helper build/product identity claim, and expected initiator PID.
2. The initiator validates the pipe client's OS-reported PID/token/image against the launched process and independently validates product identity. Claims in JSON are comparison data, never the source of truth.
3. **`initiator-challenge`** — selected protocol version, app nonce, echoed helper nonce, request ID, plan ID, plan digest, plan creation/expiry, and initiator build identity claim.
4. The helper validates the pipe server's OS-reported PID/token/image, same user/session, standard integrity expectation, protected image location, and same-product identity before accepting the challenge.
5. **`helper-ready`** — echoed request/plan IDs, digest, both nonce hashes, selected version, and a digest of the canonical handshake transcript.
6. The initiator verifies the transcript and peer state, atomically marks the confirmation/request as dispatched, then sends exactly one **`execute-request`**.

Transcript hashing detects implementation mix-ups and binds the subsequent request to this connection; peer authenticity comes from the exclusive OS channel plus process/token/image/product validation. Any mismatch, duplicate, unexpected message type, out-of-order sequence, clock/expiry failure, or second connection ends the session without mutation.

## Logical request schema

The Phase 5 JSON Schema must encode at least the following logical contract:

```text
ExecuteRequest
  envelopeVersion, protocolVersion, messageType, sessionId, sequence
  requestId, issuedAtUtc, expiresAtUtc
  initiatingUserSidBinding, logonSessionBinding
  planId, planSchemaVersion, planDigest, planCreatedAtUtc, planExpiresAtUtc
  productVersion, rulePackManifestDigest
  items[]

ActionItem
  itemId, actionType, riskLevel, actionContractVersion
  ruleId, ruleVersion, compiledCapabilityId, adapterVersion?
  requiresElevation = true
  consequenceCode, rollbackClassification
  approvedRootDescriptors[]
  targetDescriptors[]
  typedArguments?
  dependencies[], expectedEffect, evidenceDigest

TargetDescriptor
  targetId, targetKind, canonicalPath
  volumeIdentity, stableFileIdentity, objectType, reparseState
  expectedLogicalBytes?, expectedAllocatedBytes?
  observedSizeKind, observedTimestamps, cloudOrEncryptionState
```

`canonicalPath` is not a free-form privileged path API: it is one field inside a capability-specific descriptor, already bound to approved roots, evidence, rule/adapter version, plan digest, and confirmation. The helper independently resolves and validates it. A path without all required capability binding is rejected.

The exact `actionType` values are:

- `report-only`
- `open-settings`
- `recycle-files`
- `quarantine-files`
- `trusted-tool-command`
- `windows-supported-cleanup`
- `move-known-folder`
- `manual-instructions`

Only capabilities whose compiled registry says they require elevation may appear in `execute-request`; informational actions stay in the standard process. `RiskLevel` is exactly `Informational`, `Low`, `Medium`, `High`, or `Prohibited`. `Prohibited` is always rejected. The initial Phase 6 helper allowlist must not contain `High` actions.

Tool actions contain an immutable adapter ID and typed fields defined by that adapter. They never contain an executable path chosen by the user/rule, a combined command line, shell text, metacharacter-bearing escape hatch, current directory, environment block, or unrestricted argument array.

## Request limits and expiry

- One `execute-request` per helper lifetime; no pipelining, streaming mutation requests, or add-on items.
- Maximum 128 plan items and 1,024 target descriptors across the batch.
- Maximum JSON nesting depth 32, string value 32 KiB, ordinary diagnostic text 8 KiB, and total envelope 4 MiB.
- Pipe connection deadline: 30 seconds after helper launch.
- Handshake deadline: 10 seconds after connection; idle handshake/request timeout: 30 seconds.
- Plan lifetime: at most ten minutes from creation. Request expiry is the earlier of plan expiry and 60 seconds after helper launch.
- Each compiled adapter has a shorter operation-specific timeout where feasible; initial absolute helper lifetime ceiling is 15 minutes. A capability needing more requires a documented security/performance review.
- Status events are bounded and rate-limited; they cannot extend plan/request authorization or add targets.

Oversize work is not split automatically into newly authorized batches. The standard process must create a new dry run and obtain new confirmation for any different plan.

## Complete revalidation before mutation

After decoding the full request, the helper validates every item before changing anything:

1. schema, message, version, limit, nonce, sequence, request ID, expiry, user/session, plan digest, and one-time state;
2. known compiled capability and exact `ActionType`, `RiskLevel`, rule/adapter version, dependency order, argument types, and privilege requirement;
3. approved root and protected-resource policy using helper-owned code/data;
4. absolute local canonical target, component-aware containment, final opened path/volume, stable object identity, type, reparse state, material size/timestamp state, and cloud/encryption restrictions;
5. tool executable identity/version/signature/location and exact typed argument mapping where applicable;
6. durable pre-action journal assertion, rollback metadata, destination capacity/safety margin, and other capability preconditions.

One failure rejects the entire batch before mutation. The helper does not ask the standard process to “fix” security fields in place, accept a reduced target set under the old digest, or fall back to a broader mechanism. Replanning requires a new dry run and confirmation.

Immediately before each item, the executor repeats volatile identity and protected-resource checks. A change after batch validation stops that item and, by default, the remainder of the batch.

## Response and receipt schema

The helper emits bounded progress/status events and one terminal response:

```text
ExecutionReceipt
  envelopeVersion, protocolVersion, messageType, sessionId, sequence
  requestId, planId, planDigest
  helperProductIdentity, helperVersion
  startedAtUtc, finishedAtUtc, terminalState
  itemReceipts[]
  bytesBefore?, bytesAfter?, measuredDelta?, measurementQualification
  receiptDigest

ItemReceipt
  itemId, targetId(s), actionType
  state, validationOutcome, attemptedAtUtc?, verifiedAtUtc?
  actualEffect, rollbackState, normalizedErrorCode?
  identityChanged, skippedReason?, measuredBytes?
```

Terminal state is one of `Completed`, `PartiallyCompleted`, or `Failed`; recovery may later add a separate `RolledBack` receipt linked to the original. A receipt never substitutes estimated bytes for measured bytes. It contains normalized codes and bounded privacy-safe details, not stack traces, raw personal paths, access tokens, or unbounded external-tool output.

The receipt digest binds request/plan IDs, helper identity, terminal state, and item results. The standard process verifies framing, peer/session, plan digest, receipt digest, and item coverage before persistence. It then performs independent postcondition and free-space reconciliation where possible. Missing receipt is “outcome unknown,” not success.

## Cancellation, disconnect, and crash behavior

- **Before mutation:** an authenticated `cancel-request` with matching request/plan digest makes the helper reject the batch and return `Failed` with zero mutation and a normalized cancellation reason.
- **During execution:** cancellation is observed only at capability-defined safe points. The helper starts no new item, does not silently undo a partially completed vendor operation, and returns exact attempted/skipped/unknown states.
- **Disconnect:** starts no further work. Closing the pipe is not authorization to retry or a guarantee of rollback. The helper reaches the nearest safe item boundary, writes/returns whatever local evidence the approved design permits, and exits.
- **Helper crash:** the initiator marks the attempt outcome unknown and enters journal reconciliation. It does not launch another helper with the old confirmation.
- **Initiator crash:** the helper does not stay alive for another client. It stops starting work, reaches a safe boundary, and exits. Next launch reconciles journal and actual state.
- **Timeout:** treated like disconnect/cancellation according to whether mutation began. No destructive request is automatically resent.

Retries are allowed only after a new observation and plan unless a capability proves an idempotent, non-destructive recovery operation. Recovery actions receive their own typed contract and receipt.

## Audit and privacy

Before UAC launch, the standard process persists and flushes a journal entry binding request ID, plan digest, confirmation, and expected helper capability. The helper does not treat this user-writable record alone as authorization; live peer/product identity and full request validation remain mandatory.

Default logs record correlation IDs, root/category tokens, normalized error codes, counts, versions, digest prefixes, and states. They exclude complete personal paths, filenames, user SID text in exportable form, target contents, tool raw output, command-line secrets, and nonce values. Exact path/identity metadata needed for local quarantine recovery is access-restricted, stored separately, retained only as long as needed, and omitted from default exports.

## Error policy

Protocol failures are terminal and fail closed. Stable categories include incompatible version, malformed envelope, limit exceeded, peer identity mismatch, user/session mismatch, replay, expired/stale plan, digest mismatch, unknown capability, protected resource, changed target, unsupported tool, precondition failure, cancelled, partial execution, and internal failure. Detailed diagnostics are local and privacy-filtered; UI text does not expose exploitable parser detail.

The helper never responds to a malformed unauthenticated peer with plan or target information. Authentication and schema errors are rate-limited and the process exits.

## Residual risks

- A same-user attacker may repeatedly race the pipe to cause denial of service. PID binding prevents authorization but cannot guarantee availability.
- OS process/signature/package APIs and deployment modes can differ. A mode without reliable mutual identity has no elevated capability.
- A process with administrator or kernel control can subvert peer checks and is outside the application boundary.
- Canonical JSON/digest implementations can diverge. Golden cross-process vectors and duplicate-key fuzzing are mandatory before use.
- A valid compiled adapter can contain a bug. Protocol validation narrows input but cannot prove the adapter's external effect.
- A crash can lose the response after mutation. Durable journal and reconciliation reduce uncertainty but cannot always produce an immediate definitive receipt.

## Acceptance criteria

Implementation is not eligible for real mutation until tests show that:

- the endpoint is first-instance, local-only, single-client, restrictively ACLed, non-persistent, and closed after one request;
- both peers independently verify OS-reported PID, SID/session, integrity, image, protected install root, and same-product identity before plan disclosure;
- malformed, duplicate-key, oversized, deeply nested, out-of-order, unknown-version, expired, replayed, cross-session, and digest-mismatched messages fail closed;
- golden canonicalization/digest vectors match app and helper implementations;
- one invalid batch item prevents all mutation, and each item is revalidated again immediately before action;
- no request field can become an arbitrary command, executable, argument list, environment, wildcard, or unbounded path capability;
- count, size, connection, handshake, idle, adapter, output, and process-lifetime limits are enforced without excessive allocation;
- UAC denial, explicit cancellation, pipe loss, app/helper crash, and receipt loss have tested journal/reconciliation outcomes and no automatic destructive retry;
- receipts cover every requested item and distinguish measured, estimated, unknown, skipped, failed, partial, and completed outcomes without sensitive-data leakage;
- IPC schema property/fuzz tests and wrong-peer integration tests pass on every supported packaging mode;
- a security review confirms the shipped helper allowlist matches the documented initial Phase 6 scope.
## Phase 5 status

No endpoint, transport, handshake, helper binary, elevation launch, request, receipt, journal, or IPC parser is implemented. Phase 5 produces only an integrity-checked plan model that a separately approved Phase 6 design would have to revalidate. A digest alone is neither execution authorization nor peer authentication.
