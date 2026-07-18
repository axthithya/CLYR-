using System.Collections.Immutable;
using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>Phase 7.2.6H2A: the app-facing <see cref="IElevatedScanRetryService"/> composed over the existing,
/// already-completed request factory, launcher, and reconciler. Every dependency here is an in-memory fake — no
/// real launcher, IPC, process, or UAC is ever exercised, and no real drive is ever scanned.</summary>
public sealed class ElevatedScanRetryServiceTests
{
    private static readonly Guid ScanId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private const string DriveIdentity = "drive-fingerprint-service";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-18T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void EligibleFullResultReportsAvailable()
    {
        var evaluator = FakeEvaluator.Returning(EligibleResult());

        var availability = Service(evaluator, CompletingLauncher()).Evaluate(OriginalResult());

        Assert.True(availability.IsEligible);
        Assert.Equal(ElevatedScanRetryEligibilityOutcome.Eligible, availability.EligibilityOutcome);
        Assert.Equal(1, availability.ReplaceableRootCount);
    }

    [Fact]
    public void QuickResultReportsUnavailable()
    {
        var evaluator = FakeEvaluator.Returning(ElevatedScanRetryEligibilityResult.Ineligible(ElevatedScanRetryEligibilityOutcome.QuickAnalysisNotEligible));

        var availability = Service(evaluator, CompletingLauncher()).Evaluate(OriginalResult());

        Assert.False(availability.IsEligible);
        Assert.Equal(ElevatedScanRetryEligibilityOutcome.QuickAnalysisNotEligible, availability.EligibilityOutcome);
        Assert.Equal(0, availability.ReplaceableRootCount);
    }

    [Fact]
    public void IncompleteResultReportsUnavailable()
    {
        var evaluator = FakeEvaluator.Returning(ElevatedScanRetryEligibilityResult.Ineligible(ElevatedScanRetryEligibilityOutcome.ScanNotCompleted));

        var availability = Service(evaluator, CompletingLauncher()).Evaluate(OriginalResult());

        Assert.False(availability.IsEligible);
        Assert.Equal(ElevatedScanRetryEligibilityOutcome.ScanNotCompleted, availability.EligibilityOutcome);
    }

    [Fact]
    public void NoRootContributionsReportsUnavailable()
    {
        var evaluator = FakeEvaluator.Returning(ElevatedScanRetryEligibilityResult.Ineligible(ElevatedScanRetryEligibilityOutcome.NoRootContributions));

        var availability = Service(evaluator, CompletingLauncher()).Evaluate(OriginalResult() with { RootContributions = [] });

        Assert.False(availability.IsEligible);
        Assert.Equal(ElevatedScanRetryEligibilityOutcome.NoRootContributions, availability.EligibilityOutcome);
        Assert.Equal(0, availability.PermissionLimitedRootCount);
    }

    [Fact]
    public void NoReplaceableRootsReportsUnavailable()
    {
        var evaluator = FakeEvaluator.Returning(ElevatedScanRetryEligibilityResult.Ineligible(ElevatedScanRetryEligibilityOutcome.NoReplaceablePermissionLimitedRoots));
        var result = OriginalResult() with { RootContributions = [Contribution("C:\\Alpha", ScanRootEnumerationState.Completed, 0)] };

        var availability = Service(evaluator, CompletingLauncher()).Evaluate(result);

        Assert.False(availability.IsEligible);
        Assert.Equal(ElevatedScanRetryEligibilityOutcome.NoReplaceablePermissionLimitedRoots, availability.EligibilityOutcome);
        Assert.Equal(0, availability.PermissionLimitedRootCount);
    }

    [Fact]
    public void ReplaceableRootCountIsTruthful()
    {
        // Two roots look permission-limited (one InaccessibleAtRoot, one PartiallyObserved-with-entries), but the
        // eligibility evaluator (a fake here, standing in for a real duplicate/overlap rejection) says only one
        // of them actually made it into the final, safely replaceable set.
        var result = OriginalResult() with
        {
            RootContributions =
            [
                Contribution("C:\\Alpha", ScanRootEnumerationState.InaccessibleAtRoot, 0),
                Contribution("C:\\Beta", ScanRootEnumerationState.PartiallyObserved, 2),
                Contribution("C:\\Gamma", ScanRootEnumerationState.Completed, 0)
            ]
        };
        var evaluator = FakeEvaluator.Returning(new ElevatedScanRetryEligibilityResult(
            ElevatedScanRetryEligibilityOutcome.Eligible, [result.RootContributions[0]], DriveIdentity));

        var availability = Service(evaluator, CompletingLauncher()).Evaluate(result);

        Assert.Equal(2, availability.PermissionLimitedRootCount);
        Assert.Equal(1, availability.ReplaceableRootCount);
    }

    [Fact]
    public void AvailabilityCausesZeroLauncherCalls()
    {
        var launcher = CompletingLauncher();

        Service(FakeEvaluator.Returning(EligibleResult()), launcher).Evaluate(OriginalResult());

        Assert.Equal(0, launcher.CallCount);
    }

    [Fact]
    public async Task RetryDelegatesExactlyOnceToTheCoordinator()
    {
        var launcher = CompletingLauncher();
        var reconciler = FakeReconciler.ReturningApplied();

        var result = await Service(FakeEvaluator.Returning(EligibleResult()), launcher, reconciler)
            .RetryAsync(OriginalResult(), CancellationToken.None);

        Assert.Equal(1, launcher.CallCount);
        Assert.Equal(1, reconciler.CallCount);
        Assert.Equal(ElevatedScanRetryWorkflowOutcome.Applied, result.Outcome);
    }

    [Fact]
    public async Task RetryPassesTheSameScanResultObject()
    {
        var original = OriginalResult();

        var result = await Service(FakeEvaluator.Returning(EligibleResult()), CompletingLauncher(), FakeReconciler.ReturningApplied())
            .RetryAsync(original, CancellationToken.None);

        Assert.Same(original, result.OriginalResult);
    }

    [Fact]
    public async Task RetryPassesTheCallerCancellationToken()
    {
        var launcher = new CapturingLauncher();
        using var cts = new CancellationTokenSource();

        await Service(FakeEvaluator.Returning(EligibleResult()), launcher, FakeReconciler.ReturningApplied())
            .RetryAsync(OriginalResult(), cts.Token);

        Assert.Equal(cts.Token, launcher.ObservedToken);
    }

    [Fact]
    public async Task CancellationIsReturnedWithoutAutomaticRetry()
    {
        var launcher = CompletingLauncher();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await Service(FakeEvaluator.Returning(EligibleResult()), launcher, FakeReconciler.ReturningApplied())
            .RetryAsync(OriginalResult(), cts.Token);

        Assert.Equal(ElevatedScanRetryWorkflowOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, launcher.CallCount);
    }

    [Fact]
    public void ProductionCompositionContainsOnlyTheApprovedDependencyGraphAndNoSecondProcessLaunchImplementation()
    {
        var file = Path.Combine(RepositoryRoot(), "src", "Clyr.App", "ElevatedScanRetryServiceFactory.cs");
        Assert.True(File.Exists(file), $"Expected file not found: {file}");
        var text = File.ReadAllText(file);

        foreach (var expected in new[]
        {
            "new SystemClock()", "new CryptographicNonceGenerator()", "new ElevatedScanRetryRequestFactory(",
            "new ProcessTrustedApplicationBaseDirectory()", "new FileSystemElevatedScannerFileProbe()",
            "new WindowsElevatedScannerProcessStarter()", "new ElevatedScannerLauncher(",
            "new ElevatedScanResultReconcilerAdapter(", "new ElevatedScanRetryCoordinator(", "new ElevatedScanRetryService(",
        })
            Assert.Contains(expected, text, StringComparison.Ordinal);

        Assert.DoesNotContain("Process.Start", text, StringComparison.Ordinal);
        Assert.DoesNotContain("class WindowsElevatedScannerProcessStarter", text, StringComparison.Ordinal);
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static ScanResult OriginalResult() =>
        ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed) with
        {
            ScanId = ScanId,
            RootContributions = [Contribution("C:\\Alpha", ScanRootEnumerationState.InaccessibleAtRoot, 0)]
        };

    private static ScanRootContribution Contribution(string path, ScanRootEnumerationState state, long inaccessibleEntries) =>
        new(ElevatedScanManifestBuilder.NormalizePath(path), null, path, state, 1, 0, 100, 80, 80, 0, 0, 0, 0, inaccessibleEntries, 0, 0);

    private static ElevatedScanRetryEligibilityResult EligibleResult() =>
        new(ElevatedScanRetryEligibilityOutcome.Eligible, [Contribution("C:\\Alpha", ScanRootEnumerationState.InaccessibleAtRoot, 0)], DriveIdentity);

    private static ElevatedScanRetryService Service(IElevatedScanRetryEligibilityEvaluator evaluator, IElevatedScannerLauncher launcher, FakeReconciler? reconciler = null)
    {
        var factory = FakeFactory.Returning(new ElevatedScanRetryRequestBuildResult(ElevatedScanRetryEligibilityOutcome.Eligible, BuildRequest()));
        var coordinator = new ElevatedScanRetryCoordinator(factory, launcher, reconciler ?? FakeReconciler.ReturningApplied());
        return new ElevatedScanRetryService(evaluator, coordinator);
    }

    private static FakeLauncher CompletingLauncher() => FakeLauncher.Returning(new ElevatedScannerLauncherResult(ElevatedScannerLauncherOutcome.Completed, ValidResponse()));

    private static ElevatedScanRetryRequest BuildRequest()
    {
        var roots = ImmutableArray.Create(new PermissionLimitedRoot("C:\\Alpha", ScanId, DriveIdentity, null, PermissionLimitedReasonCode.AccessDenied));
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

    private sealed class FakeEvaluator(ElevatedScanRetryEligibilityResult result) : IElevatedScanRetryEligibilityEvaluator
    {
        public static FakeEvaluator Returning(ElevatedScanRetryEligibilityResult result) => new(result);
        public ElevatedScanRetryEligibilityResult EvaluateEligibility(ScanResult result_) => result;
    }

    private sealed class FakeFactory(ElevatedScanRetryRequestBuildResult result) : IElevatedScanRetryRequestFactory
    {
        public static FakeFactory Returning(ElevatedScanRetryRequestBuildResult result) => new(result);
        public ElevatedScanRetryRequestBuildResult Build(ScanResult result_) => result;
    }

    private sealed class FakeLauncher : IElevatedScannerLauncher
    {
        private readonly ElevatedScannerLauncherResult result;
        private FakeLauncher(ElevatedScannerLauncherResult result) => this.result = result;
        public int CallCount { get; private set; }
        public static FakeLauncher Returning(ElevatedScannerLauncherResult result) => new(result);

        public Task<ElevatedScannerLauncherResult> RunAsync(ElevatedScanRetryRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class CapturingLauncher : IElevatedScannerLauncher
    {
        public CancellationToken ObservedToken { get; private set; }

        public Task<ElevatedScannerLauncherResult> RunAsync(ElevatedScanRetryRequest request, CancellationToken cancellationToken)
        {
            ObservedToken = cancellationToken;
            return Task.FromResult(new ElevatedScannerLauncherResult(ElevatedScannerLauncherOutcome.Completed, ValidResponse()));
        }
    }

    private sealed class FakeReconciler(ElevatedReconciliationResult result) : IElevatedScanResultReconciler
    {
        public int CallCount { get; private set; }
        public static FakeReconciler ReturningApplied() => new(AppliedReconciliation());

        public ElevatedReconciliationResult Reconcile(ScanResult originalResult, ElevatedScanRetryRequest request, ElevatedScannerLauncherResult launcherResult)
        {
            CallCount++;
            return result;
        }
    }
}
