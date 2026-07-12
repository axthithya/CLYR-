# CLYR

> See what filled your C: drive. Understand it. Clear it safely.

CLYR is a planned native Windows storage diagnostic application for people who can see that a drive is full but cannot safely tell why. It will lead with evidence: what occupies space, how confident the measurement is, which regions were inaccessible, and why a finding needs attention. The reusable C# engine will support a WinUI 3 desktop experience and a command-line interface.

## Project status: Phase 0 complete — documentation only

**There is no installable application, scanner, or cleanup feature in this repository yet. Do not use this repository to modify real files.** Phase 0 defines the product, safety boundaries, architecture, schemas, UX, and verification strategy. Phase 1 will create a non-destructive solution skeleton; real-drive read-only scanning is not planned until Phase 2. Cleanup execution is not planned until Phase 6 and will require a separate security gate.

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

Screenshots will be added from the isolated demo-data mode after the Phase 1 UI shell exists. No mock screenshot is presented as a working product.

## Installation and quick start

Installation is not available in Phase 0. For a documentation review:

```powershell
git clone <future-repository-url>
cd clyr
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-phase0.ps1
```

The repository currently has no configured remote, so `<future-repository-url>` is intentionally illustrative. The local verification script checks documentation, schemas, examples, naming, safety language, and obvious destructive code patterns. Mermaid rendering is an optional additional gate when Mermaid CLI is available.

These are **planned**, non-functional CLI examples for later read-only phases:

```text
clyr drives
clyr scan C:
clyr scan C: --json
clyr rules validate
clyr doctor
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

CLYR uses C# on the latest patched .NET 10 LTS, WinUI 3 on the stable Windows App SDK channel, MVVM, SQLite, versioned YAML rules validated by JSON Schema, xUnit, and GitHub Actions on Windows runners. Exact versions are deliberately deferred to Phase 1 verification and central pinning.

The UI and CLI depend on application services; domain logic does not depend on Windows UI. Windows APIs and external tools are isolated behind adapters. Declarative community rules can detect and explain but cannot contain executable commands. See [ARCHITECTURE.md](docs/ARCHITECTURE.md) and [TECH_STACK.md](docs/TECH_STACK.md).

## Contributing

Start with [CONTRIBUTING.md](CONTRIBUTING.md). Rule proposals must be detection-only until the action phase, validate against the schema, include fixtures and risk metadata, and prove they cannot cross protected roots. Security-sensitive changes require designated review.

The complete plan is in [WHOLEPLAN.md](WHOLEPLAN.md), the phase sequence is in [ROADMAP.md](ROADMAP.md), and current evidence is in [PHASE_STATUS.md](PHASE_STATUS.md).

## License

Apache License 2.0. See [LICENSE](LICENSE).
