# UI and UX Specification

## Experience principles

CLYR should feel calm, inspectable, and technically honest. A full drive is a problem, not a crisis marketing opportunity. Large values do not turn red merely because they are large. Destructive styling is reserved for an explicitly destructive consequence in a later approved phase. The interface never shows a health score, countdown, artificial urgency, or a bundled **Clean now** control.

Brand tokens are centralized: product `CLYR`, tagline “See what filled your C: drive. Understand it. Clear it safely.”, repository/CLI `clyr`, namespace `Clyr`. `C:` may be a restrained motif, but the app discovers the actual system volume and never relabels another volume as C:.

## C:-first journey

1. **Welcome:** analysis is local/read-only, no account or upload is required, protected areas remain protected, and results can be partial.
2. **Drive overview:** emphasize **Analyze C:** when C: is the discovered eligible system volume. Otherwise name the actual system volume and show C: separately if present.
3. **Configuration:** default to Quick Analysis. Deep Analysis discloses time, I/O, battery, allocated/hard-link detail, privacy, and optional duplicate-candidate costs. Reparse traversal and cloud hydration are not options.
4. **Progress:** show state, elapsed time, observed totals, coverage caveats, skipped/inaccessible counts, and Cancel. Progressive findings are provisional.
5. **Results:** answer “Why is this drive full?” before future actions. Keep used, reviewable, movable, protected, unknown, and inaccessible evidence separate.
6. **Finding detail:** expose rule/version, evidence, accounting method, confidence, risk, regenerability, consequence, privilege, rollback, overlap ownership, and why an action is unavailable.
7. **Export:** default to Summary, preview redaction/coverage, and write only to a selected local destination. Detailed local export requires a privacy warning.

## Screen inventory and minimum states

| Screen | Purpose | Required states |
|---|---|---|
| Welcome and drive overview | Trust contract and capable-volume choice | Discovery, no eligible fixed volume, unsupported media, no C: assumption |
| Scan configuration | Quick/Deep, exclusions, low-impact mode | Unsupported option reason, battery/load warning |
| Scan progress | Progressive observation and cancellation | Discovering through Persisting, cancelling, DriveRemoved |
| Results dashboard | Rank causes with coverage | Complete, Partial, stale, no high-confidence finding, history unavailable |
| Why is this drive full? | Plain-language causal summary | Known causes, unknown/inaccessible share, overlap/estimate caveat |
| Category explorer | Drill into aggregates/top-N | Exact/estimated method, excluded/inaccessible descendants |
| Finding detail | Evidence and consequence | Present, no longer present, report-only, protected, unsupported action |
| Snapshot history | Browse/delete bounded history | Empty, incompatible, corruption/recovery |
| What grew? | Compare compatible snapshots | Coverage, volume, schema, and method mismatch |
| Cleanup-plan preview | Future exact opt-in plan | Phase 5 dry-run only; expired, changed, overlap, rejected |
| Execution and receipt | Future Phase 6 outcomes | Elevation denied, partial, mismatch, rollback available |
| Developer Mode | Tool-aware report-first views | Missing/unsupported version, virtual-disk distinctions |
| Rules and safety | Versions, evidence, exclusions | Invalid pack, safe fallback, action unavailable |
| Export report | Privacy mode and preview | Redaction failure, destination error, partial warning |
| About/privacy/licenses | Versions and claims | No false support claim |

Future screens may appear in demo navigation only when labeled **Planned / unavailable**; they must not imply cleanup works.

## Results dashboard

The header shows volume label, filesystem/capabilities, scan time/state, and coverage before totals. Primary values are:

1. **Currently used space:** drive evidence, not a sum of findings; method named.
2. **Safely reviewable space:** candidates, not automatically reclaimable; exact/lower-bound/estimate badge.
3. **Potentially movable space:** separate from reclaimable; destination not assumed.

Supporting values show protected, unknown, inaccessible when determinable, skipped count, and known reclaimable lower bound when evidence supports it. Movable is never added to reclaimable. Overlapping estimates cannot be shown as a precise sum.

Sections are Safe candidates, Needs review, Move instead, Protected or Windows-managed, and Unknown. Each finding card includes name, size/accounting badge, category, confidence, risk, explanation, last-modified/last-used evidence and caveat, action availability, **Open location**, **Why is this here?**, and **What happens if I remove it?**

## Finding and action language

- Disposition: Safe candidate, Review required, Move candidate, Protected, Unknown.
- Risk: Informational, Low, Medium, High, Prohibited.
- Confidence: Confirmed, High, Medium, Low, Unknown.
- Measurement: Exact, Lower bound, Estimated, Unavailable.
- Availability: Report only, Manual instructions, Planned, Unsupported, Requires refresh, Requires elevation, No rollback.

Safe candidate means evidence supports low risk in a defined context; it does not mean preselected or consequence-free. Protected explains which policy won. Unknown is valid. Last-access time is never described as authoritative usage evidence.

Future confirmation names the exact action, count/root scope, consequence, rollback, elevation, estimate method, and plan expiry—for example, “Send 12 selected files from [approved root token] to the Windows Recycle Bin,” not “Clean 2 GB.” Any selection change creates a new plan and confirmation.

## Interaction behavior

- Cancellation is keyboard/screen-reader reachable; acknowledgement is immediate even while a bounded native call finishes.
- Pause appears only if implemented and tested. Minimize is allowed; no background service/startup persistence is created.
- Excluding a path means “do not scan,” never “junk”; coverage impact is visible.
- Defaults remain empty in beta. Downloads, media, projects, virtual disks, Docker volumes, browser profiles, cloud files, saves, and Unknown are never preselected.
- Progressive results do not unexpectedly move focus/reorder beneath keyboard users or animate excessively.
- Details expose exact bytes; primary values use locale-aware units and identify the unit policy.
- Errors state what remains trustworthy and a safe next step.

## Accessibility checklist

### Structure and input

- [ ] Full keyboard navigation, predictable tab order, no traps.
- [ ] Programmatic name, role, value, state, and relationship for every control/chart.
- [ ] Semantic headings/landmarks and logical reading order.
- [ ] Visible focus in light, dark, and high contrast.
- [ ] Current Windows touch-target guidance verified at implementation time.
- [ ] No drag-only/hover-only operation; charts have list/table alternatives.

### Perception

- [ ] Text scaling/reflow does not clip values, actions, or dialogs.
- [ ] Color never carries meaning alone; icons have text.
- [ ] Contrast measured in all themes and high contrast.
- [ ] Reduced motion removes nonessential animation.
- [ ] Storage graphics have accessible summaries and exact data.
- [ ] Partial, estimated, protected, and unavailable use distinct text.

### Comprehension and safety

- [ ] Plain language and glossary define technical terms.
- [ ] Confirmation names exact action, count/root, consequence, rollback, and elevation.
- [ ] Focus moves to validation errors and summaries remain discoverable.
- [ ] Expiry never causes mutation without renewed confirmation.
- [ ] Screen-reader progress announcements are throttled/user-controllable.
- [ ] Demo data is visibly labeled and cannot resemble a real scan.

Phase 1 creates a smoke checklist; Phase 9 runs full manual/automated review. UI automation is not claimed until a maintained compatible approach is proven.

## Demo-data mode

Demo mode is an isolated deterministic provider with fictional volumes, paths, categories, errors, partial coverage, and findings. It never touches real drives and displays **Demo data — no drive was scanned**. It supports screenshot/onboarding/accessibility review, stores no real history, and creates no executable plan.

## Acceptance criteria

- Scan completeness, measurement certainty, disposition, risk, confidence, and action availability are distinguishable without color.
- Causes precede cleanup; protected/unknown/inaccessible never appear reclaimable.
- Every unavailable action has a reason; future actions have evidence and consequence.
- C: language never overrides discovered system-volume truth.
- Light/dark/high-contrast, scaling, keyboard, screen-reader, and reduced-motion checks are release gates.

## Phase 4.1 implemented information architecture

The desktop shell now routes to nine distinct pages: Overview summarizes the selected system drive and latest analysis; Scan owns drive/mode/progress/start/cancel; Results explains the latest completed or partial analysis; History owns local snapshots and two-point comparison; Developer Mode is an explicit read-only preview; Privacy, Licenses, About, and Settings contain page-specific content.

Reusable design tokens define the restrained dark palette, warm accent, typography, cards, borders, trust badges, and action hierarchy. Each page owns a vertical scroller with horizontal scrolling disabled. Navigation compacts automatically, page navigation intentionally returns to the top, result graphics have text alternatives, and controls have automation names. See [ACCESSIBILITY.md](ACCESSIBILITY.md) for implemented and manual-release checks.

The redesign does not introduce cleanup language or behavior. “Clear History” is limited to CLYR-owned aggregate snapshot rows and is described separately from drive cleanup.

### Responsive layout architecture

All nine pages are wrapped in a shared `ResponsivePageHost` control that provides:

- **Consistent viewport centering**: content is horizontally centered with `MaxWidth="1120"`.
- **Dynamic gutters**: 16px (Narrow), 24px (Medium), 32px (Wide).
- **Breakpoints**: Narrow (<760px), Medium (760–1199px), Wide (≥1200px). Pages subscribe to a `LayoutModeChanged` event to reflow grids, stack panels, and card layouts.
- **Vertical scrolling**: `VerticalScrollBarVisibility="Auto"`, `HorizontalScrollBarVisibility="Disabled"`. No page defines its own scroll viewer.
- **Accessibility**: the shared scroll viewer is named `Page content viewport` for UI Automation.

### Page-specific responsive behavior

- **Overview/Scan**: Quick/Deep mode cards switch between side-by-side (Wide/Medium) and stacked (Narrow). Trust badge stacks below the title on narrow viewports.
- **Results**: Four metric cards reflow from a single row (Wide) to 2×2 grid (Medium) to single column (Narrow). Contributor chart and details grid stack vertically on narrow viewports.
- **History**: Actions panel reflows. Comparison panel stacks vertically on narrow viewports.
- **Developer Mode**: Preview tiles use a responsive `ItemsWrapGrid` that adapts column count based on available width.
- **Licenses**: Structured license entries reflow their key-value layout based on viewport width.
- **Settings**: Action buttons reflow from horizontal to stacked on narrow viewports.

### Selected analysis card contrast

The selected analysis mode card uses three independent visual indicators to ensure accessibility across all themes:

1. **Subtle accent tint** (`SubtleAccentBrush`): a low-opacity warm accent background fill.
2. **Accent border** (`SelectedCardBorderBrush`): a visible warm-accent border distinct from unselected card borders.
3. **Check-mark indicator** (`SelectionMark`): a visible checkmark icon confirming the selection.

These are defined per-theme (Default, Light, HighContrast) in `App.xaml` and verified structurally by the responsive layout verifier.
