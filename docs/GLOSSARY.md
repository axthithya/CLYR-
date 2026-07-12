# Glossary

Terms are normative where they match schemas/contracts. UI copy may use plainer text but must preserve the distinction.

| Term | Meaning |
|---|---|
| Action adapter | Compiled first-party code that implements one immutable, version-bounded, typed operation. Not a generic process/shell runner. |
| Action type | One of `report-only`, `open-settings`, `recycle-files`, `quarantine-files`, `trusted-tool-command`, `windows-supported-cleanup`, `move-known-folder`, or `manual-instructions`. Phase 0 rules permit only `report-only`. |
| Allocated bytes | Physical filesystem allocation reported by a named supported method; not automatically equal to file length or reclaimable space. |
| Capability | Behavior proven available for the detected OS/filesystem/media/package/identity/API/tool and enabled release phase. |
| C:-first | UX centered on the common full-C: problem. Technical logic still discovers the actual system volume and can evaluate other eligible local volumes. |
| Cloud placeholder | A filesystem entry whose logical content may not be resident locally. Standard scanning must not hydrate it. |
| Complete scan | The selected supported scope reached its terminal pipeline state. It can still contain declared exclusions/inaccessible regions; coverage remains visible. |
| Confidence | Evidence strength: Confirmed, High, Medium, Low, Unknown. It is independent of risk and size. |
| Cleanup plan | Future immutable, digest-bound, expiring collection of typed items, evidence, consequences, privilege and rollback facts. It grants no authority after any material change. |
| Coverage | What the scan could and could not observe, including providers, exclusions, skipped/inaccessible counts and uncertainty. |
| Deep Analysis | Explicit read-only mode with finer bounded detail and optional capability-based allocation/link or duplicate-candidate work. |
| Detection rule | Versioned declarative YAML that recognizes/explains evidence. It cannot execute code or override protection. |
| Disposition | Product grouping: Safe candidate, Review required, Move candidate, Protected, or Unknown. |
| Dry run | Validation/resolution of a future plan that performs zero mutation and previews exact targets/effects where feasible. |
| Exact | Value directly measured by the named supported method for the stated complete scope; not a synonym for safe/reclaimable. |
| Exclusive allocated bytes | Allocation counted once after hard-link ownership/deduplication when stable identity evidence supports it. |
| Execution receipt | Immutable structured record of actual per-item attempt, verification, measurement, errors, and rollback state. |
| Finding | Evidence-backed explanation/classification produced from observations and rules after protected-policy and overlap resolution. |
| Hard link | Multiple directory entries identifying the same file data. Naive recursive totals can double count it. |
| Inaccessible | CLYR could not observe an entry/region within selected scope under allowed standard-user behavior. It is not zero or empty. |
| Known reclaimable lower bound | Non-overlapping bytes with sufficiently exact evidence and an approved consequence; separate from estimates/review/movable. |
| Logical bytes | Sum of observed file lengths. It may overstate local physical usage for sparse, compressed, hard-linked, deduplicated, or cloud content. |
| Movable bytes | Content for which an approved move workflow may be preferable. Never added to reclaimable space. |
| Partial result | Useful scan/action evidence exists but the selected operation did not fully complete or coverage/effect is incomplete. |
| Permanent deletion | An absent action type and Prohibited behavior until a dedicated future ADR/threat/UX/test gate; no Phase 0 rule can represent it. |
| Plan digest | Canonical integrity binding over plan/version/items/evidence/consequences used to detect any material change. |
| Protected | Resource/policy outcome that blocks generic actions regardless of rule, size, age, or user selection. |
| Quick Analysis | Default conservative read-only mode with progressive aggregates, no hashing, no reparse traversal, and no cloud hydration. |
| Quarantine | Future product-owned recoverable holding workflow. Cross-volume means copy, verify, then approved source removal; expiry never silently deletes. |
| Reclaimable estimate | Qualified non-exact prediction of host free-space recovery. It is not a promise and cannot overlap other reclaimable ownership. |
| Reparse point | Windows filesystem entry such as a junction, symbolic link, mount point, or cloud tag. Targets are not followed by default. |
| Report-only | Detection/explanation with no executable action. All Phase 0 rule examples are report-only. |
| Risk | Consequence policy: Informational, Low, Medium, High, Prohibited. It is not confidence or release maturity. |
| Rule pack | Versioned manifest plus ordered hashed rule documents and compatibility/provenance metadata. A signature never bypasses local validation. |
| Safe candidate | A defined low-risk review candidate supported by evidence; not preselected, guaranteed consequence-free, or automatically removable. |
| Scan session | Time-bounded observation with selected volume/mode/options, progress, coverage, aggregates, errors, and terminal state. |
| Snapshot | Minimized local aggregate scan history, not a full filename index or transactional filesystem snapshot. |
| Stable identity evidence | Volume/file identifiers and attributes used, where supported, to detect target replacement beyond a path string. |
| Stale | Evidence no longer current enough for the intended decision. Stale action evidence requires a new scan/plan/confirmation. |
| System volume | Volume containing the active Windows installation. It is discovered; it is not assumed to be C:. |
| TOCTOU | Time-of-check/time-of-use race where a target changes after validation and before action. CLYR revalidates/fails closed. |
| Unknown bytes/finding | Data or classification with insufficient trustworthy evidence. Unknown remains visible and is never automatically actionable. |
| Windows-managed | Storage whose lifecycle/accounting belongs to documented Windows mechanisms rather than ordinary recursive deletion. |

## Units and naming

- Serialized field names use bytes as integers and explicit qualification/method fields.
- UI unit policy (SI versus IEC) is selected and documented in Phase 1; it never changes raw bytes.
- Product: CLYR; repository and CLI: `clyr`; root namespace: `Clyr`.
- “C: drive” is user-facing positioning; “system volume” is technical logic.
- “Junk,” “health score,” and “PC booster” are not classifications or product claims.
