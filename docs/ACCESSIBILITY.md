# Accessibility

CLYR Phase 4.1 treats accessibility as part of the read-only product contract, not as a release afterthought.

## Implemented behavior

- Every destination is a distinct keyboard-reachable WinUI page with a named page root and named vertical content scroller.
- Every page supports vertical scrolling and disables accidental horizontal scrolling.
- Navigation uses standard WinUI navigation items, familiar symbols, text labels, tooltips, and automatic compact behavior.
- Interactive controls use descriptive accessible names. Scan progress and settings status expose polite live updates where appropriate.
- Results charts include a text alternative; meaning does not depend on color alone.
- Disabled or unavailable functions are explained in text. No cleanup action is presented as available.
- The restrained visual system uses large headings, strong foreground/background contrast, persistent labels, generous spacing, and Windows high-contrast/theme inheritance where supported.
- Nonessential motion is avoided. The app intentionally resets a page to its top when the user navigates to it.

## Verification

`scripts/verify-winui.ps1` drives the local fixture-only app through navigation, cancellation, completion, results, history comparison, license search, about version, settings, and scrolling with Windows UI Automation. It verifies viewport bounds at five window sizes (1600×900, 1366×768, 1280×720, 1000×650, 900×600), vertical scroll advancement, absence of horizontal scrolling, accessible result alternatives, and the absence of cleanup controls.

`scripts/verify-responsive-layout.ps1` structurally verifies all nine pages use `ResponsivePageHost`, the shared scroll contract, breakpoints (Narrow <760px, Medium 760–1199px, Wide ≥1200px), dynamic gutters (16/24/32px), max content width (1120px), automation names, scan-control isolation, theme brush completeness (11 brushes × 3 themes), selected-card contrast indicators, and safety boundaries — all without launching the app.

`scripts/verify-phase41.ps1` also enforces distinct page files, page-level scrolling contracts, scan-control isolation, UI architecture tests, formatting, builds, responsive layout verification, and all earlier phase gates.

## Responsive breakpoints

| Width | Mode | Gutters | Behavior |
|---|---|---|---|
| < 760px | Narrow | 16px | Single-column layout; cards and grids stack vertically; trust badge below title |
| 760–1199px | Medium | 24px | Two-column layout for cards; trust badge beside title |
| ≥ 1200px | Wide | 32px | Full multi-column layout; maximum content width 1120px centered |

## Automation ID inventory

| Page/Control | Automation name |
|---|---|
| Overview | `Overview page`, `System drive summary`, `Overview Quick Analysis card`, `Overview Deep Analysis card`, `Latest analysis actions` |
| Scan | `Scan page`, `Local volume selector`, `Quick Analysis mode card`, `Deep Analysis mode card`, `Start Analysis`, `Cancel Analysis` |
| Results | `Results page`, `Results empty state`, `Contributor visualization`, `Contributor text alternatives`, `Drive used metric card`, `Observed logical size metric card`, `Classification coverage metric card`, `Unknown storage metric card` |
| History | `Local snapshot history`, `Local history notice`, `History empty state`, `Compare two selected snapshots`, `Snapshot comparison` |
| Developer Mode | `Developer Mode page`, `Developer tool preview tiles` |
| Privacy | `Privacy page` |
| Licenses | `Licenses page`, `Search third-party licenses`, `Third-party license inventory` |
| About | `About page`, `About version text` |
| Settings | `Settings page`, `Appearance settings`, `History settings`, `Settings history toggle`, `Settings retention control`, `Clear History` |
| Shared | `Page content viewport` (scroll viewer), `Private Local Read-only trust badge`, `Page subtitle` |

## Theme verification

All 11 theme-aware brushes (`AppBackgroundBrush`, `CardBackgroundBrush`, `CardBorderBrush`, `MutedTextBrush`, `WarmAccentBrush`, `SubtleAccentBrush`, `SelectedCardBorderBrush`, `TrustBadgeBackgroundBrush`, `TrustBadgeBorderBrush`, `PrivacyBannerBrush`, `DisabledReadableBrush`) are verified in Default (dark), Light, and HighContrast theme dictionaries by the responsive layout verifier.

Selected analysis cards use three independent contrast indicators: subtle accent tint, accent border, and check-mark indicator. These are verified structurally.

## Manual review still required for release

Before a public release, test Windows text scaling at 125%, 150%, and 200%; keyboard-only operation; Narrator announcements and reading order; high-contrast themes; light/dark themes; focus visibility; localization expansion; and reduced-motion settings on supported Windows versions. The automated window-size checks do not claim to reproduce operating-system DPI or text scaling.
## Phase 5 Review Plan accessibility

The Review Plan banner, candidate list, eligible checkboxes, preview, report, and discard controls have stable accessible names. Eligibility, risk, confidence, warnings, digest, expiry, consequences, and rollback are text rather than color-only. The page participates in the shared responsive and scrolling gates.

Actual Windows High Contrast activation, 125%/150% DPI, and Windows text scaling remain uncompleted manual release checks. Automated window resizing and theme-resource inspection are not represented as operating-system scaling evidence.

