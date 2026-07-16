# Storage accounting contract

## Purpose and status

CLYR must explain storage without collapsing unlike measurements into a misleading “junk” number. This document defines the vocabulary and invariants for later scanner, rule, persistence, UI, CLI, and export work. Phase 0 contains no scanner and no measured results.

Every displayed total must answer three questions nearby: **what is measured, how precise is it, and how much of the selected scope was covered?** Size and age alone never establish that data is safe to remove.

## Measurement envelope

Each byte metric is represented as:

```text
ByteMeasurement(value, precision, source)
```

| Field | Values | Meaning |
| --- | --- | --- |
| `value` | Non-negative integer bytes or null | Null is used only when unavailable. Zero means measured/derived zero, not unknown. |
| `precision` | `exact`, `lower-bound`, `estimate`, `unavailable` | Exact applies only to the declared scope and observation time. Lower bound cannot overstate known bytes. Estimate must explain its method. |
| `source` | `file-metadata`, `filesystem-api`, `system-api`, `tool-adapter`, `rule-aggregate`, `derived`, `not-measured` | Identifies the evidence, not its safety. `not-measured` pairs only with unavailable/null. |

All arithmetic uses checked 64-bit integer operations or a wider internal accumulator with checked conversion. Overflow makes the aggregate unavailable/partial and produces a diagnostic; it never wraps or clamps silently. Decimal display units are derived at the presentation edge, while values remain integer bytes.

## Required metrics

| Metric | Definition | Important exclusions/qualification |
| --- | --- | --- |
| `logicalBytes` | Sum of file logical lengths for uniquely enumerated file records in scope. | Not physical usage. Initial metadata scans cover the primary unnamed data stream; unenumerated alternate streams are a stated coverage limitation. Parent folder summaries are views and are not re-added. |
| `allocatedBytes` | Sum of reported allocation for enumerated file records where the filesystem/API provides it. | Can count a hard-linked allocation more than once unless stable identities are deduplicated; directory and filesystem metadata may be absent. Never infer from logical length alone and call it exact. |
| `exclusiveAllocatedBytes` | Physical allocation after counting each stable file identity once and assigning one accounting owner. | Unavailable or estimated where stable IDs/allocation are not reliable. This is the preferred basis for category and candidate totals. |
| `cloudPlaceholderLogicalBytes` | Logical lengths represented by cloud placeholders. | Does not mean the bytes reside locally. Metadata queries must not hydrate content. |
| `cloudPlaceholderLocalBytes` | Locally allocated bytes attributed to cloud placeholders without hydration. | If a provider/filesystem cannot report this safely, use unavailable—not logical length. Allocation rounding can make it differ from logical content size. |
| `knownReclaimableLowerBoundBytes` | Disjoint, validated allocation known to be recoverable by currently supported, previewed actions. | Phase 0 and report-only rules contribute no positive bytes. Excludes movable, review-only, protected, unknown, inaccessible, overlapping, and unverified virtual-disk guest bytes. |
| `estimatedReclaimableBytes` | Best bounded estimate for future supported actions when exact allocation/recovery is not knowable. | Never shown as exact or added to the known lower bound. Must name assumptions and overlap state. Unavailable is preferred to a weak guess. |
| `reviewCandidateBytes` | Exclusively owned bytes that merit user review but are not approved for cleanup. | Not reclaimable. npm cache and Downloads reporting begin here unless stronger action evidence exists in a later phase. |
| `movableBytes` | Exclusively owned bytes potentially eligible for a supported migration workflow. | Never reclaimable and never added to candidate cleanup totals. Host free space changes only after a verified move and source removal. |
| `protectedBytes` | Observed bytes classified by protected-resource policy or a system-managed reporting source. | Usually a lower bound because inaccessible protected regions may exist. A rule cannot downgrade this classification. |
| `unknownBytes` | Exclusively observed bytes with no trustworthy known classification. | Unknown never defaults to junk or safe. |
| `inaccessibleBytes` | Size of inaccessible content only when a safe parent/system API supplies a defensible value. | Use unavailable when entry sizes cannot be observed. Do not guess from free-space differences. |
| `skippedItemCount` | Entries deliberately not traversed, grouped by reason (for example reparse point or excluded scope). | A count is not a byte estimate. |
| `inaccessibleItemCount` | Entries for which required metadata/enumeration was denied or failed. | Keep separate from deliberately skipped entries. |

The volume overview also uses `capacityBytes`, `usedBytes`, and `freeBytes` from a volume/filesystem API. These are point-in-time volume observations, not the sum of scanner results.

## Accounting scopes and ownership

Every aggregate declares a scope: volume, scan selection, category, finding, or ranked view. Totals are comparable only when scope, observation window, precision, and allocation basis match.

The scanner produces file-level observations as a stream. Folder sizes, top-N entries, category totals, and findings are alternative views over those observations. They are not additive to one another.

### Physical identity and hard links

Where supported, physical identity is `(volume identity, file ID)`. The first encounter creates the allocation record; later hard-link paths add logical/path observations but not another exclusive allocation. Link count alone is insufficient because not all links may be in scope. If identity retrieval fails, CLYR may show logical totals but must mark exclusive allocation unavailable/estimated and explain potential duplicate counting.

Cross-volume IDs are never compared. A path is not a stable physical identity. Case folding uses Windows ordinal case-insensitive semantics only after canonical volume resolution.

### Finding overlap algorithm

Classification can match one observation with several rules. Resolution is deterministic and independent of enumeration/load order:

1. Canonicalize the observation, determine volume/file identity when available, and reject unsafe ambiguity.
2. Apply protected-resource policy. Protected ownership is terminal for reclaimable accounting.
3. Coalesce observations with the same stable physical identity.
4. Rank matching candidates by pack trust tier, rule priority descending, root specificity descending, rule ID ordinal ascending, then rule version ordinal ascending.
5. Assign one primary accounting owner. Secondary findings may explain the same storage but receive `ownedBytes = 0` and reference the owner fingerprint.
6. Aggregate category/disposition totals from primary owners only.
7. If physical identity or containment cannot be resolved, suppress a precise reclaimable total and downgrade precision or mark unavailable.

Community priority cannot override protected policy. Pack compilation tests flag equal-precedence overlaps, and randomized rule-order tests must yield identical ownership.

Nested folder matches require the same rule: bytes owned by a child file are not also owned by every matching ancestor. A parent finding can show `inclusiveObservedBytes` as a view, but only `ownedBytes` participates in disjoint totals.

## Derived dashboard values

CLYR’s primary overview is deliberately not a single sum:

- **Currently used space** comes from the volume API and is labelled with its observation time.
- **Safely reviewable space** is `reviewCandidateBytes`, not a promise of recovery.
- **Potentially movable space** is `movableBytes`, kept separate.
- A future **known recoverable lower bound** may be shown only after supported action planning creates disjoint validated targets.

No formula adds review, movable, unknown, protected, or inaccessible values into reclaimable space. `estimatedReclaimableBytes` and `knownReclaimableLowerBoundBytes` are alternative claims, not operands to add.

Category sums need not equal volume used space. Legitimate differences include filesystem metadata, reserved storage, directories, alternate streams, inaccessible areas, out-of-scope locations, changing files, and different observation instants. The UI explains the reconciliation gap rather than assigning it silently to “unknown” unless it was actually observed and classified unknown.

## Coverage and uncertainty

A scan is a time-bounded observation. Its coverage record includes:

- selected roots and scan mode expressed as privacy-safe scope codes;
- start/end time and terminal state;
- enumerated, skipped, inaccessible, disappeared, and changed-during-scan counts;
- skipped counts by reason and reparse tag when safe;
- capabilities used or unavailable (allocation, file IDs, cloud state, external/system APIs);
- whether each metric covers all enumerated items or a subset;
- a reconciliation note when volume-level and scanner-level observations differ.

“Completed” means the declared scan plan finished, not that CLYR bypassed access controls. Any skipped or inaccessible region remains visible near totals. A partial/cancelled scan never promotes lower-bound data to exact full-volume data.

Time signals carry provenance. NTFS last-access time can be disabled, delayed, or semantically weak; it is shown only when available and never establishes safety on its own. Modified time says data changed, not that a user last used it.

## Special storage semantics

| Storage type | Required treatment |
| --- | --- |
| Sparse/compressed files | Record logical and allocation separately. Never derive physical use from length. |
| Hard links / WinSxS | Deduplicate by stable ID where feasible. Raw recursive WinSxS size is not reclaimable; Windows servicing analysis is authoritative for supported cleanup. |
| Filesystem deduplication | Do not promise per-file recovered bytes from allocation metadata alone; shared chunks can make recovery nonlinear. Use unavailable/estimate with a documented system source. |
| Cloud placeholders | Query attributes/metadata without opening content or triggering hydration. Separate placeholder logical and local bytes. |
| WSL, Docker, VM disks | Distinguish host virtual-disk allocation, deletion inside the guest, and host space released only after supported compaction. Guest free bytes are not immediately reclaimable host bytes. |
| Recycle Bin | Prefer supported shell/system reporting. Restore capability belongs to Windows and is not guaranteed by CLYR. |
| Restore points/shadow copies | Protected; use supported system APIs and never ordinary recursive deletion or naive folder totals. |
| Pagefile, swapfile, hibernation, reserved storage | Windows-managed/protected. Report with supported mechanisms; no rule-authored mutation. |
| Alternate data streams | Record whether streams were enumerated. The default file length does not represent all streams. Never open streams in a normal metadata scan. |
| EFS/inaccessible encrypted data | Do not decrypt, take ownership, or change ACLs. Report accessible metadata and uncertainty. |
| Reparse points/mounts | Skip traversal by default, record the tag/reason, and never attribute the target volume’s bytes to the link location. |
| FAT/exFAT/ReFS or unsupported APIs | Capability detection chooses available metrics. Missing file IDs/allocation downgrades precision; no fragile claim of NTFS-equivalent accuracy. |

## Cleanup-plan and verification accounting (future)

No cleanup occurs in Phase 0. In later approved phases, a plan freezes target identities, rule/pack versions, allocation evidence, overlap ownership, expected lower bound/estimate, and observation time. Immediately before execution each target is canonicalized and identity-checked again; changed or ambiguous targets are skipped.

Actual recovery is a volume observation:

```text
observedFreeSpaceDelta = freeBytesAfter - freeBytesBefore
```

It is not the sum of deleted file lengths. The receipt records before/after timestamps, filesystem API source, completed/failed/skipped target allocation, and reasons the delta may differ: concurrent writes, delayed deallocation, sharing/hard links, Recycle Bin behavior, compression/deduplication, virtual-disk compaction, filesystem metadata, or measurement granularity. A negative delta is valid evidence that other writes occurred; it is never clamped to zero or misreported as recovered bytes.

## Phase 2 implemented subset

Phase 2 reports observed logical file length only. Drive capacity/used/free come from drive metadata and are never equated with enumerated totals. `unaccountedBytes` is the non-negative difference between drive-used and observed logical bytes when drive evidence is available; it is a reconciliation clue, not a hidden-junk claim.

Allocated, exclusive allocated, ADS, sparse/compression physical effect, and stable hard-link identity are not measured. Because multiple names for one hard-linked stream can be observed more than once, scan totals and rankings are `Estimated` and carry a visible hard-link limitation. Inaccessible/skipped content remains outside observed totals and is represented by coverage counts rather than invented bytes.

## Required accounting tests

- Empty, single-file, nested-folder, changing-file, overflow, cancellation, and partial-access fixtures.
- Hard-link aliases inside/outside scope and stable-ID-unavailable fallback.
- Sparse, compressed, alternate-stream, and allocation-rounding fixtures where the test filesystem supports them.
- Cloud placeholder fakes proving metadata queries do not hydrate content.
- Junction/symlink/mount loops proving targets are skipped and not counted.
- Overlapping general/specific rules under randomized order, including protected precedence.
- Category reconciliation proving ranked/folder/finding views are not added together.
- Inaccessible bytes unknown versus safely system-reported lower bound.
- WSL/Docker/VM examples separating guest deletion from host compaction.
- Export serialization for exact, lower-bound, estimate, unavailable, and 64-bit boundary values.
- Future execution receipts comparing expected allocation with observed free-space delta under concurrent-change fakes.

Acceptance requires zero protected-path violations and no UI/CLI/export label that turns an estimate, review candidate, logical placeholder size, or movable byte into guaranteed reclaimable space.
Phase 5 plans carry observed logical bytes and item counts from source metadata. Estimated physical bytes remain unavailable unless safely evidenced. Dry-run output names hard-link, allocation, compression, cloud, inaccessible, changing, locked, filesystem-metadata, and cache-recreation limitations. No Phase 5 value is called reclaimable or recovered.
