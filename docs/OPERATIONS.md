# Operations

## Current operational status

Phase 0 is documentation only. There is no application, SDK project, package, database, service, telemetry endpoint, helper, release, or supported user operation. The only current runbook is repository/document verification. Commands for later phases are contracts and must not be presented as working until implemented.

## Repository preflight

Before any phase:

```powershell
Get-Location
git rev-parse --show-toplevel
git branch --show-current
git status --short --branch
git remote -v
rg --files -uu
dotnet --info
```

Record errors exactly. Preserve unrelated changes; never use `git reset --hard`, `git clean`, force push, history rewriting, or remote mutation to simplify work. Do not commit/push/tag/publish unless explicitly authorized.

## Local verification

Phase 0:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-phase0.ps1
```

When Mermaid CLI is available, render every `.mmd` to a temporary output outside authoritative sources; do not commit binary diagrams in Phase 0. Phase 1 introduces one `scripts/verify.ps1` entry point for format, Release build, unit/safety/integration/contract/architecture tests, rule/export validation, dependency vulnerability/license inventory, secret scan, generated-doc checks, and applicable package dry run.

The current host has .NET 8.0.28 runtimes but no SDK. Phase 1 must install and verify a supported .NET 10 SDK before any `dotnet` gate. Do not downgrade the architecture to fit the host.

## Planned product-owned data

Packaged and unpackaged path behavior is resolved/tested in Phase 1. Settings, aggregate snapshots, privacy-safe logs, demo data, action journals/receipts, and future quarantine must occupy separate stable product-owned locations. Never use the repository, current directory, arbitrary user directory, or scanned root as implicit runtime storage.

| Data area | Operator concern | Recovery/control |
|---|---|---|
| Settings | Corrupt/unsupported config must not weaken safety | Validate/version; reset to safe defaults with user-visible notice |
| Snapshot database | Interrupted migration/corruption/retention | Transactional migrations, integrity check, preserve corrupt copy privacy-safely, bounded retention and user deletion |
| Logs | Path/token/secret leakage and unbounded growth | Typed redacted fields, size/time bounds, user clear; default provisional seven days |
| Rule packs | Tamper, incompatibility, bad update | Bundled validated baseline plus atomic last-known-good rollback; schema/hash/policy always run |
| Action journal/receipt | Crash ambiguity and sensitive metadata | Persist/flush before mutation; reconcile; provisional 30-day user-controlled retention after recovery need |
| Quarantine | Disk pressure, privacy, restoration | Capacity margin, metadata/integrity, explicit restore; expiry only proposes a new plan, never silent deletion |

No database contains file contents or a complete filename index by default. Uninstall behavior and optional “remove local data” choice require clean-machine testing and clear UI.

## Support diagnostic workflow — future read-only release

1. Confirm exact CLYR version/build provenance, Windows edition/version/build, architecture, package source/identity, selected volume type/filesystem, and whether the scan was Quick/Deep, Complete/Partial.
2. Ask for a Summary export only. Never ask for raw database, detailed export, full paths, screenshots containing identities, secrets, dumps, or real fixture archives in a public issue.
3. Validate schema/version/redaction locally; quarantine the report from publication if canary/secret detection fails.
4. Reproduce with generated fixture/demo data. Do not request administrator execution or ACL/ownership changes to improve a scan.
5. Classify as product defect, unsupported capability, incomplete coverage, rule false positive, accounting limitation, or environment/tool variation.
6. Add a minimized synthetic regression and update rule evidence/risk/docs before closure.

## Common incident runbooks

### Scan fails or is partial

- Preserve the typed error/coverage summary.
- Confirm volume presence/capability and selected scope.
- Do not advise running the whole app as administrator, disabling security software, unlocking/taking ownership, or following links.
- Retry standard-user read-only analysis only when safe; otherwise keep Partial and explain the gap.

### Rule pack fails validation

- Reject it before evaluation/action.
- Use the bundled or atomic last-known-good validated pack.
- Record pack/schema/app version, failing rule ID/hash, normalized validation path, and source provenance without raw personal paths.
- Never skip schema/protection because the pack is signed or built in.

### Snapshot database fails integrity/migration

- Stop writes, preserve the original in its product-owned location, and retain the current scan in memory/export when safe.
- Do not run undocumented repair SQL or enable pragmas as an emergency workaround.
- Offer a documented reset/export path; test recovery from every supported version before release.

### Privacy-safe export validation fails

- Do not label or save the file as share-safe.
- Show a bounded reason and retain nothing automatically.
- Reproduce with privacy canaries; treat a redaction bypass as a security issue under `SECURITY.md`.

### Future action has unknown/partial outcome

- Stop starting new items; preserve and flush journal/receipt evidence.
- Do not repeat the old request or infer success from missing target/free-space change alone.
- Reobserve target identity and postconditions; reconcile to Completed, PartiallyCompleted, Failed, or RollbackAvailable.
- Require a new plan/confirmation for any retry or recovery mutation.

### Future helper identity/IPC/UAC failure

- Execute nothing; return a structured failure and keep read-only features available.
- Capture privacy-safe client/helper versions, signer/package identity result, protocol/nonce/expiry failure, and lifecycle.
- Do not fall back to a generic shell, weaker endpoint, or whole-app elevation.

### Suspected compromised release/update

- Stop publication/rollout; do not auto-delete installed data or attempt unsigned repair.
- Preserve hashes, provenance, signer/timestamp/transparency evidence and CI/release logs with restricted access.
- Revoke/rotate through documented provider processes, publish a signed advisory from established channels, and offer a verified rollback/fixed artifact.
- See `UPDATE_SECURITY.md` and `RELEASE_AND_SIGNING.md`.

## Release operations — Phase 9

Release starts from a reviewed commit on a protected branch and a clean reproducible checkout. Pin SDK/packages/actions; restore from approved sources/lock; run every gate; generate SBOM/license/provenance; build single-project MSIX; sign through protected non-exportable/least-access identity; verify contents/signature/version; test clean install, offline launch, upgrade, database migration, helper lifecycle, uninstall, and rollback; then publish only with explicit authorization.

A failed gate means no artifact publication. Store/WinGet/direct channels are separate evidence rows. Direct update is disabled absent its ADR. Secrets never enter source, logs, PR artifacts, or untrusted forks. Release artifacts map to an immutable commit and documented build environment.

## Incident severity and ownership

| Condition | Initial owner | Operational response |
|---|---|---|
| Protected-path violation or unintended mutation | Security + action owner | Stop affected capability/release, preserve evidence, private incident process |
| Privilege/IPC/update/signature bypass | Security + release owner | Disable distribution/capability; private vulnerability handling |
| Summary privacy leak | Privacy + security | Stop sharing/export path, notify affected users when applicable, fix/regression |
| Accounting/false-positive overclaim without mutation | Scanner/rules owner | Correct labels/rule, publish limitation, re-evaluate fixtures |
| Crash/resource exhaustion | Component owner | Bound workload, preserve partial result, benchmark regression |
| Accessibility blocker | UI/accessibility owner | Block affected release workflow until critical path usable |

Owners and risk status are tracked in `RISK_REGISTER.md`. Contact details are added only when real monitored channels exist.

## Phase evidence report template

```text
Phase:
Status:
Branch:
Commit or working-tree state:
Files changed:
Architecture decisions:
Security decisions:
Commands executed:
Test and check results:
Measured results:
Unverified claims:
Known limitations:
Risk register changes:
Documentation updated:
Manual verification required:
Next phase:
Suggested commit message:
```

No fabricated test count, benchmark, screenshot, support version, package behavior, or command result is acceptable.
