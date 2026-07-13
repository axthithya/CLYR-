# Support Matrix

## Status vocabulary

This is a **Phase 0 planning matrix**. There is no application or scanner, so nothing is Beta or Stable and no row is a current product support claim.

| Release status | Meaning |
|---|---|
| Planned | Specified for a future phase, but not implemented or proven. |
| Experimental | Implemented behind an explicit experimental boundary; incomplete evidence. None in Phase 0. |
| Beta | Publicly testable with documented limitations and passing beta gates. None in Phase 0. |
| Stable | Supported release with lifecycle, test, signing, upgrade, and operational commitments. None in Phase 0. |
| Unsupported | Deliberately excluded; CLYR must explain rather than attempt unsafe behavior. |

“Report-only” describes action availability, not release maturity. “Capability unavailable” is a truthful result, not a product crash.

## Support policy

CLYR may claim support only for a combination that has:

1. A Microsoft-supported Windows release on the date of the CLYR release.
2. A supported .NET 10 patch and Stable Windows App SDK package.
3. Applicable architecture, filesystem, package identity, standard-user, offline, install/upgrade/uninstall, accessibility, and scan fixture coverage.
4. A documented fallback for missing APIs, access denial, partial coverage, locked content, and unsupported identity/precision.
5. Clean-machine manual smoke evidence for the actual signed acquisition channel.

Technical framework compatibility is not a CLYR support claim. Capability detection takes precedence over fragile version-string branching, but it does not permit a version that has not passed the release matrix.

Support ends no later than the earliest of:

- Microsoft's servicing end for that OS/edition;
- .NET or Windows App SDK end of support;
- CLYR's published retirement date after a supported upgrade path;
- loss of a required safe API/packaging behavior that cannot be mitigated.

## Windows and architecture baseline

Microsoft lifecycle dates are point-in-time evidence accessed 2026-07-10 and must be refreshed at every release.

| Environment | Phase 0 disposition | Basis and trade-off | Required proof before claim | Fallback |
|---|---|---|---|---|
| Windows 11 26H1 x64 | Planned validation candidate | In Microsoft servicing at research time; current .NET/WinUI/package behavior still needs direct testing. | Build, signed install, launch, scan fixtures, export, offline, upgrade/uninstall, accessibility. | Unsupported message/no install if any required gate fails. |
| Windows 11 25H2 x64 | Planned primary validation target | In Microsoft servicing and likely public-beta baseline. | Same full lane as above on current patched OS. | No claim until evidence exists. |
| Windows 11 24H2 x64 | Planned compatibility candidate only while Microsoft-serviced | In servicing at research time but Home/Pro retirement was documented for 2026-10-13, so it may expire before CLYR ships. | Full lane plus release-date lifecycle check. | Remove from shipping matrix when servicing ends. |
| Windows 11 23H2 and older | Unsupported initially | Consumer editions are already out of servicing; edition-specific Enterprise/Education lifecycles add a test burden not funded in the initial plan. | ADR, explicit edition matrix, current servicing, full smoke/CI lane. | Upgrade Windows; no bypass. |
| Windows 11 ARM64 | Planned evaluation, not a claim | .NET/Windows App SDK advertise ARM64 support, but CLYR has no ARM hardware, native SQLite/package, performance, or filesystem evidence. | Native ARM64 build/package; real hardware install/scan/accessibility/performance; all native dependencies. | x64 supported target only; do not advertise emulation as native support. |
| Windows 11 x86 | Unsupported | Initial product and current .NET client matrix are x64/ARM64 oriented; adds package/native/test complexity. | New product decision and complete lane. | None. |
| Windows 10 22H2 Home/Pro/general channel | Unsupported initially | General support ended 2025-10-14. ESU does not create automatic CLYR support. | ADR plus supported edition/ESU policy, APIs, package, CI, and smoke evidence. | Upgrade to a supported Windows 11 release. |
| Windows 10 LTSC/IoT/ESU | Unsupported initially | Different servicing contracts and constrained/enterprise environments are not tested. | Separate edition/lifecycle/security/Store/direct-distribution matrix. | No install/support claim. |
| Windows Server | Unsupported | Product UX and filesystem behavior target Windows client, not server roles/core installations. | Separate product direction, GUI/API/role safety and release lane. | Use Windows-native/server administration tools. |
| Windows S mode, Windows PE, Safe Mode, Sandbox | Unsupported | Store/capability/filesystem/identity behavior differs or is intentionally constrained. | Separate explicit use case and clean-environment evidence. | Explain unsupported environment. |
| Virtualized Windows 11 guest | Planned test environment, not a separate support promise | Useful for clean install/failure fixtures; storage measurements may differ from physical disks. | Hypervisor/disk configuration recorded; physical hardware lane still passes. | Label benchmark/allocated-size limitations. |

The provisional Windows TFM/package minimum is not pinned in Phase 0. Windows 11 24H2 build 26100 is a candidate floor for Phase 1 experiments, but the final `TargetPlatformMinVersion` must follow the selected Stable template, actual APIs, Store rules, and release-date lifecycle. A low manifest minimum must not be mistaken for support.

## Filesystem and volume matrix

Default scope is the discovered system volume, then user-selected fixed local volumes. The UX says “Analyze C:” while technical logic discovers the actual system volume.

| Volume/filesystem | Default selection | Planned read-only behavior | Precision and safety limit | Release status / tests | Fallback |
|---|---|---|---|---|---|
| Local fixed NTFS system volume | Yes, after explicit scan start | Quick/Deep analysis, partial results, top-N, logical size; allocated size/file identity where proven | Never follow reparse points by default; access denied is partial coverage; hard-link/allocation truth requires stable ID APIs | Planned Phase 2; synthetic trees, access denial, loops, sparse/compressed/hard-link fixtures | Logical/estimated values labeled; inaccessible/skipped bytes reported |
| Other local fixed NTFS volume | User selected | Same engine, drive-agnostic | Not assumed to contain Windows; protected-resource and volume-boundary policy still applies | Planned Phase 2 | Capability unavailable/partial result |
| ReFS fixed volume | Never implicit beyond explicit eligible selection until evaluated | Read-only enumeration candidate | NTFS assumptions about IDs, compression, sparse/allocation, Recycle Bin, USN, and links cannot be copied | Planned evaluation; no current tests | Logical-size partial report; mutation unavailable |
| FAT/FAT32/exFAT fixed/removable | Not default; explicit selection only if a future capability permits | Basic read-only logical enumeration candidate | No NTFS-equivalent ACL, IDs, hard links, allocation semantics, or safe mutation assumption | Planned evaluation; no current tests | Logical-only/unknown precision; no cleanup |
| Removable USB/SD | Excluded by default | Future explicit read-only selection candidate | Device disappearance, write protection, filesystem variability, and user-media risk | Planned evaluation after fixed-volume scanner | Cancel/partial result; never auto-resume or mutate |
| Network/UNC/mapped share | Excluded and unsupported | None | Remote identity, latency, offline files, credentials, server semantics, and nonlocal mutation risk | Unsupported | Explain and do not enumerate |
| Optical media | Excluded and unsupported | None | Read-only/slow/media-specific semantics; not relevant to C: recovery | Unsupported | Explain and do not enumerate |
| Mounted VHD/VHDX/VM disk | Excluded by default | Host file reported as a protected/review item; mounted volume only through a future explicit read-only capability | Guest free space is not host reclaimable; never compact/delete automatically | Planned report-only in later developer/virtualization work | Protected/manual vendor guidance |
| WSL virtual disk | Not traversed as an ordinary folder | Report host VHDX allocation and distribution evidence separately | Deleting guest data may not reclaim host allocation; compaction is never automatic | Planned report-only Phase 7 | Supported WSL guidance/manual review |
| Docker-managed storage/volumes | Not recursively treated as junk | Tool-aware report-only detection | Volumes may contain irreplaceable data; tool/WSL backends vary | Planned Phase 7 | Unsupported versions report-only; never delete volumes |
| BitLocker unlocked local volume | Same as underlying filesystem if process can read it | Normal capability detection | CLYR does not handle/recover keys or bypass ACLs | Planned with underlying filesystem tests | Access denied/partial |
| BitLocker locked/offline volume | Excluded/unavailable | None | No unlock prompt, key collection, or privilege bypass | Unsupported for scan while locked | Explain that Windows must unlock it outside CLYR |
| Cloud-sync placeholders | Observe metadata without hydration where supported | Show logical/allocation/availability uncertainty | Never open content merely to measure/hash; no automatic download/recall/delete | Planned Phase 2 metadata fixtures/manual provider tests | Skip with placeholder/unknown allocation status |
| Junction/symbolic link/mount point/other reparse point | Encounter may be counted as an entry; target not traversed by default | Record skip reason/type where safely available | Prevents loops, cross-volume escape, duplicate counting, cloud recall, and protected-path traversal | Planned Phase 2 loop/path fixtures | Skip and expose coverage gap |
| Unknown filesystem/device namespace | Excluded | Capability unavailable | No string-based assumption or mutation | Unsupported | Explain exact detected filesystem/type when privacy-safe |

No filesystem has cleanup support in early phases. Later mutation requires stable identity, component-aware containment, final-handle verification, filesystem-specific tests, and the capability matrix; otherwise it remains report-only.

## Product capability matrix

Every row includes the fields required by the Phase 0 support contract.

| Capability | Windows requirement | Filesystem requirement | Package identity | Admin | API/tool dependency | Offline behavior | Failure/fallback | Test coverage plan | Release status |
|---|---|---|---|---|---|---|---|---|---|
| WinUI app launch/navigation | Verified local Windows 11 developer lane | None | Unpackaged developer build; no installer | No | .NET 10.0.9, Windows App SDK 2.2.0 | Developer-only | Startup error window; no web fallback | Phase 1 UI Automation launch and all-navigation selection; Phase 9 clean install/accessibility | Verified for Phase 1 only |
| CLI help/version/doctor/demo/rule validation | Verified local Windows 11 developer lane | Explicit rule path only for validation | No distribution package | No | .NET 10.0.9, first-party exact-token parser | Developer-only | Stable nonzero errors; no shell invocation | Phase 1 CLI and safety tests plus smoke commands | Verified for Phase 1 only |
| Discover system volume and fixed drives | Claimed Windows 11 lane | Local volumes | App package identity, not scan authorization | No | Documented Windows drive/known-folder APIs | Full | Partial list with stable diagnostic; never assume C: is system | Phase 2 fake adapter + patched OS manual tests | Planned |
| Analyze system volume | Claimed Windows 11 lane | NTFS first | Required for app channel | No | Windows filesystem APIs | Full | Partial result with skipped/inaccessible counts | Phase 2 fixture/integration/performance/cancellation | Planned |
| Analyze another fixed local volume | Same | NTFS first | Same | No | Same | Full | Explicitly selected eligible volume only | Phase 2 multi-volume fake/manual tests | Planned |
| Analyze removable/non-NTFS | Future claimed lane | Capability-specific | Same | No | Filesystem-specific APIs | Full | Logical-only/unsupported; no mutation | Post-Phase 2 per-filesystem/device tests | Planned evaluation |
| Logical-size totals/top-N | Same | Any approved readable local filesystem | Same | No | Streaming enumeration | Full | Estimated/partial label on inaccessible data | Phase 2 exact synthetic fixture expectations | Planned |
| Allocated-size/sparse/compressed accounting | Same | Initially NTFS; capability-detected | Same | No normally | Windows allocation/file APIs | Full | Unavailable/estimated, never substitute logical as exact | Phase 2+ allocation/sparse/compressed fixtures and manual disk checks | Planned |
| Hard-link deduplication | Same | Stable file-ID capable filesystem | Same | No normally | File identity/link APIs | Full | Potential double-count label if identity unavailable | Phase 2+ link corpus/property tests | Planned |
| Reparse protection | Same | All approved filesystems | Same | No | Reparse metadata/final path APIs | Full | Skip target and show coverage | Phase 2 loops, mount, junction, symlink, path-race corpus | Planned |
| Pause/resume/cancel/progress | Same | Approved scan target | Same | No | Cancellation/streaming scheduler | Full | Bounded cancellation; partial result distinguished | Phase 2 deterministic scheduler and latency measurement | Planned |
| Detection-only YAML rules | Same | Findings from approved scanner | Same/signed built-ins | No | YamlDotNet family + Draft 2020-12 validator | Full | Reject invalid/unsupported rule; protected policy wins | Phase 3 valid/invalid/malicious corpus and conformance | Planned |
| “Why is this drive full?” explanations | Same | Uses available evidence | Same | No | Core aggregation/rules | Full | Show unknown/protected/coverage rather than invent cause | Phase 3 curated fixtures/false-positive review | Planned |
| Privacy-safe Summary JSON export | Same | Destination selected by user | Same | No | System.Text.Json, Draft 2020-12 schema | Full | Produce no export on redaction/schema/write failure | Phase 2/3 golden/schema/privacy/failure tests | Planned |
| Detailed local-only export | Same | User-selected destination | Same | No | Future separate schema/explicit UX | Full | Unavailable; never fall back from Summary to raw detail | Later privacy review and corpus | Planned later |
| SQLite metadata migration foundation | Verified test lane only | In-memory or random OS temporary fixture | No production database path in Phase 1 | No | Microsoft.Data.Sqlite.Core 10.0.9; SQLite 3.50.4 | Foundation only | Reject newer schema; dispose and release fixture | Phase 1 initialization/version/idempotency/concurrency/release tests; Phase 4 product storage | Verified foundation; product storage planned |
| Snapshot comparison / “What grew?” | Same | Prior compatible snapshots | Same | No | SQLite/core | Full | Compare partial coverage honestly or reject incompatible schema | Phase 4 migration/diff/retention tests | Planned |
| NTFS USN acceleration | Same | Local NTFS with usable journal | Same | Possibly unavailable without rights; must have nonadmin fallback | USN APIs | Full | Full enumeration correctness path; reset/wrap handled | Phase 4 journal/reset/wrap/no-journal tests | Planned optional |
| Duplicate detection | Same | Readable selected files; opt-in | Same | No | Bounded hashing | Full | Size/partial/full hash staged; no removal action | Later staged hash/cancel/privacy/performance tests | Planned later |
| Cleanup plan/dry run | Same | Only capability-approved targets | Same | No for planning | Typed contracts/protected policy | Full | Reject stale, overlapping, ambiguous, protected targets | Phase 5 malicious-plan/property/fixture tests | Planned |
| File cleanup execution | Same | Stable identity and approved action/filesystem | Same | No for initial user-owned actions | Recycle Bin/quarantine adapters | Full | Unavailable until Phase 6; partial result/receipt later | Phase 6 isolated temp fixtures only | Planned later |
| Elevated helper | Same plus supported packaging topology | Only allowlisted capability | Strong package/executable identity required | Selective per action | UAC, authenticated IPC, Windows identity APIs | Full | No helper/no action on identity/protocol/UAC failure | Phase 6 wrong-peer/replay/TOCTOU/fuzz/interrupt tests | Planned later |
| Trusted tool cleanup | Same | Tool-owned storage semantics | Same | Capability-specific | Exact compiled vendor adapter/version/publisher | Core remains offline; tool may have its own behavior disclosed | Unsupported version/ambiguous output is report-only | Phase 7 fake process runner + disposable integration | Planned later |
| Developer storage reporting | Same | Local tool/project storage, virtual disks protected | Same | No | Tool-specific documented discovery | Full for detection | Unknown versions report-only | Phase 7 per-tool fixtures/version matrix | Planned later |
| Move known folders | Same | Supported source/destination filesystem/capacity | Same | Normally current user | Windows known-folder mechanisms | Full | No move; preserve source until verified destination | Phase 8 conflict/capacity/interruption/rollback tests | Planned later |
| Store application updates | Claimed signed package lane | Package install volume | Exact production package family | No app-wide elevation | Microsoft Store/MSIX | Installed app works offline | Not checked/unavailable; current app remains | Phase 9 flight/acquire/upgrade/offline tests | Planned later |
| Direct App Installer updates | Supported Windows 11 lanes only after ADR | Package install volume | Exact direct family/Publisher | No | App Installer, HTTPS, trusted signer | Current app works offline | Store-only/manual signed install | Phase 9 hostile descriptor, origin, downgrade, offline tests | Planned conditional |
| External signed rule packs | Claimed engine/schema lane | App-owned staging only | App identity plus pack signer | No | Future detached signature/manifest | Last-known-good/built-ins | Reject/atomic rollback; never run code | Phase 10 signature/revocation/archive/schema/resource corpus | Planned later |
| Telemetry/cloud upload | None | None | None | No | Prohibited by default | Not applicable | No data sent | Architecture/privacy negative tests | Unsupported |
| Background monitoring/startup/service | None | None | None | No | Explicit non-goal | Not applicable | User starts CLYR explicitly | Manifest/task/service negative release check | Unsupported |

## Packaging and acquisition support

| Format/channel | Planned status | Requirements | Known limitation and fallback |
|---|---|---|---|
| Signed single-project MSIX, Microsoft Store | Planned primary | Stable WinUI package, production identity, Store certification/signing, x64 lane | One executable only; no helper/second CLI until topology ADR |
| Store package flight | Planned private-beta route | Partner Center product and named flight group | Store certification still applies; if unavailable, no public test artifact |
| Trusted-signed direct MSIX | Planned conditional | Approved signer, exact Publisher, timestamp, HTTPS immutable hosting, clean-machine tests | Operational cost/identity may differ from Store; Store-only fallback |
| `.appinstaller` auto-update | Planned conditional Phase 9 | Update ADR, schema/settings, trusted direct package, offline/recovery/negative tests | No background task/downgrade initially; manual signed install or Store fallback |
| WinGet manifest | Planned after stable signed installer | Immutable publisher URL, exact SHA-256, repository validation, upgrade/uninstall evidence | Not an installer or trust substitute |
| Unpackaged/portable ZIP/EXE | Unsupported initially | Would need dependency/update/storage/elevation/signing review | Use MSIX |
| MSI/EXE installer | Unsupported initially | Separate installer/signing/uninstall attack surface | Use MSIX |
| Microsoft Store on ARM64 | Planned evaluation | Native package/dependencies and real hardware lane | x64 only until proven |

## Privilege, identity, and offline expectations

- Normal app, CLI, scanning, rules, explanations, exports, settings, and snapshots run as the interactive standard user.
- Running the entire app as administrator is unsupported and must not be requested as troubleshooting advice.
- Access denied is normal partial coverage. CLYR does not change ACLs, take ownership, enable backup privilege, unlock BitLocker, stop processes, or request elevation merely to improve a scan.
- A future helper is short-lived and action-specific; its absence or UAC denial leaves the action unavailable without degrading read-only analysis.
- Package identity is required for supported consumer UI distribution, but package identity never grants authority over user/system files.
- All core analysis works offline. Store/direct update status and vendor-tool behavior are separate capabilities and cannot make a scan fail.

## Accessibility, localization, and hardware

| Area | Phase 0 target | Claim gate | Fallback |
|---|---|---|---|
| Keyboard and screen reader | Planned full primary workflows | Automated accessibility checks where reliable plus Narrator/manual keyboard smoke on each UI release | Release blocked for critical navigation/name/role issues |
| High contrast, light, dark | Planned | Windows theme/high-contrast visual/manual tests | No custom styling that hides system semantics |
| Scaling/DPI | Planned | 100–200% and multi-DPI/window resize smoke; no clipped critical actions | Responsive/reflow layout |
| Locale | English initial | All technical contracts culture-invariant; UI string externalization; non-English path fixtures | Unsupported localization is labeled; paths still handled as Unicode |
| RTL/localized UI | Planned later | Mirroring/truncation/screen reader review per locale | English UI, no claim |
| Minimum CPU/RAM/disk | Not yet specified | Phase 2/9 measured budgets on representative HDD/SSD and low-memory systems | Publish measured requirements; do not invent numbers |
| HDD/SSD/NVMe | Planned functional support | Cancellation, responsiveness, I/O impact, and benchmark evidence by medium | Conservative bounded concurrency; performance label |
| Battery/thermal | Planned respectful behavior | Background work absent; user-started scan; power/performance tests where APIs used | Pause/cancel and conservative defaults |

## Test evidence schedule

| Phase | Evidence added to this matrix |
|---|---|
| Phase 1 | Exact SDK/WinAppSDK/Windows SDK pins; x64 build; packaged launch; CLI help; architecture tests; no-admin manifest; CI runner evidence |
| Phase 2 | Patched Windows 11 x64 + NTFS scan fixtures; reparse/access/cancel/progress/partial coverage; measured performance; cloud placeholder manual tests |
| Phase 3 | YAML/schema conformance, malicious corpus, protected override, report-only rules, privacy-safe export |
| Phase 4 | SQLite migrations, corrupt/interrupted recovery, snapshot diffs, optional USN fallback/reset/wrap |
| Phase 5 | Dry-run/stale/overlap/stable-identity/property tests; still no execution |
| Phase 6 | Isolated execution, helper/package/IPC identity, wrong-peer/replay/TOCTOU/crash/UAC denial, recovery receipts |
| Phase 7/8 | Per-tool version/publisher matrix and supported migration/filesystem tests |
| Phase 9 | Actual signed Store/direct acquisition; supported OS/architecture matrix; accessibility; install/upgrade/uninstall/offline; WACK; signing/update negative tests |
| Phase 10 | Signed rule-pack signature, canonicalization, anti-rollback, revocation, archive/resource limits, last-known-good |

For every claimed environment, the release report records OS edition/version/build and servicing state, architecture, filesystem, package source/identity/version, .NET/Windows App SDK versions, test result, known limitations, and date. A passing CI server build alone is not desktop support evidence.

## Phase 1 pinning and verification notes

- Verified locally with the workspace SDK 10.0.301, MSBuild 18.6.4, Windows target 10.0.26100.0, Windows SDK Build Tools 10.0.28000.2270, and Windows App SDK 2.2.0.
- The `win-x64` unpackaged framework-dependent developer shell builds, launches, renders all navigation destinations, and remains a developer topology rather than a support or distribution claim.
- Packaged template, production identity, clean install/upgrade/uninstall, signing, and self-contained evidence remain Phase 9 work.
- Use an explicit Windows hosted-runner label and log its image version; do not infer client OS support from a Windows Server CI runner.
- Pin all NuGet/test/native packages centrally and verify the `win-x64` closure, licenses, hashes, and vulnerabilities.
- Decide only a **provisional** Phase 1 test matrix; public support stays Planned until Phase 9 signed acquisition and smoke evidence.
- Keep ARM64, ReFS, FAT/exFAT, removable, and direct distribution as evaluation rows until their dedicated lanes exist.

## Primary evidence

Accessed **2026-07-10**:

- Microsoft, [Windows 11 Home and Pro lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/windows-11-home-and-pro)
- Microsoft, [Windows 11 release information](https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information)
- Microsoft, [Windows 10 release information](https://learn.microsoft.com/en-us/windows/release-health/release-information)
- Microsoft, [install .NET on Windows / supported OS and architectures](https://learn.microsoft.com/en-us/dotnet/core/install/windows)
- Microsoft, [Windows versions and SDK overview](https://learn.microsoft.com/en-us/windows/apps/get-started/versioning-overview)
- Microsoft, [single-project MSIX limitations](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix)
- Microsoft, [publish a Windows app](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/publish-first-app)
- Microsoft/GitHub, [hosted Windows runner images](https://github.com/actions/runner-images)

## Acceptance criteria

- No row implies current implementation or support.
- Windows version, filesystem, package identity, administrator need, API/tool dependency, offline behavior, fallback, tests, and release status are recorded for each product capability.
- NTFS fixed local volumes are first-class; ReFS/FAT/exFAT/removable are explicit evaluations; network/optical/unknown are excluded.
- Windows 10, ARM64, direct distribution, and portable builds are not claimed from framework compatibility alone.
- Standard-user and offline operation are the baseline; access denial becomes partial coverage, not an elevation prompt.
- Shipping support is refreshed against Microsoft lifecycle and proven with the actual signed acquisition channel.
