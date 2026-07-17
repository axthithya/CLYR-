# CLYR

> Phase 6 adds one narrowly allowlisted, low-risk, non-elevated cleanup action (CLYR's own stale temporary artifacts) end to end — engine, elevated-helper architecture, typed IPC, receipts, CLI, and WinUI — plus the full immutable dry-run planning from Phase 5 for everything else. Phase 6 is implementation-complete but not approved: the required real fixture-only UAC smoke test has not been run. See docs/PHASE6_EXECUTION.md.

> Phase 7 adds read-only Developer Mode detection for 14 developer tool families (Docker, WSL, Node/npm, pnpm, Yarn, .NET/NuGet, Gradle, Maven, Python/pip, Rust/Cargo, Flutter/Dart, Android SDK, Playwright, generic build output) — a closed tool taxonomy over existing findings, trusted name-only executable discovery, a narrow read-only Docker/WSL status probe, CLI, and a WinUI dashboard. No developer-tool action is enabled for execution; eligible findings route through the existing Phase 5 plan pipeline. See docs/PHASE7_DEVELOPER_MODE.md.

> See what filled your C: drive. Understand it. Clear it safely.

CLYR is a planned native Windows storage diagnostic application for people who can see that a drive is full but cannot safely tell why. It will lead with evidence: what occupies space, how confident the measurement is, which regions were inaccessible, and why a finding needs attention. The reusable C# engine will support a WinUI 3 desktop experience and a command-line interface.

## Project status: Phase 7 Developer Mode implemented and verified, awaiting approval (Phase 6 UAC smoke test still pending)

Eligible built-in findings can produce immutable ten-minute plans bound to their scan or snapshot, drive, rule pack, compatibility version, privacy mode, and explicit selection. Canonical SHA-256 digests detect edits; they are integrity checks, not authorization. Protected findings always win, user-created data remains review-only, and production execution returns ExecutionNotAvailableInPhase5.

### Preserved Phase 4.1 baseline

**The repository contains read-only scanning, detection-only classification, local aggregate history, deterministic comparison, and a redesigned WinUI shell—but no cleanup feature.** Overview, Scan, Results, History, Developer Mode, Privacy, Licenses, About, and Settings are distinct pages. Full scan controls exist only on Scan. A fixture-only UI mode supports safe automation without inspecting a real drive.

The primary target is Windows 11. Windows 10 22H2, ReFS, removable media, and packaged/unpackaged variants are **unverified**, not supported claims. See the [support matrix](docs/SUPPORT_MATRIX.md).

## Why CLYR

Most storage tools start with a list of large paths or a dramatic reclaimable-space number. CLYR starts with the user's question: **“Why is my C: drive full?”** It keeps logical size, physical allocation, movable data, review candidates, protected storage, and unknown or inaccessible space separate. Size alone is never evidence that content is safe to remove.

CLYR's promise is transparency: nothing is silently removed, every recommendation is explained, and every future action must be previewed. “Junk” is contextual, so the product does not use that term as a blanket classification.

## Planned experience

1. Discover the actual Windows system volume and offer **Analyze C:** when C: is appropriate.
2. Run a cancellable, read-only quick analysis without following reparse points or hydrating cloud placeholders.
3. Show progressive results, scan coverage, largest causes, protected areas, and uncertainty.
4. Explain each finding and export a privacy-safe report.
5. Compare local aggregate snapshots and answer “What grew?” without retaining a file inventory.
6. Only after dedicated security phases, generate an immutable dry run before any opt-in action.

No mock screenshot is presented as a working product.

## Installation and quick start

There is no installer or public package. On Windows 11 x64, use .NET SDK 10.0.301 and the stable Windows App Runtime 2.2 developer prerequisite, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-phase5.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-winui.ps1
```

The first command runs all Phase 0–4.1 regressions plus Phase 5 build, tests, formatting, planning, digest, protected-path, stale-plan, schema, CLI, repository-safety, and UI gates. The second launches the fixture-only unpackaged WinUI build and verifies all ten pages from 1600×900 through the documented 800×600 minimum without inspecting a real drive.

The Phase 3 CLI keeps explicit read-only drive discovery/scanning and adds offline rule inspection and report explanation:

```text
clyr --help
clyr --version
clyr doctor
clyr demo
clyr drives [--json]
clyr scan C:\ [--quick|--deep] [--top N] [--json] [--output <file>]
clyr rules validate <path>
clyr rules list
clyr rules verify
clyr rules describe <rule-id>
clyr explain <classified-report.json>
clyr plan candidates --snapshot <id> [--json]
clyr plan create --snapshot <id> --finding <id> [--finding <id>] [--json]
clyr plan show <plan-id> [--json]
clyr plan validate <plan-id> [--json]
clyr plan export <plan-id> --output <plan.json>
clyr plan discard <plan-id>
clyr plan execute <plan-id> --confirm-digest <prefix> [--json]
clyr execution status|receipt|list|export --output <path>|discard-receipt <execution-id>
```

## Safety and privacy

- CLYR can execute exactly one narrowly allowlisted, low-risk action — removing its own stale temporary artifacts under `%LocalAppData%\Clyr\Temp` — with an exact bounded manifest, a one-time token, and independent per-target revalidation immediately before deletion. Nothing else can be executed; every other finding remains dry-run/report-only, exactly as in Phase 5.
- The main app remains non-elevated (`asInvoker`). A separate, one-shot elevated helper (`Clyr.ElevatedHelper`) exists for any future action that needs elevation, but the one enabled action today does not use it.
- Protected Windows resources, personal content, virtual disks, credential stores, application databases, cloud placeholders, and unknown content are never cleanup targets merely because they are large.
- Reparse points are not followed by default.
- Core decisions work offline; there is no telemetry or cloud upload by default.
- Summary exports redact usernames and paths. Detailed local exports require an explicit warning and are never uploaded automatically.

Read [SAFETY_MODEL.md](docs/SAFETY_MODEL.md), [PRIVACY.md](PRIVACY.md), and [SECURITY.md](SECURITY.md) before proposing behavior that touches user data.

## Architecture

CLYR uses C# 14 on .NET SDK 10.0.301, WinUI 3 on Windows App SDK 2.2.0, SQLite through the explicit 3.50.4.5 native bundle, constrained YAML rules validated against Draft 2020-12 JSON Schema, xUnit, and GitHub Actions on Windows runners. All external versions are centrally pinned.

Build from the repository root with the workspace-local SDK or an installed .NET 10.0.301 SDK:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-phase41.ps1
```

The UI and CLI depend on application services; domain logic does not depend on Windows UI. Windows APIs and external tools are isolated behind adapters. Declarative community rules can detect and explain but cannot contain executable commands. See [ARCHITECTURE.md](docs/ARCHITECTURE.md) and [TECH_STACK.md](docs/TECH_STACK.md).

## Contributing

Start with [CONTRIBUTING.md](CONTRIBUTING.md). Rule proposals must be detection-only until the action phase, validate against the schema, include fixtures and risk metadata, and prove they cannot cross protected roots. Security-sensitive changes require designated review.

The complete plan is in [WHOLEPLAN.md](WHOLEPLAN.md), the phase sequence is in [ROADMAP.md](ROADMAP.md), and current evidence is in [PHASE_STATUS.md](PHASE_STATUS.md).

## License

Apache License 2.0. See [LICENSE](LICENSE).
