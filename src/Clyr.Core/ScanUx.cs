using Clyr.Contracts;

namespace Clyr.Core;

/// <summary>
/// The normal-app "Analyze drive" experience's one explicit lifecycle state — mode-agnostic. The normal WinUI
/// flow no longer exposes a Quick/Deep choice to the user (see <c>AppSessionViewModel.StartAsync</c>, which
/// always runs <see cref="ScanMode.Deep"/> for this flow internally); <see cref="ScanMode"/> remains a real,
/// unmodified concept for CLI compatibility and Developer Mode diagnostics, just no longer a normal-user
/// selection this state needs to track. Every other page-level fact (button text, enabled state, banner wording)
/// derives from this and from the underlying <see cref="ScanResult"/>/<see cref="ScanProgress"/>/
/// <see cref="ProgressiveScanSnapshot"/> — nothing infers scan lifecycle independently from an unrelated
/// control's own local state.
/// </summary>
public enum ScanUiLifecycleState
{
    Idle,
    Preparing,
    RunningBeforeInsights,
    RunningWithInsights,
    Cancelling,
    Completed,
    CompletedWithWarnings,
    Cancelled,
    Failed
}

public static class ScanUiLifecycle
{
    /// <param name="isScanning">True from the moment a scan starts until it reaches a terminal state.</param>
    /// <param name="liveStatus">The most recently reported live <see cref="ScanStatus"/>, used only to detect
    /// the Preparing/Cancelling sub-states while <paramref name="isScanning"/> is still true.</param>
    /// <param name="earlyInsightsReady">True once a real, valid <see cref="ProgressiveScanSnapshot"/> exists with
    /// enough aggregate data to be worth showing — never a timer- or percentage-based guess.</param>
    /// <param name="latestAttempt">The most recently finished attempt, regardless of its outcome — used only
    /// while not scanning, to compute the terminal Completed/CompletedWithWarnings/Cancelled/Failed states.</param>
    public static ScanUiLifecycleState Compute(bool isScanning, ScanStatus? liveStatus, bool earlyInsightsReady, ScanResult? latestAttempt)
    {
        if (isScanning)
        {
            if (liveStatus == ScanStatus.Cancelling) return ScanUiLifecycleState.Cancelling;
            if (liveStatus is null or ScanStatus.Preparing) return ScanUiLifecycleState.Preparing;
            return earlyInsightsReady ? ScanUiLifecycleState.RunningWithInsights : ScanUiLifecycleState.RunningBeforeInsights;
        }
        if (latestAttempt is not null)
        {
            return latestAttempt.Status switch
            {
                ScanStatus.Completed => ScanUiLifecycleState.Completed,
                ScanStatus.CompletedWithWarnings => ScanUiLifecycleState.CompletedWithWarnings,
                ScanStatus.Cancelled => ScanUiLifecycleState.Cancelled,
                _ => ScanUiLifecycleState.Failed
            };
        }
        return ScanUiLifecycleState.Idle;
    }
}
