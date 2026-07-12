# Update Security

## Status

This is a **Phase 0 security design**. CLYR currently has no network client, update endpoint, updater, package, signed rule pack, background task, or executable update logic. Core scanning/classification is designed to work offline. Nothing in this document authorizes an online service or updater; adding one requires the phase/ADR gates below.

## Security objective

An update must never turn CLYR's trusted diagnostic role into a path for arbitrary code, unsafe rules, privilege escalation, downgrade, data loss, or privacy leakage.

The secure default is:

- no update check in early phases;
- built-in rules delivered inside the signed application package;
- Microsoft Store/MSIX platform servicing preferred over a custom updater;
- installed application remains usable offline;
- signature/identity/schema/compatibility failure preserves the last-known-good version;
- no update triggers scanning, cleanup, elevation, file movement, or user-selection changes.

## Trust model

| Object | Primary trust anchor | Supplemental checks | Not sufficient by itself |
|---|---|---|---|
| Store application package | Microsoft Store signature and expected Package Identity/channel | Version, architecture, supported OS, release evidence, acquired-package smoke test | HTTPS page, filename, checksum |
| Direct MSIX (only if approved) | Trusted code-signing chain, valid timestamp, manifest Publisher/Name | SHA-256, release provenance, expected channel/version, HTTPS hosting | HTTPS, GitHub account, checksum |
| App Installer descriptor | Windows validation plus exact expected HTTPS origin and matching MainPackage Name/Publisher/Version | Schema, channel allowlist, no downgrade, update settings review | XML validity alone |
| Built-in rules | Containment in the verified application package plus embedded manifest/hash | Rule schema, engine compatibility, built-in tests | YAML parse success |
| Future external rule pack | Approved detached signature over an exact manifest and all content hashes | Schema/conformance, pack ID/version, engine range, anti-rollback, size/resource limits | TLS, repository owner, raw SHA-256 |
| Database migration | Signed application code and exact source/target schema versions | Backup/recovery, transaction, disk-space and interruption tests | Package signature without migration tests |

TLS protects transport to an origin; it is not the artifact trust anchor. SHA-256 detects accidental or known-content changes; without an authenticated expected digest it does not identify the publisher.

## Decisions

| ID | Decision | Trade-off | Failure/fallback |
|---|---|---|---|
| UPD-001 | Prefer Microsoft Store-managed MSIX updates for the public channel. | Lowest custom attack surface and Windows-managed identity/signing, but depends on Store operations/policy. | Installed app continues offline; no alternate unsigned feed. |
| UPD-002 | No self-updating executable, script download, package-manager shell command, service, scheduled task, or startup updater. | Less channel flexibility, much smaller privileged/network attack surface. | User follows a verified Store/direct release path when one exists. |
| UPD-003 | Direct `.appinstaller` updates are optional Phase 9 work behind an ADR. | Supports direct distribution but adds hosting, signing, descriptor, offline, rollback, and incident obligations. | Store-only distribution or manual signed MSIX install. |
| UPD-004 | Built-in rule updates travel with the signed application through early releases. | Slower rule cadence, but one trust/review/rollback boundary. | Last installed built-ins remain available offline. |
| UPD-005 | Community/external rules are not executable and are not auto-fetched. | Limits ecosystem speed; prevents arbitrary code supply chain. | Manual contribution/review and a later signed declarative pack design. |
| UPD-006 | Enforce monotonic application and rule-pack versions; no normal downgrade. | Emergency rollback must be shipped as a higher fixed version. | Reject lower/equal unexpected versions and preserve last-known-good. |
| UPD-007 | Validate before activation and activate atomically. | Requires staging space and duplicate storage. | Delete/retain failed staging data in the app-owned update area; keep current version. |
| UPD-008 | Update checks never send scan results, paths, filenames, drive inventory, stable user/device IDs, or usage telemetry. | No rollout analytics by default. | Generic manual version check or Store status only. |
| UPD-009 | A security update may be prominent, but forced activation blocking is not the default. | Some users can defer a fix; avoids making an offline disk diagnostic unusable due to network/service issues. | Explain impact and let the installed read-only app run unless a separately approved critical-safety policy says it cannot safely operate. |

## Application update channels

### Microsoft Store

The Store is the preferred production update authority:

1. Maintainer publishes an approved package with the exact production identity and a higher package version.
2. Store certifies and re-signs the package.
3. Windows/Store selects and applies the applicable package.
4. CLYR verifies its own data-schema compatibility at startup and reports migration/recovery failures without running destructive actions.

CLYR does not add a second in-app binary downloader for Store installations. If the Store is unavailable, the current installed app continues offline. User-facing update status must distinguish “not checked/unavailable” from “up to date.”

### Direct signed MSIX

Direct distribution remains disabled until Phase 9. If approved, Windows App Installer is preferred over custom code because Microsoft documents package identity/version validation and update settings.

Provisional safe `.appinstaller` posture:

- HTTPS-only canonical origin under publisher control.
- Exact `MainPackage` Name, Publisher, Version, and architecture.
- Current supported 2021 schema if prompt/blocking controls are used.
- User-visible on-launch check with a bounded interval.
- No `AutomaticBackgroundTask` initially.
- `ShowPrompt=true`.
- `UpdateBlocksActivation=false` for normal releases.
- Omit `ForceUpdateFromAnyVersion`; lower versions are rejected.
- No `ms-appinstaller:` protocol dependency.
- Descriptor and packages use immutable versioned URLs; never replace bytes under a published version.

These are proposed settings, not implementation. Phase 9 must verify exact supported schema/Windows behavior on every supported OS and document enterprise policy overrides. If direct hosting, signer, or recovery behavior cannot be operated reliably, CLYR is Store-only.

### GitHub Releases and WinGet

GitHub Releases may provide source, release notes, SBOM, hashes, provenance, and—only if trusted direct signing is approved—the direct MSIX. A GitHub release asset is not trusted merely because it is hosted by GitHub.

WinGet is a discovery/install manifest over an existing trusted installer:

- publish only after a stable signed production installer exists;
- use the publisher's immutable release URL;
- require the exact InstallerSha256;
- validate manifest and install/upgrade/uninstall behavior;
- a hash mismatch blocks submission/update;
- WinGet does not become a second unsigned updater inside CLYR.

## Update acceptance pipeline

### Application package

Windows performs package/signature checks, and the release process additionally requires:

1. Expected channel and package family.
2. Trusted signature path appropriate to Store/direct channel.
3. Valid timestamp for direct artifacts.
4. Version strictly greater than the installed version except an explicitly isolated test.
5. Supported processor architecture and OS.
6. Release manifest/artifact digest matches published evidence.
7. Application contract/database migration supports installed state.
8. Adequate disk space for package staging and any data backup.
9. Clean-machine and prior-version upgrade evidence exists for that version.

Failure stops installation/activation through the platform or release process. CLYR never disables signature verification, imports an untrusted root, changes system policy, or shells out with bypass flags.

### Database/settings migration

Code signing does not make a migration rollback-safe. Each migration must:

- have explicit source and target schema versions;
- reject unknown future schemas;
- preflight space and permissions;
- make a recoverable backup/snapshot when data size and privacy policy permit;
- use SQLite transactions where the operation is transaction-safe;
- be idempotent or have an explicit interrupted state;
- preserve the original on failure;
- never delete scan history merely to make startup succeed;
- emit privacy-safe local diagnostics;
- pass upgrade tests from every supported public version.

If migration fails, start in a bounded recovery/read-only mode or use the last compatible data copy. Do not offer cleanup/elevation. The UI explains how to export or reset only with explicit consent.

## Built-in rule updates

Through the read-only MVP and initial rule engine:

- rules are embedded/versioned with the signed app;
- an embedded manifest contains rule ID, rule version, schema version, and content hash;
- every built-in rule starts report-only;
- protected-path policy overrides all rules;
- schema or manifest mismatch rejects the built-in pack and records a diagnostic; it never hides a packaging error or relaxes protection;
- installing an older app/rule set is not a supported rollback route;
- a bad rule is fixed by a higher signed application version.

This intentionally couples rule cadence to application review. It avoids a second supply chain before rule semantics and false-positive governance are mature.

## Future external rule-pack protocol

External rule packs are a Phase 10 candidate, not an early capability. Before implementation, an ADR and threat-model update must define:

### Signed manifest

At minimum:

- protocol/manifest version;
- globally scoped pack ID and human name;
- monotonically increasing pack version;
- created/released timestamps used as metadata, not sole freshness proof;
- minimum/maximum compatible engine and rule-schema versions;
- channel;
- ordered list of every file path, byte length, and SHA-256;
- declared rule IDs and versions;
- signing algorithm/key ID and detached signature;
- optional expiry/revocation metadata with a defined offline policy.

Sign an exact canonical manifest byte representation; hashes bind every content byte. Archive the signature, certificate/key chain, and provenance. Never sign only a ZIP filename or mutable URL.

### Content restrictions

- YAML/JSON and approved inert assets only.
- No DLL, EXE, script, PowerShell, command template, macro, native library, serialized object graph, or dynamic assembly.
- No arbitrary URL/reference resolution.
- No arbitrary filesystem roots, environment expansion, registry write, process invocation, or cleanup action.
- Detection-only unless a compiled first-party adapter in the signed application implements an allowlisted typed action in a later approved phase.
- Bounded compressed/uncompressed size, file count, path length, YAML nodes/depth/aliases, schema references, regex complexity/time, and evaluation budget.
- Reject duplicate IDs, duplicate YAML keys, path traversal, absolute archive paths, device names, alternate data stream syntax, and case-colliding entries.

### Staging and activation

1. Download into an app-owned untrusted staging directory using bounded I/O.
2. Enforce size/count limits while streaming; defend against archive expansion.
3. Verify the signed manifest and signer/channel.
4. Verify every file hash/length and reject unlisted files.
5. Parse with constrained YAML settings.
6. Validate Draft 2020-12 locally with network reference resolution disabled.
7. Check engine/schema compatibility and monotonic version.
8. Run protected-root and semantic validation.
9. Write a complete versioned directory and atomically switch the active pointer/index.
10. Keep a bounded last-known-good copy and audit record.

Any failure leaves the current pack active. A partially staged pack is never evaluated. Cleanup of staging touches only the exact app-owned staging directory and is tested against path/reparse attacks.

### Revocation and offline behavior

Signature validity is not proof that a once-trusted rule is still safe. A future design needs:

- signed revocation metadata with sequence/expiry;
- a denylist shipped in application updates;
- incident guidance for offline users;
- last-check timestamp that does not falsely say “safe” or “current”;
- no fail-open activation when revocation metadata is malformed;
- a policy for expired metadata that preserves built-in protected behavior.

When offline or unable to refresh, CLYR uses the last-known-good validated pack or signed built-ins. It does not delete rules, block basic scanning, or treat stale metadata as a reason to execute broader behavior.

## Anti-rollback and compatibility

- Application package versions increase within a package family.
- Rule pack versions increase within a pack/channel and store the highest accepted sequence.
- Equal-version/different-hash artifacts are rejected as repository compromise or operational error.
- A newer schema/engine requirement that the installed app cannot satisfy is “unsupported update,” not “best effort.”
- Unknown fields follow the schema's explicit policy; safety-critical unknown action types are rejected.
- Clock time is not the only anti-rollback signal because clocks can be wrong.
- Clearing local state must not allow an unsigned/older pack to supersede signed built-ins.
- Beta and stable channels use Store flighting or deliberately distinct identity/trust configuration; a beta feed cannot update stable by URL manipulation.

## Network and privacy controls

If an update-check feature is later approved:

- use the platform channel where possible;
- send only data necessary to request applicable metadata (for example current package family/version/architecture as already inherent in the platform transaction);
- do not create a CLYR account, stable installation ID, advertising ID, or fingerprint;
- do not send drive capacity, filesystem, findings, paths, filenames, username, SID, tool inventory, logs, reports, or rule matches;
- do not follow arbitrary redirects across trust boundaries;
- use bounded timeouts, download limits, and cancellation;
- never log query tokens, signed URLs, certificate secrets, full proxy credentials, or response bodies containing secrets;
- distinguish proxy/captive-portal/TLS/timeout failure without suggesting the installed app is unsafe;
- keep update checks independent of scan start and results.

No telemetry is inferred from update hosting logs as a product feature. Server access logging, retention, and privacy notices require an operational review.

## User-visible states

The UI must not collapse uncertainty into “up to date.”

| State | Required meaning/action |
|---|---|
| Not configured | This channel has no update mechanism; link only to verified release guidance. |
| Not checked | No successful check in this session; current install can run offline. |
| Checking | Cancellable/bounded platform request; does not block a scan. |
| Current | Authoritative channel reported no higher applicable version at the displayed time. |
| Available | Show version, source/channel, security/release notes, restart/data-migration impact. |
| Downloading/platform-managed | Show Windows/Store ownership; CLYR does not claim byte-level status it cannot observe. |
| Verification failed | Do not install/activate; preserve current; show safe remediation. |
| Unsupported | Update requires another OS/architecture/engine; preserve current and explain. |
| Staged/restart required | No cleanup or elevation starts; save user-visible state safely. |
| Failed/unknown | Current version remains; provide privacy-safe diagnostics and verified manual route. |
| Revoked/blocked | Disable only affected external pack/capability; protected built-ins and offline scan remain. |

## Failure and fallback matrix

| Failure/attack | Required response | Preserved capability |
|---|---|---|
| Offline, proxy failure, Store unavailable, timeout | No alarming retry loop; show not checked/unavailable | Installed offline scan/explain/export |
| TLS/redirect/origin error | Stop direct fetch; do not bypass validation | Current install/last-known-good rules |
| Package signature or Publisher mismatch | Reject and report channel verification failure | Current install |
| Lower/equal unexpected application version | Reject; no ForceUpdateFromAnyVersion | Current install |
| Equal rule version with different hash | Quarantine staging/evidence; security event | Current rules |
| Unsupported rule/schema/engine version | Reject entire pack or isolated rule per signed manifest policy; never reinterpret | Signed built-ins/current compatible pack |
| YAML/schema/semantic failure | Reject before activation | Current rules |
| Archive/path traversal or size limit | Abort staging and clean only app-owned staging safely | Current rules |
| Disk full during download/stage/migration | Abort, preserve current and source data, explain space need | Current app/data |
| Crash/power loss during activation | Atomic pointer remains old or new complete version; recover incomplete staging | Last complete version |
| Revocation metadata stale/unavailable | Label freshness unknown; use documented offline last-known-good/built-in policy | Protected built-ins |
| Signing credential suspected compromised | Freeze channel, revoke/disable, publish advisory, no replacement-in-place | Offline current version with affected-feature guidance |
| Bad released rule/application | Stop promotion; ship higher fixed signed version; preserve evidence | Safe unaffected capabilities |

## Incident response

For a suspected malicious or corrupted update:

1. Freeze Store flight/direct feed/WinGet submission and signing workflow.
2. Preserve artifact, digest, signature, provenance, logs, workflow commit, and affected version evidence.
3. Disable/revoke signing or hosting credentials as appropriate.
4. Determine package family, versions, rule IDs, and capabilities affected without collecting user paths.
5. Publish a security advisory from a verified project channel; never replace bytes under the same version/URL.
6. Prepare a higher fixed version or signed rule-pack revocation after clean-room review.
7. Test upgrade/recovery from the affected version and any migrated database.
8. Verify the actual acquired Store/direct artifact after publication.
9. Document root cause and harden workflow/trust policy before resuming.

If the signer itself is compromised, certificate/profile/identity transition requires Microsoft/CA coordination. Changing manifest Publisher is not assumed to update an existing package family.

## Phase gates

### Phase 1

- No network/update implementation.
- Pin supported SDK/package/tool inputs.
- Use central packages, lock/audit/license inventory.
- Create only a non-production MSIX packaging spike.
- Ensure built-in demo/rule assets have deterministic version/hash metadata if present.
- Add tests proving the core works with no network.

### Phase 3

- Built-in rules embedded/versioned in the app.
- Hash/manifest and Draft 2020-12 validation.
- No remote rule feed; all rules report-only.

### Phase 4+

- Versioned, interruption-safe database migrations and recovery evidence.

### Before Phase 9

- Approve application update ADR/channel.
- Reserve Package Identity and signer.
- Threat-model Store/direct topology, signing, hosting, privacy, offline, rollback, and compromise.
- Run clean install/upgrade/uninstall and bad-update negative tests on the support matrix.

### Phase 9

- Store-managed update path first.
- Direct `.appinstaller` only if all proposed settings and signer/hosting operations pass.
- Protected OIDC release signing, SBOM, hashes, provenance, immutable assets, and incident runbook.
- No custom updater.

### Phase 10

- External signed declarative rule packs only after protocol, canonical signing, revocation, last-known-good, resource-limit, conformance, and malicious-corpus tests pass.

## Primary evidence

Accessed **2026-07-10**:

- Microsoft, [publish a Windows app](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/publish-first-app)
- Microsoft, [App Installer auto-update and repair](https://learn.microsoft.com/en-us/windows/msix/app-installer/auto-update-and-repair--overview)
- Microsoft, [create an App Installer file](https://learn.microsoft.com/en-us/windows/msix/app-installer/how-to-create-appinstaller-file)
- Microsoft, [App Installer update settings](https://learn.microsoft.com/en-us/windows/msix/app-installer/update-settings)
- Microsoft, [app package updates and package-family/version constraints](https://learn.microsoft.com/en-us/windows/msix/app-package-updates)
- Microsoft, [sign an MSIX package](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide)
- Microsoft, [WinGet manifest hashes](https://learn.microsoft.com/en-us/windows/package-manager/package/manifest) and [repository validation](https://learn.microsoft.com/en-us/windows/package-manager/package/repository)
- GitHub, [secure use of Actions](https://docs.github.com/en/actions/reference/security/secure-use), [OIDC](https://docs.github.com/en/actions/concepts/security/openid-connect), and [artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations)
- JSON Schema project, [Draft 2020-12](https://json-schema.org/draft/2020-12)

## Acceptance criteria

- CLYR stays functional offline and never labels an unperformed check “current.”
- Application code is updated only through an authenticated package identity/signature channel.
- Direct update is disabled until signing, hosting, UI, offline, migration, and recovery tests pass.
- Built-in rules remain the only updateable rules in early releases.
- Future rule packs are signed, declarative, bounded, schema-valid, anti-rollback, atomic, revocable, and last-known-good.
- No update path accepts arbitrary code/commands, bypasses Windows trust, silently downgrades, or starts a cleanup/elevation action.
- Failure preserves the installed app, user data, protected policy, and current valid rule set.
