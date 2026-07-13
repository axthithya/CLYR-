# ADR 0006: Phase 1 developer packaging is unpackaged

- Status: Accepted for Phase 1 only
- Date: 2026-07-13

## Decision

The Phase 1 WinUI shell builds as an unpackaged, framework-dependent Windows application with `WindowsPackageType=None`, Windows App SDK 2.2.0, and an `asInvoker` manifest. This is a developer validation topology, not a public installer decision.

## Rationale

The phase must prove the stable WinUI toolchain and application shell without reserving a production identity, requesting elevation, registering startup behavior, or solving the future multi-executable helper topology prematurely. Single-project MSIX constraints still need resolution before the separately gated Phase 6 helper can exist.

## Consequences

- Developers need the .NET 10 SDK and compatible Windows App SDK runtime/tooling.
- No installer, package identity, updater, service, scheduled task, startup entry, or elevation capability is produced.
- Phase 9 retains the final signing, MSIX, distribution, and update decision.
