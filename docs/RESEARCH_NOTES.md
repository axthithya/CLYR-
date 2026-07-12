# Research Notes

## Purpose and evidence rules

This is the dated evidence ledger for Phase 0. It records facts used by the technical stack, support, release/signing, and update-security plans. The repository contains documentation only; no entry in this file is evidence that CLYR builds, installs, scans, signs, or updates.

- Research snapshot: **2026-07-10**
- Source policy: Microsoft documentation first for Windows and .NET; official upstream documentation or repositories for third-party components.
- Version policy: a version shown here is a point-in-time observation, not a dependency pin. Phase 1 must re-query the authoritative release and package feeds immediately before creating `global.json` and `Directory.Packages.props`.
- Conflict policy: when primary sources disagree, CLYR does not guess. The feature remains unpinned or unsupported until a reproducible check resolves the conflict.
- Destructive-integration policy: weak, undocumented, locale-dependent, or version-dependent evidence permits report-only behavior at most.

All web sources below were accessed on **2026-07-10**.

## Local environment evidence

The following was observed locally with `dotnet --info` from the repository root:

| Item | Observed value | Consequence and fallback |
|---|---|---|
| .NET host | 8.0.28, x64, RID `win-x64` | Can host compatible applications; it is not a build toolchain. |
| Installed runtime | `Microsoft.NETCore.App 8.0.28` | Does not satisfy the selected .NET 10 target. |
| Installed desktop runtime | `Microsoft.WindowsDesktop.App 8.0.28` | Does not prove WinUI 3 or Windows App SDK build support. |
| Installed SDKs | **None** | Phase 1 build, restore, template, test, and package commands are blocked until a supported .NET 10 SDK is installed. |
| `global.json` | Absent | Phase 1 must create it only after verifying an installed, supported .NET 10 SDK patch. |

The exact fallback is to keep Phase 0 documentation-only. WinUI 3 must not be replaced with another UI framework because this machine lacks an SDK.

## .NET, Windows, and UI evidence

| ID | Primary source | Supported versions or behavior relied on | Decision, fallback, and verification note |
|---|---|---|---|
| R-001 | Microsoft, [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy) | The page dated 2026-06-09 lists .NET 10 as active LTS, patch 10.0.9, with support through 2028-11-14. Microsoft requires the latest patch for support. | Select the .NET 10 LTS line, but do not treat 10.0.9 as a repository pin. At Phase 1, re-check the page and download metadata, install the then-current supported patch, and pin the SDK feature band plus patch policy. If installation is unavailable, Phase 1 is blocked. |
| R-002 | Microsoft, [install .NET on Windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows) | Distinguishes the SDK from runtimes; lists supported Windows/architecture combinations; states that .NET 10 development in Visual Studio requires Visual Studio 2026 18.0 or later. | A runtime-only host cannot build CLYR. Phase 1 must verify x64 SDK and WinUI workload/tool compatibility. CLI-only SDK success is not evidence that MSIX packaging works. |
| R-003 | Microsoft, [control the SDK with global.json](https://learn.microsoft.com/en-us/dotnet/core/install/upgrade#control-sdk-version-with-globaljson) | `global.json` controls SDK selection independently of the target runtime; roll-forward policy controls acceptable patch/feature-band movement. | Phase 1 will set `allowPrerelease: false` and a documented roll-forward policy. If the exact SDK is unavailable, restore/build fails with setup guidance rather than silently using a preview or another major version. |
| R-004 | Microsoft, [Windows App SDK overview](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/) | Stable is the production, supported channel; Preview and Experimental can change and are unsupported for production. | CLYR uses only Stable. If the newest Stable fails a required spike, use a still-supported prior Stable only through an ADR with evidence; never fall back to Preview/Experimental for a release. |
| R-005 | Microsoft, [Windows versions and SDK overview](https://learn.microsoft.com/en-us/windows/apps/get-started/versioning-overview) | At access time it lists Windows App SDK 2.2.x, dated 2026-06-09, and a Windows 10 1809 API floor. | This is evidence for a release line, not enough to pin a NuGet package. Verify package existence, release notes, target framework compatibility, and a generated C# packaged template in Phase 1. |
| R-006 | Microsoft, [Windows App SDK downloads](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads) | The retrieved downloads page lagged the version overview and still presented 1.8 as Stable. | Official pages disagree. No exact Windows App SDK version is selected in Phase 0. |
| R-007 | Microsoft upstream, [Windows App SDK releases](https://github.com/microsoft/WindowsAppSDK/releases) | The retrieved upstream feed identified 2.1.3 as its latest Stable item, also conflicting with R-005. | Phase 1 must reconcile the English release notes, NuGet registration, downloads page, upstream signed tag/release, and generated template. Record the evidence and package hash before pinning. |
| R-008 | Microsoft, [single-project MSIX](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix) | A C# WinUI packaged template can produce MSIX without a separate packaging project, but single-project MSIX supports only one executable in the generated package. | It is suitable for the initial read-only app. The later elevated helper creates a known packaging conflict; before Phase 6, an ADR must select a multi-executable packaging topology or separate trusted delivery. No helper is added to the Phase 0-5 package. |
| R-009 | Microsoft, [Windows 11 Home and Pro lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/windows-11-home-and-pro) | At access time 24H2, 25H2, and 26H1 were in servicing, with different retirement dates. | The Phase 0 matrix is provisional and lifecycle-aware. Phase 9 must remove versions that are out of servicing and run real install/launch/smoke tests before any support claim. |
| R-010 | Microsoft, [Windows 10 release information](https://learn.microsoft.com/en-us/windows/release-health/release-information) | General Windows 10 22H2 support ended 2025-10-14; ESU and LTSC have separate conditions. | Windows 10 is unsupported by the initial CLYR plan even though individual frameworks may technically run there. No ESU/LTSC claim without a funded test lane and ADR. |
| R-011 | GitHub, [hosted runner images](https://github.com/actions/runner-images) | Explicit Windows 2025 and Windows 2025 with Visual Studio 2026 runner labels are documented; `-latest` is a moving label. | Phase 1 should prefer an explicit image label and log image/tool versions. If the WinUI workload is absent, provision documented components or block packaging; do not switch stacks. |

## Libraries, formats, and dependency controls

| ID | Primary source | Supported versions or behavior relied on | Decision, fallback, and verification note |
|---|---|---|---|
| R-012 | Microsoft, [Microsoft.Data.Sqlite overview](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/) | `Microsoft.Data.Sqlite` is Microsoft's lightweight ADO.NET provider for SQLite and may be used without Entity Framework Core. | Use it directly to keep persistence explicit. Phase 1 must verify the exact stable package, native SQLite assets for `win-x64`, license closure, and clean shutdown/migration behavior. Failure leaves persistence behind an in-memory test adapter. |
| R-013 | YamlDotNet maintainers, [YamlDotNet repository](https://github.com/aaubry/YamlDotNet) | Upstream describes a mature YAML parser/serializer, MIT licensing, .NET 10 support, and active releases. | YamlDotNet is the selected parser family, subject to exact package and transitive-license verification. Configure a constrained representation: no arbitrary runtime type construction, bounded input, duplicate-key rejection, and schema validation before use. Invalid or unsupported YAML is rejected, never partially executed. |
| R-014 | JSON Schema project, [Draft 2020-12](https://json-schema.org/draft/2020-12) | Defines the 2020-12 schema dialect and canonical meta-schema URI. | Use Draft 2020-12 for new CLYR schemas unless the rule-schema work records a narrower accepted decision. Unknown keywords/unsupported vocabularies fail validation rather than being ignored. |
| R-015 | Corvus maintainers, [Corvus.JsonSchema](https://github.com/corvus-dotnet/Corvus.JsonSchema) | Apache-2.0 upstream claims Draft 2020-12 support and uses the official JSON Schema test suite; dynamic validation uses Roslyn/code generation and has cold-start cost. | Candidate only, not selected. Phase 1 must test validation conformance, untrusted-input resource limits, code-generation implications, trim/package behavior, and full dependency licensing. If it fails, validation remains a build/tool boundary and runtime rules remain embedded and prevalidated. |
| R-016 | Json Everything maintainers, [json-everything](https://github.com/json-everything/json-everything) | Upstream supports Draft 2020-12 and .NET 10, but current binary packages include an additional maintenance-fee EULA for revenue-generating users despite the source repository's MIT label. | Do not adopt `JsonSchema.Net` binaries under the current terms without explicit legal/maintainer approval. This is a licensing safety fallback, not a technical criticism. |
| R-017 | NJsonSchema maintainers, [NJsonSchema](https://github.com/RicoSuter/NJsonSchema) | MIT licensed and actively maintained; its README states Draft v4+ and a dependency on Json.NET. | Candidate comparison only. Phase 1 must prove the exact CLYR dialect and official conformance corpus; otherwise it is rejected. |
| R-018 | Microsoft, [.NET Community Toolkit](https://github.com/CommunityToolkit/dotnet) | `CommunityToolkit.Mvvm` is Microsoft-maintained, platform-agnostic, and intended for MVVM applications. | Selected MVVM helper family after exact package/license/security verification. If rejected, use small first-party observable/command primitives without changing MVVM boundaries. |
| R-019 | Microsoft, [System.CommandLine overview](https://learn.microsoft.com/en-us/dotnet/standard/commandline/) | Provides parsing, help, validation, and Windows/POSIX conventions; official C# examples use the stable 2.0 API. | Selected for the CLI shell, subject to Phase 1 pin and tests. Unrecognized commands and invalid values fail without invoking a use case. |
| R-020 | xUnit maintainers, [xUnit.net v3 getting started](https://xunit.net/docs/getting-started/v3/getting-started) | xUnit v3 supports .NET 8+ and Microsoft Testing Platform/VSTest options; examples explicitly warn that displayed versions may differ. | Select xUnit v3 stable, but copy no example version. Phase 1 must pin the framework, runner/platform, analyzers, and test SDK as a tested set. If v3 integration is blocked, record it before considering another supported xUnit line. |
| R-021 | Microsoft, [dependency injection](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/usage) and [logging](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/overview) | Microsoft Extensions provide DI, hosting/configuration integration, and structured `ILogger` abstractions. | Use these abstractions. A privacy-safe rolling local file provider remains a Phase 1 selection; no network sink or telemetry provider is allowed. |
| R-022 | Serilog maintainers, [Serilog](https://github.com/serilog/serilog) | Apache-2.0 structured logging library, actively maintained. | Candidate provider for local structured files. Verify the core, Microsoft logging adapter, file sink, retention semantics, and every package license before adoption. Fallback is a small app-owned JSON Lines provider behind `ILogger`. |
| R-023 | Microsoft, [NuGet Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management) | `Directory.Packages.props` centrally controls package versions. | All package versions are centralized; projects do not carry ad hoc versions. |
| R-024 | Microsoft, [PackageReference lock files](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies) | `packages.lock.json` records full dependency closure and locked mode fails on drift. | Phase 1 evaluates and enables lock files for applications; CI uses locked restore once generated. A lock update is reviewed like source. |
| R-025 | Microsoft, [NuGet auditing](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages) | .NET 10 defaults `NuGetAuditMode` to all; `--vulnerable --include-transitive` includes transitive findings. | Restore/audit failures are visible. High/critical findings block release; suppressions require a dated risk record, owner, expiry, and advisory URL. |

## Packaging, signing, updates, and supply-chain evidence

| ID | Primary source | Supported versions or behavior relied on | Decision, fallback, and verification note |
|---|---|---|---|
| R-026 | Microsoft, [publish a Windows app](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/publish-first-app) | Microsoft recommends Store distribution for most apps; Store handles signing and updates. Direct MSIX can use `.appinstaller`; the web-install protocol is disabled by default due to security concerns. | Store is the preferred public path. Direct distribution is conditional and never depends on `ms-appinstaller:`. Users may manually open a downloaded signed file. |
| R-027 | Microsoft, [code-signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options) | Store-submitted MSIX is re-signed by Microsoft; direct distribution needs a trusted production signing route. | Use Store signing for Store artifacts. If direct signed releases are approved, use Azure Artifact Signing or another publicly trusted, approved method; self-signed certificates are test-only. |
| R-028 | Microsoft, [MSIX signing guide](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide) | Manifest Publisher must exactly match certificate Subject for direct signing; SHA-256 and RFC 3161 timestamping are documented; self-signed certificates require explicit trust. | Never store a PFX/password in the repository. Phase 9 verifies the signature, chain, timestamp, manifest identity, and install on disposable clean machines. Any mismatch blocks publication. |
| R-029 | Microsoft, [package Identity schema](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-identity) | Package identity includes case-sensitive Name, Publisher, architecture, and four-part Version; Publisher must match the signing subject. | Reserve and freeze production identity before beta. Test/beta identities must not accidentally update production. Identity changes require migration planning. |
| R-030 | Microsoft, [app package updates](https://learn.microsoft.com/en-us/windows/msix/app-package-updates) | Updates stay within one package family (Name plus Publisher) and normally require a higher version. | Enforce monotonic versions. Do not enable downgrade overrides in normal channels; recover from a bad release with a higher fixed version. |
| R-031 | Microsoft, [package version numbering](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/pwa/app-package-requirements#package-version-numbering) | MSIX identity uses four numeric parts; for Store packages the fourth part is reserved and authored as 0. | Phase 1/9 must define a deterministic SemVer-to-MSIX mapping with overflow checks. No ad hoc build-number reuse. |
| R-032 | Microsoft, [App Installer update and repair](https://learn.microsoft.com/en-us/windows/msix/app-installer/auto-update-and-repair--overview) and [manual App Installer files](https://learn.microsoft.com/en-us/windows/msix/app-installer/how-to-create-appinstaller-file) | Windows 11 supports App Installer update settings; target package Name, Publisher, and Version are validated. Update checks and activation blocking are configurable. | Direct updates remain disabled until Phase 9 threat review, trusted HTTPS hosting, signing, offline behavior, and recovery tests pass. Initial proposal is user-visible on-launch checks, no background update task, and no downgrade. |
| R-033 | GitHub, [secure use reference](https://docs.github.com/en/actions/reference/security/secure-use) | A full commit SHA is the immutable way to reference an Action; least privilege and untrusted-input handling are required. | Phase 1 pins Actions to reviewed full SHAs with version comments, declares minimum token permissions, and avoids privileged `pull_request_target` build paths. |
| R-034 | GitHub, [OpenID Connect](https://docs.github.com/en/actions/concepts/security/openid-connect) | OIDC supplies short-lived cloud tokens instead of long-lived cloud credentials. | Any future signing-service authentication uses a protected environment and narrowly scoped OIDC trust. If unavailable, signing is manual/offline; a long-lived secret is not added as a shortcut. |
| R-035 | GitHub, [artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations) | Attestations establish build provenance for artifacts. | Phase 9 emits and verifies provenance for release artifacts where repository plan/features permit. Attestation supplements, never replaces, Windows code signing. |
| R-036 | GitHub, [immutable release management](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository) | Immutable releases should be drafted with all assets before publication; afterward asset mutation is restricted. | Build all assets, hashes, SBOM, and notes before publishing. If immutability is unavailable, release permissions and a no-replacement policy are required. |
| R-037 | Microsoft, [WinGet manifests](https://learn.microsoft.com/en-us/windows/package-manager/package/manifest) and [repository submission](https://learn.microsoft.com/en-us/windows/package-manager/package/repository) | Installer manifests carry SHA-256; submissions validate installer origin and hash. | WinGet readiness begins only after a stable signed installer exists. A hash mismatch blocks submission/update. |
| R-038 | Microsoft upstream, [SBOM Tool](https://github.com/microsoft/sbom-tool) | Microsoft's tool produces SPDX SBOMs for build artifacts. | Candidate for Phase 9. Pin and verify the tool itself; generate the SBOM from the exact staged payload and archive it beside the signed artifact. |

## Resolved decisions

1. The runtime line is .NET 10 LTS, latest supported patch at build time.
2. The UI remains C# WinUI 3 on the Stable Windows App SDK channel.
3. The initial consumer package is signed MSIX; Microsoft Store is preferred.
4. Windows 11 x64 is the first validation target. Windows 10 is not an initial support claim.
5. Dependencies use Central Package Management, exact pins, locked restore where applicable, vulnerability auditing, and a reviewed license inventory.
6. Application and rule updates fail closed and preserve a last-known-good/offline path.

## Unresolved questions and safe defaults

| Question | Why unresolved | Safe default | Decision point |
|---|---|---|---|
| Exact .NET 10 SDK patch | No SDK is installed; patches continue to ship. | No build claim. | Phase 1 preflight |
| Exact Stable Windows App SDK package | Official Microsoft sources conflict. | No package pin and no UI scaffold. | Phase 1 preflight/template spike |
| Runtime framework-dependent vs self-contained | Size, servicing, WinAppSDK runtime, and clean-machine behavior need measurement. | No distributable artifact. | Phase 1 package spike; confirm Phase 9 |
| Runtime JSON Schema validator | Dialect, license, code-generation, and resource-limit trade-offs remain. | Embedded/prevalidated rules only; invalid input rejected. | Phase 1 dependency review |
| Local structured file provider | Microsoft abstractions do not by themselves choose a rolling file sink. | No network logging; app-owned fallback if needed. | Phase 1 |
| Production package identity and signer | Requires publisher/Partner Center and funding decisions. | Test identity/certificate only; no public package. | Before public beta |
| Multi-executable packaging for future helper | Single-project MSIX is single-executable. | No helper in early package. | Before Phase 6 |

## Phase 1 research refresh checklist

- Re-run `dotnet --info`; archive SDK/runtime/OS output in the phase report.
- Re-check R-001 through R-008 and reconcile Windows App SDK release metadata.
- Generate a fresh C# WinUI packaged template in a disposable directory and inspect its exact target framework, Windows SDK, Windows App SDK, and packaging properties.
- Verify every direct and transitive NuGet package ID, exact stable version, owners, repository, license text in the package, target frameworks, native assets, advisories, and package hash.
- Run the official JSON Schema Test Suite subset required by CLYR's declared dialect against the chosen validator, plus size/depth/reference-abuse tests.
- Verify `win-x64` Debug/Release build, tests, packaged launch, and CLI help on a supported Windows 11 machine.
- Pin `global.json`, central package versions, test platform packages, and Actions full commit SHAs only after those checks.
- Record any divergence here and in an ADR; do not silently weaken the selected stack.
