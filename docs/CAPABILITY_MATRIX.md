# Capability Matrix

## Reading this matrix

This is a Phase 0 design contract, not implemented capability or support. **Planned** means specified for a future phase; **Unsupported** means CLYR must explain and not attempt it. Experimental/Beta/Stable require implementation and evidence and therefore do not apply in Phase 0. `SUPPORT_MATRIX.md` contains the full OS/filesystem/package combinations and release-evidence schedule.

At runtime, CLYR will detect capability from volume type/filesystem/features, package identity, user/session/integrity, API availability, installed tool/version/publisher, and selected mode. Capability detection can narrow behavior safely; it cannot expand the published support matrix.

## Read-only product capabilities

| Capability / phase | Windows | Filesystem/media | Package identity | Admin | Dependency | Offline | Safe fallback | Planned tests | Status |
|---|---|---|---|---|---|---|---|---|---|
| WinUI demo shell / 1 | Proven Windows 11 lane | App install volume | Primary MSIX identity | No | .NET 10, stable Windows App SDK | Full | Explain unsupported environment; no web UI | Packaged launch/navigation/accessibility | Planned |
| CLI help/doctor / 1 | Proven Windows 11 lane | None | Packaging topology unresolved | No | .NET 10, selected parser | Full | Stable nonzero diagnostic; no shell | Golden output/exit/architecture | Planned |
| Drive/system-volume discovery / 2 | Proven Windows 11 lane | Fixed local first | No scan authority implied | No | Documented Windows APIs | Full | Partial list; never assume C: | Fake adapter plus patched-OS manual | Planned |
| Quick Analysis / 2 | Same | NTFS fixed local first | UI identity only | No | Streaming Windows metadata adapters | Full | Partial/unsupported result with coverage | No-write, reparse, access, cancel, performance | Planned |
| Deep Analysis / 2 | Same | Capability-approved local volume | Same | No | Optional allocation/identity providers | Full | Logical-only/Unavailable labels | Allocated/link/sparse/compressed fixtures | Planned |
| Removable/ReFS/FAT/exFAT analysis / post-2 | Future evaluated lane | Explicit read-only capability only | Same | No | Filesystem-specific adapters | Full | Excluded/unsupported; never mutate | Device removal and per-filesystem matrix | Planned evaluation |
| Network/UNC/optical/unknown volume | None initially | Excluded | None | No | None | N/A | Explain and do not enumerate | Negative capability tests | Unsupported |
| Detection-only YAML / 3 | Proven app lane | Observed aggregates/approved roots | Built-ins bundled | No | Bounded YAML + Draft 2020-12 validator | Full | Reject pack and use validated last-known-good/built-ins | Schema/parser/malicious/manifest corpus | Planned |
| Explanation/classification / 3 | Same | Available scan evidence | Same | No | Core policy/rules | Full | Unknown/Protected/coverage explanation | Curated false-positive/overlap/protection | Planned |
| Summary JSON export / 2–3 | Same | User-selected local destination | Same | No | `System.Text.Json`, bundled schema/redaction | Full | Produce no share-safe export on validation failure | Schema/golden/privacy/write-failure | Planned |
| Detailed local export / later | Same | User-selected destination | Same | No | Separately reviewed schema/UX | Full | Summary only; never leak raw fallback | Privacy corpus and warning UX | Planned later |
| Aggregate snapshots/diff / 4 | Same | Product-owned storage | Identity controls data path | No | `Microsoft.Data.Sqlite` | Full | In-memory result/history unavailable; corruption recovery | Every migration, interruption, retention, partial diff | Planned |
| Optional USN acceleration / 4 | Same | NTFS with usable journal | Same | No-admin fallback required | USN APIs | Full | Full enumeration; reset/wrap handled | Journal missing/reset/wrap/rights | Planned optional |
| Duplicate candidates / later | Same | Explicit readable selected scope | Same | No | Staged bounded hashing | Full | Size-only/partial evidence; never remove | Hash/cancel/cloud/privacy/performance | Planned later |

## Recovery capabilities

Every row below remains unavailable until its phase gate. Protected/Prohibited always wins; missing stable identity, containment, rollback evidence, or adapter support makes an item report-only.

| Capability / phase | Windows/filesystem | Package identity | Admin | Dependency | Offline | Safe fallback | Planned tests | Status |
|---|---|---|---|---|---|---|---|---|
| Immutable cleanup plan / 5 | Built-in report-only eligible metadata | App/CLI contract version | No | Core policy, stable observation evidence | Full | Reject stale/duplicate/ambiguous/protected; execution unavailable | Property/malicious/schema/CLI/UI fixtures | Implemented preview; not execution |
| Selected Recycle Bin action / 6 | Approved local volume/item type | Approved topology | Usually no | Windows Recycle Bin adapter | Full | Action unavailable; never permanent-delete fallback | Disposable user-owned fixture, identity/race/rollback semantics | Planned later |
| Product-owned quarantine / 6 | Capacity/filesystem checked | Product-owned location | No for user data | Journal, copy/move, verification | Full | Keep source; retain quarantine; no silent expiry deletion | Capacity, cross-volume, crash, conflict, restore | Planned later |
| Elevated allowlisted action / 6 | Proven Windows/target capability | Signed same-product helper/client | Selective | UAC, authenticated typed IPC, Windows identity | Full | Denial/rejection; read-only product continues | Wrong peer/session, replay, expiry, fuzz, TOCTOU, lifecycle | Planned later |
| Trusted tool command / 7 | Docker Desktop and WSL only, any version | Compiled first-party probe (`--version`/`--status` only) | None (probe requires no elevation) | Trusted PATH/known-folder discovery, fixed args, 5s timeout, 4096-byte output bound | Full — no network involved | Missing/failed/timed-out probe becomes `ProbeFailed`/`NotInstalled`; never blocks classification-based reporting | `DeveloperToolProbeRunnerTests` (real bounded probe against the repo-pinned dotnet.exe), `DeveloperToolRegistryTests` (fake locator/runner status-matrix) | **Implemented (Phase 7)** — Docker/WSL only; other 12 tools have no probe by design |
| Developer storage report / 7 | 14 tool families, version not distinguished | App identity | No | Existing rule-based classification; no tool-specific interface queried | Full for detection | Report-only/Unavailable; never delete volumes/disks/dependencies; Docker/WSL/Android emulator storage remains `Protected` | `DeveloperModeTests` (taxonomy/report-builder/registry, 31 tests), `Phase7DeveloperModeCliTests` (9 tests), extended `scripts/verify-winui.ps1` | **Implemented (Phase 7)** — see docs/PHASE7_DEVELOPER_MODE.md |
| Move known folder / 8 | Supported Windows API and destination | App identity | Normally no | Known-folder mechanism, copy/verify/journal | Full | Preserve source and report failure | Capacity/filesystem/conflict/interruption/rollback | Planned later |
| WSL/Docker/VM automatic delete or compact | None | None | No | Explicit non-goal | N/A | Protected/report-only/manual supported guidance | Negative action tests | Unsupported |
| Permanent deletion | None | None | No | No action type | N/A | Recycle/quarantine/manual review only | Schema/contract negative tests | Prohibited |

## Distribution and online capabilities

| Capability / phase | Requirements | Offline behavior | Fallback | Evidence | Status |
|---|---|---|---|---|---|
| Signed single-project MSIX / 9 | Production identity, exact Publisher, supported OS/architecture, signing and lifecycle | Installed app works fully offline | No public artifact until gates pass | Clean install/upgrade/uninstall/signature/WACK | Planned primary |
| Helper/CLI package topology / before 6 | ADR resolving single-project MSIX one-executable limit without identity weakening | Full | Do not ship helper/action | Client/helper identity and lifecycle matrix | Undecided / unavailable |
| Store update / 9 | Store identity, signed traceable artifact, staged rollout/rollback | Current version continues | Manual supported release process | Upgrade/offline/rollback/revocation | Planned |
| Direct `.appinstaller` / 9 conditional | Separate ADR, trusted HTTPS, stable signer/origin, anti-downgrade | Current app continues | Store-only/manual signed install | Hostile descriptor/origin/downgrade tests | Planned conditional |
| WinGet / after signed stable installer | Immutable URL/hash/publisher and validated manifest | Installer acquisition needs network; app does not | Store/direct documented path | Install/upgrade/uninstall manifest tests | Planned later |
| Telemetry/cloud upload/LLM | Separate product/privacy/security ADR would be required | Not applicable | Local deterministic behavior | Negative network/dependency checks | Unsupported by default |
| Background service/startup/scheduled task | Separate approved ADR would be required; no current need | N/A | User-started app only | Manifest/task/service negative release check | Unsupported |

## Capability decision sequence

```text
published environment supported?
  -> selected volume/media eligible?
  -> required API/feature/package identity present?
  -> current user/integrity sufficient without broad elevation?
  -> adapter/rule/protocol/schema version supported and validated?
  -> target evidence current, contained, non-reparse, stable, not protected?
  -> phase/release flag and required tests enabled?
yes: expose precisely scoped capability
no: report unavailable reason and safest read-only fallback
```

OS version strings alone never grant capability. Elevation never converts unsupported into supported. A signed rule/update never bypasses schema, protection, or compatibility. Network loss never breaks core analysis.

## Acceptance criteria

- Each capability states environment/media, package/admin need, dependency, offline behavior, fallback, test plan, and release state.
- NTFS fixed local read-only analysis is first-class; all other media is explicitly evaluated or excluded.
- Report-only and Planned are not confused with supported maturity.
- The unavailable path is deterministic, explanatory, and non-mutating.
- This matrix and `SUPPORT_MATRIX.md` are refreshed before every support/release claim.
