# CLYR

> See what filled your C: drive. Understand it. Clear it safely.

CLYR is a planned native Windows storage diagnostic application for people who can see that a drive is full but cannot safely tell why. It will lead with evidence: what occupies space, how confident the measurement is, which regions were inaccessible, and why a finding needs attention. The reusable C# engine will support a WinUI 3 desktop experience and a command-line interface.

## Project status: Phase 3 detection-only classification complete

**The repository now contains the Phase 3 metadata classifier and explanation surfaces, but no cleanup feature.** One bounded streaming pass assigns mutually exclusive storage categories, secondary tags, confidence, informational status, protected precedence, explicit Unknown/coverage gaps, and privacy-safe findings. The first-party pack must pass offline manifest, compatibility, category-registry, and SHA-256 verification before activation. External rules can be linted but never auto-activate.

The primary target is Windows 11. Windows 10 22H2, ReFS, removable media, and packaged/unpackaged variants are **unverified**, not supported claims. See the [support matrix](docs/SUPPORT_MATRIX.md).

## Why CLYR

Most storage tools start with a list of large paths or a dramatic reclaimable-space number. CLYR starts with the user's question: **“Why is my C: drive full?”** It keeps logical size, physical allocation, movable data, review candidates, protected storage, and unknown or inaccessible space separate. Size alone is never evidence that content is safe to remove.

CLYR's promise is transparency: nothing is silently removed, every recommendation is explained, and every future action must be previewed. “Junk” is contextual, so the product does not use that term as a blanket classification.

## Planned experience

1. Discover the actual Windows system volume and offer **Analyze C:** when C: is appropriate.
2. Run a cancellable, read-only quick analysis without following reparse points or hydrating cloud placeholders.
3. Show progressive results, scan coverage, largest causes, protected areas, and uncertainty.
4. Explain each finding and export a privacy-safe report.
5. In later phases, compare snapshots and answer “What grew?”
6. Only after dedicated security phases, generate an immutable dry run before any opt-in action.

Screenshots remain deferred until the demo-only shell is reviewed. No mock screenshot is presented as a working product.

## Installation and quick start

There is no installer or public package. On Windows 11 x64, use .NET SDK 10.0.301 and the stable Windows App Runtime 2.2 developer prerequisite, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-phase2.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-winui.ps1
```

The first command runs every Phase 0/1 regression plus Phase 2 scanner, Windows-adapter, schema, bounded-memory, and CLI drive-discovery gates. It deliberately does not start a real-drive scan. The second launches the unpackaged WinUI build, verifies navigation, drive overview, Quick/Deep controls, cancellation, and the read-only disclosure, then closes the process without scanning.

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
```

## Safety and privacy

- The early product is read-only; no cleanup controls are present.
- The main app remains non-elevated. A future helper, if approved, is short-lived and accepts only typed, allowlisted actions.
- Protected Windows resources, personal content, virtual disks, credential stores, application databases, cloud placeholders, and unknown content are never cleanup targets merely because they are large.
- Reparse points are not followed by default.
- Core decisions work offline; there is no telemetry or cloud upload by default.
- Summary exports redact usernames and paths. Detailed local exports require an explicit warning and are never uploaded automatically.

Read [SAFETY_MODEL.md](docs/SAFETY_MODEL.md), [PRIVACY.md](PRIVACY.md), and [SECURITY.md](SECURITY.md) before proposing behavior that touches user data.

## Architecture

CLYR uses C# 14 on .NET SDK 10.0.301, WinUI 3 on Windows App SDK 2.2.0, SQLite through the explicit 3.50.4.5 native bundle, constrained YAML rules validated against Draft 2020-12 JSON Schema, xUnit, and GitHub Actions on Windows runners. All external versions are centrally pinned.

Build from the repository root with the workspace-local SDK or an installed .NET 10.0.301 SDK:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-phase2.ps1
```

The UI and CLI depend on application services; domain logic does not depend on Windows UI. Windows APIs and external tools are isolated behind adapters. Declarative community rules can detect and explain but cannot contain executable commands. See [ARCHITECTURE.md](docs/ARCHITECTURE.md) and [TECH_STACK.md](docs/TECH_STACK.md).

## Contributing

Start with [CONTRIBUTING.md](CONTRIBUTING.md). Rule proposals must be detection-only until the action phase, validate against the schema, include fixtures and risk metadata, and prove they cannot cross protected roots. Security-sensitive changes require designated review.

The complete plan is in [WHOLEPLAN.md](WHOLEPLAN.md), the phase sequence is in [ROADMAP.md](ROADMAP.md), and current evidence is in [PHASE_STATUS.md](PHASE_STATUS.md).

## License

Apache License 2.0. See [LICENSE](LICENSE).
