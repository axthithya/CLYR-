# ADR 0008: Phase 2 metadata-only scanner boundary

- Status: Accepted for Phase 2
- Date: 2026-07-13
- Decision owners: Scanner, privacy, and Windows platform

## Context

Phase 2 must answer which accessible regions occupy a selected local drive without turning a size scan into content inspection, cloud hydration, privilege escalation, persistence, or cleanup. Allocated-size and hard-link identity APIs add platform-specific complexity that has not yet been proven across the supported matrix.

## Decision

Implement one UI-independent, depth-first streaming scanner shared by WinUI and CLI. Phase 2 supports ready fixed NTFS volumes, requires an exact drive-root input, skips all reparse points, observes cloud placeholders from attributes without hydration, reads logical length metadata only, retains bounded top-N/grouped errors, throttles progress to four updates per second, and preserves partial results on cancellation or isolated errors.

Quick Analysis is limited to three directory levels with a smaller default top-N. Deep Analysis traverses accessible directories with a defensive depth bound and larger top-N. Both remain metadata-only. Phase 2 does not deduplicate hard links and therefore labels observed logical totals Estimated with an explicit possible-double-count note. Allocated bytes are Unavailable rather than guessed.

Results stay in memory. The only write path is an explicit user-selected privacy-safe JSON export; export substitutes ranked path tokens and declares that paths, usernames, filenames, and contents are excluded. No scan starts from `doctor`, `drives`, UI launch, tests, or verification scripts.

## Consequences

- Entry-count growth does not cause proportional retained model growth.
- Inaccessible or changing entries reduce coverage instead of becoming zero-byte fiction.
- Quick is intentionally incomplete and exposes its depth-limit warning.
- Logical totals can differ from drive-used space; unaccounted space remains visible.
- Allocated-size, hard-link identity, snapshots, classification, and explanations remain separately gated work.

## Validation

Fake adapters cover hostile roots, large streams, reparse loops, cloud placeholders, access denial, cancellation, overlap, depth limits, and privacy. Isolated temporary fixtures exercise the Windows metadata adapter. Synthetic 10k, 100k, and 1M streams record timing and retained managed memory. Safety tests reject mutation/process APIs, and the Phase 2 verifier never starts a real-drive scan.
