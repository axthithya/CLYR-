using System.Collections.Immutable;
using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>Phase 7.2.6G1: pure, in-memory reconciliation between a completed non-elevated Deep Analysis and
/// one elevated retry attempt. No test here touches the filesystem, opens a pipe, launches a process, or
/// triggers UAC — every fixture is an in-memory typed value.</summary>
public sealed class ElevatedScanResultReconcilerTests
{
    private static readonly Guid ScanId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private const string DriveIdentity = "drive-fingerprint-reconciler";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-17T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void SuccessfulEligibleRootProducesANewCombinedResult()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture("C:\\Data\\Alpha")));
        var original = OriginalResult();
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed,
            [RootResult("C:\\Data\\Alpha", ElevatedRootRetryOutcome.Completed, 500, 300)]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.Equal(ElevatedReconciliationOutcome.Applied, result.Outcome);
        Assert.True(result.IsApplied);
        Assert.Equal(500, result.CombinedLogicalBytesObserved);
    }

    [Fact]
    public void OriginalResultRemainsUnchanged()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture("C:\\Data\\Alpha")));
        var original = OriginalResult();
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed,
            [RootResult("C:\\Data\\Alpha", ElevatedRootRetryOutcome.Completed, 500, 300)]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.Same(original, result.OriginalResult);
        Assert.Equal(0, original.LogicalBytesObserved);
    }

    [Fact]
    public void CompletedRootIsRemovedFromRemainingInaccessibleRoots()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture("C:\\Data\\Alpha"), RootFixture("C:\\Data\\Beta")));
        var original = OriginalResult();
        var response = ResponseFor(request, ElevatedScanRetryOutcome.PartiallyCompleted,
        [
            RootResult("C:\\Data\\Alpha", ElevatedRootRetryOutcome.Completed, 500, 300),
            RootResult("C:\\Data\\Beta", ElevatedRootRetryOutcome.StillInaccessible, 0, 0)
        ]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.True(result.IsApplied);
        Assert.DoesNotContain(result.RemainingInaccessibleRoots, root => root.NormalizedRootPath == "C:\\Data\\Alpha");
    }

    [Fact]
    public void StillInaccessibleRootRemains()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture("C:\\Data\\Alpha"), RootFixture("C:\\Data\\Beta")));
        var original = OriginalResult();
        var response = ResponseFor(request, ElevatedScanRetryOutcome.PartiallyCompleted,
        [
            RootResult("C:\\Data\\Alpha", ElevatedRootRetryOutcome.Completed, 500, 300),
            RootResult("C:\\Data\\Beta", ElevatedRootRetryOutcome.StillInaccessible, 0, 0)
        ]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.True(result.IsApplied);
        Assert.Contains(result.RemainingInaccessibleRoots, root => root.NormalizedRootPath == "C:\\Data\\Beta");
    }

    [Fact]
    public void RootAbsentFromRequestIsRejected()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture("C:\\Data\\Alpha")));
        var original = OriginalResult();
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed,
            [RootResult("C:\\Data\\Outside", ElevatedRootRetryOutcome.Completed)]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.Equal(ElevatedReconciliationOutcome.RootSetMismatch, result.Outcome);
        Assert.False(result.IsApplied);
    }

    [Fact]
    public void DuplicateResponseRootIsRejected()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture("C:\\Data\\Alpha")));
        var original = OriginalResult();
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed,
        [
            RootResult("C:\\Data\\Alpha", ElevatedRootRetryOutcome.Completed),
            RootResult("C:\\Data\\Alpha", ElevatedRootRetryOutcome.Completed)
        ]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.Equal(ElevatedReconciliationOutcome.RootSetMismatch, result.Outcome);
    }

    [Fact]
    public void MissingExpectedRootResultIsDetected()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture("C:\\Data\\Alpha"), RootFixture("C:\\Data\\Beta")));
        var original = OriginalResult();
        var response = ResponseFor(request, ElevatedScanRetryOutcome.PartiallyCompleted,
            [RootResult("C:\\Data\\Alpha", ElevatedRootRetryOutcome.Completed)]); // Beta's result is missing

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.Equal(ElevatedReconciliationOutcome.RootSetMismatch, result.Outcome);
    }

    [Fact]
    public void OriginalScanIdMismatchIsRejected()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture("C:\\Data\\Alpha")));
        var original = OriginalResult() with { ScanId = Guid.NewGuid() };
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed,
            [RootResult("C:\\Data\\Alpha", ElevatedRootRetryOutcome.Completed)]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.Equal(ElevatedReconciliationOutcome.NotEligible, result.Outcome);
    }

    [Fact]
    public void DriveMismatchIsRejected()
    {
        var mismatchedRoot = new PermissionLimitedRoot("C:\\Data\\Alpha", ScanId, "a-different-drive-identity", null, PermissionLimitedReasonCode.AccessDenied);
        var request = BuildRequest(ImmutableArray.Create(mismatchedRoot));
        var original = OriginalResult();
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed,
            [RootResult("C:\\Data\\Alpha", ElevatedRootRetryOutcome.Completed)]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.Equal(ElevatedReconciliationOutcome.ValidationRejected, result.Outcome);
    }

    [Fact]
    public void NonFullDeepOriginalScanIsNotEligible()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture("C:\\Data\\Alpha")));
        var original = OriginalResult(mode: ScanMode.Quick);
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed,
            [RootResult("C:\\Data\\Alpha", ElevatedRootRetryOutcome.Completed)]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.Equal(ElevatedReconciliationOutcome.NotEligible, result.Outcome);
    }

    [Fact]
    public void UacDenialAppliesZeroChanges()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture("C:\\Data\\Alpha")));
        var original = OriginalResult();

        var result = ElevatedScanResultReconciler.Reconcile(original, request, NotCompleted(ElevatedScannerLauncherOutcome.Denied), new FixedClock(Now));

        Assert.Equal(ElevatedReconciliationOutcome.Denied, result.Outcome);
        Assert.False(result.IsApplied);
        Assert.Equal(0, result.Attempt.AdditionalLogicalBytes);
        Assert.Single(result.RemainingInaccessibleRoots);
    }

    [Fact]
    public void TimeoutAppliesZeroChanges()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture("C:\\Data\\Alpha")));
        var original = OriginalResult();

        var result = ElevatedScanResultReconciler.Reconcile(original, request, NotCompleted(ElevatedScannerLauncherOutcome.ConnectionTimedOut), new FixedClock(Now));

        Assert.Equal(ElevatedReconciliationOutcome.TimedOut, result.Outcome);
        Assert.Equal(0, result.Attempt.AdditionalAllocatedBytes);
    }

    [Fact]
    public void InvalidResponseAppliesZeroChanges()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture("C:\\Data\\Alpha")));
        var original = OriginalResult();
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed,
            [RootResult("C:\\Data\\Alpha", ElevatedRootRetryOutcome.Completed)])
            with
        { Nonce = new string('z', ElevatedScanRetryProtocol.MinNonceLength) };

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.Equal(ElevatedReconciliationOutcome.InvalidResponse, result.Outcome);
        Assert.False(result.IsApplied);
    }

    [Fact]
    public void OriginalPartialRootContributionPreventsBlindAddition()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture("C:\\Data\\Alpha")));
        var original = OriginalResult(logicalBytesObserved: 12345); // the original scan observed something, somewhere
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed,
            [RootResult("C:\\Data\\Alpha", ElevatedRootRetryOutcome.Completed, 500, 300)]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.Equal(ElevatedReconciliationOutcome.RequiresReplacementData, result.Outcome);
        Assert.False(result.IsApplied);
        Assert.Null(result.CombinedLogicalBytesObserved);
    }

    [Fact]
    public void CrossScanUniqueAllocatedTotalIsNotFalselyCalculated()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture("C:\\Data\\Alpha")));
        var originalAllocation = new AllocationAccounting(1000, 800, 0, 0, 0, 0, 5, AccountingConsistency.Consistent);
        var original = OriginalResult() with { Allocation = originalAllocation };
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed,
            [RootResult("C:\\Data\\Alpha", ElevatedRootRetryOutcome.Completed, 500, 300)]);

        var result = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.True(result.IsApplied);
        Assert.True(result.Consistency.HasFlag(AccountingConsistency.CrossScanIdentityReconciliationUnavailable));
        Assert.Contains(result.Attempt.AccountingLimitations, item => item.Contains("hard link", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NewCombinedResultReceivesANewExecutionId()
    {
        var request = BuildRequest(ImmutableArray.Create(RootFixture("C:\\Data\\Alpha")));
        var original = OriginalResult();
        var response = ResponseFor(request, ElevatedScanRetryOutcome.Completed,
            [RootResult("C:\\Data\\Alpha", ElevatedRootRetryOutcome.Completed)]);

        var resultA = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));
        var resultB = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.NotEqual(Guid.Empty, resultA.Attempt.ReconciliationExecutionId);
        Assert.NotEqual(original.ScanId, resultA.Attempt.ReconciliationExecutionId);
        Assert.NotEqual(resultA.Attempt.ReconciliationExecutionId, resultB.Attempt.ReconciliationExecutionId);
    }

    private static ScanResult OriginalResult(long logicalBytesObserved = 0, ScanMode mode = ScanMode.Deep, ScanStatus status = ScanStatus.Completed) =>
        ScanFixtures.Result(mode, status, observed: logicalBytesObserved) with { ScanId = ScanId };

    private static PermissionLimitedRoot RootFixture(string path, string? stableRootIdentifier = null) =>
        new(path, ScanId, DriveIdentity, stableRootIdentifier, PermissionLimitedReasonCode.AccessDenied);

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
    private static ElevatedScannerLauncherResult NotCompleted(ElevatedScannerLauncherOutcome outcome) => new(outcome, null);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
