# ADR 0017: A closed, compiled developer tool registry with an honest, never-false-negative status machine

- Status: Implemented in Phase 7
- Date: 2026-07-17
- Scope: `Clyr.Core.DeveloperMode.DeveloperToolRegistry`, `Clyr.Contracts.DeveloperMode.DeveloperToolStatus`

## Context

Developer Mode reports on tools it cannot fully observe: a folder being absent does not prove a tool is not
installed (it may store data elsewhere, in a location this phase does not scan, or the user may simply not have
used it yet), and a probe failing does not prove the tool is broken (it may be a permissions issue, a
version this phase's argument set does not support, or a transient failure). Reporting `NotInstalled` too
eagerly is a factual claim CLYR cannot back up from a single missing folder, and the spec explicitly forbids it:
"Do not report a tool as absent merely because one expected folder is missing."

Equally, the set of tools itself must not be open-ended: an earlier design considered letting the rule pack
declare new "developer tool" categories, which would let a future rule-pack update silently add a fifteenth
tool family with no corresponding review of its safety classification, taxonomy mapping, or (for probe-capable
tools) argument allowlist.

## Decision

- `DeveloperToolRegistry.Descriptors` is a compiled, `static readonly` `ImmutableArray<DeveloperToolDescriptor>`
  — exactly fourteen entries, not data-driven, not extensible by a rule pack or any runtime configuration.
  Adding a fifteenth tool requires a code change and a new release, the same bar as adding a new
  `BuiltInExecutionActions` entry in Phase 6.
- `DeveloperToolStatus.NotInstalled` is reachable only through `DeveloperToolRegistry.ProbeAsync` failing to
  locate a trusted executable for a probe-capable tool (Docker, WSL) — i.e., only when CLYR actively tried to
  find the tool through a real, reviewed discovery path and failed. For every non-probe tool (the other twelve),
  the absence of classification evidence produces `DeveloperToolStatus.Unavailable` ("no scan evidence yet"),
  never `NotInstalled` — a `NonProbeToolsWithoutEvidenceReportUnavailableNeverNotInstalled`-style test in
  `DeveloperToolRegistryTests` enforces this as an invariant, not just a convention.
- `DeveloperToolRegistry.DetectAllAsync` always returns exactly one `DeveloperToolReport` per descriptor, in
  registry order — a tool is never silently omitted from the dashboard because it happened to have zero
  findings; it is shown with an honest `Unavailable`/`InstalledNoData` status and a diagnostic explaining why.
- Every other status (`PartiallyDetected`, `InstalledNoData`, `PermissionLimited`, `UnsupportedVersion`,
  `ProbeFailed`) is a distinct, named outcome rather than collapsing everything uncertain into a generic
  "unknown" — so a user can distinguish "the probe itself failed" from "the tool has no cache yet" from "we
  found some evidence but couldn't confirm the tool's own status."

## Consequences

- The dashboard can truthfully say less than a user might want (e.g. "no evidence yet" for a tool that actually
  is installed but has no matching classification finding and no probe defined) rather than guessing.
- Every status transition is deterministically testable without a real Docker/WSL installation
  (`DeveloperToolRegistryTests` uses `FakeLocator`/`FakeProbeRunner`), while the two probe-capable tools are
  additionally verified against the real local machine in this session.
- A future tool addition is deliberately friction-ful (code change, taxonomy entry, descriptor entry, tests) —
  judged correct for a closed list whose entries each carry their own safety classification and (for two of
  them) a hard-coded process-launch argument list that must be reviewed, not just registered.
