using System.Collections.Immutable;
using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>Phase 7.2.6G2: safe replacement reconciliation using real per-root original-scan contributions. No
/// filesystem, IPC, process, or UAC involved — every fixture is an in-memory typed value.</summary>
public sealed class ElevatedScanResultReconcilerRootContributionTests
{
    private static readonly Guid ScanId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string DriveIdentity = "drive-fingerprint-root-contribution";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-18T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private const string RootPath = "C:\\Data\\Alpha";

    [Fact]
    public void CompletedElevatedRootReplacesZeroByteInaccessibleContribution()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture(RootPath)));
        var original = OriginalResult(5000, [Contribution(RootPath, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0)]);
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300)]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.True(result.IsApplied);
        Assert.Equal(5500, result.CombinedLogicalBytesObserved);
        Assert.Empty(result.RemainingInaccessibleRoots);
    }

    [Fact]
    public void PartialOriginalRootIsSubtractedBeforeElevatedBytesAreAdded()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture(RootPath)));
        var original = OriginalResult(5200, [Contribution(RootPath, ScanRootEnumerationState.PartiallyObserved, 200, 100)])
            with
        { Allocation = new AllocationAccounting(5200, 5200, 0, 0, 0, 0, 0, AccountingConsistency.Consistent) };
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300)]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.True(result.IsApplied);
        // 5200 (whole original scan, which already includes Alpha's partial 200) - 200 (subtract Alpha's own
        // partial contribution) + 500 (the elevated retry's full figure for Alpha) = 5500.
        Assert.Equal(5500, result.CombinedLogicalBytesObserved);
        Assert.Equal(5200 - 100 + 300, result.CombinedAllocatedBytesObserved);
    }

    [Fact]
    public void CompletedOriginalRootIsNotEligibleForRetryReplacement()
    {
        // Phase (root-reconciliation correction): this anomaly is now a per-root skip (section 7 partial
        // application), not a whole-response-shaped outcome of its own — with only one requested root and
        // nothing else to apply, the reconciliation as a whole reports the same "nothing could be safely
        // reconciled" outcome as any other per-root anomaly.
        var request = BuildRequest(ImmutableArray.Create(RootFixture(RootPath)));
        var original = OriginalResult(5000, [Contribution(RootPath, ScanRootEnumerationState.Completed, 5000, 3000)]);
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300)]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.Equal(ElevatedReconciliationOutcome.AccountingBasisMismatch, result.Outcome);
        Assert.False(result.IsApplied);
    }

    [Fact]
    public void MissingOriginalContributionSkipsThatRootRatherThanAbortingTheWholeResponse()
    {
        // Phase (root-reconciliation correction): with only one requested root and no original contribution for
        // it, nothing can be safely applied — reported the same as any other per-root anomaly when it leaves
        // zero roots reconciled, rather than the old whole-response RequiresReplacementData outcome.
        var request = BuildRequest(ImmutableArray.Create(RootFixture(RootPath)));
        // Contributions exist (proving this scan does carry per-root data) but none for the retried root itself.
        var original = OriginalResult(5000, [Contribution("C:\\Data\\Other", ScanRootEnumerationState.Completed, 100, 50)]);
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300)]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.Equal(ElevatedReconciliationOutcome.AccountingBasisMismatch, result.Outcome);
        Assert.False(result.IsApplied);
        Assert.Null(result.CombinedLogicalBytesObserved);
    }

    [Fact]
    public void AValidRootStillAppliesWhenAnotherRootInTheSameResponseHasNoOriginalContribution()
    {
        // Phase (root-reconciliation correction), section 7: partial application — Alpha has no original
        // contribution record (an anomaly, skipped) but Beta is a genuine, valid PartiallyObserved root; Beta's
        // safe contribution must still apply rather than being discarded because Alpha, elsewhere in the same
        // response, could not be reconciled.
        const string alphaPath = "C:\\Data\\Alpha";
        const string betaPath = "C:\\Data\\Beta";
        var roots = ImmutableArray.Create(RootFixture(alphaPath), RootFixture(betaPath));
        var request = BuildRequest(roots);
        var original = OriginalResult(1000, [Contribution(betaPath, ScanRootEnumerationState.PartiallyObserved, 200, 100)]);
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed,
        [
            RootResult(alphaPath, ElevatedRootRetryOutcome.Completed, 500, 300),
            RootResult(betaPath, ElevatedRootRetryOutcome.Completed, 600, 400),
        ]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.True(result.IsApplied);
        // 1000 (original, including Beta's partial 200) - 200 (Beta's own partial contribution) + 600 (Beta's
        // complete elevated figure) = 1400. Alpha contributes nothing (skipped, no original contribution).
        Assert.Equal(1400, result.CombinedLogicalBytesObserved);
        Assert.Single(result.RemainingInaccessibleRoots, root => root.NormalizedRootPath == alphaPath);
        Assert.Single(result.AppliedRootDeltas, delta => delta.CanonicalRootIdentity == ElevatedScanManifestBuilder.NormalizePath(betaPath));
    }

    [Fact]
    public void DuplicateRetryRootResultsAreRejectedEvenWithRealContributionsPresent()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture(RootPath)));
        var original = OriginalResult(0, [Contribution(RootPath, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0)]);
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed,
        [
            RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300),
            RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300)
        ]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.Equal(ElevatedReconciliationOutcome.RootSetMismatch, result.Outcome);
    }

    [Fact]
    public void OriginalResultRemainsUnchanged()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture(RootPath)));
        var original = OriginalResult(5000, [Contribution(RootPath, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0)]);
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300)]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.Same(original, result.OriginalResult);
        Assert.Equal(5000, original.LogicalBytesObserved);
        Assert.Equal(ScanRootEnumerationState.InaccessibleAtRoot, original.RootContributions[0].EnumerationState);
    }

    [Fact]
    public void CombinedResultGetsANewExecutionId()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture(RootPath)));
        var original = OriginalResult(5000, [Contribution(RootPath, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0)]);
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300)]);

        var resultA = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));
        var resultB = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.NotEqual(Guid.Empty, resultA.Attempt.ReconciliationExecutionId);
        Assert.NotEqual(resultA.Attempt.ReconciliationExecutionId, resultB.Attempt.ReconciliationExecutionId);
    }

    [Fact]
    public void CrossScanUniqueAllocationRemainsQualified()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture(RootPath)));
        var originalAllocation = new AllocationAccounting(1000, 800, 0, 0, 0, 0, 5, AccountingConsistency.Consistent);
        var original = OriginalResult(5000, [Contribution(RootPath, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0)]) with { Allocation = originalAllocation };
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300)]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.True(result.IsApplied);
        Assert.True(result.Consistency.HasFlag(AccountingConsistency.CrossScanIdentityReconciliationUnavailable));
        Assert.Contains(result.Attempt.AccountingLimitations, item => item.Contains("hard link", StringComparison.OrdinalIgnoreCase));
    }

    private static ScanResult OriginalResult(long observed, IReadOnlyList<ScanRootContribution> contributions) =>
        ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: observed) with { ScanId = ScanId, RootContributions = contributions };

    private static ScanRootContribution Contribution(string path, ScanRootEnumerationState state, long logicalBytes, long allocatedBytes) =>
        new(ElevatedScanManifestBuilder.NormalizePath(path), null, path, state, 1, 0, logicalBytes, allocatedBytes, allocatedBytes, 0, 0, 0, 0, 0, 0, 0);

    private static PermissionLimitedRoot RootFixture(string path) =>
        new(path, ScanId, DriveIdentity, null, PermissionLimitedReasonCode.AccessDenied);

    private static ElevatedRootRetryResult RootResult(string path, ElevatedRootRetryOutcome outcome, long logicalBytes = 0, long allocatedBytes = 0) =>
        new(path, null, outcome, 1, 0, logicalBytes, allocatedBytes, allocatedBytes, 0, 0, 0, 0);

    private static ElevatedScanRetryRequest BuildRequest(ImmutableArray<PermissionLimitedRoot> roots)
    {
        var manifest = ElevatedScanManifestBuilder.Build(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots, ScanId, DriveIdentity, roots);
        return new ElevatedScanRetryRequest(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots,
            new string('a', ElevatedScanRetryProtocol.MinNonceLength), Now, Now.AddMinutes(1), ScanId, DriveIdentity,
            manifest.Value!.Digest, roots, 16);
    }

    private static ElevatedScanRetryResponse ResponseFor(ElevatedScanRetryRequest request, ElevatedScanRetryOutcome outcome,
        ImmutableArray<ElevatedRootRetryResult> rootResults) =>
        new(request.ProtocolVersion, request.Nonce, outcome, Now, Now.AddSeconds(1), request.PermissionLimitedRoots.Length,
            rootResults.Count(item => item.Outcome == ElevatedRootRetryOutcome.Completed),
            rootResults.Count(item => item.Outcome != ElevatedRootRetryOutcome.Completed),
            10, 2, 1000, 800, 800, 0, 0, 0, [], rootResults);

    private static ElevatedScannerLauncherResult Completed(ElevatedScanRetryResponse response) => new(ElevatedScannerLauncherOutcome.Completed, response);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
