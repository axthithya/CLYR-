# Data model

## Status and design intent

This document defines the Phase 0 conceptual model. No SQLite database or persistence code exists yet. The model is optimized for explaining storage safely: aggregate snapshots and bounded ranked items are retained by default, while a permanent index of every filename is explicitly excluded.

The authoritative names below are shared by the future core, persistence, CLI, UI, and export contracts. DTOs and SQLite columns may use idiomatic casing, but their meaning must not drift.

## Domain boundaries

| Aggregate | Purpose | Root identity and invariants |
| --- | --- | --- |
| `Volume` | Privacy-safe identity and observed capacity of a local volume. | Local identity is stable only inside one installation. Export identity is report-scoped. Drive letters are observations, not durable IDs. |
| `ScanSession` | One time-bounded read-only observation of a volume. | Random UUID; deterministic state transitions; exactly one mode and rule-pack binding. A scan is not a transactional filesystem snapshot. |
| `ScanCoverage` | Counts and uncertainty for skipped/inaccessible regions. | Always present, including completed scans. “Complete” means complete within declared scope, not privileged access to everything. |
| `StorageAccounting` | Separately typed byte measurements. | Never exposes one aggregate “junk” value; each value carries precision and source. See `STORAGE_ACCOUNTING.md`. |
| `CategoryAggregate` | Bounded totals by storage category. | A physical item has at most one accounting owner in a scan. Category totals never add parent-folder summaries to child files. |
| `Finding` | Explainable classification produced from evidence and a rule. | Bound to scan, pack digest, rule ID/version, disposition, risk, confidence, and action availability. Phase 0/initial Phase 3 is `report-only`. |
| `FindingEvidence` | Structured reasons supporting a finding. | Codes and typed values, not raw exception text or file content. Evidence records provenance and precision. |
| `RankedItem` | Bounded top-N view for a scan. | Optional; default persistence uses redacted location tokens. It is a view, never added to aggregate totals. |
| `ScanDiagnostic` | Aggregated skipped/error information. | Stable code, count, and privacy-safe scope class. No stack trace or personal path in normal persistence. |
| `RulePackBinding` | Exact classification provenance. | Pack ID, SemVer, schema major, digest, trust tier; immutable for a completed scan. |
| `ExportDescriptor` | Locally generated export metadata. | Records mode and schema version only when useful; CLYR does not upload reports. The user controls the exported file. |
| `ActionPlan` / `ExecutionReceipt` | Future cleanup planning and audit aggregates. | Conceptual only until Phases 5–6; isolated from scan summaries and never inferred from a finding. |

## Identity and value types

### IDs

- `ScanId`, `FindingId`, `ReportId`, and future `PlanId` are random UUIDs and convey no path or user identity.
- `LocalVolumeKey` is a keyed digest of stable volume evidence using an installation-scoped secret. It enables local snapshot comparison but must not be copied directly into support exports.
- `ExportVolumeToken` is derived with a fresh report-scoped salt. It prevents correlating the same machine across independently shared reports; the export schema serializes it as `volumeIdHash`.
- `FindingFingerprint` is a SHA-256 digest of normalized rule/scan evidence. A support-safe fingerprint must not be a plain unsalted hash of a guessable personal path.
- A filesystem item’s stable identity is `(volume identity, file ID)` where supported. Path is evidence, not identity. If stable identity is unavailable, deduplication precision must be downgraded.

### Measurements

Every byte measurement is a tuple:

```text
ByteMeasurement(value, precision, source)
precision = Exact | LowerBound | Estimate | Unavailable
source = FileMetadata | FilesystemApi | SystemApi | ToolAdapter | RuleAggregate | Derived | NotMeasured
```

`Unavailable` requires a null value and `NotMeasured`; all other values are non-negative integers. Counts are non-negative 64-bit integers. Time values are UTC instants with the original evidence source; a missing or unreliable last-access value is not converted to zero.

### Paths and location disclosure

`LocationEvidence` has an explicit disclosure tier:

| Tier | Stored form | Default persistence | Support-safe export |
| --- | --- | --- | --- |
| `None` | Category/scope code only | Preferred for aggregates | Allowed |
| `Redacted` | Known-root token plus nonidentifying depth or report-scoped digest | Allowed for bounded top-N/finding evidence | Digest or category only |
| `DetailedLocal` | Canonical full path encrypted only if a later ADR approves it | Opt-in, off by default | Prohibited |

The model has no field for file contents. Authentication tokens, account IDs, project secrets, machine names, usernames, and unredacted diagnostic messages are not valid domain data.

## Scan and finding states

The scan state machine is:

```text
Created -> Discovering -> Scanning -> Aggregating -> Classifying -> Persisting -> Completed
```

`Cancelled`, `Partial`, `Failed`, and `DriveRemoved` are explicit terminal/alternate outcomes. A partial scan persists its coverage and uncertainty rather than masquerading as complete. State history should be append-only or transactionally represented so a crash cannot leave a “Completed” scan without aggregates.

A finding’s observation state is one of `Present`, `NoLongerPresent`, or `StaleSnapshot`. Its disposition is independently `safe-candidate`, `review-required`, `move-candidate`, `protected`, or `unknown`. Its action availability is independently `report-only`, `unsupported`, or a future typed action. These axes must not be collapsed into one status.

## Conceptual SQLite model

Names are conceptual and do not authorize implementation in Phase 0.

| Table | Important columns | Retention/privacy notes |
| --- | --- | --- |
| `SchemaVersion` | version, appliedUtc, migrationDigest | No user data. One row per applied migration. |
| `ProductSetting` | key, typedValue, updatedUtc | Separate from scans; allowlisted keys only. Secrets use an OS-protected store, not plaintext SQLite. |
| `VolumeIdentity` | localVolumeKey, filesystem, firstSeenUtc, lastSeenUtc | No serial number, drive label, mount path, or username in exports. |
| `ScanSession` | scanId, localVolumeKey, mode, state, startedUtc, endedUtc, rulePackBindingId | Parent for one immutable snapshot. |
| `ScanCoverage` | scanId, scopeCode, state, enumeratedCount, skippedCount, inaccessibleCount | One required row per scan; detailed reasons are children. |
| `ScanAccounting` | scanId, metricCode, valueNullable, precision, source | One row per defined metric; unique `(scanId, metricCode)`. |
| `CategoryAggregate` | scanId, categoryCode, logical/allocated measurement fields, itemCount | Unique `(scanId, categoryCode)`; accounting ownership already resolved. |
| `RankedItem` | scanId, rankKind, rank, redactedLocationToken, measurements, evidenceUtc | Bounded by configured top-N; no full path by default. |
| `Finding` | findingId, scanId, fingerprint, ruleId, ruleVersion, disposition, risk, confidence, actionAvailability, owned measurements | Immutable after persistence; no cleanup authorization. |
| `FindingEvidence` | findingId, sequence, evidenceCode, typedValue, precision, source | Bounded count; no arbitrary key/value bags. |
| `ScanDiagnostic` | scanId, diagnosticCode, scopeCode, count | Aggregated and privacy-safe; raw exceptions stay in short-lived redacted logs. |
| `RulePackBinding` | id, packId, packVersion, schemaMajor, digest, trustTier | Referenced by sessions; retained while any snapshot uses it. |
| `ExportDescriptor` | reportId, scanId, schemaVersion, mode, createdUtc | Optional local history; never stores exported content or destination path by default. |
| `ActionPlan`, `ActionJournal`, `ExecutionReceipt` | future typed plan state, target evidence, verified outcome | Separate future tables with stricter retention and migrations; absent until their approved phases. |

Relationships:

```text
VolumeIdentity 1 ── * ScanSession 1 ── 1 ScanCoverage
                              │       ├── * ScanAccounting
                              │       ├── * CategoryAggregate
                              │       ├── * RankedItem
                              │       ├── * Finding 1 ── * FindingEvidence
                              │       └── * ScanDiagnostic
RulePackBinding 1 ────────────┘
ScanSession 1 ── * ExportDescriptor
```

Foreign keys are mandatory. Parent deletion cascades through scan-owned aggregates in one transaction; a referenced rule-pack binding is deleted only when no snapshots depend on it. Findings and aggregates are not updated in place after a pack change—reclassification creates new derived snapshot data with new provenance.

## Data classification and retention

Defaults are provisional product decisions to be tested in Phase 4, but they are bounded now so implementation cannot default to indefinite retention.

| Data class | Examples | Default retention | User control / deletion |
| --- | --- | --- | --- |
| Public project data | Schema versions, built-in rule IDs, category codes | With installed version | Removed on uninstall; no user sensitivity. |
| Low-sensitivity local summary | Scan time, aggregate bytes, diagnostic counts, pack digest | Bounded by both count and age; exact defaults are resolved and benchmarked in Phase 4 | History can be disabled; delete one snapshot or all history immediately. |
| Pseudonymous local metadata | Installation-scoped volume key, finding fingerprints | Same lifetime as referenced snapshots | Removed with snapshot/history reset; installation secret removed on full reset/uninstall. |
| Redacted top-N metadata | Known-root token, report-safe digest, sizes | Same snapshot limits; top-N remains bounded | User may disable ranked-item persistence independently. |
| Detailed local paths | Opt-in detailed snapshot/export staging | Not persisted by default; staged data is removed after successful export or cancellation | Explicit warning and immediate delete control. A future persistent mode needs a privacy ADR. |
| Redacted operational logs | Stable event/error codes, timings | Provisional rolling 7 days with a size cap; validate in Phase 1 | Clear logs in settings; never required for scanning. |
| Support-safe export file | User-created JSON | Outside app retention after the user selects a destination | User owns and deletes the file; CLYR never uploads it. |
| Detailed local export file | Full paths if explicitly selected in a future schema | Outside app retention | Prominent warning; never uploaded automatically. |
| Future action audit | Plan/receipt IDs, typed outcomes, recovered bytes | Provisional 30-day cap, subject to Phase 6 recovery/privacy review | Clear history when no recovery depends on it, without deleting user files. |

Deletion of local history must be transactional and verifiable. It must not delete exported files outside app storage, rule packs, user content, or the scanned locations. Retention pruning runs in the normal app process without elevation and records only aggregate success/failure diagnostics.

## Integrity, migrations, and recovery

- Use monotonically versioned, transactional migrations and test upgrade from every supported schema version.
- Enable SQLite foreign-key enforcement explicitly and verify it per connection.
- Do not enable WAL, encryption, compression, or custom pragmas without an ADR covering shutdown, recovery, packaging, and backup behavior.
- Write a scan snapshot and its terminal state atomically. Progressive UI data can be transient; a cancelled/partial persisted snapshot must contain consistent coverage.
- Run bounded integrity checks at safe lifecycle points. On corruption, preserve the damaged database for user-directed diagnostic handling, create no secret-bearing copies, and offer reset; never mutate scanned content.
- Back up before a destructive migration. Validate free space and use atomic replacement where supported.
- Settings, summaries, logs, exports, and future action audit stores remain logically separated even if some share a database file.
- All integer conversions reject overflow. Timestamps are UTC RFC 3339 values at external boundaries.

## Cross-record invariants

1. A completed scan has an end time, coverage row, all required accounting metrics, and one verified rule-pack binding.
2. A partial/cancelled/failed scan never claims complete coverage or silently substitutes zero for unknown bytes.
3. Category and finding ownership are resolved before persistence; the same physical allocation is counted once per accounting view.
4. `movableBytes`, `reviewCandidateBytes`, and `protectedBytes` are not aliases for reclaimable space.
5. Every finding records its rule version and pack digest. Removing or updating a pack cannot rewrite history.
6. Summary-export construction reads only fields classified support-safe and derives fresh report-scoped tokens.
7. No persisted scan or finding authorizes mutation. A later immutable, revalidated `ActionPlan` is the only possible source of cleanup authority.
8. Cleanup tables, when introduced, cannot cascade into deletion of scan evidence or user files.

## Open decisions before persistence implementation

- Benchmark snapshot count and age caps under realistic database sizes, then choose and document bounded Phase 4 defaults.
- Select the exact installation-secret storage mechanism for local keyed identities.
- Decide whether optional detailed local snapshots merit persistence at all; default remains off.
- Define corruption-export support without leaking sensitive local paths.
- Specify stable file identity behavior and fallback precision across NTFS, ReFS, FAT/exFAT, removable, and unsupported volumes.

These questions do not block Phase 1’s migration skeleton, but each must be resolved before the affected data is stored.
