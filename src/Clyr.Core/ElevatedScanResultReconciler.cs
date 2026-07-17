using System.Collections.Immutable;
using Clyr.Contracts;

namespace Clyr.Core;

/// <summary>Every way one reconciliation attempt can end. Only <see cref="Applied"/> means the original result
/// was safely combined with new elevated coverage; every other value means the original result is reported
/// unchanged, for a specific, typed reason.</summary>
public enum ElevatedReconciliationOutcome
{
    Applied,
    NotEligible,
    Denied,
    Cancelled,
    TimedOut,
    LaunchFailed,
    InvalidResponse,
    ValidationRejected,
    RootSetMismatch,
    RequiresReplacementData,
    AccountingBasisMismatch
}

/// <summary>
/// An immutable audit record of one reconciliation attempt — never mutated, never reused. Carries its own fresh
/// <see cref="ReconciliationExecutionId"/> (distinct from <see cref="OriginalScanExecutionId"/> and from the
/// retry request's own correlation), so a caller can tell two separate reconciliation attempts against the same
/// original scan apart even if both otherwise look identical.
/// </summary>
public sealed record ElevatedRetryAttempt(
    Guid ReconciliationExecutionId, Guid OriginalScanExecutionId, string DriveIdentity, string RequestNonce,
    DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc, ElevatedScannerLauncherOutcome LauncherOutcome,
    ElevatedScanRetryOutcome? ResponseOutcome, int RootsRequested, int RootsCompleted, int RootsStillInaccessible,
    long AdditionalLogicalBytes, long AdditionalAllocatedBytes, ImmutableArray<string> AccountingLimitations, bool Applied);

/// <summary>
/// The immutable result of one reconciliation attempt. <see cref="OriginalResult"/> is always the exact,
/// untouched object the caller passed in — reconciliation never mutates it and never returns a modified copy
/// pretending to be the original. When <see cref="IsApplied"/>, <see cref="RemainingInaccessibleRoots"/> is the
/// original permission-limited root set with only the successfully retried roots removed, and
/// <see cref="CombinedLogicalBytesObserved"/>/<see cref="CombinedAllocatedBytesObserved"/> carry the safely
/// combined totals; when not applied, those two are <see langword="null"/> and every originally requested root
/// is still considered inaccessible.
/// </summary>
public sealed record ElevatedReconciliationResult(
    ElevatedReconciliationOutcome Outcome, ScanResult OriginalResult, ElevatedRetryAttempt Attempt,
    ImmutableArray<PermissionLimitedRoot> RemainingInaccessibleRoots,
    ImmutableArray<ElevatedRootRetryResult> AppliedRootResults,
    long? CombinedLogicalBytesObserved, long? CombinedAllocatedBytesObserved, AccountingConsistency Consistency)
{
    public bool IsApplied => Outcome == ElevatedReconciliationOutcome.Applied;
}

/// <summary>
/// Pure, in-memory reconciliation between a completed non-elevated Deep ("Full") Analysis
/// <see cref="ScanResult"/> and one elevated permission-limited-root retry attempt. No filesystem access, no
/// process launch, no IPC, no mutation of any kind — every check here either accepts an already-typed value or
/// rejects it with a specific typed outcome. The original result is never mutated; a successful reconciliation
/// produces a new, separate <see cref="ElevatedReconciliationResult"/> alongside it.
/// </summary>
public static class ElevatedScanResultReconciler
{
    public static ElevatedReconciliationResult Reconcile(ScanResult originalResult, ElevatedScanRetryRequest request,
        ElevatedScannerLauncherResult launcherResult, IClock? clock = null)
    {
        var effectiveClock = clock ?? new SystemClock();
        var startedAtUtc = effectiveClock.UtcNow;
        var reconciliationId = Guid.NewGuid();

        if (launcherResult.Outcome != ElevatedScannerLauncherOutcome.Completed || launcherResult.Response is null)
            return NotApplied(MapLauncherOutcome(launcherResult.Outcome), originalResult, request, reconciliationId,
                startedAtUtc, effectiveClock.UtcNow, launcherResult.Outcome, null,
                ["The launcher did not return a completed, typed response."]);

        var response = launcherResult.Response;

        // Defense-in-depth: the launcher (and the IPC client beneath it) already guarantee this before ever
        // returning Completed, but this is never trusted merely because it arrived from an upstream layer.
        if (response.ProtocolVersion != request.ProtocolVersion || !string.Equals(response.Nonce, request.Nonce, StringComparison.Ordinal))
            return NotApplied(ElevatedReconciliationOutcome.InvalidResponse, originalResult, request, reconciliationId,
                startedAtUtc, effectiveClock.UtcNow, launcherResult.Outcome, response.Outcome,
                ["The response protocol version or nonce did not match the request."]);

        if (!ElevatedScanRetryValidator.Validate(request, effectiveClock.UtcNow).IsValid)
            return NotApplied(ElevatedReconciliationOutcome.ValidationRejected, originalResult, request, reconciliationId,
                startedAtUtc, effectiveClock.UtcNow, launcherResult.Outcome, response.Outcome,
                ["The retry request failed independent revalidation."]);

        var responseOutcome = MapResponseOutcome(response.Outcome);
        if (responseOutcome is { } terminalOutcome)
            return NotApplied(terminalOutcome, originalResult, request, reconciliationId, startedAtUtc,
                effectiveClock.UtcNow, launcherResult.Outcome, response.Outcome,
                [$"The retry response's own outcome was {response.Outcome}, not Completed or PartiallyCompleted."]);

        if (!IsEligibleOriginalScan(originalResult) || request.OriginalScanExecutionId != originalResult.ScanId)
            return NotApplied(ElevatedReconciliationOutcome.NotEligible, originalResult, request, reconciliationId,
                startedAtUtc, effectiveClock.UtcNow, launcherResult.Outcome, response.Outcome,
                ["The original scan is not an eligible completed Deep Analysis bound to this request."]);

        if (request.PermissionLimitedRoots.Any(root => root.OriginalScanExecutionId != originalResult.ScanId))
            return NotApplied(ElevatedReconciliationOutcome.NotEligible, originalResult, request, reconciliationId,
                startedAtUtc, effectiveClock.UtcNow, launcherResult.Outcome, response.Outcome,
                ["One or more requested roots are not bound to the original scan's execution ID."]);

        if (!TryCorrelateRootResults(request, response, out var resultsByPath))
            return NotApplied(ElevatedReconciliationOutcome.RootSetMismatch, originalResult, request, reconciliationId,
                startedAtUtc, effectiveClock.UtcNow, launcherResult.Outcome, response.Outcome,
                ["The response's per-root results do not correspond exactly, one-to-one, to the requested roots."]);

        // Phase 7.2.6G1 can only prove a root contributed zero bytes to the original scan in the narrow case
        // where the entire original scan observed zero logical bytes overall — there is no finer, per-root
        // accounting signal on ScanResult yet (that is later, out-of-scope work). Anything short of that proof
        // means the elevated bytes cannot be safely added without risking double-counting whatever the original
        // scan already attributed to part of this root.
        if (!OriginalScanProvedZeroRootContribution(originalResult))
            return NotApplied(ElevatedReconciliationOutcome.RequiresReplacementData, originalResult, request, reconciliationId,
                startedAtUtc, effectiveClock.UtcNow, launcherResult.Outcome, response.Outcome,
                ["The original scan may already have observed part of one or more retried roots; exact " +
                 "replacement requires root-level original accounting that is not yet available."]);

        var remaining = ImmutableArray.CreateBuilder<PermissionLimitedRoot>();
        var applied = ImmutableArray.CreateBuilder<ElevatedRootRetryResult>();
        long additionalLogical = 0, additionalAllocated = 0;
        var rootsCompleted = 0;
        foreach (var requestRoot in request.PermissionLimitedRoots)
        {
            var rootResult = resultsByPath[ElevatedScanManifestBuilder.NormalizePath(requestRoot.NormalizedRootPath)];
            if (rootResult.Outcome == ElevatedRootRetryOutcome.Completed)
            {
                rootsCompleted++;
                applied.Add(rootResult);
                additionalLogical += Math.Max(0, rootResult.LogicalBytesObserved);
                additionalAllocated += Math.Max(0, rootResult.AllocatedBytesObserved);
            }
            else remaining.Add(requestRoot);
        }

        // Never a falsely exact global-unique-allocation total: the elevated engine only de-duplicates hard
        // links within its own retry attempt, and the original scan may hold another link to the same physical
        // content outside the retried roots. Combined unique allocation is deliberately never computed here.
        var consistency = AccountingConsistency.Consistent;
        var limitations = ImmutableArray<string>.Empty;
        if (applied.Count > 0)
        {
            consistency |= AccountingConsistency.CrossScanIdentityReconciliationUnavailable;
            limitations = ["Combined unique allocated bytes are not calculated across the original scan and the " +
                "retried roots; a hard link could span both and be double-counted."];
        }

        var completedAtUtc = effectiveClock.UtcNow;
        var attempt = new ElevatedRetryAttempt(reconciliationId, originalResult.ScanId, request.DriveIdentity, request.Nonce,
            startedAtUtc, completedAtUtc, launcherResult.Outcome, response.Outcome, request.PermissionLimitedRoots.Length,
            rootsCompleted, remaining.Count, additionalLogical, additionalAllocated, limitations, true);

        var combinedLogical = originalResult.LogicalBytesObserved + additionalLogical;
        long? combinedAllocated = originalResult.Allocation is null && additionalAllocated == 0
            ? null
            : (originalResult.Allocation?.AllocatedBytesObserved ?? 0) + additionalAllocated;

        return new ElevatedReconciliationResult(ElevatedReconciliationOutcome.Applied, originalResult, attempt,
            remaining.ToImmutable(), applied.ToImmutable(), combinedLogical, combinedAllocated, consistency);
    }

    private static bool IsEligibleOriginalScan(ScanResult originalResult) =>
        originalResult.Mode == ScanMode.Deep && originalResult.Status is ScanStatus.Completed or ScanStatus.CompletedWithWarnings;

    /// <summary>The only currently provable "this root contributed zero bytes" condition: the whole original
    /// scan observed literally nothing. See the call site for why a finer, per-root signal is not yet
    /// available.</summary>
    private static bool OriginalScanProvedZeroRootContribution(ScanResult originalResult) => originalResult.LogicalBytesObserved <= 0;

    private static bool TryCorrelateRootResults(ElevatedScanRetryRequest request, ElevatedScanRetryResponse response,
        out Dictionary<string, ElevatedRootRetryResult> resultsByPath)
    {
        resultsByPath = [];
        if (response.RootResults.IsDefaultOrEmpty) return false;
        if (response.RootResults.Length > ElevatedScanRetryProtocol.MaxRoots) return false;

        var requestPaths = new HashSet<string>(request.PermissionLimitedRoots
            .Select(root => ElevatedScanManifestBuilder.NormalizePath(root.NormalizedRootPath)), StringComparer.Ordinal);

        foreach (var rootResult in response.RootResults)
        {
            var normalized = ElevatedScanManifestBuilder.NormalizePath(rootResult.CanonicalRootIdentity);
            if (!requestPaths.Contains(normalized)) return false; // a root outside the request — rejected
            if (!resultsByPath.TryAdd(normalized, rootResult)) return false; // a duplicate root result — rejected
        }

        // Every requested root must have exactly one corresponding result — anything short of that is treated
        // as a missing expected root result, not a partial success.
        return resultsByPath.Count == requestPaths.Count;
    }

    private static ElevatedReconciliationOutcome MapLauncherOutcome(ElevatedScannerLauncherOutcome outcome) => outcome switch
    {
        ElevatedScannerLauncherOutcome.Denied => ElevatedReconciliationOutcome.Denied,
        ElevatedScannerLauncherOutcome.Cancelled => ElevatedReconciliationOutcome.Cancelled,
        ElevatedScannerLauncherOutcome.ConnectionTimedOut or ElevatedScannerLauncherOutcome.ResponseTimedOut => ElevatedReconciliationOutcome.TimedOut,
        ElevatedScannerLauncherOutcome.InvalidResponse => ElevatedReconciliationOutcome.InvalidResponse,
        ElevatedScannerLauncherOutcome.ValidationRejected => ElevatedReconciliationOutcome.ValidationRejected,
        _ => ElevatedReconciliationOutcome.LaunchFailed // HelperMissing, InvalidLaunchPlan, LaunchFailed, ProtocolRejected, Failed
    };

    /// <summary>Returns the reconciliation outcome to stop at for a terminal (non-continuable) response outcome,
    /// or <see langword="null"/> when the response outcome is <c>Completed</c>/<c>PartiallyCompleted</c> and
    /// reconciliation should proceed.</summary>
    private static ElevatedReconciliationOutcome? MapResponseOutcome(ElevatedScanRetryOutcome outcome) => outcome switch
    {
        ElevatedScanRetryOutcome.Completed or ElevatedScanRetryOutcome.PartiallyCompleted => null,
        ElevatedScanRetryOutcome.ValidationRejected => ElevatedReconciliationOutcome.ValidationRejected,
        ElevatedScanRetryOutcome.Cancelled => ElevatedReconciliationOutcome.Cancelled,
        ElevatedScanRetryOutcome.TimedOut => ElevatedReconciliationOutcome.TimedOut,
        _ => ElevatedReconciliationOutcome.InvalidResponse // ProtocolRejected, Failed
    };

    private static ElevatedReconciliationResult NotApplied(ElevatedReconciliationOutcome outcome, ScanResult originalResult,
        ElevatedScanRetryRequest request, Guid reconciliationId, DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc,
        ElevatedScannerLauncherOutcome launcherOutcome, ElevatedScanRetryOutcome? responseOutcome, ImmutableArray<string> limitations)
    {
        var attempt = new ElevatedRetryAttempt(reconciliationId, originalResult.ScanId, request.DriveIdentity, request.Nonce,
            startedAtUtc, completedAtUtc, launcherOutcome, responseOutcome, request.PermissionLimitedRoots.Length,
            0, request.PermissionLimitedRoots.Length, 0, 0, limitations, false);
        return new ElevatedReconciliationResult(outcome, originalResult, attempt, request.PermissionLimitedRoots,
            [], null, null, AccountingConsistency.Consistent);
    }
}
