# Proposed GitHub Project Setup

This repository has no remote in Phase 0. These milestones, labels, templates, owners, and branch rules are proposals to apply after maintainers create and authorize a GitHub repository. Local metadata does not create issues, teams, rulesets, releases, or a remote.

## First three milestones

### M1 — Phase 1: Engineering foundation

Goal: buildable non-destructive WinUI/CLI/demo/test skeleton. Exit requires pinned supported toolchain, Release build/tests, app launch, CLI help, architecture boundaries, rule validation, CI, no admin, and no real scan/action. Suggested issues cover SDK/Windows App SDK verification; solution/projects; analyzer/format/central packages; DI/config/logging; contracts/SQLite foundation; fake filesystem/process/demo; UI/CLI shells; tests/CI/dependency and license inventory.

### M2 — Phase 2: Read-only scanner MVP

Goal: safe selected-volume observation with progressive/partial explanation inputs. Exit requires no-write proof, bounded streaming, cancellation, reparse protection, error/coverage truth, responsive UI, CLI JSON, fixtures, and benchmarks. Cleanup controls and action rules are excluded.

### M3 — Phase 3: Trustworthy findings and reports

Goal: detection-only rule engine, protected precedence, deterministic overlap, explanations, “Why is this drive full?”, and privacy-safe report. Exit requires malicious-rule rejection, fixtures/evidence for every built-in, unknown remaining unknown, and no executable rule action.

## Proposed labels

| Label | Color | Meaning |
|---|---|---|
| `phase:0` … `phase:10` | `5319E7` | Earliest approved phase; later work is not pulled forward. |
| `type:bug` | `D73A4A` | Reproducible defect. |
| `type:feature` | `A2EEEF` | Product capability proposal. |
| `type:docs` | `0075CA` | Documentation/research/decision change. |
| `type:rule` | `BFDADC` | Detection rule/schema/fixture work. |
| `type:security` | `B60205` | Private-first security-sensitive work. |
| `type:false-positive` | `E99695` | Finding evidence/classification may be unsafe. |
| `area:scanner` | `1D76DB` | Enumeration, metadata, accounting, coverage. |
| `area:safety` | `B60205` | Protection, paths, plans, actions. |
| `area:privacy` | `C5DEF5` | Logs, retention, export/redaction. |
| `area:ui-accessibility` | `FBCA04` | UX, WinUI, accessibility. |
| `area:rules` | `BFD4F2` | Rule engine/packs/schema. |
| `area:persistence` | `006B75` | SQLite/snapshots/journals. |
| `area:packaging-release` | `0052CC` | MSIX, signing, update, supply chain. |
| `risk:prohibited` | `7A0000` | Conflicts with a hard safety boundary. |
| `risk:high-review` | `B60205` | Needs security/protected-owner review. |
| `status:needs-evidence` | `FBCA04` | Official source, fixture, or measurement missing. |
| `status:blocked` | `D4C5F9` | External decision/capability blocks progress. |
| `good first issue` | `7057FF` | Bounded, non-sensitive contribution. |

Risk labels do not replace domain `RiskLevel` values.

## Templates and ownership

Issue forms cover bug, false-positive, new rule (report-only at current phase), feature, and a security redirect. The PR template requires safety/protected/privacy impact, tests, accessibility, schema/migration notes, and evidence.

`CODEOWNERS` uses a provisional `@clyr/reviewers` team for safety, helper, actions/schemas, privacy/export, persistence migrations, and workflow/release paths. **This token is not claimed to exist.** Hosting is blocked until real organization/team or verified handles replace it and GitHub validates the file. Never merge a sensitive change merely because an unresolved owner requested no review.

After hosting, require PRs, verified sensitive CODEOWNER approval and dismissal on new commits, active phase CI, resolved conversations, restricted workflow/release/environment changes, signed release artifacts/tags, protected production environments, no untrusted-fork secrets, and private vulnerability reporting before release.

## Automation boundaries

Phase 0 CI validates documentation/schemas only. Phase 1 adds restore/format/Release build/unit/safety/contract/architecture/rule/dependency/license/no-secret gates and package dry run only when inputs exist. Phase 9 alone adds publication. Community scripts and release/update actions never run automatically from untrusted rule contributions.
