# ADR 0001: Native Windows Stack

- **Status:** Accepted; Phase 1 versions verified
- **Date:** 2026-07-10
- **Decision owners:** Product architecture and Windows platform

## Context

CLYR must enumerate local volumes, use Windows filesystem/cloud/package APIs, remain responsive during long scans, support selective future elevation, integrate the Recycle Bin/known folders, and ship as a Windows desktop product. The core and CLI must remain reusable and testable without UI dependencies.

## Decision

Use C# on the latest patched .NET 10 LTS, WinUI 3 on the stable Windows App SDK channel, MVVM, and a UI-independent Core behind narrow Windows adapters. Use a .NET console CLI over the same application services. Target signed single-project MSIX first after hardening. Exact SDK, Windows App SDK, and package versions are verified and centrally pinned at Phase 1 start; preview/experimental packages are excluded from release builds.

## Consequences

- Native Windows capabilities and Fluent/accessibility integration are available without a browser server.
- C# contracts can be shared by app, CLI, tests, and a future minimal helper.
- WinUI packaging/build/tooling increases Windows-specific test and signing work.
- The project remains Windows-first, not cross-platform.
- Phase 1 verified .NET SDK 10.0.301, Windows App SDK 2.2.0, Windows target 10.0.26100.0, and an unpackaged framework-dependent `win-x64` launch.
- Production MSIX identity, signing, clean installation, and lifecycle evidence remain Phase 9 responsibilities.

## Alternatives

- **Electron/web server:** rejected for attack surface, runtime size, and mismatch with required native capabilities.
- **WPF/WinForms:** mature but conflict with the fixed WinUI 3 decision absent a proven blocker.
- **UI-specific business logic:** rejected because safety policies require headless fixture tests and CLI reuse.
- **Native C++ throughout:** greater API control but higher memory-safety and contribution cost for no established MVP need.

## Validation

Phase 1 proved restore, warning-free Release build, WinUI launch/navigation, CLI commands, architecture boundaries, and pinned Windows runner CI. Package identity and full accessibility certification remain later gates; no silent UI-framework substitution is allowed.
