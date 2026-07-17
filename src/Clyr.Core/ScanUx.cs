using Clyr.Contracts;

namespace Clyr.Core;

/// <summary>
/// Pure, deterministic scan-mode selection logic shared by every surface that offers a Quick/Deep toggle. There
/// is exactly one authoritative selection value (a nullable <see cref="ScanMode"/>) — no independent
/// QuickSelected/DeepSelected booleans exist anywhere, so "both selected" or "neither's checkmark matches" is
/// structurally impossible to represent, not merely avoided by convention.
/// </summary>
public static class ScanModeSelector
{
    /// <summary>
    /// Clicking the currently selected mode clears the selection (toggle-off); clicking the other mode replaces
    /// the selection entirely (the previous mode is never left selected alongside the new one).
    /// </summary>
    public static ScanMode? Toggle(ScanMode? current, ScanMode clicked) => current == clicked ? null : clicked;
}

/// <summary>
/// The scan page's one explicit lifecycle state. Every other page-level fact (button text, enabled state,
/// banner wording) is derived from this and from the underlying <see cref="ScanResult"/>/<see cref="ScanProgress"/>
/// — nothing infers scan lifecycle independently from an unrelated control's own local state.
/// </summary>
public enum ScanUiLifecycleState
{
    IdleNoMode, IdleQuickSelected, IdleDeepSelected,
    ScanningQuick, ScanningDeep, Cancelling,
    CompletedQuick, CompletedDeep,
    CompletedWithWarningsQuick, CompletedWithWarningsDeep,
    CancelledQuick, CancelledDeep,
    FailedQuick, FailedDeep
}

public static class ScanUiLifecycle
{
    /// <param name="effectiveMode">The mode actually running while scanning, or the currently selected mode
    /// while idle — the caller passes whichever is live; while scanning this must be the mode captured at scan
    /// start, since the user's current selection may have already moved on for the next attempt.</param>
    /// <param name="isScanning">True from the moment a scan starts until it reaches a terminal state.</param>
    /// <param name="liveStatus">The most recently reported live <see cref="ScanStatus"/>, used only to detect
    /// the Cancelling sub-state while <paramref name="isScanning"/> is still true.</param>
    /// <param name="latestAttempt">The most recently finished attempt, regardless of its outcome — used only
    /// while not scanning, to compute the terminal Completed/CompletedWithWarnings/Cancelled/Failed states.</param>
    public static ScanUiLifecycleState Compute(ScanMode? effectiveMode, bool isScanning, ScanStatus? liveStatus, ScanResult? latestAttempt)
    {
        if (isScanning)
        {
            if (liveStatus == ScanStatus.Cancelling) return ScanUiLifecycleState.Cancelling;
            return effectiveMode == ScanMode.Deep ? ScanUiLifecycleState.ScanningDeep : ScanUiLifecycleState.ScanningQuick;
        }
        if (latestAttempt is not null)
        {
            var deep = latestAttempt.Mode == ScanMode.Deep;
            return latestAttempt.Status switch
            {
                ScanStatus.Completed => deep ? ScanUiLifecycleState.CompletedDeep : ScanUiLifecycleState.CompletedQuick,
                ScanStatus.CompletedWithWarnings => deep ? ScanUiLifecycleState.CompletedWithWarningsDeep : ScanUiLifecycleState.CompletedWithWarningsQuick,
                ScanStatus.Cancelled => deep ? ScanUiLifecycleState.CancelledDeep : ScanUiLifecycleState.CancelledQuick,
                _ => deep ? ScanUiLifecycleState.FailedDeep : ScanUiLifecycleState.FailedQuick
            };
        }
        return effectiveMode switch
        {
            ScanMode.Quick => ScanUiLifecycleState.IdleQuickSelected,
            ScanMode.Deep => ScanUiLifecycleState.IdleDeepSelected,
            _ => ScanUiLifecycleState.IdleNoMode
        };
    }

    /// <summary>The exact primary-button wording for the given state, honoring the "Again" suffix only when the
    /// currently selected mode matches the mode of the most recent attempt — reselecting the other mode after a
    /// completion is a fresh first run of that mode, not a rescan, and must not say "Again".</summary>
    public static string PrimaryActionText(ScanMode? selectedMode, ScanResult? latestAttempt)
    {
        if (selectedMode is null) return "Choose Quick or Deep Analysis";
        var modeName = selectedMode == ScanMode.Quick ? "Quick" : "Deep";
        var again = latestAttempt is not null && latestAttempt.Mode == selectedMode;
        return again ? $"Run {modeName} Analysis Again" : $"Run {modeName} Analysis";
    }
}
