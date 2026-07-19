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
/// One successfully-applied retried root's already-deduplicated accounting delta — the exact amount that root
/// contributes on top of whatever the original scan already counted for it, never the elevated engine's raw
/// per-root totals. <see cref="ElevatedScanResultReconciler.Reconcile"/> computes this once, per root, by
/// subtracting the original scan's own <see cref="ScanRootContribution"/> figures (when the root was
/// <see cref="ScanRootEnumerationState.PartiallyObserved"/>) from the elevated engine's figures for that same
/// root — the same subtraction already used for <see cref="ElevatedRetryAttempt.AdditionalLogicalBytes"/>, now
/// exposed per root (rather than only as one pre-summed total) so a caller — see
/// <c>ElevatedScanResultEnricher</c> — can truthfully recompute coverage, issue counts, and per-root rankings
/// without re-deriving this same subtraction independently and risking drift.
/// </summary>
public sealed record AppliedRootAccountingDelta(
    string CanonicalRootIdentity, string DisplayRootPath, string? StableRootIdentifier, ElevatedRootRetryResult RootResult,
    long DeltaLogicalBytes, long DeltaAllocatedBytes, long DeltaFilesExamined, long DeltaDirectoriesExamined,
    long DeltaHardLinkEntriesDetected, long DeltaAllocationUnavailableCount, long DeltaSparseFileCount,
    long DeltaCompressedFileCount, long OriginalInaccessibleEntryCount);

/// <summary>
/// The immutable result of one reconciliation attempt. <see cref="OriginalResult"/> is always the exact,
/// untouched object the caller passed in — reconciliation never mutates it and never returns a modified copy
/// pretending to be the original. When <see cref="IsApplied"/>, <see cref="RemainingInaccessibleRoots"/> is the
/// original permission-limited root set with only the successfully retried roots removed, and
/// <see cref="CombinedLogicalBytesObserved"/>/<see cref="CombinedAllocatedBytesObserved"/> carry the safely
/// combined totals; when not applied, those two are <see langword="null"/> and every originally requested root
/// is still considered inaccessible. <see cref="AppliedRootDeltas"/> carries the same combined totals broken out
/// per root — always empty when not applied.
/// </summary>
public sealed record ElevatedReconciliationResult(
    ElevatedReconciliationOutcome Outcome, ScanResult OriginalResult, ElevatedRetryAttempt Attempt,
    ImmutableArray<PermissionLimitedRoot> RemainingInaccessibleRoots,
    ImmutableArray<ElevatedRootRetryResult> AppliedRootResults,
    long? CombinedLogicalBytesObserved, long? CombinedAllocatedBytesObserved, AccountingConsistency Consistency,
    ImmutableArray<AppliedRootAccountingDelta> AppliedRootDeltas = default)
{
    public bool IsApplied => Outcome == ElevatedReconciliationOutcome.Applied;
    public ImmutableArray<AppliedRootAccountingDelta> AppliedRootDeltas { get; init; } = AppliedRootDeltas.IsDefault ? [] : AppliedRootDeltas;
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

        // Phase 7.2.6G2: validate every completed retry root's safety BEFORE applying any of them — one
        // unsafe root aborts the whole reconciliation (never a partial application), matching Phase 7.2.6G1's
        // established "return NotApplied, change nothing" pattern.
        var deltas = new List<AppliedRootAccountingDelta>();
        foreach (var requestRoot in request.PermissionLimitedRoots)
        {
            var normalized = ElevatedScanManifestBuilder.NormalizePath(requestRoot.NormalizedRootPath);
            var rootResult = resultsByPath[normalized];
            if (rootResult.Outcome != ElevatedRootRetryOutcome.Completed) continue;

            if (originalResult.RootContributions.Count > 0)
            {
                // Phase 7.2.6G2: a real per-root signal exists — use it instead of the coarser Phase 7.2.6G1
                // whole-scan heuristic below.
                var contribution = FindContribution(originalResult, normalized);
                if (contribution is null)
                    return NotApplied(ElevatedReconciliationOutcome.RequiresReplacementData, originalResult, request, reconciliationId,
                        startedAtUtc, effectiveClock.UtcNow, launcherResult.Outcome, response.Outcome,
                        ["No original root-level contribution record exists for a retried root; exact replacement requires it."]);
                if (contribution.EnumerationState == ScanRootEnumerationState.Completed)
                    return NotApplied(ElevatedReconciliationOutcome.RootSetMismatch, originalResult, request, reconciliationId,
                        startedAtUtc, effectiveClock.UtcNow, launcherResult.Outcome, response.Outcome,
                        ["A retried root was already fully Completed by the original scan and should never have been in the retry manifest."]);

                // InaccessibleAtRoot contributed zero of every figure by construction, so nothing needs
                // subtracting. PartiallyObserved contributed a truthful-but-incomplete amount of every figure
                // that must be subtracted before the elevated figure is added, so the original's partial
                // contribution is never double-counted — the same rule applied uniformly across bytes, file/
                // directory counts, and every other per-root descriptive count this reconciler now tracks.
                var partial = contribution.EnumerationState == ScanRootEnumerationState.PartiallyObserved;
                deltas.Add(new(normalized, contribution.RootPath, requestRoot.StableRootIdentifier, rootResult,
                    rootResult.LogicalBytesObserved - (partial ? contribution.LogicalBytesObserved : 0),
                    rootResult.AllocatedBytesObserved - (partial ? contribution.AllocatedBytesObserved : 0),
                    rootResult.FilesExamined - (partial ? contribution.FilesExamined : 0),
                    rootResult.DirectoriesExamined - (partial ? contribution.DirectoriesExamined : 0),
                    rootResult.HardLinkEntriesDetected - (partial ? contribution.HardLinkEntriesDetected : 0),
                    rootResult.AllocationUnavailableCount - (partial ? contribution.AllocationUnavailableCount : 0),
                    rootResult.SparseFileCount - (partial ? contribution.SparseFileCount : 0),
                    rootResult.CompressedFileCount - (partial ? contribution.CompressedFileCount : 0),
                    contribution.InaccessibleEntryCount));
            }
            else
            {
                // Phase 7.2.6G1 legacy fallback for a scan with no per-root contributions recorded at all
                // (for example, a Quick Analysis result, or one produced before this phase): the only provable
                // "this root contributed zero bytes" condition is the whole original scan having observed
                // literally nothing. Anything short of that means the elevated bytes cannot be safely added
                // without risking double-counting whatever the original scan already attributed to part of
                // this root.
                if (originalResult.LogicalBytesObserved > 0)
                    return NotApplied(ElevatedReconciliationOutcome.RequiresReplacementData, originalResult, request, reconciliationId,
                        startedAtUtc, effectiveClock.UtcNow, launcherResult.Outcome, response.Outcome,
                        ["The original scan may already have observed part of one or more retried roots, and carries no " +
                         "root-level contribution records; exact replacement cannot be proven safe."]);
                deltas.Add(new(normalized, requestRoot.NormalizedRootPath, requestRoot.StableRootIdentifier, rootResult,
                    Math.Max(0, rootResult.LogicalBytesObserved), Math.Max(0, rootResult.AllocatedBytesObserved),
                    Math.Max(0, rootResult.FilesExamined), Math.Max(0, rootResult.DirectoriesExamined),
                    Math.Max(0, rootResult.HardLinkEntriesDetected), Math.Max(0, rootResult.AllocationUnavailableCount),
                    Math.Max(0, rootResult.SparseFileCount), Math.Max(0, rootResult.CompressedFileCount), 0));
            }
        }

        var appliedPaths = deltas.Select(delta => delta.CanonicalRootIdentity).ToHashSet(StringComparer.Ordinal);
        var remaining = ImmutableArray.CreateBuilder<PermissionLimitedRoot>();
        foreach (var requestRoot in request.PermissionLimitedRoots)
            if (!appliedPaths.Contains(ElevatedScanManifestBuilder.NormalizePath(requestRoot.NormalizedRootPath)))
                remaining.Add(requestRoot);

        var applied = ImmutableArray.CreateBuilder<ElevatedRootRetryResult>(deltas.Count);
        long additionalLogical = 0, additionalAllocated = 0;
        foreach (var delta in deltas) { applied.Add(delta.RootResult); additionalLogical += delta.DeltaLogicalBytes; additionalAllocated += delta.DeltaAllocatedBytes; }
        var rootsCompleted = deltas.Count;

        // Section 3: retried bytes can never make observed bytes exceed the drive's own used-space basis — a
        // real invariant, not merely a display nicety. The original scan's own reported "not observed" figure is
        // an accounting-basis estimate (drive-used minus observed), not a precise measurement of exactly what
        // lives inside these specific roots, so a genuinely small overshoot here is possible without either side
        // being individually wrong; when it happens, this reconciliation is rejected outright (matching the
        // established "one unsafe condition aborts the whole reconciliation, never a partial or silently clamped
        // application" pattern above) rather than ever presenting an impossible combined total.
        // Only guarded when the original scan's own basis was already consistent (observed at or under drive-used) —
        // a scan that already reported LogicalExceedsDriveUsed has a pre-existing, independent basis difference
        // unrelated to this retry, and rejecting an otherwise-correct retry over that pre-existing condition would
        // be punishing the wrong cause.
        if (originalResult.DriveUsedBytes is { } driveUsedBytes && originalResult.LogicalBytesObserved <= driveUsedBytes
            && originalResult.LogicalBytesObserved + additionalLogical > driveUsedBytes)
            return NotApplied(ElevatedReconciliationOutcome.AccountingBasisMismatch, originalResult, request, reconciliationId,
                startedAtUtc, effectiveClock.UtcNow, launcherResult.Outcome, response.Outcome,
                ["The retried roots' combined contribution could not be safely applied because it would make observed " +
                 "bytes exceed this drive's own used-space basis."]);

        // Never a falsely exact global-unique-allocation total: the elevated engine (and, per root,
        // ScanCoordinator) only de-duplicate hard links within their own attempt/root, and the original scan may
        // hold another link to the same physical content outside the retried roots. Combined unique allocation
        // is deliberately never computed here, regardless of whether per-root unique values exist.
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
            remaining.ToImmutable(), applied.ToImmutable(), combinedLogical, combinedAllocated, consistency,
            deltas.ToImmutableArray());
    }

    private static bool IsEligibleOriginalScan(ScanResult originalResult) =>
        originalResult.Mode == ScanMode.Deep && originalResult.Status is ScanStatus.Completed or ScanStatus.CompletedWithWarnings;

    private static ScanRootContribution? FindContribution(ScanResult originalResult, string normalizedRequestPath) =>
        originalResult.RootContributions.FirstOrDefault(contribution => string.Equals(contribution.CanonicalRootIdentity, normalizedRequestPath, StringComparison.Ordinal));

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
