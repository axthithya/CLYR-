# Executive Brief

## The problem

A nearly full Windows system drive creates urgency but not understanding. Windows users encounter opaque system-managed storage, per-user caches, application data, virtual disks, build output, downloads, and inaccessible regions. Existing tools often expose a treemap or a broad “clean” button without separating physical allocation from logical size, protected content from candidates, or evidence from guesswork. A wrong decision can remove personal data, break tools, or recover less space than promised.

## The product

CLYR is a native Windows desktop diagnostic and recovery product with a reusable command-line engine. Its first job is to answer **“Why is my C: drive full?”** It discovers the actual system volume, produces a progressive read-only analysis, makes uncertainty and skipped regions visible, explains findings, and exports a privacy-safe support report. The core stays drive-agnostic while the product journey is deliberately C:-first.

> **Tagline:** See what filled your C: drive. Understand it. Clear it safely.

> **Promise:** Nothing is silently removed, every recommendation is explained, and every future action is previewed.

## Differentiated value

CLYR separates values that other tools commonly collapse: logical bytes, allocated bytes, hard-link-adjusted allocation, cloud-placeholder local bytes, known reclaimable lower bounds, estimates, review candidates, movable content, protected content, unknown content, and scan coverage. It never treats size or age alone as proof of waste. Detection rules are declarative and versioned; community rules cannot execute arbitrary programs. Future cleanup is gated behind immutable plans, immediate target revalidation, typed adapters, post-action measurement, and receipts.

## Users

- **Primary — everyday Windows user:** needs a calm explanation and safe next step, not filesystem expertise.
- **Primary — developer/creator:** needs Docker, WSL, package-cache, emulator, build-output, and virtual-disk context without automated removal.
- **Secondary — gamer:** needs large launcher, recording, shader-cache, archive, and save-data distinctions.
- **Secondary — support engineer:** needs a redacted, deterministic report that can be shared without exposing names or personal paths.
- **Secondary — contributor/security reviewer:** needs schemas, fixtures, safety tests, and review boundaries for extending detection.

## Product sequence

The first useful release is read-only: drive discovery, scanning, explanation, export, snapshots, and growth comparison. Dry-run planning follows only after rule and protection hardening. Execution begins later with a tiny allowlist of user-selected, low-risk actions. Developer integrations and move workflows follow one adapter at a time. Packaging, signing, accessibility, and beta hardening precede any public-beta claim.

## Trust boundaries

The normal app and CLI do not run as administrator. They do not take ownership, change ACLs, hydrate cloud content, follow reparse points by default, or execute rule-supplied shell text. Protected system paths, documents, media, projects, browser profiles, credentials, application databases, Docker volumes, WSL/VM disks, game saves, and unknown data remain protected or review-only. A future elevated helper is short-lived, authenticates its caller, accepts only versioned capability-bound requests, revalidates targets, returns a receipt, and exits.

## Success and current status

MVP success is a user installing CLYR, scanning the system volume, understanding the largest causes, seeing uncertainty and protected areas, and exporting a privacy-safe report without CLYR modifying any user or system file. Quality is measured locally in controlled fixtures—completion, cancellation latency, peak memory, classification confidence, false positives, accessibility, and a protected-path violation count that must remain zero. No telemetry is required.

Phase 0 establishes this contract. There is currently no application, scanner, cleanup code, package, or supported runtime. Phase 1 creates a buildable, non-destructive skeleton; Phase 2 begins read-only scanning.
