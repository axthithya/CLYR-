# ADR 0015: Developer tool storage is reported through the existing CleanupCandidate model, not a parallel one

- Status: Implemented in Phase 7
- Date: 2026-07-17
- Scope: `Clyr.Contracts.DeveloperMode`, `Clyr.Core.DeveloperMode.DeveloperToolReportBuilder`

## Context

Phase 7 needs a typed, closed catalogue of ~14 developer tool families (Docker, WSL, Node/npm, pnpm, Yarn,
.NET/NuGet, Gradle, Maven, Python/pip, Rust/Cargo, Flutter/Dart, Android SDK, Playwright, generic build output)
and a way to group their storage findings, show per-tool status, and — where a finding is already eligible —
let the user route it into a plan. Phase 5 already has a complete, reviewed model for exactly that last part:
`CleanupCandidate`, `CleanupEligibility`, `RiskLevel`, `FindingConfidence`, `CleanupConsequence`, and the
`CleanupCandidateFactory.FromScan`/`FromSnapshot` construction path that derives all of it from a rule finding.

A tempting alternative was a parallel `DeveloperFinding` model (its own eligibility/risk/consequence fields)
that Developer Mode would translate into a `CleanupCandidate` only at the moment a plan is created. That would
duplicate the eligibility decision logic (`CleanupCandidateFactory.Decide`/`Risk`/`Consequence`) in a second
place, with the attendant risk that the two copies drift — a developer finding could show as safe in the
Developer Mode dashboard while Phase 5/6's own plan pipeline would refuse to make it `DryRunEligible`, or vice
versa.

## Decision

`DeveloperToolReport.Candidates` is `ImmutableArray<CleanupCandidate>` — the exact same type Review Plan and
the `plan` CLI commands already consume. `DeveloperToolReportBuilder.FromScan`/`FromSnapshot` call the existing
`CleanupCandidateFactory.FromScan`/`FromSnapshot` to obtain the full, already-decided candidate list, then only
add a grouping step on top: each candidate's `FindingId` is mapped back to its source rule ID (a
`Dictionary<findingId, ruleId>` built from the original `StorageFinding`/`SnapshotFinding` list, since
`CleanupCandidateFactory` does not expose rule ID on non-actionable candidates), and `DeveloperToolTaxonomy`
(`Clyr.Core.DeveloperMode.DeveloperToolTaxonomy`) — a closed, compiled `ImmutableDictionary<ruleId, DeveloperToolId>`
covering every developer-related built-in rule — decides which `DeveloperToolReport` each candidate belongs to.

`DeveloperStorageCategory` (in `Clyr.Contracts.DeveloperMode`) is an additive, display-only vocabulary layered
on top of the existing `StorageCategory` enum for narrower dashboard labelling (e.g. distinguishing a
`ContainerBuildCache` from a `ContainerVolume`, both of which map to the shared `StorageCategory.Containers`).
`StorageCategory` itself is not modified, edited, or extended — avoiding schema/compatibility risk to every
existing Phase 2–6 consumer of that enum.

Developer Mode's "Review in plan" action (`DeveloperModeViewModel.CreatePlanAsync`, `clyr developer plan`) calls
the exact same `CleanupCandidateFactory.FromSnapshot` and `CleanupPlanBuilder.Create` used by every other
snapshot-derived finding — there is no second plan-construction path for developer findings.

## Consequences

- Developer Mode cannot show a finding as executable that Phase 5/6's own eligibility logic would reject, and
  cannot miss a genuinely eligible finding either — both surfaces read the same decision, computed once.
- Adding a new developer rule automatically flows through the existing plan/execution pipeline with zero new
  code in `CleanupCandidateFactory`; the only new code required is one taxonomy entry mapping the rule ID to a
  `DeveloperToolId`/`DeveloperStorageCategory`.
- The taxonomy is a closed, compiled dictionary, not rule-pack-declared data — a rule pack cannot introduce a
  new developer tool category or silently change which tool a finding is attributed to.
- The cost is a small amount of indirection (recovering `ruleId` from `findingId` via a side dictionary) that a
  parallel model would not need — judged acceptable to avoid duplicating the eligibility decision.
