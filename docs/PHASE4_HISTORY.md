# Phase 4 snapshot and growth contract

Phase 4 answers “what aggregate measurements changed?” It does not answer what process or person caused a change and never recommends deletion.

## Lifecycle and persistence

Automatic capture runs after completed scans. Completed-with-warnings is stored as `Partial`; user cancellation may be stored as `Cancelled`; failed scans are not stored. `ScanId` uniqueness makes a retried save idempotent. One SQLite transaction writes the snapshot, categories, findings, warnings, and retention pruning. UTC is used throughout.

The default retention is 20 records per privacy-safe drive identity (configurable 2–1000). Disabling history prevents future automatic saves but does not silently delete existing data. Delete and clear require confirmation in CLI/UI and affect only CLYR database records.

## Privacy and identity

Stored data is aggregate capacity/used/free, observed/classified/unknown/unaccounted bytes, coverage counters, category/finding aggregates, rule/schema/app versions, warnings, state/mode/time, drive-letter display root, and an HMAC fingerprint. Raw volume GUIDs exist only transiently. No file list, raw child path, username, machine name, SID, volume serial, content, or content hash is persisted.

The local random HMAC key is not synchronized. Key deletion/rotation makes continuity unavailable. Reformat normally creates a new identity. A cloned volume identifier can collide; CLYR therefore treats capacity/filesystem/coverage drift as warnings and does not describe identity as proof.

## Comparison

The report includes drive used/free, observed, classified, unknown, unaccounted, file/skipped coverage, categories, and findings. Deltas are signed and labeled increased, decreased, unchanged, new, no-longer-present, uncertain, or incomparable. Arithmetic saturates on overflow. Significance is 250 MiB absolute OR both 50 MiB and 10 percent relative. Rule changes yield classification-adjusted compatibility; scan mode/filesystem/coverage drift reduces confidence.

CLI: `clyr snapshots list`, `show <id>`, `compare <before> <after>`, `delete <id> --yes`, `clear --yes`, and `settings`.

## Recovery and limitations

CLYR does not auto-delete a database that SQLite reports as corrupt. Preserve the file for support, then explicitly clear or replace it only by user choice. Full-scan comparison is authoritative for Phase 4; the USN interface deliberately reports unsupported. Logical-size, hard-link, inaccessible-entry, reparse, and cloud-placeholder limitations from Phase 2/3 remain.
