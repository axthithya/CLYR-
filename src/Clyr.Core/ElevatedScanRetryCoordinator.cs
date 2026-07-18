using System.Collections.Concurrent;
using Clyr.Contracts;

namespace Clyr.Core;

/// <summary>Narrow, testable abstraction over <see cref="ElevatedScanRetryRequestFactory"/> — exists purely so
/// <see cref="ElevatedScanRetryCoordinator"/> can be exercised with a fake in tests. The real production
/// implementation (the factory itself) is unmodified except for declaring this interface.</summary>
public interface IElevatedScanRetryRequestFactory
{
    ElevatedScanRetryRequestBuildResult Build(ScanResult result);
}

/// <summary>Narrow, testable abstraction over <see cref="ElevatedScannerLauncher"/>. Exposes only the one typed
/// request/response exchange — never a process handle, launch plan, pipe name, or any other launch
/// implementation detail.</summary>
public interface IElevatedScannerLauncher
{
    Task<ElevatedScannerLauncherResult> RunAsync(ElevatedScanRetryRequest request, CancellationToken cancellationToken);
}

/// <summary>Narrow, testable abstraction over <see cref="ElevatedScanResultReconciler"/>, which is a static class
/// and so cannot implement this itself — see <see cref="ElevatedScanResultReconcilerAdapter"/> for the thin,
/// side-effect-free wrapper used in production.</summary>
public interface IElevatedScanResultReconciler
{
    ElevatedReconciliationResult Reconcile(ScanResult originalResult, ElevatedScanRetryRequest request, ElevatedScannerLauncherResult launcherResult);
}

/// <summary>The one production implementation of <see cref="IElevatedScanResultReconciler"/> — a thin,
/// stateless call-through to the existing, already-reviewed static <see cref="ElevatedScanResultReconciler"/>
/// (Phase 7.2.6G1/G2), which is deliberately left unmodified by this task.</summary>
public sealed class ElevatedScanResultReconcilerAdapter(IClock? clock = null) : IElevatedScanResultReconciler
{
    public ElevatedReconciliationResult Reconcile(ScanResult originalResult, ElevatedScanRetryRequest request, ElevatedScannerLauncherResult launcherResult) =>
        ElevatedScanResultReconciler.Reconcile(originalResult, request, launcherResult, clock);
}

/// <summary>Every way one end-to-end elevated-retry workflow attempt can end. <see cref="Applied"/> is the only
/// outcome carrying a combined result; every other value means <see cref="ElevatedScanRetryWorkflowResult.OriginalResult"/>
/// is reported completely unchanged. <see cref="AlreadyRunning"/> exists only at the workflow layer — neither the
/// request factory, the launcher, nor the reconciler know anything about concurrent attempts; that guard belongs
/// to the coordinator alone.</summary>
public enum ElevatedScanRetryWorkflowOutcome
{
    Applied,
    NotEligible,
    AlreadyRunning,
    Denied,
    Cancelled,
    TimedOut,
    HelperMissing,
    InvalidLaunchPlan,
    LaunchFailed,
    ConnectionTimedOut,
    ResponseTimedOut,
    ProtocolRejected,
    ValidationRejected,
    InvalidResponse,
    RequiresReplacementData,
    AccountingBasisMismatch,
    RootSetMismatch,
    Failed
}

/// <summary>
/// The immutable, bounded result of one <see cref="ElevatedScanRetryCoordinator.RetryAsync"/> attempt.
/// <see cref="OriginalResult"/> is always the exact same object reference the caller passed in — this workflow
/// never mutates it and never constructs a look-alike copy pretending to be the original.
/// <see cref="CombinedResult"/> (the reconciler's own <see cref="ElevatedReconciliationResult"/>, already an
/// immutable value distinct from the original) is populated only when <see cref="Outcome"/> is
/// <see cref="ElevatedScanRetryWorkflowOutcome.Applied"/>. <see cref="StatusMessageKey"/> is a small, fixed,
/// non-identifying key (never a raw exception message, path, or IPC payload) a caller can map to user-facing text.
/// </summary>
public sealed record ElevatedScanRetryWorkflowResult(
    ElevatedScanRetryWorkflowOutcome Outcome, ScanResult OriginalResult,
    ElevatedScanRetryEligibilityOutcome? RequestBuildOutcome, ElevatedScannerLauncherOutcome? LauncherOutcome,
    ElevatedReconciliationOutcome? ReconciliationOutcome, ElevatedReconciliationResult? CombinedResult,
    long? AdditionalLogicalBytes, long? AdditionalAllocatedBytes,
    int RootsRequested, int RootsCompleted, int RootsStillInaccessible, string StatusMessageKey)
{
    public bool IsApplied => Outcome == ElevatedScanRetryWorkflowOutcome.Applied;
}

/// <summary>
/// Orchestrates the full, already-independently-reviewed elevated permission-limited-root retry workflow —
/// request construction (<see cref="IElevatedScanRetryRequestFactory"/>), the one-shot elevated launch
/// (<see cref="IElevatedScannerLauncher"/>), and safe reconciliation (<see cref="IElevatedScanResultReconciler"/>)
/// — behind one typed, bounded entry point. Contains no UI logic, no dialogs, no navigation, no process-launch
/// implementation, no named-pipe implementation, no scanning implementation, and no persistence implementation of
/// its own; every one of those concerns belongs to the injected dependency that already owns it. The public
/// <see cref="RetryAsync"/> method accepts only the original typed <see cref="ScanResult"/> and a
/// <see cref="CancellationToken"/> — no path, executable name, pipe name, manifest, nonce, command, argument,
/// user-entered root, launch plan, or raw IPC data can ever reach this class from a caller.
/// </summary>
public sealed class ElevatedScanRetryCoordinator(
    IElevatedScanRetryRequestFactory requestFactory, IElevatedScannerLauncher launcher, IElevatedScanResultReconciler reconciler)
{
    /// <summary>Bounded concurrency guard keyed by the original scan's execution ID: at most one entry per
    /// currently in-flight retry, always removed (in the <c>finally</c> block below) the instant that retry ends
    /// for any reason — applied, denied, cancelled, timed out, or failed. Never grows without bound and is never
    /// persisted; a process restart clears it entirely, which is correct since no retry can still be "in flight"
    /// across a restart.</summary>
    private readonly ConcurrentDictionary<Guid, byte> activeRetries = new();

    public async Task<ElevatedScanRetryWorkflowResult> RetryAsync(ScanResult originalResult, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Preserved(originalResult, ElevatedScanRetryWorkflowOutcome.Cancelled, null, null, null, 0);

        if (!activeRetries.TryAdd(originalResult.ScanId, 0))
            return Preserved(originalResult, ElevatedScanRetryWorkflowOutcome.AlreadyRunning, null, null, null, 0);

        try
        {
            var buildResult = requestFactory.Build(originalResult);
            if (!buildResult.IsSuccess)
                return Preserved(originalResult, ElevatedScanRetryWorkflowOutcome.NotEligible, buildResult.Outcome, null, null, 0);

            var request = buildResult.Request!;
            // Section 10: cancellation after request construction but before launch never reaches the launcher.
            if (cancellationToken.IsCancellationRequested)
                return Preserved(originalResult, ElevatedScanRetryWorkflowOutcome.Cancelled, buildResult.Outcome, null, null, request.PermissionLimitedRoots.Length);

            // Exactly one launcher call, ever, per RetryAsync attempt — never retried automatically.
            var launcherResult = await launcher.RunAsync(request, cancellationToken).ConfigureAwait(false);
            if (launcherResult.Outcome != ElevatedScannerLauncherOutcome.Completed || launcherResult.Response is null)
                return Preserved(originalResult, MapLauncherOutcome(launcherResult.Outcome), buildResult.Outcome,
                    launcherResult.Outcome, null, request.PermissionLimitedRoots.Length);

            // Reconciliation is a synchronous, pure, in-memory computation (no I/O, no cancellation point) — once
            // a valid typed response exists there is no window in which an ambiguous partially-applied state
            // could occur.
            var reconciliationResult = reconciler.Reconcile(originalResult, request, launcherResult);
            return FromReconciliation(originalResult, buildResult.Outcome, launcherResult.Outcome, reconciliationResult, request.PermissionLimitedRoots.Length);
        }
        finally
        {
            activeRetries.TryRemove(originalResult.ScanId, out _);
        }
    }

    private static ElevatedScanRetryWorkflowResult Preserved(ScanResult originalResult, ElevatedScanRetryWorkflowOutcome outcome,
        ElevatedScanRetryEligibilityOutcome? requestBuildOutcome, ElevatedScannerLauncherOutcome? launcherOutcome,
        ElevatedReconciliationOutcome? reconciliationOutcome, int rootsRequested) =>
        new(outcome, originalResult, requestBuildOutcome, launcherOutcome, reconciliationOutcome, null, null, null,
            rootsRequested, 0, 0, StatusMessageKeyFor(outcome));

    private static ElevatedScanRetryWorkflowResult FromReconciliation(ScanResult originalResult,
        ElevatedScanRetryEligibilityOutcome requestBuildOutcome, ElevatedScannerLauncherOutcome launcherOutcome,
        ElevatedReconciliationResult reconciliationResult, int rootsRequested)
    {
        var outcome = MapReconciliationOutcome(reconciliationResult.Outcome);
        var combined = reconciliationResult.IsApplied ? reconciliationResult : null;
        return new(outcome, originalResult, requestBuildOutcome, launcherOutcome, reconciliationResult.Outcome, combined,
            combined?.Attempt.AdditionalLogicalBytes, combined?.Attempt.AdditionalAllocatedBytes,
            rootsRequested, reconciliationResult.Attempt.RootsCompleted, reconciliationResult.Attempt.RootsStillInaccessible,
            StatusMessageKeyFor(outcome));
    }

    private static ElevatedScanRetryWorkflowOutcome MapLauncherOutcome(ElevatedScannerLauncherOutcome outcome) => outcome switch
    {
        ElevatedScannerLauncherOutcome.Denied => ElevatedScanRetryWorkflowOutcome.Denied,
        ElevatedScannerLauncherOutcome.Cancelled => ElevatedScanRetryWorkflowOutcome.Cancelled,
        ElevatedScannerLauncherOutcome.HelperMissing => ElevatedScanRetryWorkflowOutcome.HelperMissing,
        ElevatedScannerLauncherOutcome.InvalidLaunchPlan => ElevatedScanRetryWorkflowOutcome.InvalidLaunchPlan,
        ElevatedScannerLauncherOutcome.LaunchFailed => ElevatedScanRetryWorkflowOutcome.LaunchFailed,
        ElevatedScannerLauncherOutcome.ConnectionTimedOut => ElevatedScanRetryWorkflowOutcome.ConnectionTimedOut,
        ElevatedScannerLauncherOutcome.ResponseTimedOut => ElevatedScanRetryWorkflowOutcome.ResponseTimedOut,
        ElevatedScannerLauncherOutcome.ProtocolRejected => ElevatedScanRetryWorkflowOutcome.ProtocolRejected,
        ElevatedScannerLauncherOutcome.ValidationRejected => ElevatedScanRetryWorkflowOutcome.ValidationRejected,
        ElevatedScannerLauncherOutcome.InvalidResponse => ElevatedScanRetryWorkflowOutcome.InvalidResponse,
        _ => ElevatedScanRetryWorkflowOutcome.Failed // Completed never reaches here; Failed covers everything else.
    };

    private static ElevatedScanRetryWorkflowOutcome MapReconciliationOutcome(ElevatedReconciliationOutcome outcome) => outcome switch
    {
        ElevatedReconciliationOutcome.Applied => ElevatedScanRetryWorkflowOutcome.Applied,
        ElevatedReconciliationOutcome.NotEligible => ElevatedScanRetryWorkflowOutcome.NotEligible,
        ElevatedReconciliationOutcome.Denied => ElevatedScanRetryWorkflowOutcome.Denied,
        ElevatedReconciliationOutcome.Cancelled => ElevatedScanRetryWorkflowOutcome.Cancelled,
        ElevatedReconciliationOutcome.TimedOut => ElevatedScanRetryWorkflowOutcome.TimedOut,
        ElevatedReconciliationOutcome.LaunchFailed => ElevatedScanRetryWorkflowOutcome.LaunchFailed,
        ElevatedReconciliationOutcome.InvalidResponse => ElevatedScanRetryWorkflowOutcome.InvalidResponse,
        ElevatedReconciliationOutcome.ValidationRejected => ElevatedScanRetryWorkflowOutcome.ValidationRejected,
        ElevatedReconciliationOutcome.RootSetMismatch => ElevatedScanRetryWorkflowOutcome.RootSetMismatch,
        ElevatedReconciliationOutcome.RequiresReplacementData => ElevatedScanRetryWorkflowOutcome.RequiresReplacementData,
        ElevatedReconciliationOutcome.AccountingBasisMismatch => ElevatedScanRetryWorkflowOutcome.AccountingBasisMismatch,
        _ => ElevatedScanRetryWorkflowOutcome.Failed
    };

    /// <summary>A small, fixed, non-identifying key — never a raw exception message, path, or IPC payload —
    /// a caller can map to user-facing text.</summary>
    private static string StatusMessageKeyFor(ElevatedScanRetryWorkflowOutcome outcome) => outcome switch
    {
        ElevatedScanRetryWorkflowOutcome.Applied => "elevated-retry.applied",
        ElevatedScanRetryWorkflowOutcome.NotEligible => "elevated-retry.not-eligible",
        ElevatedScanRetryWorkflowOutcome.AlreadyRunning => "elevated-retry.already-running",
        ElevatedScanRetryWorkflowOutcome.Denied => "elevated-retry.denied",
        ElevatedScanRetryWorkflowOutcome.Cancelled => "elevated-retry.cancelled",
        ElevatedScanRetryWorkflowOutcome.TimedOut => "elevated-retry.timed-out",
        ElevatedScanRetryWorkflowOutcome.HelperMissing => "elevated-retry.helper-missing",
        ElevatedScanRetryWorkflowOutcome.InvalidLaunchPlan => "elevated-retry.invalid-launch-plan",
        ElevatedScanRetryWorkflowOutcome.LaunchFailed => "elevated-retry.launch-failed",
        ElevatedScanRetryWorkflowOutcome.ConnectionTimedOut => "elevated-retry.connection-timed-out",
        ElevatedScanRetryWorkflowOutcome.ResponseTimedOut => "elevated-retry.response-timed-out",
        ElevatedScanRetryWorkflowOutcome.ProtocolRejected => "elevated-retry.protocol-rejected",
        ElevatedScanRetryWorkflowOutcome.ValidationRejected => "elevated-retry.validation-rejected",
        ElevatedScanRetryWorkflowOutcome.InvalidResponse => "elevated-retry.invalid-response",
        ElevatedScanRetryWorkflowOutcome.RequiresReplacementData => "elevated-retry.requires-replacement-data",
        ElevatedScanRetryWorkflowOutcome.AccountingBasisMismatch => "elevated-retry.accounting-basis-mismatch",
        ElevatedScanRetryWorkflowOutcome.RootSetMismatch => "elevated-retry.root-set-mismatch",
        _ => "elevated-retry.failed"
    };
}
