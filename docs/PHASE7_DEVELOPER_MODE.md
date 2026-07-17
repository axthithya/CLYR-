# Phase 7 Developer Mode: read-only developer tool storage detection

Status: implementation complete across every reviewable surface — Core taxonomy/registry/probe, CLI, and WinUI
are built, tested (including 31 new deterministic Core tests, 9 new CLI tests, an extended Safety boundary test,
and a real rendered WinUI Automation run through the new detection flow), and verified against a real local
machine (actual Docker Desktop 29.1.2 and actual WSL were both correctly detected).

## Scope

Developer Mode becomes a real, read-only storage dashboard for a closed set of 14 developer tool families,
replacing the Phase 6 static preview page:

1. **Contracts** (`Clyr.Contracts.DeveloperMode`) — `DeveloperToolId`, `DeveloperToolStatus`,
   `DeveloperStorageCategory`, `DeveloperToolDescriptor`, `DeveloperToolReport`, `DeveloperToolProbeRequest`/
   `DeveloperToolProbeResult`, `DeveloperToolExecutableCandidate`.
2. **Taxonomy** (`Clyr.Core.DeveloperMode.DeveloperToolTaxonomy`) — a closed, compiled mapping from every
   developer-related built-in rule ID to a `DeveloperToolId`/`DeveloperStorageCategory`.
3. **Report builder** (`DeveloperToolReportBuilder`) — turns a `ScanResult`/`StorageSnapshot` into
   per-tool `DeveloperToolReport`s by reusing `CleanupCandidateFactory` unchanged (ADR-0015).
4. **Trusted discovery and probe** (`TrustedExecutableLocator`, `DeveloperToolProbeRunner`) — a narrow,
   closed-argument, read-only `docker --version` / `wsl --status` check for exactly two tools, never a
   user-supplied path or argument (ADR-0016).
5. **Registry** (`DeveloperToolRegistry`) — the closed, compiled list of all 14 tools and the orchestrator that
   combines classification with the narrow probe into an honest status per tool, never a false `NotInstalled`
   for a tool this phase cannot actually probe (ADR-0017).
6. **CLI** — `clyr developer tools|scan|show|findings|plan|capabilities|doctor`. Deliberately absent:
   `developer run`, `--command`, `--exe`, `--args`, `--path`, or any prune/clean-all subcommand.
7. **WinUI** (`DeveloperModePage`) — a snapshot picker, a "Detect developer tools" button, per-tool cards
   (status/version/observed bytes/finding count), a details panel per tool (diagnostics, per-finding
   eligibility/risk/consequence), and, only for findings the existing Phase 5 pipeline already marks
   `DryRunEligible`, a "Review in plan" button that builds a plan through the same
   `CleanupPlanBuilder`/`ICleanupPlanStore` path as every other finding and hands off to the Review Plan page.

## Built-in developer rules added this phase

Five new report-only rules in `rules/builtin/rules.yaml` (manifest bumped to `1.2.0`, digest recomputed):
`developer.yarn.cache`, `developer.pip.cache`, `developer.cargo.registry`, `developer.flutter.pubcache`,
`developer.buildoutput.generic`. All five are `status: Informational` or `Review`, `protected: false`, and — like
every prior rule — detection-only; none of them, on their own, changes what `CleanupCandidateFactory` decides is
executable.

## Hard boundaries (unchanged from the spec, verified by tests)

- No arbitrary command execution, no shell (`cmd.exe`/`powershell.exe`), no user-supplied path or argument
  anywhere in the developer-tool code path.
- No Docker volume deletion, no WSL unregister/VHDX deletion, no AVD deletion, no package install/update,
  no "prune everything"/"clean all developer tools" action, no network probe.
- The one new `Process.Start` call site (`DeveloperToolProbeRunner.cs`) is scoped exactly like Phase 6's
  `ElevatedHelperLauncher.cs` — `Clyr.Safety.Tests.RepositorySafetyTests` and `scripts/verify-phase0.ps1`'s
  repository-wide scan both enforce this by file path, not by broadening what they allow.
- `Clyr.Safety.Tests.UiArchitectureTests.DeveloperModePageHasNoToolExecutionOrRunControls` allowlists exactly
  two Click handlers (`DetectClick`, `CloseDetails`) in the page XAML and asserts no install/uninstall/prune/
  clean-now/execute-tool vocabulary appears anywhere in the page or its code-behind.
- `clyr developer capabilities` truthfully reports `executableDeveloperActions: []` — no developer-tool finding
  is allowlisted for Phase 6 execution; every one remains dry-run/report-only or manual-review.

## What is deliberately out of scope this phase

- **Docker/WSL storage numbers** come from classification (rule-based folder scanning of `containers.docker`/
  `virtualization.wsl`/`virtualization.vhdx`), not from parsing `docker system df`/`wsl -l -v` output — judged
  too fragile to build and verify reliably in this session (ADR-0016). Only installed/running *status* comes
  from the real probe.
- **Move-to-another-drive workflows** remain Phase 8 and are not touched.
- **Docker/WSL/Android emulator storage remains `Protected`** in `CleanupCandidateFactory` exactly as before —
  Developer Mode surfaces these findings for visibility only; nothing about this phase changes their eligibility.

## Verification

`scripts/verify-phase7.ps1` chains `scripts/verify-phase6.ps1 -SkipInteractiveUac` (the full Phase 0–6 gate,
skipping the Phase 6 interactive-desktop-only UAC smoke test, which is a Phase 6 concern unrelated to Developer
Mode) and adds: a warning-free Release build and full test run, a formatting check, the new Developer Mode Core
tests, the new `Phase7DeveloperModeCliTests`, the extended Safety test project, a dependency vulnerability
audit, the Phase 7-specific repository safety scans described above, credential/machine-path/whitespace scans,
and (unless `-SkipUiAutomation`) the extended `scripts/verify-winui.ps1`, which now also drives the real
Developer Mode detection flow (select a snapshot, detect, open a tool's details, close them) and re-asserts no
forbidden control appeared. See the completion report for the exact command-by-command results from this
session's run.
