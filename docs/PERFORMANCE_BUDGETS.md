# Performance Budgets and Benchmark Plan

## Status

These are provisional engineering budgets, not measured results or support claims. They are chosen to preserve interactivity, bound damage from hostile/huge trees, and make regressions visible. Phase 2 benchmarks on controlled SSD/HDD fixtures revise them with hardware and methodology; changes require rationale in the decision log or an ADR when architectural.

## Provisional budgets

| Metric | Initial budget | Why reasonable | Measurement / revision phase |
|---|---:|---|---|
| UI input/render responsiveness during scan | No UI-thread filesystem I/O; 95th percentile input-to-frame under 100 ms in demo/fixture smoke | Noticeable stalls erode trust; architecture can isolate I/O | Phase 1 demo, Phase 2 scan |
| Progress publication | At most 4 UI updates/second; immediate terminal/cancel acknowledgement event | Prevents render/screen-reader flooding while remaining legible | Phase 1/2 |
| Cancellation acknowledgement | Orchestrator acknowledges within 250 ms; bounded native operation completes or times out within documented 2 s target | Separates UI acknowledgement from uninterruptible API behavior | Phase 2 per adapter |
| Managed scan memory | Working-set growth under 256 MiB for a 1,000,000-observation synthetic stream, excluding OS cache/UI | Forces bounded queues, counters, and top-N rather than retained entries | Phase 2; tune by measured baseline |
| Work queue | Default capacity at most 4,096 pending observations and at most 4 metadata workers | Conservative starting point across disks; avoids uncontrolled parallelism | Phase 2 SSD/HDD matrix |
| Top-N retention | Configured N plus fixed overhead per view; default N at most 1,000 | Keeps memory independent of entry count | Phase 2 unit/benchmark |
| Standard scan content reads/hash | Zero bytes | Prevent hydration/privacy/I/O surprises | Phase 2 adapter instrumentation |
| Rule evaluation | One shared traversal; p95 compiled rule evaluation under 10 microseconds per relevant observation in synthetic benchmark | Detects accidental subtree rescans/regex abuse; number is provisional | Phase 3 |
| YAML rule file/pack | 256 KiB per rule, 10 MiB/2,000 rules per pack, parser depth 32, aliases disabled or tightly bounded | Bounds parser and review/resource abuse; revise from real packs | Phase 3 security benchmark |
| Summary export | Under 5 MiB and 5 s for maximum retained aggregate model | Keeps a support artifact bounded and previewable | Phase 3/4 |
| SQLite UI-facing query | 95th percentile under 100 ms on the supported retained dataset | Prevents history/detail navigation stalls | Phase 4 |
| Snapshot retention | Provisional 30 aggregate snapshots per volume and user-visible size cap | Bounded disk/privacy cost while supporting growth analysis | Phase 4 product study |
| Helper IPC request | 1 MiB maximum, 500 items maximum, ten-minute plan expiry | Bounds elevated parser/work and confirmation drift | Phase 5/6 fuzz/soak |
| External tool | Adapter-specific timeout; stdout/stderr each capped at 1 MiB; no unbounded process tree | Prevents hangs/resource capture; strict adapters may use smaller limits | Phase 7 |
| Quarantine copy | Capacity preflight includes at least 10% or 1 GiB safety margin, whichever is larger | Avoids filling destination during cross-volume copy; validate per workflow | Phase 6/8 |

Budgets do not guarantee scan duration: duration depends on entry count, device, filesystem, access, load, antivirus, cloud provider, and selected detail. The UI shows elapsed work and progressive evidence without inventing an ETA until a measured estimator exists.

## Benchmark matrix

### Synthetic stream fixtures

- 10 thousand, 100 thousand, 1 million, and 10 million generated metadata observations without filesystem allocation.
- Deep/wide distributions, category/rule skew, many errors, many reparse skips, top-N worst cases, cancellation at fixed counts.
- Measures throughput, allocations, peak working set, queue depth, progress count, cancellation, and determinism.

### Filesystem fixtures

- Disposable NTFS SSD and HDD: many small files, large files, deep paths, hard links, sparse/compressed files, locked/changing entries, and reparse loop.
- ReFS/read-only removable/cloud/EFS only on explicitly provisioned capability-tagged machines; no inferred support.
- Cold/warm runs distinguished; OS cache, Defender status, power mode, filesystem, CPU/RAM, free space, package identity, and build commit recorded.

### Application fixtures

- Demo dashboard with maximum categories/findings and screen-reader progress.
- Rule packs at typical and maximum allowed sizes, including malicious depth/regex/alias attempts.
- Snapshot database at retention/size limits and migrations from every supported schema.
- Future fake external tools with delay, output flood, locale, child process, and cancellation.
- Future action journal interruption at every state; IPC request boundary and replay load.

No benchmark scans or cleans the developer's real system folders. Filesystem benchmarks use a canonicalized temporary root on a disposable volume/VM and assert root containment before setup/teardown.

## Method

1. Pin Release build, SDK/package lock, fixture version/seed, machine/power/storage configuration, and command.
2. Warm up when the scenario calls for it; report cold and warm separately.
3. Run enough iterations to report median, p95 where meaningful, range, and sample count—never a single flattering run.
4. Capture CPU time, wall time, allocations/GC, peak working set, I/O bytes/operations, queue depth, progress events, errors, and cancellation latency.
5. Compare to the same baseline class; do not compare unlike SSD/HDD or cold/warm data.
6. Store compact machine-safe results, not personal paths or raw entry names.
7. A regression over budget fails the relevant gate or receives a documented temporary exception with owner, expiry, risk, and follow-up.

## Responsiveness design

Filesystem work never runs on the UI thread. Progress is coalesced/throttled and immutable. Top-N/category updates batch rather than reorder continuously. Cancellation stops producers first and drains bounded work. Low-impact mode reduces worker count/prioritization and does not create a service. Minimized operation remains within the same user-started process/session.

## Accuracy versus performance

- Standard scan does not hash content.
- Hard-link/allocated/ADS/cloud/system-provider work is capability- and mode-labeled; unavailable evidence stays unavailable.
- Approximation must not be silently introduced to meet speed. It receives an Estimated qualification and coverage details.
- Quick Analysis may stop refining a view while Deep Analysis continues deeper, but both preserve ownership/no-double-count invariants.
- USN may accelerate later scans only with a correct full-enumeration fallback and reset/wrap detection.

## Success metrics without telemetry

Controlled tests record completion/partial rate, cancellation, memory/I/O, UI responsiveness, high-confidence classified-byte share, fixture false positives, protected violations (must be zero), future planned-versus-measured recovery, crash recovery, accessibility findings, and package lifecycle reliability. Product installs collect none of these remotely by default.

## Acceptance criteria

- Phase reports publish exact benchmark command, environment, fixture/seed, samples, and measured results or state unverified.
- Entry-count growth does not cause proportional retained object growth.
- No performance optimization weakens protection, privacy, cancellation, or uncertainty labels.
- Device-specific results do not become universal support claims.
- Provisional numbers are revised from evidence before public beta.
