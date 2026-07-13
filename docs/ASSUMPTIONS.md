# Assumptions and Open Questions

This log makes bootstrap decisions explicit. An assumption may guide documentation but cannot override a safety gate.

## Accepted assumptions

| ID | Assumption | Reason and constraint | Revisit |
|---|---|---|---|
| A-001 | Apache-2.0 is the project license. | The empty repository had no conflicting license. Dependencies must be inventoried and compatible before adoption. | Every dependency proposal |
| A-002 | `CLYR`, repository slug `clyr`, root namespace `Clyr`, and executable `clyr` are authoritative. | Obsolete CLI examples in the bootstrap source are non-authoritative; retaining them would create brand drift. | Only through a branding ADR |
| A-003 | Windows 11 is the only primary target claim in planning. | No binaries or smoke-test evidence exist. Windows 10 22H2 remains an evaluation candidate, not a support claim. | Phase 1/9 test matrices |
| A-004 | The latest patched .NET 10 LTS and a stable Windows App SDK will be selected and pinned in Phase 1. | Resolved in Phase 1 with SDK 10.0.301 and Windows App SDK 2.2.0 after restore, build, and launch verification. | Every servicing update |
| A-005 | System-volume discovery is authoritative; C: is a UX default, not a technical invariant. | Windows can be installed elsewhere. | Phase 2 drive discovery |
| A-006 | Summary history stores aggregates and redacted top-N evidence; a full filename index is opt-in and local-only if ever added. | Data minimization reduces privacy and corruption impact. | Phase 4 schema review |
| A-007 | Community YAML is detection-only unless a compiled first-party adapter and action-phase review explicitly enable an action. | Declarative files must not introduce code execution. | Phase 5+
| A-008 | Default selections are empty during beta. | Even low-risk classification needs real-world false-positive evidence. | Public-beta UX review |
| A-009 | Source/project directories are represented by substantive responsibility notes until Phase 1 creates actual projects. | Phase 0 prohibits implementation and meaningless empty directories. | Phase 1 scaffolding |
| A-010 | Repository hosting will be GitHub, but no remote is created in Phase 0. | GitHub metadata is required, while remote mutation is not authorized. | When maintainers create a remote |

## Open questions ranked by impact

| Rank | Question | Impact | Safe default / owner | Decision deadline |
|---:|---|---|---|---|
| 1 | Which authenticated IPC primitive and executable-identity check satisfy packaged and unpackaged helper scenarios? | Critical: privilege boundary and spoofing risk. | No elevated helper; Security owner. | Before Phase 6 design freeze |
| 2 | What file-identity strategy is reliable across NTFS, ReFS, removable media, Recycle Bin, and cloud placeholders? | Critical: prevents path-swap/TOCTOU actions. | Report-only or reject mutation on ambiguity; Windows owner. | Phase 5 |
| 3 | Which signed production packaging mode passes install, upgrade, and signing tests beyond the verified Windows App SDK 2.2.0 unpackaged developer shell? | High: architecture and delivery. | No distribution support claim; Platform owner. | Phase 9 |
| 4 | Can allocated-size and hard-link accounting be accurate and performant on supported filesystems without privilege or hydration? | High: truthfulness of totals. | Label logical totals and uncertainty; Scanner owner. | Phase 2 |
| 5 | Which privacy-preserving path tokens remain stable enough for snapshot diffs? | High: usefulness versus identity leakage. | Aggregate only; Privacy owner. | Phase 4 |
| 6 | What quick-scan concurrency defaults work on HDD, SSD, battery, and busy disks? | Medium: responsiveness and device impact. | Conservative bounded concurrency; Performance owner. | Phase 2 benchmark |
| 7 | How should Phase 9 extend the proven Phase 1 UI Automation smoke test to accessibility and packaged-install coverage? | Medium: release confidence. | Keep `scripts/verify-winui.ps1`; Test owner. | Phase 9 |
| 8 | Which code-signing identity and release channel will the project fund and operate? | Medium: distribution and helper trust. | No release artifact; Release owner. | Phase 9 |
| 9 | Should Windows 10 22H2 remain a compatibility target after its lifecycle and API evaluation? | Medium: audience versus test burden. | Unsupported until proven; Product owner. | Phase 1 capability review |
| 10 | What retention defaults balance “What grew?” usefulness with disk/privacy costs? | Medium. | Bounded aggregate snapshots with user deletion; Product/Privacy. | Phase 4 |

No open question authorizes a broader or destructive implementation. Material changes to safety, licensing, or public positioning require an ADR and maintainer approval.
