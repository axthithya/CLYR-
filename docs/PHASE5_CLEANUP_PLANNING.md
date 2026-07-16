# Phase 5 cleanup planning and dry-run contract

Status: implemented in Phase 5; awaiting approval. Phase 6 execution has not begun.

## Boundary

CLYR may classify findings, let the user select eligible findings, create an immutable plan, validate it, display a dry-run, hold a bounded process-local plan record, and export a privacy-safe report. It cannot delete, move, rename, recycle, quarantine, invoke tools or shells, elevate, change permissions/ownership, modify the registry or Windows settings, or contact a helper. The production executor has one result: ExecutionNotAvailableInPhase5.

## Eligibility and action policy

Eligibility is closed: NotEligible, ManualReviewOnly, DryRunEligible, Protected, Unsupported, or InsufficientEvidence. Protected always overrides. User downloads, documents, media, archives, projects, dependencies, output, logs, dumps, and temporary data remain review-only unless a later feature obtains explicit per-item evidence. Windows/system/recovery, Docker, WSL, virtual disks, cloud data, unknown ProgramData, credential stores, databases, and similar locations are prohibited.

Only ReportOnly and ReviewFiles have Phase 5 representations. RecycleFiles, QuarantineFiles, TrustedToolCommand, WindowsSupportedCleanup, MoveKnownFolder, and ManualInstructions are closed typed vocabulary for future design; none executes. Rules cannot contain executable paths, shell text, scripts, generic argument arrays, or arbitrary roots. The first optional built-in action descriptor is the exact npm-cache segment and is report-only. Existing browser rules cover profile-level aggregates, so they remain InsufficientEvidence instead of being mislabeled as exact cache roots.

## Immutable plan

A plan contains schema version, random plan ID, application version, creation/expiry, source scan/snapshot, privacy-safe drive identity, verified rule-pack identity/version/digest, category registry version, application compatibility version, privacy mode, selection identity, root identities, sorted items, impact, risk, confidence, consequences, rollback potential, warnings, execution availability, and SHA-256 digest.

All collections are immutable and exposed as ImmutableArray or immutable maps. Changing selection creates a new ID and plan. Canonical UTF-8 JSON has fixed field and item ordering and contains no display-time recalculation. The SHA-256 digest detects accidental or malicious edits; it is not a signature and provides no signer authenticity.

Plans expire after at most ten minutes. Validation fails closed for schema/digest edits, expiry, missing or changed scan/snapshot, drive mismatch, rule-pack mismatch, category-registry mismatch, application incompatibility, privacy-mode mismatch, changed target identity/metadata, reparse state, protected paths, or unsupported target syntax.

## Path and TOCTOU preparation

Lexical Windows validation requires an absolute local drive path, normalizes separators, uses case-insensitive component-aware containment, and rejects traversal, sibling-prefix confusion, UNC/device namespaces, alternate data streams, environment substitution, trailing dots/spaces, ambiguous 8.3 aliases, reparse points, junctions, symlinks, and mount points. Protected components and file types always win.

Future exact targets can bind canonical path, approved-root identity, volume identity, stable file identity, logical size, creation/last-write time, attributes, reparse state, cloud state, source rule, and target state. Phase 5 never assumes the filesystem remains unchanged and never uses this metadata to authorize mutation.

## Accounting, consequences, and rollback

The UI and CLI say potential logical bytes affected, observed logical size, estimated space, eligible for review, or dry-run candidate. They never promise reclaimable or recovered bytes. Physical bytes remain null unless safely available. Limitations name allocation, hard links, compression, cloud placeholders, inaccessible/locked/changing files, filesystem metadata, and cache recreation.

Each built-in candidate explains what the data is, why it exists, possible regeneration, network/application/session effects, rollback limits, and unknowns. Rollback vocabulary is None, RecycleBinPotential, QuarantinePotential, ToolManaged, Manual, or Unknown; Phase 5 performs none of them.

## Persistence, export, CLI, and UI

Plans are memory-only, bounded to the latest 16 records in one process, and expire. Full raw target paths are not persisted by default. Discard removes only the process-local plan record. Export is explicit and writes a versioned support-safe report with a privacy-safe drive fingerprint, no raw user name, raw volume identifier, identity key, file content, or unrestricted path.

CLI commands are plan candidates, create, show, validate, export, and discard. There is no execute, apply, clean, delete, or prune command. Diagnostics use stderr and stable usage/not-found/unavailable categories.

Review Plan uses ResponsivePageHost, selects nothing by default, disables ineligible/protected choices, shows the dry-run banner, potential logical bytes, digest, expiry, risk, confidence, consequences, rollback, protected validation, stale status, and execution unavailability. Save dry-run report, Discard plan, and Done are the only final controls.

## Verification and manual release checks

Automated gates cover eligibility, immutability, deterministic canonical digest, changed selection, duplicate/empty/oversized input, overflow, stale/expired/tampered plans, path attacks, protected resources, rule descriptor integrity, export schema/privacy, disabled executor, CLI surface, repository primitives, ten-page UI selection/preview, scrolling, and responsive bounds.

Actual Windows High Contrast activation, 125%/150% DPI, and Windows text scaling remain manual release checks. They are not represented as completed.

## Phase 6 handoff

Phase 6 — Low-risk execution and elevated helper remains approval-gated. It must revalidate every target immediately before any action, implement only an explicitly approved tiny allowlist, journal and receipt outcomes, and separately prove elevation/IPC/TOCTOU behavior. Nothing in a Phase 5 plan grants authority to execute.

