# Release, Packaging, and Signing

## Status and scope

This is a **Phase 0 release design**, not a release procedure that has been executed. CLYR has no binary, package identity, certificate, Partner Center product, signer, release workflow, or installer. Phase 0 does not create, sign, publish, or update anything.

The design covers the read-only application first. Mutation and an elevated helper do not exist before later phases and require another packaging/signing review.

## Decisions

| ID | Decision | Rationale and trade-off | Fallback |
|---|---|---|---|
| REL-001 | Signed MSIX is the primary consumer format; Microsoft Store is the preferred public channel. | MSIX provides package identity/integrity and Windows-managed install/update. The Store handles signing and delivery, but requires Partner Center identity, certification, and policy compliance. | No public artifact. A direct download is not an unsigned shortcut. |
| REL-002 | Use single-project MSIX only while the package contains one executable. | It is the simplest supported packaged WinUI path. Microsoft documents a one-executable limit, which conflicts with the future helper. | Before Phase 6, approve an ADR for a packaging project, separate package, or other verified topology. Until then, no helper ships. |
| REL-003 | Freeze production Package Identity before public beta. | MSIX updates stay within a package family derived from Name and Publisher. Accidental identity/signer changes strand or split installations. | Use clearly separate development identities; do not publish under a temporary production identity. |
| REL-004 | Store artifacts use Store signing. Direct production artifacts, if approved, use Azure Artifact Signing or another publicly trusted, reviewed signing method with SHA-256 and RFC 3161 timestamping. | Store signing removes private-key handling. Direct signing gives channel flexibility but creates cost, signer eligibility, key/identity operations, and incident-response obligations. | Store-only distribution. Self-signed certificates remain disposable test-only credentials. |
| REL-005 | Untrusted PR builds never receive signing authority. | Build contributions are untrusted input; signing would turn a workflow compromise into trusted malware. | PRs build/test/package with no production signature. Signing runs only for an approved immutable commit in a protected release environment. |
| REL-006 | Every release is traceable to one commit and includes hashes, dependency/license evidence, an SBOM, and build provenance where available. | A code signature identifies the signer and protects the package, but does not explain source or dependencies. Evidence adds auditability at operational cost. | Do not publish when any required evidence cannot be generated or verified. |
| REL-007 | Versions are monotonic per package family; normal update paths never enable downgrade. | Windows normally requires a higher package version. Downgrade can reintroduce vulnerabilities and creates database compatibility risk. | Recover with a higher fixed version. Emergency downgrade requires a separate signed incident decision and tested data compatibility. |
| REL-008 | Release automation starts only in Phase 9. | Packaging, signing, clean-machine tests, privacy/security review, and responsible disclosure must precede public automation. | Manual, non-public package spikes in disposable environments only. |

## Distribution channels

| Channel | Identity/signature | Audience | Update path | Release posture |
|---|---|---|---|---|
| Developer | Dedicated non-production package Name and self-signed development certificate trusted only on the developer machine | Contributors | Reinstall test package | Never public; certificate and private key are ignored and disposable |
| CI validation | Non-production identity; unsigned output or ephemeral test signing only when a packaging test requires it | Maintainers | None | Build artifact expires; banner/metadata says not for distribution |
| Private beta | Prefer Microsoft Store package flight; otherwise separately approved trusted-signed MSIX for named testers | Controlled testers | Store flight or reviewed direct path | Beta warning; read-only capabilities first |
| Public beta | Production Store identity and Store signature; direct artifact only if its identity/signer/update path are independently proven | Public | Store-managed; optional reviewed direct MSIX path | Phase 9 gates apply |
| Stable | Same production identity as its channel and monotonic version | Public | Store-managed; independently secured direct path if retained | Immutable release evidence and support matrix required |

Do not assume Store and direct artifacts can share a package family. Partner Center supplies Store identity values, while direct MSIX certificate Subject must match manifest Publisher. Phase 9 must prove identity/signer continuity or deliberately use distinct package families and explain migration/coexistence.

The `ms-appinstaller:` web protocol is not a required install path. Microsoft notes that it is disabled by default due to security concerns. Direct users can manually download and open a signed `.msix` or `.appinstaller` only after direct distribution is approved.

## Package topology

### Initial read-only package

- One packaged C# WinUI 3 executable.
- CLYR display name and assets.
- No service, scheduled task, startup entry, updater executable, shell extension, driver, or elevated helper.
- Standard-user execution; the manifest must not request full-application elevation.
- Package capabilities kept to the minimum supported desktop set and reviewed from the generated manifest.
- CLI distribution topology remains a Phase 1 spike: it may be a separately delivered executable/package because single-project MSIX cannot contain a second executable. It must still share core assemblies and versioned contracts.

### Future helper boundary

Before Phase 6, the release/security owners must decide and test:

- how the UI, CLI, and helper are packaged;
- which executable carries an Authenticode signature in addition to package integrity;
- how the normal process verifies helper path, package/Publisher identity, signer, file hash/version, and protocol compatibility;
- upgrade/uninstall behavior when multiple executables/packages exist;
- whether Store signing changes any inner-binary identity check;
- how a partially upgraded or side-loaded combination fails closed.

No signing document authorizes the helper or elevation.

## Package identity and version policy

Production identity fields are operational security data:

- `Package/Identity/Name`: reserve in Partner Center before beta; case-sensitive and stable.
- `Package/Identity/Publisher`: use the exact Partner Center value for Store packages or exact certificate Subject for direct packages.
- `PublisherDisplayName`: user-facing publisher name, reviewed for consistency.
- `ProcessorArchitecture`: x64 first; ARM64 only after a complete build/install/smoke lane.
- Package family: treated as an immutable update boundary.

MSIX uses `Major.Minor.Build.Revision`. For Store packages the fourth field is authored as `0`; the remaining numeric fields have platform limits and Major cannot be zero. SemVer prerelease labels and build metadata do not fit directly.

Therefore Phase 0 does **not** invent a mapping. Before the first externally installed package, maintainers must approve a deterministic mapping with these properties:

1. Strictly increasing package version inside each identity/channel.
2. No collision between beta, release candidate, stable, and hotfix builds.
3. Store-authored fourth component remains zero.
4. Source/product SemVer remains visible in assembly informational version, release notes, and release manifest.
5. Numeric overflow and duplicate-version checks fail the build.
6. Rebuilding the same source cannot silently create a different public version.

Phase 1 may use an obviously non-production test identity/version for package experiments. It must not reserve or consume a production version sequence.

## Signing model

### Development

- Generate a self-signed code-signing certificate only for local/test identity.
- Trust it only on disposable or developer machines that need the test.
- Never commit `.pfx`, private keys, passwords, exported certificates, thumbprints as trust policy, or certificate-store scripts containing secrets.
- Remove test trust after the test when practical.

### Microsoft Store

- Associate the project with the reserved Partner Center identity.
- Build the documented upload artifact, normally `.msixupload`.
- Do not require a public CA certificate for the Store submission; Microsoft re-signs the accepted MSIX.
- Validate the post-Store package identity/signature on an acquired clean-machine install, not only the pre-submission upload.

### Direct production distribution

Allowed only after an ADR and Phase 9 review. Preferred signer is Azure Artifact Signing (formerly Trusted Signing) when project eligibility/region/cost allow it.

- Authenticate from a protected GitHub environment using narrowly scoped OIDC and short-lived credentials.
- Restrict trust to the release workflow, immutable repository identity, protected tag/environment, and intended signing profile.
- Use SHA-256 file digest and SHA-256 RFC 3161 timestamp digest.
- Make manifest Publisher exactly match certificate Subject.
- Timestamp every production signature; verify the timestamp and full chain.
- Never expose signing to fork/PR code, arbitrary workflow inputs, reusable workflows from untrusted refs, or mutable Actions.
- If Artifact Signing is unavailable, use an approved publicly trusted signing process operated outside untrusted CI. Store-only is safer than a rushed key-secret workaround.

### Future helper and CLI

Package signature covers package integrity, but future cross-process identity policy may require each executable to carry a publisher-owned Authenticode signature. That decision must consider Store re-signing, file replacement, package location, catalog/package verification, and offline operation. Until proven, the helper is absent and privileged requests are unavailable.

## Proposed release pipeline (Phase 9)

The workflow is intentionally not created in Phase 0.

1. **Authorize**
   - protected, reviewed commit;
   - protected release environment with required reviewers;
   - annotated release intent/version;
   - no release from a fork or arbitrary pull-request head.
2. **Reproduce inputs**
   - checkout the exact full commit SHA;
   - use reviewed Actions pinned to full commit SHAs;
   - print runner image, OS, Visual Studio/MSBuild, Windows SDK, and `dotnet --info`;
   - restore in locked mode from approved sources.
3. **Quality gates**
   - formatting and generated-document checks;
   - Release build with warnings policy;
   - unit, safety, integration, architecture, schema, and packaging tests;
   - NuGet audit including transitive dependencies;
   - license/notice inventory and no-secret scan;
   - current support-matrix check.
4. **Stage once**
   - create an isolated staging directory from build outputs;
   - generate the package once from that staged payload;
   - produce an SPDX SBOM from the same payload;
   - create a machine-readable release manifest containing commit, versions, RIDs, toolchain, dependency-lock digest, artifact names, and hashes.
5. **Package validation**
   - inspect manifest identity/capabilities;
   - run Windows App Certification Kit when applicable;
   - verify package contents contain no symbols, secrets, local paths, user data, test certificates, logs, databases, or fixtures;
   - run clean install/launch/upgrade/uninstall/offline tests on every supported architecture/OS lane.
6. **Sign**
   - sign only the already validated production artifact;
   - do not rebuild after signing;
   - verify signature, chain, timestamp, identity, and artifact SHA-256.
7. **Provenance**
   - generate GitHub artifact attestation where available;
   - verify the attestation and artifact digest;
   - attach SBOM, hashes, notices, release manifest, and concise release notes.
8. **Publish safely**
   - upload all assets to a draft release/submission;
   - independent reviewer compares staged and uploaded hashes;
   - publish Store flight/private beta first;
   - make a GitHub release immutable after all assets are present;
   - promote only after telemetry-free manual feedback and support channels are ready.
9. **Post-release**
   - acquire from the actual channel on clean supported machines;
   - verify signature/identity/version and run read-only smoke checks;
   - archive evidence and update support/known issues.

The release job's `GITHUB_TOKEN` has minimum job-level permissions. Build/test jobs are read-only. Only provenance/release jobs receive the narrow additional scopes they need; only the signing job receives OIDC token permission.

## Required release artifacts and evidence

| Artifact/evidence | Requirement |
|---|---|
| Store upload / direct MSIX | Exact named artifact; architecture and channel unambiguous |
| SHA-256 checksum file | Covers every downloadable binary/evidence file; generated after signing |
| Release manifest | Product/package/assembly versions, commit, build run, SDK/toolchain, RID/OS target, dependency-lock digest, artifact digests |
| SPDX SBOM | Generated from exact staged/signed payload; tool/version recorded |
| Third-party notices/license inventory | Direct and transitive packages/tools/assets |
| Provenance attestation | Links artifact digest to repository/workflow/commit where available |
| Release notes | User-visible changes, security notes, known limitations, support matrix, data-migration impact |
| Verification record | WACK/package checks, signature/timestamp, clean install, launch, upgrade, offline, uninstall results |
| Symbols | Private or separately access-controlled unless a privacy/security review approves publication |

Reproducible builds are an investigation target, not a claim. If two clean builds differ, record why; signature timestamps and package metadata are expected sources to isolate. Provenance plus signed hashes remains required.

## Verification gates

Phase 9 must run current supported tools; command names below are verification intentions, not evidence of execution:

- `SignTool verify /pa /v <artifact>` or current MSIX-specific verification guidance.
- PowerShell `Get-AuthenticodeSignature` as a secondary inspection, not the sole gate.
- Package manifest/identity inspection.
- Windows App Certification Kit.
- Installation and acquisition from the actual Store/direct channel on disposable clean Windows 11 systems.
- Upgrade from every supported previous public version.
- Uninstall and reinstall, verifying documented treatment of settings, SQLite data, logs, quarantine, and file associations.
- Offline launch after installation.
- Negative tests: modified package, wrong Publisher, expired/untrusted self-signed cert, lower version, mismatched architecture, incomplete download, unavailable update host, denied elevation.

Any signature state other than valid/trusted/timestamped as required blocks public distribution. A checksum alone never makes an unsigned binary trustworthy.

## Upgrade, rollback, and incident behavior

### Normal upgrade

- Same package family and approved channel.
- Higher package version only.
- Compatible settings/database migration; migration is transactional, backed up where needed, and tested from every supported prior schema.
- Old application remains usable when the update service is unreachable; CLYR's diagnostic core stays offline.
- No update launches cleanup or changes user selections.

### Bad release

- Stop flight/promotion and new direct downloads immediately.
- Preserve compromised/bad artifact hashes and evidence for the advisory; never replace an asset under the same name/version.
- Publish a higher fixed version after full gates.
- Store rollback to an older package may stop new acquisition but does not reliably repair devices already on the higher bad version; those require a higher fixed package.
- Direct `ForceUpdateFromAnyVersion` stays disabled by default.
- If data migration is involved, use a tested forward repair or explicit recovery tool; never silently discard the database.

### Signing compromise

- Disable the signing identity/workflow and hosting path.
- Revoke/disable credentials with the signer/CA and preserve audit logs.
- Publish a security advisory with affected digests/versions and verified remediation.
- Coordinate package identity/certificate transition with Microsoft/CA; do not change Publisher and assume Windows will treat it as an update.
- Rebuild from a reviewed clean commit in a clean environment only after root cause is contained.

## Failure and fallback matrix

| Failure | User/release behavior | Safe fallback |
|---|---|---|
| No .NET 10/WinUI/MSIX toolchain | Build/package gate fails | Documentation or non-distributable design work only |
| Windows App SDK Stable source conflict | Exact pin blocked | Reconcile official NuGet/template/release evidence; no preview |
| Partner Center identity unavailable | Store package cannot be final | Do not publish; continue non-production test identity |
| Production signer unavailable/ineligible | Direct artifact cannot be trusted | Store-only or no public artifact |
| Publisher/certificate mismatch | Signing/install verification fails | Correct identity before signing; never bypass |
| WACK, clean-install, upgrade, or uninstall failure | Release blocked | Fix and rerun every affected lane |
| Vulnerability/license gate fails | Release blocked | Upgrade/remove dependency or document an explicitly accepted time-bounded risk where policy permits |
| SBOM/provenance generation unavailable | Release evidence incomplete | Block public beta until reviewed alternative evidence exists |
| Update/Store service unavailable | No update | Installed app remains offline-capable; display non-alarming status if user checks |
| Helper packaging unresolved | Privileged feature unavailable | Keep application read-only/no helper |

## Phase 1 work versus later release work

### Phase 1

Implemented decision: the Phase 1 shell is an unpackaged, framework-dependent `win-x64` developer build using Windows App SDK 2.2.0. It is not signed, published, installed, or assigned a production package identity. The embedded manifest requests `asInvoker`; there are no capabilities, startup entries, services, scheduled tasks, or update channels. ADR 0006 records why final MSIX and multi-executable decisions remain deferred.

- Completed: pinned .NET SDK and stable Windows App SDK, central package/audit/license inventory, build/test CI, explicit `asInvoker` manifest, and clean unpackaged launch evidence.
- Deferred: MSIX package identity, signing, self-contained comparison, clean install/upgrade/uninstall, and publication remain Phase 9 work.

### Before Phase 6

- Resolve multi-executable/helper packaging and identity via ADR.
- Define helper executable signing/verification and mixed-version rejection.

### Phase 9

- Reserve production identity and select/fund signer/channel.
- Implement protected signing/release pipeline.
- Run complete clean-machine/install/update/uninstall/security/accessibility gates.
- Generate SBOM, notices, hashes, release manifest, and provenance.
- Publish private flight, then public beta only after evidence passes.

## Primary evidence

Accessed **2026-07-10**:

- Microsoft, [package a WinUI app with single-project MSIX](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix)
- Microsoft, [publish a Windows app](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/publish-first-app)
- Microsoft, [code-signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
- Microsoft, [sign an MSIX package](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide)
- Microsoft, [Package Identity schema](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-f-identity)
- Microsoft, [app package updates](https://learn.microsoft.com/en-us/windows/msix/app-package-updates)
- Microsoft, [Store package version numbering](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/pwa/app-package-requirements#package-version-numbering)
- GitHub, [secure use of Actions](https://docs.github.com/en/actions/reference/security/secure-use), [OIDC](https://docs.github.com/en/actions/concepts/security/openid-connect), [artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations), and [immutable release management](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)
- Microsoft upstream, [SBOM Tool](https://github.com/microsoft/sbom-tool)

## Acceptance criteria

- No release path permits an unsigned or unverifiable public binary.
- Package identity, version, signer, source commit, artifact digest, SBOM, and dependency evidence are traceable.
- PR code cannot reach signing authority.
- Store and direct identities are not assumed interchangeable.
- Single-project MSIX's one-executable limit is explicit, with no hidden helper workaround.
- Rollback strategy uses higher fixed versions and preserves user data; downgrade is not a normal control.
- Exact signer, identity, SDK, package, Action, and tool versions remain Phase 1/9 verified pins, not Phase 0 guesses.
