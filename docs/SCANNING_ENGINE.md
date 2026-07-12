# Scanning Engine

## Contract

The Phase 2 scanner will be read-only. It observes a chosen eligible volume during a time interval; it is not a transactional filesystem snapshot. Files may appear, disappear, grow, shrink, move, lock, or change permissions while enumeration proceeds. Results therefore carry start/end times, mode/options, coverage, skipped/inaccessible counts, error summaries, and an exact/estimated/partial state.

The scanner must never write to the scanned volume, open file contents during a standard size scan, hydrate cloud placeholders, decrypt EFS content, take ownership, change ACLs, unlock files, terminate processes, follow a reparse point by default, or request elevation merely to improve coverage.

## Modes

| Behavior | Quick Analysis | Deep Analysis |
|---|---|---|
| Entry point | Default C:-first experience | Explicit opt-in after cost/privacy warning |
| Goal | Useful progressive top-level and known-location explanation quickly | Finer folder/file, allocation, and optional candidate detail |
| Content reads/hash | Never | Never by default; duplicate discovery is a separate explicit option and staged |
| Reparse traversal | Never | Never by default; no general enable switch until separately reviewed |
| Cloud placeholders | Metadata that does not hydrate | Same; content hydration prohibited |
| Aggregation | Top-level/category/known roots and bounded top-N | Deeper bounded aggregates/top-N |
| Allocated size | Capability-based when cheap and safe; otherwise unavailable | Optional provider with cost/accuracy label |
| Hard links | Estimate/limitation label if not deduplicated | Optional file-ID accounting with bounded state |
| I/O concurrency | Conservative default | Configurable within measured hard bound |
| Battery/load | Low-impact option; no service | Explicit warning and low-impact option |
| Cancellation | Required through every layer | Required through every layer |
| Pause/resume | Optional cooperative in-session pause after measurement; never claimed until tested | Same; persistent resume requires a later contract |

The initial dashboard may render progressive aggregates before enumeration finishes, but it must remain visibly **Scanning**. Progressive numbers are observations-to-date and cannot be presented as final percentages or reclaimable totals.

## Pipeline

```text
discover capability -> create session -> seed bounded work queue
-> stream directory entries -> reject/record exclusions and reparse points
-> read safe metadata -> normalize observation -> aggregate ownership/top-N
-> evaluate detection rules -> apply protected-resource precedence
-> emit throttled progress/findings -> persist bounded aggregate snapshot
```

Enumeration uses bounded queues/channels and a configurable worker limit. It does not retain one object per file. Directory aggregates are rolled up as subtrees finish; top-N uses fixed-size heaps; category counters use stable keys; errors use bounded samples plus counts. A directory is not recursively re-enumerated per rule. Rules consume shared observations/aggregates or explicitly indexed known roots.

The default favors predictable disk access over maximum parallelism. SSD/HDD/battery/load fixtures determine settings; no target is claimed before measurement. Progress notifications are time- and change-throttled so UI rendering cannot amplify traversal load.

## Safe metadata observation

`FileSystemEntryMetadata` distinguishes path token, entry kind, logical length, attributes, timestamps/evidence source, reparse tag, cloud state, compression/sparse state, stable volume/file ID when safely available, hard-link count, allocated bytes and method, observation time, and errors. A field is nullable/unavailable rather than guessed.

- Extended-length and volume-GUID paths are preserved internally through Windows-aware normalization; display paths are separate.
- Case comparison is ordinal/capability-aware, never current-culture.
- Trailing dots/spaces, 8.3 aliases, alternate separators, device/UNC syntax, mount points, alternate data streams, and reserved names are adversarial cases.
- Reparse tags are recorded when available; the target is not traversed.
- Cloud metadata queries must use documented no-hydration behavior; ambiguity becomes skipped/unavailable.
- EFS/inaccessible metadata remains inaccessible; CLYR does not bypass it.
- Alternate data streams and system-managed allocations are not silently folded into logical length. Their coverage is labeled.

## Accounting

Logical byte totals sum observed file lengths. Allocated bytes are reported only with an identified method and capability. Hard-linked physical allocation is counted once when stable file IDs are tracked; otherwise the total is an estimate with a potential double-count limitation. Sparse/compressed/deduplicated/cloud content keeps logical and physical/local values separate.

Every file has one primary aggregate owner for double-count prevention. Findings may reference the same evidence, but reclaimable ownership is resolved by deterministic priority, specificity, rule ID, and stable tie-break rules. Movable, protected, review, unknown, and reclaimable values are separate. See `STORAGE_ACCOUNTING.md`.

## Coverage

A scan exposes:

- eligible scope and exclusions;
- observed item/directory counts;
- logical and physical evidence availability;
- skipped and inaccessible item counts with bounded reason samples;
- reparse/cloud/encrypted/unsupported counts;
- whether hard-link, ADS, allocation, and system-managed providers ran;
- start/end times and volume identity/capabilities;
- status: Complete, Partial, Cancelled, Failed, or DriveRemoved.

`inaccessibleBytes` is present only when a supported provider can determine it; otherwise inaccessible space is explicitly unknown. Folder totals are never labeled exact if any descendant was skipped or changed materially.

## Cancellation, pause, and failure

Cancellation tokens reach discovery, enumeration, metadata, aggregation, classification, persistence, and UI/CLI output. Producers stop accepting work, workers observe cancellation between bounded operations, resources/handles close, and the orchestrator decides whether useful aggregates warrant Partial rather than Cancelled. A blocking native call gets a documented timeout/cancellation adapter or bounded task isolation; cancellation is not faked.

Pause, if implemented, stops producers and drains/parks bounded workers without holding unnecessary handles. Phase 2 may omit pause if reliable behavior cannot be proven; UI and documentation then say unavailable. Persistent resume is not inferred from a partial snapshot.

Per-entry access denied, path too long, locked, disappeared, metadata, and invalid-reparse faults increment typed coverage and continue. Unsupported volume, failed initialization, memory-safety invariant, or corrupted internal state can fail the session. Volume removal yields DriveRemoved and retains useful partial aggregates. Persistence failure does not erase an in-memory result; it marks history unavailable.

## State machine

Allowed primary path:

```text
Created -> Discovering -> Scanning -> Aggregating -> Classifying
-> Persisting -> Completed
```

Terminal alternatives are Cancelled, Partial, Failed, and DriveRemoved. Transitions are centralized, monotonic, idempotently observed, and unit tested; terminal states do not transition. `docs/diagrams/scan-state-machine.mmd` is authoritative.

## Duplicate discovery

Duplicate analysis is not a standard scan and never implies automatic removal. An eventual opt-in mode groups by logical size, hashes a bounded sample/partial content only for candidates, then full-hashes likely duplicates with progress/cancellation/privacy warnings. Hard links are not duplicates. Cloud content is not hydrated without a separate explicit contract. Results are Review required.

## Test fixtures

Temporary synthetic adapters/trees cover deep/wide/million-entry generated streams, access denied, long paths, locked/disappearing/resizing files, cancellation at each state, drive removal, hard links, sparse/compressed files, alternate streams, case/path aliases, reparse loops/mounts, cloud placeholders, EFS simulation, low memory, slow metadata, progress floods, unsupported filesystem, and protected lookalikes. CI never scans or cleans a real system location.

## Acceptance criteria

- No write-capable adapter is reachable from scan orchestration.
- Cancellation acknowledgement and memory/I/O behavior meet measured Phase 2 budgets.
- Reparse points are skipped/recorded and cycles cannot escape the fixture root.
- Partial/inaccessible/estimated states remain visible in UI, CLI, snapshots, and export.
- Repeated rules do not cause repeated subtree traversal.
- A changing file or failed metadata call cannot crash the whole scan.
- No elevated, network, content-read, or hydration dependency exists.
