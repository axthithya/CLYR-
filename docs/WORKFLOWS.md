# Workflows

These workflows describe planned end-to-end behavior. Solid arrows apply to read-only phases; cleanup, elevation, rollback, and release steps are future gated designs and are not implemented in Phase 0.

## Scan

```mermaid
flowchart TD
    A[Select eligible volume and mode] --> B[Create scan session]
    B --> C[Discover volume capabilities]
    C --> D[Enumerate entries as a bounded stream]
    D --> E{Cancelled or volume removed?}
    E -->|Cancelled| P[Persist or return explicitly partial result]
    E -->|Removed| R[Return DriveRemoved partial state]
    E -->|Continue| F{Excluded, reparse point, or unsupported?}
    F -->|Yes| G[Record skipped reason and safe metadata]
    F -->|No| H[Read metadata without opening content]
    H --> I[Normalize and aggregate]
    G --> I
    I --> J[Resolve accounting and overlap]
    J --> K[Apply detection-only rules]
    K --> L[Protected policy wins]
    L --> M[Generate findings and coverage]
    M --> N[Persist bounded aggregate snapshot]
    N --> O[Render Why is this drive full?]
```

Source: `docs/diagrams/scan-workflow.mmd`.

## Finding generation

```mermaid
flowchart TD
    A[Metadata observation or aggregate] --> B[Normalize category and accounting evidence]
    B --> C[Evaluate bounded detection rules]
    C --> D[Build candidate evidence]
    D --> E{Protected resource or policy prohibition?}
    E -->|Yes| F[Protected and Prohibited]
    E -->|No| G[Resolve overlapping findings deterministically]
    G --> H[Assign confidence and risk]
    H --> I{Evidence sufficient?}
    I -->|No| J[Unknown or Review required]
    I -->|Yes| K[Report-only candidate]
    F --> L[Explain decision, evidence, and action availability]
    J --> L
    K --> L
```

Protection is evaluated after trusted path/capability normalization and before display or any future plan. Size and age are evidence attributes, not safety proof.

## Snapshot and “What grew?”

```mermaid
flowchart LR
    Old[Older aggregate snapshot] --> Compat{Compatible schema, volume, and accounting methods?}
    New[Newer aggregate snapshot] --> Compat
    Compat -->|No| Explain[Show unsupported comparison reason]
    Compat -->|Yes| Coverage{Coverage comparable?}
    Coverage --> Delta[Compute category, directory-token, and finding deltas]
    Delta --> Rank[Rank growth with confidence]
    Coverage -->|Material mismatch| Warn[Attach uncertainty and do not claim exact growth]
    Warn --> Rank
    Rank --> View[Render What grew?]
```

Incomplete scans can be compared only with visible coverage caveats. The future USN provider accelerates observation but never replaces a complete fallback.

## Cleanup dry run — Phase 5 only

```mermaid
flowchart TD
    A[User selects eligible findings] --> B[Resolve exact candidate targets]
    B --> C[Validate rule pack, protected policy, containment, and overlap]
    C --> D[Capture stable identity and consequences]
    D --> E[Create immutable digest-bound plan with ten-minute expiry]
    E --> F[Dry-run every typed item against fake or read-only adapters]
    F --> G[Show target set, bytes, evidence, risk, privilege, consequence, rollback]
    G --> H{User changes selection, evidence changes, or plan expires?}
    H -->|Yes| I[Discard and generate a new plan]
    H -->|No| J[Explicit confirmation may be recorded]
    J --> K[Stop: Phase 5 has no real executor]
```

## Cleanup execution — Phase 6 or later

```mermaid
flowchart TD
    A[Confirmed unexpired plan] --> B[Persist action journal before mutation]
    B --> C[Revalidate complete plan, capability, identity, links, roots, free space]
    C --> D{Any mismatch or prohibited target?}
    D -->|Yes| E[Fail closed and require a new plan]
    D -->|No| F{Elevation required?}
    F -->|No| G[Execute typed standard-user adapter]
    F -->|Yes| H[Authenticate short-lived helper and send bounded typed batch]
    G --> I[Record per-item outcome]
    H --> I
    I --> J[Verify target and free space]
    J --> K[Write immutable receipt]
    K --> L{Complete, partial, failed, or rollback available}
```

No permanent-delete action exists. Partial completion is never summarized as all-or-nothing success.

## Elevation — Phase 6 or later

```mermaid
sequenceDiagram
    participant U as User
    participant A as Standard-user app
    participant H as Elevated helper
    participant W as Approved Windows API
    U->>A: Confirm exact immutable plan
    A->>A: Verify expiry, digest, user, session, targets
    A->>H: Launch signed same-product helper via UAC
    H->>A: Authenticated endpoint challenge
    A->>H: Versioned request, nonce, capability, digest
    H->>H: Validate caller/session/executables/replay/full batch
    alt validation fails or UAC denied
        H-->>A: Structured rejection
    else valid
        H->>W: Exact allowlisted typed operations
        W-->>H: Per-item outcomes
        H-->>A: Integrity-bound execution receipt
    end
    H->>H: Erase transient material and exit
```

A pipe/localhost endpoint alone is not authentication. The helper never listens persistently and accepts no free-form command or path outside the validated action contract.

## Quarantine and rollback — Phase 6 or later

```mermaid
flowchart TD
    A[Eligible confirmed target] --> B{Same volume?}
    B -->|Yes| C[Move into product-owned quarantine]
    B -->|No| D[Check destination capacity and filesystem]
    D --> E[Copy with proportional verification]
    E --> F{Copy verified and identity unchanged?}
    F -->|No| G[Keep source; record failure]
    F -->|Yes| H[Remove source using approved action]
    C --> I[Persist original path, identity, metadata, rule, plan, expiry]
    H --> I
    I --> J[Expose restore or new explicit disposal plan]
    J --> K{Restore requested and destination safe?}
    K -->|Yes| L[Restore with conflict handling and verify]
    K -->|No| M[Retain or report manual resolution]
```

Expiry never silently deletes quarantined content. Credentials, protected/system content, EFS, cloud content that would hydrate, and unknown application databases are ineligible without specialized review.

## Rule contribution

```mermaid
flowchart LR
    A[Proposal and synthetic fixture] --> B[Schema and bounded-parser validation]
    B --> C[Protected-path and malicious-input tests]
    C --> D[Evidence, privacy, overlap, and false-positive review]
    D --> E[Maintainer and sensitive-owner review]
    E --> F[Merge as versioned report-only rule]
    F --> G[Build manifest hashes and optional later signature]
    G --> H[Local schema, hash, compatibility, and policy validation]
    H --> I[Detection and explanation]
    I -. Phase 5+ approved adapter reference .-> J[Plan validation]
    J -. Phase 6+ .-> K[Execution receipt]
```

## Release — Phase 9 or later

```mermaid
flowchart TD
    A[Protected branch and reviewed commit] --> B[Restore from lock and pinned sources]
    B --> C[Format, Release build, all tests, schemas, architecture]
    C --> D[Security, secret, dependency, license, SBOM, provenance checks]
    D --> E[Accessibility, performance, privacy, clean-machine lifecycle tests]
    E --> F{All release gates pass?}
    F -->|No| G[No release; record exact failure]
    F -->|Yes| H[Build single-project MSIX]
    H --> I[Sign with protected identity]
    I --> J[Verify signature, contents, version, offline install/upgrade/uninstall]
    J --> K[Create traceable release notes and artifacts]
    K --> L[Publish with rollback/revocation plan]
```

No release workflow exists in Phase 0. Tags, publication, Store/WinGet submission, and update activation require explicit authorization and Phase 9 gates.

## Workflow acceptance criteria

- Every diagram distinguishes current read-only work from future actions.
- Cancellation, partial results, expiry, validation failure, elevation denial, and partial execution have explicit paths.
- Protected policy and identity validation cannot be bypassed by a rule or confirmation.
- User confirmation does not authorize changed targets.
- Release publication cannot occur after a failed gate.
