# ADR 0016: Trusted executable discovery and a narrow, closed-argument read-only probe

- Status: Implemented in Phase 7
- Date: 2026-07-17
- Scope: `Clyr.Core.DeveloperMode.TrustedExecutableLocator`, `Clyr.Core.DeveloperMode.DeveloperToolProbeRunner`,
  `Clyr.Core.DeveloperMode.DeveloperToolRegistry`

## Context

Two of the fourteen Phase 7 tools (Docker Desktop, WSL) are best identified by asking the tool itself rather
than only inferring from on-disk folders — Docker and WSL manage their own storage (images, containers, virtual
disks) largely outside any single well-known folder CLYR can safely enumerate. That requires launching a real
process, which is exactly the kind of primitive Phase 6 (ADR-0002, ADR-0012) confined to one reviewed launcher.
Phase 7 must add a second, legitimate process-launch call site without loosening that boundary into a general
"run a command" capability.

The spec's hard boundary is explicit: no arbitrary command execution, no shell (`cmd.exe`/`powershell.exe`), no
user-supplied path or arguments, no terminal, and the probe must never be able to mutate anything — Docker
volume deletion, WSL unregister/VHDX deletion, and AVD deletion are explicitly prohibited even indirectly.

## Decision

Two narrow, closed components, each reviewed and scoped as tightly as `ElevatedHelperLauncher`:

1. **`TrustedExecutableLocator`** (`IDeveloperToolExecutableLocator`) resolves an executable path for a
   `DeveloperToolDescriptor` by name only: an exact filename match (`docker.exe`, `wsl.exe`) against `PATH`,
   falling back to a depth-bounded (4 levels) search of `ProgramFiles`, `ProgramFilesX86`, `System`, and
   `LocalApplicationData`. It never accepts a user-supplied path, never searches the whole drive, and rejects
   anything that is not a real `.exe` file or that is a reparse point (`FileAttributes.ReparsePoint`) — a
   symlink/junction cannot be used to redirect discovery to an attacker-controlled binary.
2. **`DeveloperToolProbeRunner`** (`IDeveloperToolProbeRunner`) launches exactly the executable path the locator
   returned, with a fixed, hard-coded argument list per tool (`docker.exe --version`, `wsl.exe --status` — see
   `DeveloperToolRegistry.ProbeAsync`), `UseShellExecute = false` (no shell interpretation of the command line),
   `CreateNoWindow = true`, arguments passed via `ArgumentList` (never string-concatenated, so there is no
   injection surface even in principle), a bounded timeout (5 seconds) that kills the process tree on expiry,
   and bounded stdout reading (4096 bytes, `OutputTruncated` reported rather than reading further). There is no
   parameter anywhere in the CLI, WinUI, or `IDeveloperToolProbeRequest` that lets a caller change the
   executable path or the argument list — both are hard-coded per `DeveloperToolId` inside
   `DeveloperToolRegistry`, not passed in from outside.

`DeveloperToolRegistry.DetectAllAsync` is the only caller of either component in production code, and only for
the two tools whose `DeveloperToolDescriptor.RequiresProbe` is `true`. The other twelve tools never launch a
process at all — their status comes entirely from classification (rule-based folder detection), consistent with
the spec's fallback rule: "If a tool has no reliable machine-readable probe, fall back to known-root metadata
scanning rather than fragile command parsing."

`Clyr.Safety.Tests.RepositorySafetyTests` extends its existing "process launch only inside the reviewed
boundary" scan to allow exactly one additional file, `DeveloperToolProbeRunner.cs`, and adds a dedicated test
(`DeveloperModeBoundaryNeverMutatesToolsAndOnlyEverAsksForStatus`) asserting no mutating Docker/WSL subcommand
text (`system prune`, `volume rm`, `--unregister`, etc.) appears anywhere under `src/Clyr.Core/DeveloperMode/`.
`scripts/verify-phase0.ps1`'s repository-wide `Process\.Start` scan is extended the same way, by file path, not
by broadening the pattern.

## Consequences

- Docker/WSL status detection genuinely works against a real installation (verified live in this session:
  actual Docker Desktop 29.1.2 and actual WSL were both correctly detected through this exact path) without any
  new general-purpose execution capability existing anywhere in the app.
- A future tool that *does* need argument-bearing probing (e.g. `docker system df --format json`) is explicitly
  out of scope for this phase — parsing tool-specific JSON/text output reliably was judged too risky to build
  and verify without extensive real-environment testing in this session, so Docker/WSL storage numbers still
  come from classification (rule-based folder scanning), and only their installed/running *status* comes from
  the probe. This is a known, documented limitation, not a silent gap.
- WSL's `--status` output format did not match the version-extraction regex in local testing (a cosmetic
  "version: unknown" outcome) — the tool's `FullyDetected` status itself was unaffected, since status derives
  from probe success plus classification evidence, not from whether a version string could be parsed out.
