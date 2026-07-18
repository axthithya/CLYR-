using System.Collections.Immutable;
using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>Phase 7.2.6H1: the pure application-orchestration coordinator connecting the request factory,
/// launcher, and reconciler. Every dependency here is an in-memory fake — no real launcher, IPC, process, or
/// UAC is ever exercised, and no real drive is ever scanned.</summary>
public sealed class ElevatedScanRetryCoordinatorTests
{
    private static readonly Guid ScanId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private const string DriveIdentity = "drive-fingerprint-coordinator";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-18T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task EligibleResultCallsFactoryLauncherAndReconcilerOnce()
    {
        var factory = EligibleFactory();
        var launcher = CompletingLauncher();
        var reconciler = FakeReconciler.ReturningApplied();

        var result = await Coordinator(factory, launcher, reconciler).RetryAsync(OriginalResult(), CancellationToken.None);

        Assert.Equal(1, factory.CallCount);
        Assert.Equal(1, launcher.CallCount);
        Assert.Equal(1, reconciler.CallCount);
        Assert.Equal(ElevatedScanRetryWorkflowOutcome.Applied, result.Outcome);
    }

    [Fact]
    public async Task AppliedReconciliationReturnsANewCombinedResult()
    {
        var original = OriginalResult();
        var reconciler = FakeReconciler.ReturningApplied();

        var result = await Coordinator(EligibleFactory(), CompletingLauncher(), reconciler).RetryAsync(original, CancellationToken.None);

        Assert.True(result.IsApplied);
        Assert.NotNull(result.CombinedResult);
        Assert.True(result.CombinedResult!.IsApplied);
        Assert.Equal(1500, result.CombinedResult.CombinedLogicalBytesObserved);
        Assert.Equal(500, result.AdditionalLogicalBytes);
    }

    [Fact]
    public async Task OriginalScanResultRemainsTheSameObjectReference()
    {
        var original = OriginalResult();

        var result = await Coordinator(EligibleFactory(), CompletingLauncher(), FakeReconciler.ReturningApplied())
            .RetryAsync(original, CancellationToken.None);

        Assert.Same(original, result.OriginalResult);
    }

    [Fact]
    public async Task QuickResultCausesZeroLauncherCalls()
    {
        var factory = FakeFactory.Returning(new ElevatedScanRetryRequestBuildResult(ElevatedScanRetryEligibilityOutcome.QuickAnalysisNotEligible, null));
        var launcher = CompletingLauncher();

        var result = await Coordinator(factory, launcher, FakeReconciler.ReturningApplied()).RetryAsync(OriginalResult(), CancellationToken.None);

        Assert.Equal(0, launcher.CallCount);
        Assert.Equal(ElevatedScanRetryWorkflowOutcome.NotEligible, result.Outcome);
        Assert.Same(OriginalResultReference, result.OriginalResult);
    }

    [Fact]
    public async Task NoEligibleRootsCausesZeroLauncherCalls()
    {
        var factory = FakeFactory.Returning(new ElevatedScanRetryRequestBuildResult(ElevatedScanRetryEligibilityOutcome.NoReplaceablePermissionLimitedRoots, null));
        var launcher = CompletingLauncher();

        var result = await Coordinator(factory, launcher, FakeReconciler.ReturningApplied()).RetryAsync(OriginalResult(), CancellationToken.None);

        Assert.Equal(0, launcher.CallCount);
        Assert.Equal(ElevatedScanRetryWorkflowOutcome.NotEligible, result.Outcome);
    }

    [Fact]
    public async Task UacDenialPreservesOriginalAndSkipsReconciliation()
    {
        var original = OriginalResult();
        var reconciler = FakeReconciler.ReturningApplied();
        var launcher = FakeLauncher.Returning(new ElevatedScannerLauncherResult(ElevatedScannerLauncherOutcome.Denied, null));

        var result = await Coordinator(EligibleFactory(), launcher, reconciler).RetryAsync(original, CancellationToken.None);

        Assert.Equal(ElevatedScanRetryWorkflowOutcome.Denied, result.Outcome);
        Assert.Same(original, result.OriginalResult);
        Assert.Null(result.CombinedResult);
        Assert.Equal(0, reconciler.CallCount);
    }

    [Fact]
    public async Task CancellationPreservesOriginalAndSkipsReconciliation()
    {
        var original = OriginalResult();
        var reconciler = FakeReconciler.ReturningApplied();
        var launcher = CompletingLauncher();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await Coordinator(EligibleFactory(), launcher, reconciler).RetryAsync(original, cts.Token);

        Assert.Equal(ElevatedScanRetryWorkflowOutcome.Cancelled, result.Outcome);
        Assert.Same(original, result.OriginalResult);
        Assert.Null(result.CombinedResult);
        Assert.Equal(0, launcher.CallCount);
        Assert.Equal(0, reconciler.CallCount);
    }

    [Fact]
    public async Task HelperMissingPreservesOriginal()
    {
        var original = OriginalResult();
        var launcher = FakeLauncher.Returning(new ElevatedScannerLauncherResult(ElevatedScannerLauncherOutcome.HelperMissing, null));

        var result = await Coordinator(EligibleFactory(), launcher, FakeReconciler.ReturningApplied()).RetryAsync(original, CancellationToken.None);

        Assert.Equal(ElevatedScanRetryWorkflowOutcome.HelperMissing, result.Outcome);
        Assert.Same(original, result.OriginalResult);
        Assert.Null(result.CombinedResult);
    }

    [Fact]
    public async Task InvalidLaunchPlanPreservesOriginal()
    {
        var original = OriginalResult();
        var launcher = FakeLauncher.Returning(new ElevatedScannerLauncherResult(ElevatedScannerLauncherOutcome.InvalidLaunchPlan, null));

        var result = await Coordinator(EligibleFactory(), launcher, FakeReconciler.ReturningApplied()).RetryAsync(original, CancellationToken.None);

        Assert.Equal(ElevatedScanRetryWorkflowOutcome.InvalidLaunchPlan, result.Outcome);
        Assert.Same(original, result.OriginalResult);
        Assert.Null(result.CombinedResult);
    }

    [Fact]
    public async Task ConnectionTimeoutPreservesOriginal()
    {
        var original = OriginalResult();
        var launcher = FakeLauncher.Returning(new ElevatedScannerLauncherResult(ElevatedScannerLauncherOutcome.ConnectionTimedOut, null));

        var result = await Coordinator(EligibleFactory(), launcher, FakeReconciler.ReturningApplied()).RetryAsync(original, CancellationToken.None);

        Assert.Equal(ElevatedScanRetryWorkflowOutcome.ConnectionTimedOut, result.Outcome);
        Assert.Same(original, result.OriginalResult);
        Assert.Null(result.CombinedResult);
    }

    [Fact]
    public async Task InvalidResponseSkipsReconciliation()
    {
        var reconciler = FakeReconciler.ReturningApplied();
        var launcher = FakeLauncher.Returning(new ElevatedScannerLauncherResult(ElevatedScannerLauncherOutcome.InvalidResponse, null));

        var result = await Coordinator(EligibleFactory(), launcher, reconciler).RetryAsync(OriginalResult(), CancellationToken.None);

        Assert.Equal(ElevatedScanRetryWorkflowOutcome.InvalidResponse, result.Outcome);
        Assert.Equal(0, reconciler.CallCount);
    }

    [Fact]
    public async Task RequiresReplacementDataExposesNoCombinedResult()
    {
        var reconciler = FakeReconciler.Returning(NotAppliedReconciliation(ElevatedReconciliationOutcome.RequiresReplacementData));

        var result = await Coordinator(EligibleFactory(), CompletingLauncher(), reconciler).RetryAsync(OriginalResult(), CancellationToken.None);

        Assert.Equal(ElevatedScanRetryWorkflowOutcome.RequiresReplacementData, result.Outcome);
        Assert.Null(result.CombinedResult);
        Assert.Null(result.AdditionalLogicalBytes);
    }

    [Fact]
    public async Task AccountingBasisMismatchExposesNoFalseCombinedValue()
    {
        var reconciler = FakeReconciler.Returning(NotAppliedReconciliation(ElevatedReconciliationOutcome.AccountingBasisMismatch));

        var result = await Coordinator(EligibleFactory(), CompletingLauncher(), reconciler).RetryAsync(OriginalResult(), CancellationToken.None);

        Assert.Equal(ElevatedScanRetryWorkflowOutcome.AccountingBasisMismatch, result.Outcome);
        Assert.Null(result.CombinedResult);
        Assert.False(result.IsApplied);
    }

    [Fact]
    public async Task ConcurrentSecondRetryReturnsAlreadyRunning()
    {
        var gate = new TaskCompletionSource();
        var launcher = FakeLauncher.Gated(gate.Task, new ElevatedScannerLauncherResult(ElevatedScannerLauncherOutcome.Completed, ValidResponse()));
        var coordinator = Coordinator(EligibleFactory(), launcher, FakeReconciler.ReturningApplied());
        var original = OriginalResult();

        var firstTask = coordinator.RetryAsync(original, CancellationToken.None);
        var second = await coordinator.RetryAsync(original, CancellationToken.None);

        Assert.Equal(ElevatedScanRetryWorkflowOutcome.AlreadyRunning, second.Outcome);
        gate.SetResult();
        var first = await firstTask;
        Assert.Equal(ElevatedScanRetryWorkflowOutcome.Applied, first.Outcome);
        Assert.Equal(1, launcher.CallCount);
    }

    [Fact]
    public async Task GuardIsReleasedAfterFailureOrCancellation()
    {
        var original = OriginalResult();
        var launcher = FakeLauncher.Returning(new ElevatedScannerLauncherResult(ElevatedScannerLauncherOutcome.Denied, null));
        var coordinator = Coordinator(EligibleFactory(), launcher, FakeReconciler.ReturningApplied());

        var first = await coordinator.RetryAsync(original, CancellationToken.None);
        var second = await coordinator.RetryAsync(original, CancellationToken.None);

        Assert.Equal(ElevatedScanRetryWorkflowOutcome.Denied, first.Outcome);
        Assert.Equal(ElevatedScanRetryWorkflowOutcome.Denied, second.Outcome);
        Assert.Equal(2, launcher.CallCount);
    }

    [Fact]
    public async Task NoAutomaticSecondLaunchOccurs()
    {
        var launcher = FakeLauncher.Returning(new ElevatedScannerLauncherResult(ElevatedScannerLauncherOutcome.LaunchFailed, null));

        await Coordinator(EligibleFactory(), launcher, FakeReconciler.ReturningApplied()).RetryAsync(OriginalResult(), CancellationToken.None);

        Assert.Equal(1, launcher.CallCount);
    }

    private static readonly ScanResult OriginalResultReference = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed) with { ScanId = ScanId };

    private static ScanResult OriginalResult() => OriginalResultReference;

    private static ElevatedScanRetryCoordinator Coordinator(IElevatedScanRetryRequestFactory factory, IElevatedScannerLauncher launcher,
        IElevatedScanResultReconciler reconciler) => new(factory, launcher, reconciler);

    private static FakeFactory EligibleFactory() => FakeFactory.Returning(new ElevatedScanRetryRequestBuildResult(
        ElevatedScanRetryEligibilityOutcome.Eligible, BuildRequest()));

    private static FakeLauncher CompletingLauncher() => FakeLauncher.Returning(new ElevatedScannerLauncherResult(ElevatedScannerLauncherOutcome.Completed, ValidResponse()));

    private static ElevatedScanRetryRequest BuildRequest()
    {
        var roots = ImmutableArray.Create(new PermissionLimitedRoot("C:\\Data\\Alpha", ScanId, DriveIdentity, null, PermissionLimitedReasonCode.AccessDenied));
        var manifest = ElevatedScanManifestBuilder.Build(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots, ScanId, DriveIdentity, roots);
        return new ElevatedScanRetryRequest(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots,
            new string('a', ElevatedScanRetryProtocol.MinNonceLength), Now, Now.AddMinutes(1), ScanId, DriveIdentity,
            manifest.Value!.Digest, roots, 16);
    }

    private static ElevatedScanRetryResponse ValidResponse() =>
        new(ElevatedScanRetryProtocol.Version, new string('a', ElevatedScanRetryProtocol.MinNonceLength), ElevatedScanRetryOutcome.Completed,
            Now, Now.AddSeconds(1), 1, 1, 0, 10, 2, 500, 300, 300, 0, 0, 0, []);

    private static ElevatedReconciliationResult AppliedReconciliation()
    {
        var attempt = new ElevatedRetryAttempt(Guid.NewGuid(), ScanId, DriveIdentity, new string('a', ElevatedScanRetryProtocol.MinNonceLength),
            Now, Now.AddSeconds(1), ElevatedScannerLauncherOutcome.Completed, ElevatedScanRetryOutcome.Completed, 1, 1, 0, 500, 300, [], true);
        return new ElevatedReconciliationResult(ElevatedReconciliationOutcome.Applied, OriginalResult(), attempt, [], [], 1500, 1300, AccountingConsistency.Consistent);
    }

    private static ElevatedReconciliationResult NotAppliedReconciliation(ElevatedReconciliationOutcome outcome)
    {
        var attempt = new ElevatedRetryAttempt(Guid.NewGuid(), ScanId, DriveIdentity, new string('a', ElevatedScanRetryProtocol.MinNonceLength),
            Now, Now.AddSeconds(1), ElevatedScannerLauncherOutcome.Completed, ElevatedScanRetryOutcome.Completed, 1, 0, 1, 0, 0, [], false);
        return new ElevatedReconciliationResult(outcome, OriginalResult(), attempt, [], [], null, null, AccountingConsistency.Consistent);
    }

    private sealed class FakeFactory(ElevatedScanRetryRequestBuildResult result) : IElevatedScanRetryRequestFactory
    {
        public int CallCount { get; private set; }
        public static FakeFactory Returning(ElevatedScanRetryRequestBuildResult result) => new(result);

        public ElevatedScanRetryRequestBuildResult Build(ScanResult result_)
        {
            CallCount++;
            return result;
        }
    }

    private sealed class FakeLauncher : IElevatedScannerLauncher
    {
        private readonly ElevatedScannerLauncherResult result;
        private readonly Task? gate;

        private FakeLauncher(ElevatedScannerLauncherResult result, Task? gate)
        {
            this.result = result;
            this.gate = gate;
        }

        public int CallCount { get; private set; }
        public static FakeLauncher Returning(ElevatedScannerLauncherResult result) => new(result, null);
        public static FakeLauncher Gated(Task gate, ElevatedScannerLauncherResult result) => new(result, gate);

        public async Task<ElevatedScannerLauncherResult> RunAsync(ElevatedScanRetryRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (gate is not null) await gate.ConfigureAwait(false);
            return result;
        }
    }

    private sealed class FakeReconciler(ElevatedReconciliationResult result) : IElevatedScanResultReconciler
    {
        public int CallCount { get; private set; }
        public static FakeReconciler ReturningApplied() => new(AppliedReconciliation());
        public static FakeReconciler Returning(ElevatedReconciliationResult result) => new(result);

        public ElevatedReconciliationResult Reconcile(ScanResult originalResult, ElevatedScanRetryRequest request, ElevatedScannerLauncherResult launcherResult)
        {
            CallCount++;
            return result;
        }
    }
}
