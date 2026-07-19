using System.ComponentModel;
using System.Text.Json;
using Clyr.Contracts;

namespace Clyr.Core;

/// <summary>The administrator-retry action's one explicit lifecycle phase — every displayed fact (button
/// enabled state, status text, whether a summary panel shows) derives from this, mirroring the established
/// <see cref="ScanUiLifecycleState"/> pattern rather than inventing independent booleans. Phase 7.2.6H2E splits
/// what was one coarse <c>TimedOut</c> value into three distinct phases so the UI can name the actual typed
/// outcome truthfully (a helper that reached its own bounded operation budget is a materially different story
/// from one that never even answered the pipe) rather than a single generic "did not respond in time".
/// <para/>
/// Phase (Administrator Retry validation correction): a later pass found every remaining rejection reason
/// (identity/root mismatches, an unreconcilable accounting response, and outright helper/launch failures) was
/// still collapsed into one generic <c>Failed</c> phase/message — <see cref="ResponseMismatch"/>,
/// <see cref="AccountingMismatch"/>, and <see cref="HelperValidationFailed"/> split that into the three bounded,
/// truthful categories a user can actually reason about, per the same "name the real typed outcome" principle
/// above. <c>Failed</c> itself remains only for the rare unexpected-exception boundary case.</summary>
public enum AdministratorRetryPhase
{
    Hidden, Idle, Running, Applied, Denied, Cancelled, OperationTimedOut, ConnectionTimedOut, ResponseTimedOut,
    ResponseMismatch, AccountingMismatch, HelperValidationFailed, Failed, NotEligible
}

/// <summary>
/// A small, immutable, bounded snapshot of the administrator-retry action's current on-screen state. Carries no
/// raw path, manifest, nonce, pipe name, or executable/process detail — only plain counts, a fixed status key,
/// and pre-composed safe display text. <see cref="CombinedResult"/> is populated only when
/// <see cref="Phase"/> is <see cref="AdministratorRetryPhase.Applied"/>, and is always a separate value from
/// whatever <see cref="ScanResult"/> the caller evaluated — never a replacement for it.
/// <see cref="RunningSinceUtc"/> is populated only while <see cref="IsAdministratorRetryRunning"/>, so the caller
/// can compute an elapsed duration for display without this type (or the WinUI-free
/// <see cref="AdministratorRetryController"/> that owns it) ever running its own background timer.
/// </summary>
public sealed record AdministratorRetryUiState(
    bool IsAdministratorRetryAvailable, bool IsAdministratorRetryRunning, AdministratorRetryPhase Phase,
    string AdministratorRetryStatusKey, string AdministratorRetryTitle, string AdministratorRetryStatusText,
    int ReplaceableRootCount, long? AdditionalLogicalBytes, long? AdditionalAllocatedBytes,
    int RootsCompleted, int RootsStillInaccessible, DateTimeOffset? RunningSinceUtc, ElevatedReconciliationResult? CombinedResult)
{
    public static AdministratorRetryUiState Hidden { get; } =
        new(false, false, AdministratorRetryPhase.Hidden, "hidden", string.Empty, string.Empty, 0, null, null, 0, 0, null, null);
}

/// <summary>
/// Pure, deterministic text and state projection for the administrator-retry action — no launch, no IPC, no UAC,
/// no filesystem access, and no eligibility reimplementation; every visibility decision is read straight from an
/// already-computed <see cref="ElevatedScanRetryAvailability"/>, and every outcome mapping is read straight from
/// an already-computed <see cref="ElevatedScanRetryWorkflowResult"/>. Every user-facing string here is calm,
/// truthful, and safe to render as-is — never an exception message, stack trace, raw protocol response, pipe
/// name, executable path, or request nonce. Byte counts are exposed as plain numbers only — formatting them into
/// human-readable sizes (GiB/MiB) is the WinUI page's own job, exactly as it already does for every other metric
/// on the Results page.
/// </summary>
public static class AdministratorRetryUx
{
    /// <summary>The elevated helper's own bounded operation budget — read from the same coordinated policy the
    /// transport enforces, never a second, independently maintained number.</summary>
    public static TimeSpan SafetyLimit => ElevatedScanRetryTimeoutPolicy.Default.OperationBudget;

    public const string ButtonText = "Retry restricted areas as administrator";
    public const string RetryAgainButtonText = "Retry administrator scan";
    public const string SupportingText =
        "Some folders could not be fully inspected. CLYR can retry only those scan areas with administrator access.";

    public const string ConfirmationPrimaryButtonText = "Continue to Windows permission";
    public const string ConfirmationCloseButtonText = "Cancel";

    public const string RunningTitle = "Scanning restricted areas as administrator";
    public const string RunningSupportingText = "CLYR is reading file and folder metadata only. Large areas can take several minutes.";

    public const string AppliedTitle = "Administrator retry completed";
    public const string DeniedTitle = "Administrator permission cancelled";
    public const string DeniedText = "No administrator scan was performed. Your original scan is unchanged.";
    public const string CancelledTitle = "Administrator retry cancelled";
    public const string CancelledText = "No retry result was applied. Your original scan is unchanged.";
    public const string OperationTimedOutTitle = "Administrator retry reached its safety limit";
    public const string ConnectionTimedOutTitle = "Administrator helper did not connect";
    public const string ResponseTimedOutTitle = "Administrator helper stopped responding";
    public const string ResponseMismatchTitle = "The helper response did not match this analysis";
    public const string ResponseMismatchText = "CLYR did not apply the administrator result because the response did not correspond to this analysis. Your original scan is unchanged.";
    public const string AccountingMismatchTitle = "The returned storage accounting could not be reconciled safely";
    public const string AccountingMismatchText = "CLYR did not apply the administrator result because the returned figures could not be safely combined with this analysis. Your original scan is unchanged.";
    public const string HelperValidationFailedTitle = "The administrator helper could not be validated";
    public const string HelperValidationFailedText = "CLYR did not apply the administrator result because the helper could not be safely validated. Your original scan is unchanged.";
    public const string FailedTitle = "Administrator retry could not be completed";
    public const string FailedText = "CLYR did not apply the administrator result because it could not be safely validated. Your original scan is unchanged.";

    /// <summary>Confirmation-dialog title, including the truthful replaceable-root count — never a raw path.</summary>
    public static string ConfirmationTitle(int replaceableRootCount) =>
        $"Retry {replaceableRootCount} restricted area{(replaceableRootCount == 1 ? "" : "s")}?";

    public static string ConfirmationBody(TimeSpan safetyLimit) =>
        "Windows will ask for administrator permission. CLYR will only read file and folder metadata in the " +
        "restricted scan areas. It will not delete, move, rename or modify files.\n\n" +
        $"This can take several minutes. The retry has a {FormatMinutes(safetyLimit)} safety limit.";

    /// <summary>Bounded running-state primary status text — the exact replaceable-root count, never a path.</summary>
    public static string RunningPrimaryStatus(int replaceableRootCount) =>
        $"Retrying {replaceableRootCount} restricted area{(replaceableRootCount == 1 ? "" : "s")}…";

    /// <summary>The full helper-operation-timeout message: the bounded safety-limit statement plus a supporting
    /// detail that never claims all missing space was found, and derives the "existing X%-accounted result"
    /// figure from the original scan itself — never a hard-coded number.</summary>
    public static string OperationTimedOutMessage(ScanResult originalResult)
    {
        var percentageText = ScanAccounting.Summarize(originalResult).AccountedPercentage is { } percentage
            ? $"{percentage:F1}%-accounted"
            : "already-accounted";
        return $"The restricted-area scan did not finish within the {FormatMinutes(SafetyLimit)} safety limit. " +
            "No partial result was applied, and your original scan remains unchanged. " +
            $"No files were modified. You may retry once or continue using the existing {percentageText} result.";
    }

    private static string FormatMinutes(TimeSpan value) => $"{value.TotalMinutes:N0}-minute";

    /// <summary>The one entry point for deciding whether the action should even be shown — reads only
    /// <see cref="ElevatedScanRetryAvailability.IsEligible"/> and its truthful root count; never inspects a root
    /// path or re-derives eligibility rules itself.</summary>
    public static AdministratorRetryUiState FromAvailability(ElevatedScanRetryAvailability availability) =>
        availability.IsEligible
            ? new(true, false, AdministratorRetryPhase.Idle, availability.SafeStatusMessageKey, string.Empty, string.Empty,
                availability.ReplaceableRootCount, null, null, 0, 0, null, null)
            : AdministratorRetryUiState.Hidden;

    public static AdministratorRetryUiState Running(AdministratorRetryUiState current, DateTimeOffset nowUtc) =>
        current with
        {
            IsAdministratorRetryRunning = true,
            Phase = AdministratorRetryPhase.Running,
            AdministratorRetryTitle = RunningTitle,
            AdministratorRetryStatusText = RunningPrimaryStatus(current.ReplaceableRootCount),
            RunningSinceUtc = nowUtc
        };

    public static AdministratorRetryUiState FromWorkflowResult(ElevatedScanRetryWorkflowResult result)
    {
        var phase = PhaseFor(result.Outcome);
        var combined = phase == AdministratorRetryPhase.Applied ? result.CombinedResult : null;
        return new(true, false, phase, result.StatusMessageKey, TitleFor(phase), TextFor(phase, result.OriginalResult),
            result.RootsRequested, result.AdditionalLogicalBytes, result.AdditionalAllocatedBytes,
            result.RootsCompleted, result.RootsStillInaccessible, null, combined);
    }

    /// <summary>Builds a calm terminal state directly from a phase, for the rare case where an unexpected
    /// operational exception escaped <see cref="IElevatedScanRetryService.RetryAsync"/> and there is no
    /// <see cref="ElevatedScanRetryWorkflowResult"/> to project from — see
    /// <see cref="AdministratorRetryController.RunAsync"/>. Never carries any count or combined-result data that
    /// would otherwise have come from a real response.</summary>
    public static AdministratorRetryUiState Terminal(AdministratorRetryPhase phase, ScanResult originalResult) =>
        new(true, false, phase, "elevated-retry." + phase.ToString().ToLowerInvariant(), TitleFor(phase), TextFor(phase, originalResult),
            0, null, null, 0, 0, null, null);

    /// <summary>Every exception kind this action's retry boundary is expected to see and safely contain —
    /// cancellation, timeout, and ordinary IPC/transport/serialization failures — mapped to <see langword="true"/>.
    /// <see cref="OutOfMemoryException"/> (the only one of the fatal triad a managed <c>catch</c> clause could
    /// otherwise intercept — <see cref="StackOverflowException"/> and <c>AccessViolationException</c> already
    /// terminate the process before any handler runs) is deliberately excluded so it is never silently caught and
    /// reported as a calm UI message.</summary>
    public static bool IsRecoverable(Exception exception) => exception is not OutOfMemoryException;

    private static AdministratorRetryPhase PhaseFor(ElevatedScanRetryWorkflowOutcome outcome) => outcome switch
    {
        ElevatedScanRetryWorkflowOutcome.Applied => AdministratorRetryPhase.Applied,
        ElevatedScanRetryWorkflowOutcome.Denied => AdministratorRetryPhase.Denied,
        ElevatedScanRetryWorkflowOutcome.NotEligible => AdministratorRetryPhase.NotEligible,
        ElevatedScanRetryWorkflowOutcome.Cancelled => AdministratorRetryPhase.Cancelled,
        ElevatedScanRetryWorkflowOutcome.TimedOut => AdministratorRetryPhase.OperationTimedOut,
        ElevatedScanRetryWorkflowOutcome.ConnectionTimedOut => AdministratorRetryPhase.ConnectionTimedOut,
        ElevatedScanRetryWorkflowOutcome.ResponseTimedOut => AdministratorRetryPhase.ResponseTimedOut,
        // The response did not correspond to this analysis — identity/shape mismatches, never a raw protocol dump.
        ElevatedScanRetryWorkflowOutcome.InvalidResponse or ElevatedScanRetryWorkflowOutcome.ValidationRejected
            or ElevatedScanRetryWorkflowOutcome.RootSetMismatch => AdministratorRetryPhase.ResponseMismatch,
        // The response was well-formed and matched this analysis, but its accounting could not be safely combined.
        ElevatedScanRetryWorkflowOutcome.RequiresReplacementData or ElevatedScanRetryWorkflowOutcome.AccountingBasisMismatch
            => AdministratorRetryPhase.AccountingMismatch,
        // The helper/launch itself could not be trusted or completed — never a raw exception, pipe name, or path.
        ElevatedScanRetryWorkflowOutcome.HelperMissing or ElevatedScanRetryWorkflowOutcome.InvalidLaunchPlan
            or ElevatedScanRetryWorkflowOutcome.LaunchFailed or ElevatedScanRetryWorkflowOutcome.ProtocolRejected
            => AdministratorRetryPhase.HelperValidationFailed,
        _ => AdministratorRetryPhase.Failed // AlreadyRunning, Failed, and any future unmapped outcome.
    };

    private static string TitleFor(AdministratorRetryPhase phase) => phase switch
    {
        AdministratorRetryPhase.Applied => AppliedTitle,
        AdministratorRetryPhase.Denied => DeniedTitle,
        AdministratorRetryPhase.Cancelled => CancelledTitle,
        AdministratorRetryPhase.OperationTimedOut => OperationTimedOutTitle,
        AdministratorRetryPhase.ConnectionTimedOut => ConnectionTimedOutTitle,
        AdministratorRetryPhase.ResponseTimedOut => ResponseTimedOutTitle,
        AdministratorRetryPhase.ResponseMismatch => ResponseMismatchTitle,
        AdministratorRetryPhase.AccountingMismatch => AccountingMismatchTitle,
        AdministratorRetryPhase.HelperValidationFailed => HelperValidationFailedTitle,
        AdministratorRetryPhase.NotEligible => string.Empty,
        _ => FailedTitle
    };

    private static string TextFor(AdministratorRetryPhase phase, ScanResult originalResult) => phase switch
    {
        AdministratorRetryPhase.Applied => string.Empty, // the page composes the full "inspected N areas, added X" sentence from the numeric fields.
        AdministratorRetryPhase.Denied => DeniedText,
        AdministratorRetryPhase.Cancelled => CancelledText,
        AdministratorRetryPhase.OperationTimedOut => OperationTimedOutMessage(originalResult),
        AdministratorRetryPhase.ConnectionTimedOut or AdministratorRetryPhase.ResponseTimedOut => "Your original scan is unchanged.",
        AdministratorRetryPhase.ResponseMismatch => ResponseMismatchText,
        AdministratorRetryPhase.AccountingMismatch => AccountingMismatchText,
        AdministratorRetryPhase.HelperValidationFailed => HelperValidationFailedText,
        AdministratorRetryPhase.NotEligible => string.Empty,
        _ => FailedText
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
/// synchronously before any awaiting begins, so a rapid duplicate call can never race past this guard. Never
/// starts a background timer of its own — <see cref="AdministratorRetryUiState.RunningSinceUtc"/> gives a caller
/// (such as a WinUI page's own <c>DispatcherTimer</c>) everything it needs to display elapsed time itself.
/// </summary>
public sealed class AdministratorRetryController(IElevatedScanRetryService service, IClock? clock = null) : IDisposable
{
    private readonly IClock clock = clock ?? new SystemClock();
    private CancellationTokenSource? cancellation;
    private ScanResult? evaluatedFor;
    private bool disposed;

    public AdministratorRetryUiState State { get; private set; } = AdministratorRetryUiState.Hidden;

    /// <summary>Raised only while this controller is not yet disposed — see <see cref="Dispose"/> and
    /// <see cref="SetState"/>. A caller (such as <c>ResultsPage</c>) may run on any thread by the time this
    /// fires — <see cref="IElevatedScanRetryService.RetryAsync"/> is awaited with <c>ConfigureAwait(false)</c>
    /// below, so its continuation (and the <see cref="SetState"/> call that follows it) resumes on whatever
    /// thread-pool thread completed the underlying operation, never necessarily the original caller's thread.
    /// This type stays WinUI-free on purpose; marshaling back to a UI thread is the subscriber's job.</summary>
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
        !disposed && cancellation is null && State.IsAdministratorRetryAvailable && !State.IsAdministratorRetryRunning && ReferenceEquals(originalResult, evaluatedFor);

    /// <summary>
    /// Runs exactly one retry attempt. Every operational exception this narrow boundary is expected to see —
    /// cancellation, a raw <see cref="TimeoutException"/>, transport/serialization failures
    /// (<see cref="IOException"/>, its <see cref="EndOfStreamException"/> subclass,
    /// <see cref="JsonException"/>, <see cref="Win32Exception"/>), and an
    /// <see cref="ObjectDisposedException"/> caused by this controller's own lifecycle cancellation racing this
    /// call — is contained here and mapped to one of the existing calm terminal states, never left to escape into
    /// an awaiting <c>async void</c> UI handler. <see cref="OutOfMemoryException"/> is deliberately never caught
    /// (see <see cref="AdministratorRetryUx.IsRecoverable"/>) — a fatal runtime failure is preserved, not hidden
    /// behind a reassuring message.
    /// </summary>
    public async Task RunAsync(ScanResult originalResult)
    {
        if (!CanStart(originalResult)) return;
        var ownCancellation = new CancellationTokenSource();
        cancellation = ownCancellation;
        SetState(AdministratorRetryUx.Running(State, clock.UtcNow));
        try
        {
            var result = await service.RetryAsync(originalResult, ownCancellation.Token).ConfigureAwait(false);
            SetState(AdministratorRetryUx.FromWorkflowResult(result));
        }
        catch (OperationCanceledException)
        {
            SetState(AdministratorRetryUx.Terminal(AdministratorRetryPhase.Cancelled, originalResult));
        }
        catch (ObjectDisposedException)
        {
            // Only reachable if Dispose() raced this call and cancelled/disposed the token source it was
            // holding out from under it — that is itself a form of cancellation, never an app-visible failure.
            SetState(AdministratorRetryUx.Terminal(AdministratorRetryPhase.Cancelled, originalResult));
        }
        catch (TimeoutException)
        {
            SetState(AdministratorRetryUx.Terminal(AdministratorRetryPhase.OperationTimedOut, originalResult));
        }
        catch (Exception exception) when (AdministratorRetryUx.IsRecoverable(exception))
        {
            // IOException (and its EndOfStreamException subclass), JsonException, Win32Exception, and any other
            // non-fatal operational failure this boundary was not specifically expecting all land here — one
            // calm, bounded message, never the exception's own message or stack trace.
            SetState(AdministratorRetryUx.Terminal(AdministratorRetryPhase.Failed, originalResult));
        }
        finally
        {
            SafeDispose(ownCancellation);
            if (ReferenceEquals(cancellation, ownCancellation)) cancellation = null;
        }
    }

    /// <summary>Cancels the in-flight attempt, if any — never a generic process-kill, never an automatic restart.</summary>
    public void Cancel() => SafeCancel(cancellation);

    /// <summary>Cancels any in-flight attempt and permanently stops <see cref="StateChanged"/> from firing again —
    /// safe to call even if <see cref="RunAsync"/>'s own <c>finally</c> is disposing the same token source
    /// concurrently (see <see cref="SafeCancel"/>/<see cref="SafeDispose"/>).</summary>
    public void Dispose()
    {
        disposed = true;
        SafeCancel(cancellation);
    }

    private static void SafeCancel(CancellationTokenSource? source)
    {
        try { source?.Cancel(); }
        catch (ObjectDisposedException) { /* Already disposed by a concurrent RunAsync completion — nothing to cancel. */ }
    }

    private static void SafeDispose(CancellationTokenSource source)
    {
        try { source.Dispose(); }
        catch (ObjectDisposedException) { /* CancellationTokenSource.Dispose() is normally idempotent; guarded defensively regardless. */ }
    }

    private void SetState(AdministratorRetryUiState state)
    {
        if (disposed) return;
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
