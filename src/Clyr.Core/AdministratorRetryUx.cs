using Clyr.Contracts;

namespace Clyr.Core;

/// <summary>The administrator-retry action's one explicit lifecycle phase — every displayed fact (button
/// enabled state, status text, whether a summary panel shows) derives from this, mirroring the established
/// <see cref="ScanUiLifecycleState"/> pattern rather than inventing independent booleans.</summary>
public enum AdministratorRetryPhase
{
    Hidden, Idle, Running, Applied, Denied, Cancelled, TimedOut, Failed, NotEligible
}

/// <summary>
/// A small, immutable, bounded snapshot of the administrator-retry action's current on-screen state. Carries no
/// raw path, manifest, nonce, pipe name, or executable/process detail — only plain counts, a fixed status key,
/// and pre-composed safe display text. <see cref="CombinedResult"/> is populated only when
/// <see cref="Phase"/> is <see cref="AdministratorRetryPhase.Applied"/>, and is always a separate value from
/// whatever <see cref="ScanResult"/> the caller evaluated — never a replacement for it.
/// </summary>
public sealed record AdministratorRetryUiState(
    bool IsAdministratorRetryAvailable, bool IsAdministratorRetryRunning, AdministratorRetryPhase Phase,
    string AdministratorRetryStatusKey, string AdministratorRetryStatusText,
    int ReplaceableRootCount, long? AdditionalLogicalBytes, long? AdditionalAllocatedBytes,
    int RootsCompleted, int RootsStillInaccessible, ElevatedReconciliationResult? CombinedResult)
{
    public static AdministratorRetryUiState Hidden { get; } =
        new(false, false, AdministratorRetryPhase.Hidden, "hidden", string.Empty, 0, null, null, 0, 0, null);
}

/// <summary>
/// Pure, deterministic text and state projection for the administrator-retry action — no launch, no IPC, no UAC,
/// no filesystem access, and no eligibility reimplementation; every visibility decision is read straight from an
/// already-computed <see cref="ElevatedScanRetryAvailability"/>, and every outcome mapping is read straight from
/// an already-computed <see cref="ElevatedScanRetryWorkflowResult"/>. Every user-facing string here is calm,
/// truthful, and safe to render as-is — never an exception message, stack trace, raw protocol response, pipe
/// name, executable path, or request nonce.
/// </summary>
public static class AdministratorRetryUx
{
    public const string ButtonText = "Retry restricted areas as administrator";
    public const string SupportingText =
        "Some folders could not be fully inspected. CLYR can retry only those scan areas with administrator access.";

    public const string ConfirmationTitle = "Retry restricted areas?";
    public const string ConfirmationBody =
        "Windows will ask for administrator permission. CLYR will only read file and folder metadata for the " +
        "restricted scan areas. It will not delete, move, rename or modify files.";
    public const string ConfirmationPrimaryButtonText = "Continue";
    public const string ConfirmationCloseButtonText = "Cancel";

    public const string RunningStatusText = "Retrying restricted areas…";
    public const string AppliedStatusText = "Administrator retry completed.";
    public const string DeniedStatusText = "Administrator permission was cancelled. Your original scan is unchanged.";
    public const string CancelledStatusText = "Administrator retry was cancelled. Your original scan is unchanged.";
    public const string TimedOutStatusText = "The administrator retry did not respond in time. Your original scan is unchanged.";
    public const string FailedStatusText = "Administrator retry could not be completed. Your original scan is unchanged.";

    /// <summary>The one entry point for deciding whether the action should even be shown — reads only
    /// <see cref="ElevatedScanRetryAvailability.IsEligible"/> and its truthful root count; never inspects a root
    /// path or re-derives eligibility rules itself.</summary>
    public static AdministratorRetryUiState FromAvailability(ElevatedScanRetryAvailability availability) =>
        availability.IsEligible
            ? new(true, false, AdministratorRetryPhase.Idle, availability.SafeStatusMessageKey, string.Empty,
                availability.ReplaceableRootCount, null, null, 0, 0, null)
            : AdministratorRetryUiState.Hidden;

    public static AdministratorRetryUiState Running(AdministratorRetryUiState current) =>
        current with { IsAdministratorRetryRunning = true, Phase = AdministratorRetryPhase.Running, AdministratorRetryStatusText = RunningStatusText };

    public static AdministratorRetryUiState FromWorkflowResult(ElevatedScanRetryWorkflowResult result)
    {
        var phase = PhaseFor(result.Outcome);
        var combined = phase == AdministratorRetryPhase.Applied ? result.CombinedResult : null;
        return new(true, false, phase, result.StatusMessageKey, TextFor(phase),
            result.RootsRequested, result.AdditionalLogicalBytes, result.AdditionalAllocatedBytes,
            result.RootsCompleted, result.RootsStillInaccessible, combined);
    }

    private static AdministratorRetryPhase PhaseFor(ElevatedScanRetryWorkflowOutcome outcome) => outcome switch
    {
        ElevatedScanRetryWorkflowOutcome.Applied => AdministratorRetryPhase.Applied,
        ElevatedScanRetryWorkflowOutcome.Denied => AdministratorRetryPhase.Denied,
        ElevatedScanRetryWorkflowOutcome.NotEligible => AdministratorRetryPhase.NotEligible,
        ElevatedScanRetryWorkflowOutcome.Cancelled => AdministratorRetryPhase.Cancelled,
        ElevatedScanRetryWorkflowOutcome.TimedOut or ElevatedScanRetryWorkflowOutcome.ConnectionTimedOut
            or ElevatedScanRetryWorkflowOutcome.ResponseTimedOut => AdministratorRetryPhase.TimedOut,
        _ => AdministratorRetryPhase.Failed // HelperMissing, InvalidLaunchPlan, LaunchFailed, ProtocolRejected, ValidationRejected,
                                            // InvalidResponse, RequiresReplacementData, AccountingBasisMismatch, RootSetMismatch,
                                            // AlreadyRunning, Failed — every one of these is reported as the same calm, bounded message.
    };

    private static string TextFor(AdministratorRetryPhase phase) => phase switch
    {
        AdministratorRetryPhase.Applied => AppliedStatusText,
        AdministratorRetryPhase.Denied => DeniedStatusText,
        AdministratorRetryPhase.Cancelled => CancelledStatusText,
        AdministratorRetryPhase.TimedOut => TimedOutStatusText,
        AdministratorRetryPhase.NotEligible => string.Empty,
        _ => FailedStatusText
    };
}

/// <summary>
/// Owns exactly one administrator-retry attempt's lifecycle for one completed <see cref="ScanResult"/>: asking
/// <see cref="IElevatedScanRetryService"/> for availability, and — only on an explicit, already-confirmed
/// caller request — running exactly one retry attempt through it. Contains no UI logic, no dialog, no navigation,
/// and no privileged capability of its own; every real decision and every byte of privileged behavior stays
/// behind <see cref="IElevatedScanRetryService"/>. Prevents a second concurrent attempt for the same (or any)
/// result outright — <see cref="RunAsync"/> is a no-op unless <see cref="CanStart"/> is true at the moment it is
/// called, and a fresh <see cref="AdministratorRetryUiState.IsAdministratorRetryRunning"/> value is set
/// synchronously before any awaiting begins, so a rapid duplicate call can never race past this guard.
/// </summary>
public sealed class AdministratorRetryController(IElevatedScanRetryService service) : IDisposable
{
    private CancellationTokenSource? cancellation;
    private ScanResult? evaluatedFor;

    public AdministratorRetryUiState State { get; private set; } = AdministratorRetryUiState.Hidden;
    public event EventHandler? StateChanged;

    /// <summary>Re-evaluates visibility for the given result (or hides the action entirely for
    /// <see langword="null"/> — the caller passes <see langword="null"/> while no completed result exists, or
    /// while a new scan is running). Never called mid-retry: a retry already in flight for a previous evaluation
    /// keeps its own state undisturbed until it finishes, so a routine page refresh can never yank the button or
    /// its running status out from under an active attempt.</summary>
    public void Evaluate(ScanResult? originalResult)
    {
        if (cancellation is not null) return;
        evaluatedFor = originalResult;
        SetState(originalResult is null ? AdministratorRetryUiState.Hidden : AdministratorRetryUx.FromAvailability(service.Evaluate(originalResult)));
    }

    /// <summary>True only when the action is currently available, not already running, and
    /// <paramref name="originalResult"/> is the exact same result this controller most recently evaluated
    /// availability for.</summary>
    public bool CanStart(ScanResult originalResult) =>
        cancellation is null && State.IsAdministratorRetryAvailable && !State.IsAdministratorRetryRunning && ReferenceEquals(originalResult, evaluatedFor);

    public async Task RunAsync(ScanResult originalResult)
    {
        if (!CanStart(originalResult)) return;
        cancellation = new CancellationTokenSource();
        SetState(AdministratorRetryUx.Running(State));
        try
        {
            var result = await service.RetryAsync(originalResult, cancellation.Token).ConfigureAwait(false);
            SetState(AdministratorRetryUx.FromWorkflowResult(result));
        }
        finally
        {
            cancellation.Dispose();
            cancellation = null;
        }
    }

    /// <summary>Cancels the in-flight attempt, if any — never a generic process-kill, never an automatic restart.</summary>
    public void Cancel() => cancellation?.Cancel();

    public void Dispose() => cancellation?.Cancel();

    private void SetState(AdministratorRetryUiState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
