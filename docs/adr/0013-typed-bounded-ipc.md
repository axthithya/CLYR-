# ADR 0013: Typed, bounded, versioned IPC between CLYR and the elevated helper

- Status: Implemented in Phase 6
- Date: 2026-07-17
- Scope: `Clyr.Contracts.ExecutionIpc`, `Clyr.Core.Execution.ElevatedHelperIpc` / `HelperIpcSerializer`

## Context

ADR-0002 required the helper to accept only "one versioned, expiring, nonce-protected, typed request" and to
expose no generic command, script, or path surface over IPC. This ADR records the concrete protocol shape and
transport chosen to satisfy that.

## Decision

- **Contracts.** `HelperRequest` and `HelperResponse` are closed `sealed record` types with concrete,
  non-polymorphic properties: protocol version, request ID, nonce, session ID, user SID, drive identity, action
  ID, trusted root identity/path, plan ID/digest, token expiry, and an `ImmutableArray<HelperTargetManifestItem>`
  manifest bounded to `HelperProtocol.MaxManifestItems` (512). There is no command field, script field,
  executable-path field, or unrestricted argument list — the type system itself forecloses that surface, not
  just a runtime check.
- **Serialization.** `HelperIpcSerializer` uses `System.Text.Json`'s default reflection contract. Because every
  serialized type is a concrete sealed record, there is no polymorphic type discriminator anywhere in this
  protocol — .NET's `System.Text.Json` has no equivalent of a `TypeNameHandling`-style unsafe deserialization
  mode unless one opts into `[JsonDerivedType]`, which this code never does. Every message is additionally
  bounded to `HelperProtocol.MaxMessageBytes` (256 KiB), checked both before serializing and after reading a
  length-prefixed frame off the wire.
- **Transport.** A Windows named pipe (`ElevatedHelperIpc`), with a random 128-bit hex pipe name generated fresh
  per request (`NewPipeName`), a `PipeSecurity` ACL restricted to the current Windows user, exactly one server
  instance (`maxNumberOfServerInstances: 1`), and an overall request timeout
  (`HelperProtocol.RequestTimeout`, 30s). The server accepts one connection, reads one frame, writes one frame,
  and returns — there is no way to send a second request down the same pipe, and the helper process exits
  immediately after, so there is nothing left to replay against even within one run.
- **Peer validation.** The pipe ACL restricts the connecting identity to the current Windows user; the request
  itself carries the session ID and user SID that the helper's handler cross-checks against the built-in
  allowlist and live filesystem state (see ADR-0012). There is no separate handshake exchanging executable
  identity/signature today — see Consequences.

## Consequences

- Because there is no polymorphic surface and no command/script/path field in the wire format, a hostile peer
  that could write to the pipe has nothing to redirect into arbitrary execution — the worst it can do is send a
  well-formed request naming a target outside the trusted root, which `ExecutionTargetProcessor` rejects.
- The 256 KiB / 512-item bounds mean a malicious or buggy caller cannot exhaust memory or CPU by sending an
  unbounded manifest; oversized frames are rejected before full deserialization.
- What is *not* yet implemented: explicit verification of the helper binary's own identity/signature by the
  client, or of the client's identity by the helper beyond the pipe ACL and same-user constraint; protocol
  downgrade/replay fuzzing beyond the two tests that exist (`HelperIpcTests.cs`); a forged-completion-response
  test. These remain open items for Phase 6 completion (see `docs/PHASE6_EXECUTION.md`).
- A real fixture-only UAC smoke test — launching the helper through an actual Windows elevation prompt and
  exchanging a real request/response — has not been performed in this environment (no interactive session
  available); see `scripts/run-phase6-uac-smoke.ps1`.
