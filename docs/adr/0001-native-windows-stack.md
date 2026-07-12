# ADR 0001: Native Windows Stack

- **Status:** Accepted for planning; exact versions pending Phase 1 verification
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
- The current host has no .NET SDK, so Phase 1 is blocked until a supported .NET 10 SDK is installed and verified.
- Stable-channel version evidence is currently inconsistent across official pages; Phase 1 must reconcile release notes, NuGet, templates, and support lifecycle rather than copying a stale number.

## Alternatives

- **Electron/web server:** rejected for attack surface, runtime size, and mismatch with required native capabilities.
- **WPF/WinForms:** mature but conflict with the fixed WinUI 3 decision absent a proven blocker.
- **UI-specific business logic:** rejected because safety policies require headless fixture tests and CLI reuse.
- **Native C++ throughout:** greater API control but higher memory-safety and contribution cost for no established MVP need.

## Validation

Phase 1 must prove restore, Release build, launch, accessibility shell, package identity behavior, CLI help, architecture boundaries, and Windows runner CI. A documented blocker requires a superseding ADR; no silent UI-framework substitution is allowed.
