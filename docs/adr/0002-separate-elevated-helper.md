# ADR 0002: Separate Short-Lived Elevated Helper

- **Status:** Accepted for planned Phase 6; no helper is implemented in Phase 0
- **Date:** 2026-07-10
- **Decision owners:** Security architecture and Windows platform

## Context

CLYR's broadest input surface—filesystem names and metadata, rule data, reports, persistence, and external-tool output—does not need administrator rights for its primary read-only purpose. Some narrowly supported future Windows cleanup mechanisms may require higher integrity. Elevating the desktop app or CLI would place UI, parsers, rules, database, and scan orchestration inside the privileged attack surface. A persistent service would create a long-lived local administration endpoint and lifecycle burden.

Windows UAC proves only that a user consented to start a process with a higher-integrity token. It does not prove that a cleanup target is safe, current, previewed, or authorized by CLYR's action policy.

## Decision

The WinUI app and CLI run `asInvoker` at the initiating user's standard integrity level. They never require elevation at startup and do not elevate themselves in place.

Beginning no earlier than Phase 6, CLYR may ship a separately built `Clyr.ElevatedHelper` for a small compiled allowlist of capabilities that have proven elevation requirements. The helper:

- is launched through UAC only after a non-mutating dry run and digest-bound CLYR confirmation;
- runs only from a signed same-product identity in an administrator-protected installation; unsigned, portable, user-writable, or identity-ambiguous builds cannot elevate;
- connects to one unpredictable, restrictive, local-only named pipe created by the initiator;
- mutually validates peer PID, user SID, logon session, integrity, executable image, product/package/signer identity, and compatible protocol;
- accepts one versioned, expiring, nonce-protected, typed request bound to one immutable confirmed plan;
- revalidates the complete batch and every volatile target immediately before action;
- uses only compiled action/capability contracts and exact typed adapter arguments;
- returns a structured execution receipt, clears transient state, and exits.

There is no CLYR Windows service, scheduled task, startup entry, persistent listener, generic elevated filesystem API, shell interface, or background helper.

The helper rejects `Prohibited` actions, protected resources, arbitrary/free-form commands, scripts, executable paths, command lines, environment blocks, unknown rule/adapter IDs, unsupported versions, wildcards, free-form paths outside capability-specific descriptors, changed targets, replayed/stale requests, and any request whose product identity cannot be established. Administrator access never overrides these denials.

## Initial capability boundary

The first execution phase may consider only:

- Recycle Bin operations for explicitly selected eligible files in disposable test or user-owned locations;
- deletion or quarantine of CLYR-owned test data;
- selected supported cache-clean operations with a compiled adapter and exact allowlisted arguments.

This ADR does not approve those operations by itself; each still requires its own evidence, fixtures, `RiskLevel`, dry run, consequence/rollback UX, and phase acceptance. Broad Windows cleanup, virtual-disk operations, arbitrary user content, permanent deletion, and direct protected-path mutation remain unavailable.

## Consequences

- Most CLYR code and untrusted parsing remain at standard integrity.
- The privileged attack surface is a small executable and protocol that can be fuzzed and capability-reviewed.
- Users may see UAC only when an exact approved action requires it, after seeing CLYR's richer consequence preview.
- The app must maintain two binaries, strict shared contracts, signing/package identity, secure IPC, crash reconciliation, and multi-session tests.
- A helper launch adds latency and UAC denial becomes a normal handled outcome.
- Privileged cleanup cannot work in distribution modes that do not establish trustworthy same-product identity and protected installation.
- Separation reduces but does not eliminate risk: a helper or compiled adapter bug can still have severe impact.

## Alternatives considered

- **Run the whole app/CLI as administrator:** rejected; it unnecessarily elevates UI, scanning, rule parsing, export, database, and third-party input surfaces and encourages routine privileged use.
- **Elevate the same executable on demand:** rejected; it preserves the large code surface and makes privileged/nonprivileged modes easy to confuse.
- **Install a Windows service or persistent broker:** rejected; it adds an always-present local attack surface, service ACL/update lifecycle, and broader confused-deputy risk before a demonstrated need.
- **Use a generic OS/shell command broker:** rejected as `Prohibited`; action safety cannot be enforced over arbitrary command or path strings.
- **Never support elevated actions:** safest and adequate for early read-only phases, but it would prevent a later small set of supported Windows cleanup mechanisms. If secure identity/IPC gates cannot be met, this remains the fallback.

## Security and operational constraints

- The helper loads no community code/rules/plugins and discovers no executable through the current directory or user `PATH`.
- It does not enable take-ownership, generic backup/restore, debug, or similar broad privileges as a shortcut; it never changes ACLs, takes ownership, decrypts, hydrates, kills processes, or loads drivers.
- One invalid item rejects the whole privileged batch before mutation. After execution begins, a changed target or failure stops at the next safe boundary and produces `PartiallyCompleted` when any effect is possible.
- The action journal is durable before mutation. Disconnect/crash/timeout never triggers an automatic destructive retry.
- Endpoint, schema, limits, expiry, cancellation, receipt, and recovery behavior are specified in `docs/SECURE_IPC.md`; process/token rules are in `docs/PRIVILEGE_MODEL.md`.

## Validation

Before enabling a real helper, Phase 6 must prove:

- app and CLI remain standard-user and cannot execute cleanup when launched elevated outside the broker flow;
- app/helper installation and dependencies are not standard-user writable;
- wrong PID, SID/session, integrity, image, signer/package/product, version, nonce, plan digest, expiry, sequence, and request replay all fail before mutation;
- malformed/oversized IPC and malicious typed-action input survive property/fuzz testing;
- path traversal, device aliases, sibling prefixes, alternate streams, same-path replacement, junction/symlink/mount swaps, and protected resources are rejected;
- UAC denial, cancellation, disk full, app/helper crash, disconnect, tool timeout, partial result, and lost receipt reconcile without automatic retry;
- no arbitrary command, program, argument array, environment, wildcard, or generic path API is reachable;
- every capability in the shipped helper matches the reviewed allowlist and uses only disposable fixtures during automated testing.

Failure of any identity, protected-resource, path/target integrity, plan integrity, journal durability, or arbitrary-execution gate keeps elevation disabled.

## Revisit triggers

Revisit this ADR before adding a service, persistent listener, unpackaged/portable elevation, a new token privilege, remote endpoint, cross-user action, or action outside the initial capability boundary. A superseding ADR must compare safety and lifecycle impact and cannot silently weaken existing denials.
