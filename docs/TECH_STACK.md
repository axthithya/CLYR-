# Technical Stack

## Status

The Phase 0 technology decisions remain the baseline. Phase 1 implements the pinned developer foundation recorded below; scanner, cleanup, installer, signing, updater, and release artifacts remain absent.

The current machine is not a viable build host: it has the x64 .NET 8.0.28 runtimes but **no .NET SDK**. This blocks templates, restore, build, tests, and MSIX packaging. The safe fallback is documentation-only work; it is not permission to substitute another UI or runtime.

## Governing constraints

- Product: native Windows desktop application plus a reusable CLI engine.
- Primary platform: Windows 11, standard-user process.
- Language/runtime: C# on the latest supported patch of .NET 10 LTS.
- UI: WinUI 3 from the Stable Windows App SDK channel.
- Architecture: MVVM presentation over UI-independent application/core services.
- Persistence: SQLite through `Microsoft.Data.Sqlite`.
- Rules: versioned YAML instances validated against JSON Schema Draft 2020-12.
- Tests: xUnit.
- Distribution: signed MSIX first; Microsoft Store preferred for public delivery.
- Privacy: local structured logs with no telemetry or network sink by default.
- Prohibited substitutions: Electron, embedded browser server, preview/experimental production packages, UI-dependent core, or silent administrator requirement.

## Selected technology families

“Selected” fixes the technology family or boundary. It does not approve an unpinned package.

| Area | Selection and purpose | Why selected | Trade-offs and security implications | Version/update/fallback policy |
|---|---|---|---|---|
| Client OS | Windows 11 desktop, x64 first | Matches the storage-diagnostics problem and required filesystem/packaging APIs. | Windows-only; support claims require lifecycle, install, accessibility, filesystem, and clean-machine evidence. | Test all claimed Windows releases before beta. Remove an OS from support when Microsoft servicing or CLYR test coverage ends. |
| Runtime | .NET 10 LTS, target `net10.0`; Windows-facing projects use an explicit Windows TFM | Current LTS and fixed by the bootstrap specification. | Framework-dependent deployment reduces bundled runtime size but adds a prerequisite; self-contained deployment increases size and makes CLYR responsible for republishing runtime fixes. | Pin a current supported SDK in `global.json` during Phase 1. Publishing mode remains undecided until install/update measurements; no distributable is the fallback. |
| Language | C# with compiler defaults for the pinned .NET 10 SDK | Native .NET/WinUI language with strong async, cancellation, and type support. | Preview features would bind source to an unstable compiler. | No `LangVersion=preview`. Nullable reference types and first-party warnings are enabled. |
| Desktop UI | WinUI 3, C# packaged app, Stable Windows App SDK | Native Windows UI, Fluent/accessibility integration, and MSIX template path. | Windows App SDK is independently serviced. The current official sources conflict on the latest stable version. WinUI/packaging tooling may require Visual Studio components beyond the CLI SDK. | Exact stable package deferred to Phase 1 reconciliation. Never use Preview/Experimental in release builds. If Stable cannot build, document the blocker or use a still-supported prior Stable through an ADR; do not change UI frameworks. |
| Presentation pattern | MVVM with `CommunityToolkit.Mvvm` as the preferred helper family | Microsoft-maintained, platform-agnostic implementation that reduces command/property boilerplate. | Source generators/analyzers add build inputs and generated code; view models can become service locators if boundaries are ignored. | Pin only after package/license/analyzer verification. Fallback is small first-party MVVM primitives, not code-behind business logic. |
| Core/application | Plain .NET class libraries with interfaces at OS, clock, persistence, process, and filesystem boundaries | Reuse between WinUI and CLI; deterministic fixture tests without real-drive mutation. | More explicit adapters and DTO mapping. This is intentional safety isolation. | No UI, WinUI, SQL, shell, or package-deployment references in the core. Architecture tests enforce dependency direction in Phase 1. |
| CLI | .NET console project with a minimal first-party exact-token parser | Keeps the Phase 1 command surface tiny and avoids adding a parser dependency before scanning exists. | CLI syntax is still a compatibility contract. Untrusted tokens never become shell text. | Commands are restricted to help, version, doctor, demo, and rule validation. Reassess a parser library only when a larger command tree is approved. |
| Dependency injection/configuration | `Microsoft.Extensions.DependencyInjection`, configuration options, and related Microsoft Extensions where useful | Official abstractions shared by CLI and desktop composition roots. | Incorrect service lifetimes can retain scan state or private data. Generic Host background-service features must not be enabled incidentally. | Align package major with .NET 10 and pin centrally. Composition roots are the only registration locations; no background service is added without an ADR. |
| JSON | `System.Text.Json` for contracts, reports, and settings | Included with .NET, source-generation support, no additional serializer dependency. | Serialization options, unknown-field behavior, enum representation, numeric limits, and schema versions are compatibility/security choices. | Pin behavior in tests, not by relying on process-global defaults. No polymorphic type-name activation from untrusted input. |
| YAML | YamlDotNet 18.1.0 for declarative rule instances | Mature MIT parser compatible with .NET 10. | YAML aliases, deep graphs, duplicate keys, tags, and coercion can create denial-of-service or confusion hazards. | Phase 1 enforces a 256 KiB bound, duplicate-key rejection, safe object deserialization, no executable fields, then schema validation. |
| JSON Schema | Draft 2020-12 via JsonSchema.Net 7.4.0 | Offline validation with a reviewed MIT binary line. | Newer binary terms add a maintenance-fee agreement; Corvus runtime compilation failed under the .NET 10 test host. | Keep 7.4.0 until a reviewed permissive compatible update exists; never resolve schema references over the network. |
| Persistence | Microsoft.Data.Sqlite.Core 10.0.9 plus SQLitePCLRaw.bundle_e_sqlite3 3.0.3 | Makes the managed provider and patched native bundle explicit and auditable. | SQLite remains neither an authorization nor secrecy boundary. | Native initialization is guarded in `SqliteRuntime`; SourceGear.sqlite3 3.50.4.5 resolves and reports SQLite 3.50.4. |
| Logging API | Small app-owned JSON Lines writer behind `ILocalLog` | Provides deterministic structured local logging without a network sink or another provider dependency. | Local files can leak usernames and paths. | Every message and exception is privacy-redacted; callers must supply an explicit product-owned directory. Retention policy remains deferred. |
| Unit/integration tests | xUnit v3 stable; Microsoft Testing Platform or VSTest runner selected as a tested set | Bootstrap requirement; supported modern .NET testing model. | Framework, adapter, test SDK, analyzers, and runner must be compatible. Parallel tests can race over fixtures. | Pin all test components centrally after a clean spike. No prerelease test package in release CI. Fixture roots are isolated; real drives are never cleanup targets. |
| Property/fuzz tests | A maintained property-based library if license/review passes, plus deterministic corpus tests | Required for paths, schemas, IPC, privacy redaction, and overlap resolution. | Random tests become irreproducible without seeds and bounded generators. | Package choice deferred. Always print/store the failing seed; fallback is deterministic generated corpora until a library passes review. |
| Benchmarks | BenchmarkDotNet candidate plus app-owned end-to-end fixture timing | Separates microbenchmarks from measured scan budgets. | Microbenchmarks do not prove full-drive behavior; benchmark tooling adds packages. | Pin only when benchmarks begin. Phase 2 records hardware, OS, filesystem, fixture, build, and tool versions. No invented numbers. |
| CI | GitHub Actions on an explicit Windows hosted-runner image; Visual Studio 2026-capable lane for WinUI packaging | Repository target and authoritative Windows toolchain. | Hosted images change; Actions are executable supply-chain dependencies; untrusted PRs must not access signing. | Prefer an explicit `windows-2025-vs2026` or verified successor label, log image metadata, and pin Actions to reviewed full commit SHAs with readable version comments. A missing workload blocks packaging. |
| Packaging | Unpackaged framework-dependent `win-x64` developer build in Phase 1 | Proves WinUI build and launch without inventing production identity or signing. | Requires the matching .NET and Windows App Runtime prerequisites and is not a distribution format. | MSIX, identity, signing, install/upgrade/uninstall, and self-contained decisions remain Phase 9 gates. |

## Provisional target and build settings

Phase 1 should begin from these settings, then record justified deviations:

- `TargetFramework`: `net10.0` for portable core/testable libraries; an explicit `net10.0-windows...` TFM for Windows and UI projects.
- `TargetPlatformMinVersion`: not pinned in Phase 0. Provisional validation floor is Windows 11 24H2 (build 26100), but the generated stable template, API needs, OS lifecycle, and Store requirements must decide the actual value.
- Runtime identifier: `win-x64` first. ARM64 is a build/test candidate, not a support claim. x86 is out of initial scope.
- `Nullable`: enabled.
- Implicit usings: enabled unless a generated-code/tool conflict is demonstrated.
- First-party compiler warnings: errors in CI and Release builds, with narrow documented exceptions only.
- Built-in .NET analyzers: enabled at the level shipped with the pinned SDK; security/reliability rules cannot be broadly suppressed.
- Deterministic/CI build properties: enabled where supported, while reproducibility remains an investigation rather than a promise.
- Unsafe code: disabled in first-party projects unless an ADR and targeted tests justify a narrowly isolated Windows interop adapter.
- Restore: PackageReference with Central Package Management; a single approved NuGet v3 source initially; lock files/locked mode for applications after validation.
- Package versions: exact stable versions only. No floating ranges, wildcards, or unreviewed prerelease packages.

The Windows SDK/target platform version is also deferred. The current Microsoft Windows SDK release page is an observation, not a reason to select the newest SDK without a WinUI/MSIX compatibility test.

## Dependency and tool admission gate

A package or build tool is not approved until the Phase 1 review records:

1. Exact ID and stable version from the authoritative package feed.
2. Upstream repository/owner and release/tag evidence.
3. Package hash and signer/repository metadata when available.
4. Direct and transitive dependency graph for every target/RID.
5. License text from the actual package, compatibility with Apache-2.0 distribution, notices, and any EULA/service fee.
6. Target frameworks, native assets, trimming/AOT/source-generator behavior where relevant.
7. Known vulnerabilities, deprecations, maintenance status, and security-reporting route.
8. Required capabilities: filesystem, network, process, registry, environment, code generation, or telemetry.
9. Malformed/untrusted-input behavior, resource limits, and a safe failure mode.
10. Upgrade/removal cost and a viable fallback.

Commercial, source-available, dual-licensed, or additional-EULA packages require explicit maintainer/legal approval. A repository license badge is insufficient when the distributed NuGet package carries other terms.

## Pinning and update policy

### SDK

- Re-check Microsoft support metadata at Phase 1 start.
- Install the latest supported .NET 10 SDK patch.
- Create `global.json` with an exact base version, `allowPrerelease: false`, and a documented patch/feature-band roll-forward choice.
- CI installs or selects the same SDK policy and prints `dotnet --info`.
- Apply supported security patches promptly through reviewed pull requests; do not jump major runtime lines automatically.

### NuGet

- Declare versions once in `Directory.Packages.props`.
- Restore full closure in locked mode in CI after the lock-file spike.
- Enable NuGet audit for all direct/transitive packages and separately enumerate vulnerable/deprecated/outdated packages.
- Any advisory suppression has an owner, rationale, URL, expiry, and test evidence; high/critical vulnerabilities block release unless the security owner accepts a documented exceptional risk.
- Dependabot may propose updates but never auto-merge them.

### Windows App SDK and Windows SDK

- Use only a supported Stable Windows App SDK version.
- Read release notes and known issues, inspect the actual NuGet package, run packaged build/launch/install/update smoke tests, and record the chosen version.
- Service updates stay within the selected supported line after regression checks. Major-line changes require an explicit compatibility review.
- If Microsoft sources still disagree, do not pin until NuGet and a reproducible template/build establish the artifact.

### GitHub Actions and tools

- Pin Actions to reviewed full commit SHAs, with a version comment for maintainability.
- Declare least-privilege token permissions per job.
- Pin local tools in a tool manifest or verified setup step; do not execute “latest” remote scripts during release.
- Use OIDC and protected environments for future signing, never a repository PFX/password.

## Explicitly rejected or deferred choices

| Choice | Status | Reason / safe fallback |
|---|---|---|
| Electron or a local browser server | Rejected | Cannot meet native filesystem, packaging, privilege, and attack-surface goals. |
| .NET 8 as project target | Rejected | Installed runtimes do not override the fixed .NET 10 LTS decision. |
| Preview/Experimental Windows App SDK | Rejected for release | Unsupported/unstable channel; block or use an approved supported Stable line. |
| Entity Framework Core | Deferred/not needed | Direct `Microsoft.Data.Sqlite` keeps migrations/queries explicit and reduces dependency surface. Revisit only through an ADR with measured benefit. |
| `JsonSchema.Net` binary package | Not approved | Current upstream package EULA creates a licensing/funding concern. |
| Corvus.JsonSchema | Candidate | Apache-2.0 and 2020-12 evidence are positive; runtime code generation, cold start, resource control, and dependency closure need a Phase 1 spike. |
| Serilog local file stack | Candidate | Core is Apache-2.0 and structured; every adapter/sink plus redaction and retention behavior still needs verification. |
| Unpackaged/portable distribution | Unsupported | Dependency, update, identity, storage, and helper behavior have not been threat-modeled or tested. |
| Custom updater | Rejected without ADR | Prefer Store/MSIX platform update paths; no update code exists in early phases. |

## Phase 1 verification and pinning record

### Implemented record — 2026-07-13

- SDK: .NET SDK 10.0.301 with `latestPatch` roll-forward and prerelease disabled.
- Language/TFM: C# 14; `net10.0`; WinUI uses `net10.0-windows10.0.26100.0` and `win-x64`.
- UI: Microsoft.WindowsAppSDK 2.2.0 and Microsoft.Windows.SDK.BuildTools 10.0.28000.2270, stable only.
- Persistence: Microsoft.Data.Sqlite.Core 10.0.9 plus SQLitePCLRaw.bundle_e_sqlite3 3.0.3; native SourceGear.sqlite3 3.50.4.5.
- Rules: YamlDotNet 18.1.0 and MIT JsonSchema.Net 7.4.0 with remote resolution unused. Corvus.Json.Validator 5.2.6 was tested and rejected because its runtime compiler could not resolve .NET 10 core metadata in the test host.
- Tests: Microsoft.NET.Test.Sdk 18.7.0, xUnit 2.9.3, runner 3.1.5, and coverlet.collector 10.0.1.
- Packaging: unpackaged framework-dependent developer build for Phase 1; no public package or installer.

All versions are exact and managed in `Directory.Packages.props`. The package audit reports no known vulnerable packages. `JsonSchema.Net` is deliberately held at the last reviewed MIT line because later binary releases introduce a maintenance-fee agreement.

The following checklist remains the evidence required whenever these pins or packaging choices change:

| Item | Required recorded evidence | Failure behavior |
|---|---|---|
| .NET SDK | Exact SDK, `dotnet --info`, Microsoft support status, `global.json` policy | Block build |
| Windows/Visual Studio SDK | Exact Windows SDK and installed WinUI/MSIX workload/components | Block UI/package gates |
| Windows App SDK | Exact Stable NuGet version, release notes, package hash, template/build/launch result | Block UI scaffold or document supported prior-Stable ADR |
| All NuGet packages | Exact central pins, transitive graph, lock files, audit, licenses, hashes | Reject package or use stated fallback |
| JSON Schema validator | 2020-12 conformance subset, format policy, offline references, hostile-input/resource tests, license | Keep rules embedded/prevalidated; reject external rules |
| Test stack | Exact xUnit/runner/platform/analyzer set; CLI and IDE discovery | Block test gate |
| Logging provider | Local-only output, redaction, ACL/location, bounded retention, disk-full behavior | Disable file logging or use verified first-party provider |
| CI Actions | Explicit runner, action SHAs, token permissions, dependency/update ownership | Block CI/release workflow |
| Package mode | Framework-dependent/self-contained sizes, prerequisites, clean install/upgrade/uninstall/offline result | No distributable |

## Primary evidence

Accessed **2026-07-10**:

- Microsoft, [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy), [Windows installation/toolchain requirements](https://learn.microsoft.com/en-us/dotnet/core/install/windows), and [SDK selection with global.json](https://learn.microsoft.com/en-us/dotnet/core/install/upgrade#control-sdk-version-with-globaljson)
- Microsoft, [Windows App SDK channels](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/), [Windows/SDK version overview](https://learn.microsoft.com/en-us/windows/apps/get-started/versioning-overview), [downloads](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads), and [single-project MSIX](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix)
- Microsoft upstream, [Windows App SDK releases](https://github.com/microsoft/WindowsAppSDK/releases)
- Microsoft, [Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/), [dependency injection](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/usage), [logging](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/overview), [System.CommandLine](https://learn.microsoft.com/en-us/dotnet/standard/commandline/), and [.NET Community Toolkit](https://github.com/CommunityToolkit/dotnet)
- YamlDotNet maintainers, [YamlDotNet](https://github.com/aaubry/YamlDotNet)
- JSON Schema project, [Draft 2020-12](https://json-schema.org/draft/2020-12); Corvus maintainers, [Corvus.JsonSchema](https://github.com/corvus-dotnet/Corvus.JsonSchema)
- xUnit maintainers, [xUnit v3 getting started](https://xunit.net/docs/getting-started/v3/getting-started)
- Microsoft, [Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management), [locked restore](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies), and [NuGet audit](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages)
- GitHub, [official hosted runner images](https://github.com/actions/runner-images) and [secure use of Actions](https://docs.github.com/en/actions/reference/security/secure-use)

## Acceptance criteria for this decision

- Every major technology has a purpose, rationale, trade-off, security impact, version policy, and fallback.
- No unverified package version is pinned.
- The .NET 8 runtimes/no-SDK local state is explicit and not misrepresented as build readiness.
- Stable WinUI 3 remains selected despite the current setup blocker and source-version conflict.
- Draft 2020-12 is consistent with the rule/export schema work; the validator remains safely unselected.
- Windows 11, MSIX, dependency locking/auditing, signing, and update decisions agree with the support and release documents.
