using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>Phase 7.2.6H2B: the pure administrator-retry UI state/lifecycle logic, exercised through a fake
/// <see cref="IElevatedScanRetryService"/>. No real launcher, IPC, process, or UAC is ever exercised, and no real
/// drive is ever scanned — the fake is the only "service" involved anywhere in this file.</summary>
public sealed class AdministratorRetryUxTests
{
    private static readonly Guid ScanId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    [Fact]
    public void EligibleDeepResultShowsTheRetryAction()
    {
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 2, 2, "elevated-retry-availability.eligible"));
        var controller = new AdministratorRetryController(service);

        controller.Evaluate(OriginalResult());

        Assert.True(controller.State.IsAdministratorRetryAvailable);
        Assert.Equal(2, controller.State.ReplaceableRootCount);
        Assert.Equal(AdministratorRetryPhase.Idle, controller.State.Phase);
    }

    [Fact]
    public void QuickResultHidesTheAction()
    {
        var service = FakeService.Evaluating(new(false, ElevatedScanRetryEligibilityOutcome.QuickAnalysisNotEligible, 0, 0, "elevated-retry-availability.quick-analysis-not-eligible"));
        var controller = new AdministratorRetryController(service);

        controller.Evaluate(OriginalResult());

        Assert.False(controller.State.IsAdministratorRetryAvailable);
        Assert.Equal(AdministratorRetryPhase.Hidden, controller.State.Phase);
    }

    [Fact]
    public void NoReplaceableRootsHidesTheAction()
    {
        var service = FakeService.Evaluating(new(false, ElevatedScanRetryEligibilityOutcome.NoReplaceablePermissionLimitedRoots, 0, 0, "elevated-retry-availability.no-replaceable-roots"));
        var controller = new AdministratorRetryController(service);

        controller.Evaluate(OriginalResult());

        Assert.False(controller.State.IsAdministratorRetryAvailable);
    }

    [Fact]
    public void RunningScanHidesTheAction()
    {
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        var controller = new AdministratorRetryController(service);

        // The page passes null while Session.IsScanning is true, regardless of any previously-successful result.
        controller.Evaluate(null);

        Assert.False(controller.State.IsAdministratorRetryAvailable);
        Assert.Equal(AdministratorRetryPhase.Hidden, controller.State.Phase);
    }

    [Fact]
    public async Task NoRetryCallWithoutExplicitRunAsync()
    {
        // Simulates a cancelled/dismissed confirmation dialog: the page simply never calls RunAsync at all.
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        var controller = new AdministratorRetryController(service);
        controller.Evaluate(OriginalResult());

        await Task.Yield();

        Assert.Equal(0, service.RetryCallCount);
    }

    [Fact]
    public async Task ConfirmedRunCallsRetryExactlyOnce()
    {
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.NextResult = AppliedResult();
        var controller = new AdministratorRetryController(service);
        var original = OriginalResult();
        controller.Evaluate(original);

        await controller.RunAsync(original);

        Assert.Equal(1, service.RetryCallCount);
        Assert.Equal(AdministratorRetryPhase.Applied, controller.State.Phase);
    }

    [Fact]
    public async Task StateReflectsRunningWhileRetryIsInFlight()
    {
        var gate = new TaskCompletionSource();
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.Gate = gate.Task;
        service.NextResult = AppliedResult();
        var controller = new AdministratorRetryController(service);
        var original = OriginalResult();
        controller.Evaluate(original);

        var run = controller.RunAsync(original);

        Assert.True(controller.State.IsAdministratorRetryRunning);
        Assert.False(controller.CanStart(original));
        gate.SetResult();
        await run;
        Assert.False(controller.State.IsAdministratorRetryRunning);
    }

    [Fact]
    public async Task DuplicateRunAsyncCallCausesNoSecondRetry()
    {
        var gate = new TaskCompletionSource();
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.Gate = gate.Task;
        service.NextResult = AppliedResult();
        var controller = new AdministratorRetryController(service);
        var original = OriginalResult();
        controller.Evaluate(original);

        var first = controller.RunAsync(original);
        var second = controller.RunAsync(original); // no-op: a retry is already in flight

        gate.SetResult();
        await first;
        await second;

        Assert.Equal(1, service.RetryCallCount);
    }

    [Fact]
    public async Task OriginalScanResultReferenceIsPreservedThroughTheWorkflow()
    {
        var original = OriginalResult();
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.NextResult = AppliedResult() with { OriginalResult = original };
        var controller = new AdministratorRetryController(service);
        controller.Evaluate(original);

        await controller.RunAsync(original);

        Assert.Same(original, service.LastRetryArgument);
    }

    [Fact]
    public async Task AppliedResultStoresTheCombinedResultSeparately()
    {
        var applied = AppliedResult();
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.NextResult = applied;
        var controller = new AdministratorRetryController(service);
        var original = OriginalResult();
        controller.Evaluate(original);

        await controller.RunAsync(original);

        Assert.NotNull(controller.State.CombinedResult);
        Assert.Same(applied.CombinedResult, controller.State.CombinedResult);
    }

    [Fact]
    public async Task ReEvaluatingTheEnrichedResultAfterASuccessfulRetryKeepsTheTerminalSummary()
    {
        // Confirmed real-machine defect: applying a successful retry's enriched result fires
        // AppSessionViewModel.StateChanged synchronously, which the page's own subscription turns into another
        // Evaluate(...) call for the now-enriched result — before the page ever renders the just-set Applied
        // state. The old behavior discarded that terminal summary outright, so only the plain retry button ever
        // reached the screen even though the retry had already succeeded.
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.NextResult = AppliedResult();
        var controller = new AdministratorRetryController(service);
        var original = OriginalResult();
        controller.Evaluate(original);

        await controller.RunAsync(original);
        Assert.Equal(AdministratorRetryPhase.Applied, controller.State.Phase);
        var combinedBeforeReEvaluate = controller.State.CombinedResult;

        // Enrichment always preserves ScanId (see AppSessionViewModel.ApplyEnrichedResult) — a different object,
        // same identity.
        var enriched = original with { LogicalBytesObserved = original.LogicalBytesObserved + 500 };
        controller.Evaluate(enriched);

        Assert.Equal(AdministratorRetryPhase.Applied, controller.State.Phase);
        Assert.Same(combinedBeforeReEvaluate, controller.State.CombinedResult);
        // The fresh availability check still merges in — so a "retry again" button can appear beneath the
        // summary when further restricted roots remain eligible.
        Assert.True(controller.State.IsAdministratorRetryAvailable);
        Assert.Equal(1, controller.State.ReplaceableRootCount);
    }

    [Fact]
    public async Task ReEvaluatingADifferentScanIdAfterATerminalOutcomeStartsCompletelyFresh()
    {
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.NextResult = AppliedResult();
        var controller = new AdministratorRetryController(service);
        var original = OriginalResult();
        controller.Evaluate(original);
        await controller.RunAsync(original);
        Assert.Equal(AdministratorRetryPhase.Applied, controller.State.Phase);

        // A genuinely new Drive Analysis — different ScanId — must never inherit the previous analysis's
        // terminal summary.
        var newAnalysis = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed) with { ScanId = Guid.NewGuid() };
        controller.Evaluate(newAnalysis);

        Assert.Equal(AdministratorRetryPhase.Idle, controller.State.Phase);
        Assert.Null(controller.State.CombinedResult);
    }

    [Fact]
    public async Task DeniedOutcomeShowsANeutralMessageAndRestoresAvailability()
    {
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.NextResult = TerminalResult(ElevatedScanRetryWorkflowOutcome.Denied);
        var controller = new AdministratorRetryController(service);
        var original = OriginalResult();
        controller.Evaluate(original);

        await controller.RunAsync(original);

        Assert.Equal(AdministratorRetryPhase.Denied, controller.State.Phase);
        Assert.Equal(AdministratorRetryUx.DeniedText, controller.State.AdministratorRetryStatusText);
        Assert.Equal(AdministratorRetryUx.DeniedTitle, controller.State.AdministratorRetryTitle);
        Assert.False(controller.State.IsAdministratorRetryRunning);
        Assert.Null(controller.State.CombinedResult);
    }

    [Fact]
    public async Task TimedOutAndFailedOutcomesPreserveTruthfulMessagesWithNoCombinedResult()
    {
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        var controller = new AdministratorRetryController(service);
        var original = OriginalResult();

        service.NextResult = TerminalResult(ElevatedScanRetryWorkflowOutcome.ConnectionTimedOut);
        controller.Evaluate(original);
        await controller.RunAsync(original);
        Assert.Equal(AdministratorRetryPhase.ConnectionTimedOut, controller.State.Phase);
        Assert.Equal(AdministratorRetryUx.ConnectionTimedOutTitle, controller.State.AdministratorRetryTitle);
        Assert.Null(controller.State.CombinedResult);

        service.NextResult = TerminalResult(ElevatedScanRetryWorkflowOutcome.Failed);
        controller.Evaluate(original);
        await controller.RunAsync(original);
        Assert.Equal(AdministratorRetryPhase.Failed, controller.State.Phase);
        Assert.Equal(AdministratorRetryUx.FailedText, controller.State.AdministratorRetryStatusText);
        Assert.Null(controller.State.CombinedResult);
    }

    [Fact]
    public async Task AdditionalCoverageAndRootCountsAreProjectedTruthfully()
    {
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.NextResult = AppliedResult();
        var controller = new AdministratorRetryController(service);
        var original = OriginalResult();
        controller.Evaluate(original);

        await controller.RunAsync(original);

        Assert.Equal(500, controller.State.AdditionalLogicalBytes);
        Assert.Equal(300, controller.State.AdditionalAllocatedBytes);
        Assert.Equal(1, controller.State.RootsCompleted);
        Assert.Equal(0, controller.State.RootsStillInaccessible);
    }

    [Fact]
    public async Task FakeRetryCompletionRaisedFromAWorkerThreadDoesNotRequireUiThreadAccessFromCore()
    {
        // Proves Core has no thread-affinity of its own: the fake forces a real thread-pool hop before
        // completing, exactly like the real IElevatedScanRetryService.RetryAsync (awaited with
        // ConfigureAwait(false) inside AdministratorRetryController.RunAsync) does in production.
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.NextResult = AppliedResult();
        service.CompleteOnThreadPool = true;
        var controller = new AdministratorRetryController(service);
        var original = OriginalResult();
        controller.Evaluate(original);
        var raisedThreadIds = new List<int>();
        controller.StateChanged += (_, _) => raisedThreadIds.Add(Environment.CurrentManagedThreadId);

        await controller.RunAsync(original);

        Assert.Equal(AdministratorRetryPhase.Applied, controller.State.Phase);
        Assert.NotEmpty(raisedThreadIds);
    }

    [Fact]
    public async Task ControllerConvertsAnUnexpectedNonFatalServiceExceptionIntoFailed()
    {
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.ExceptionToThrow = new InvalidOperationException("unexpected");
        var controller = new AdministratorRetryController(service);
        var original = OriginalResult();
        controller.Evaluate(original);

        await controller.RunAsync(original);

        Assert.Equal(AdministratorRetryPhase.Failed, controller.State.Phase);
        Assert.Equal(AdministratorRetryUx.FailedText, controller.State.AdministratorRetryStatusText);
        Assert.False(controller.State.IsAdministratorRetryRunning);
        Assert.DoesNotContain("unexpected", controller.State.AdministratorRetryStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OperationCanceledExceptionProducesCancelled()
    {
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.ExceptionToThrow = new OperationCanceledException();
        var controller = new AdministratorRetryController(service);
        var original = OriginalResult();
        controller.Evaluate(original);

        await controller.RunAsync(original);

        Assert.Equal(AdministratorRetryPhase.Cancelled, controller.State.Phase);
        Assert.False(controller.State.IsAdministratorRetryRunning);
    }

    [Fact]
    public async Task TimeoutProducesTheExistingTimedOutState()
    {
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.ExceptionToThrow = new TimeoutException();
        var controller = new AdministratorRetryController(service);
        var original = OriginalResult();
        controller.Evaluate(original);

        await controller.RunAsync(original);

        Assert.Equal(AdministratorRetryPhase.OperationTimedOut, controller.State.Phase);
        Assert.False(controller.State.IsAdministratorRetryRunning);
    }

    [Fact]
    public async Task PipeIoFailureProducesTheBoundedFailureState()
    {
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.ExceptionToThrow = new EndOfStreamException("pipe closed unexpectedly");
        var controller = new AdministratorRetryController(service);
        var original = OriginalResult();
        controller.Evaluate(original);

        await controller.RunAsync(original);

        Assert.Equal(AdministratorRetryPhase.Failed, controller.State.Phase);
        Assert.Equal(AdministratorRetryUx.FailedText, controller.State.AdministratorRetryStatusText);
        Assert.DoesNotContain("pipe closed", controller.State.AdministratorRetryStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BusyStateIsClearedAfterEveryFailure()
    {
        var original = OriginalResult();
        foreach (Exception exception in new Exception[]
        {
            new InvalidOperationException(), new OperationCanceledException(), new TimeoutException(), new IOException(),
        })
        {
            var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
            service.ExceptionToThrow = exception;
            var controller = new AdministratorRetryController(service);
            controller.Evaluate(original);

            await controller.RunAsync(original);

            Assert.False(controller.State.IsAdministratorRetryRunning);
            Assert.True(controller.CanStart(original));
        }
    }

    [Fact]
    public async Task DuplicateClickStillCausesOneServiceCallEvenWhenTheFirstAttemptFails()
    {
        var gate = new TaskCompletionSource();
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.Gate = gate.Task;
        service.ExceptionToThrow = new IOException("transport failure");
        var controller = new AdministratorRetryController(service);
        var original = OriginalResult();
        controller.Evaluate(original);

        var first = controller.RunAsync(original);
        var second = controller.RunAsync(original); // no-op: a retry is already in flight

        gate.SetResult();
        await first;
        await second;

        Assert.Equal(1, service.RetryCallCount);
        Assert.Equal(AdministratorRetryPhase.Failed, controller.State.Phase);
    }

    [Fact]
    public async Task DisposeRacingWithCompletionDoesNotRaiseAPostDisposalStateEvent()
    {
        var gate = new TaskCompletionSource();
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.Gate = gate.Task;
        service.NextResult = AppliedResult();
        var controller = new AdministratorRetryController(service);
        var original = OriginalResult();
        controller.Evaluate(original);
        var run = controller.RunAsync(original);
        var eventsAfterDispose = 0;

        controller.Dispose();
        controller.StateChanged += (_, _) => eventsAfterDispose++;
        gate.SetResult();
        await run;

        Assert.Equal(0, eventsAfterDispose);
    }

    [Fact]
    public async Task OriginalScanResultRemainsTheSameReferenceAfterFailure()
    {
        var original = OriginalResult();
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.ExceptionToThrow = new InvalidOperationException("boom");
        var controller = new AdministratorRetryController(service);
        controller.Evaluate(original);

        await controller.RunAsync(original);

        Assert.Same(original, service.LastRetryArgument);
        Assert.Equal(ScanStatus.Completed, original.Status);
        Assert.Equal(ScanMode.Deep, original.Mode);
    }

    [Fact]
    public async Task RunningStateIncludesRootCountAndBoundedDurationExplanation()
    {
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 3, 3, "elevated-retry-availability.eligible"));
        var gate = new TaskCompletionSource();
        service.Gate = gate.Task;
        service.NextResult = AppliedResult();
        var controller = new AdministratorRetryController(service, new FixedClock(Now));
        var original = OriginalResult();
        controller.Evaluate(original);

        var run = controller.RunAsync(original);

        Assert.Equal(AdministratorRetryPhase.Running, controller.State.Phase);
        Assert.Equal("Retrying 3 restricted areas…", controller.State.AdministratorRetryStatusText);
        Assert.Equal(AdministratorRetryUx.RunningTitle, controller.State.AdministratorRetryTitle);
        Assert.Equal(Now, controller.State.RunningSinceUtc);
        Assert.Contains("several minutes", AdministratorRetryUx.RunningSupportingText, StringComparison.Ordinal);
        gate.SetResult();
        await run;
    }

    [Fact]
    public async Task TimeoutUiStateIncludesSafetyLimitDetailWithoutRawPaths()
    {
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.NextResult = TerminalResult(ElevatedScanRetryWorkflowOutcome.TimedOut);
        var controller = new AdministratorRetryController(service);
        var original = OriginalResult();
        controller.Evaluate(original);

        await controller.RunAsync(original);

        Assert.Equal(AdministratorRetryPhase.OperationTimedOut, controller.State.Phase);
        Assert.Equal(AdministratorRetryUx.OperationTimedOutTitle, controller.State.AdministratorRetryTitle);
        Assert.Contains("10-minute", controller.State.AdministratorRetryStatusText, StringComparison.Ordinal);
        Assert.Contains("accounted", controller.State.AdministratorRetryStatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", controller.State.AdministratorRetryStatusText, StringComparison.Ordinal);
        Assert.DoesNotContain(":\\", controller.State.AdministratorRetryTitle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunningSinceIsClearedOnEveryTerminalOutcomeIncludingDisposal()
    {
        var original = OriginalResult();

        var appliedService = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        appliedService.NextResult = AppliedResult();
        var appliedController = new AdministratorRetryController(appliedService, new FixedClock(Now));
        appliedController.Evaluate(original);
        await appliedController.RunAsync(original);
        Assert.Null(appliedController.State.RunningSinceUtc);

        var timedOutService = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        timedOutService.NextResult = TerminalResult(ElevatedScanRetryWorkflowOutcome.TimedOut);
        var timedOutController = new AdministratorRetryController(timedOutService, new FixedClock(Now));
        timedOutController.Evaluate(original);
        await timedOutController.RunAsync(original);
        Assert.Null(timedOutController.State.RunningSinceUtc);

        var cancelledService = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        cancelledService.ExceptionToThrow = new OperationCanceledException();
        var cancelledController = new AdministratorRetryController(cancelledService, new FixedClock(Now));
        cancelledController.Evaluate(original);
        await cancelledController.RunAsync(original);
        Assert.Null(cancelledController.State.RunningSinceUtc);

        // Disposal never leaves a stale "running since" value visible either — the last-set state before
        // disposal is whatever it already was (Idle here, since no attempt was ever started).
        var idleController = new AdministratorRetryController(FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible")));
        idleController.Evaluate(original);
        idleController.Dispose();
        Assert.Null(idleController.State.RunningSinceUtc);
    }

    [Fact]
    public async Task NoAutomaticSecondLauncherInvocationOccursAfterATimeout()
    {
        var service = FakeService.Evaluating(new(true, ElevatedScanRetryEligibilityOutcome.Eligible, 1, 1, "elevated-retry-availability.eligible"));
        service.NextResult = TerminalResult(ElevatedScanRetryWorkflowOutcome.TimedOut);
        var controller = new AdministratorRetryController(service);
        var original = OriginalResult();
        controller.Evaluate(original);

        await controller.RunAsync(original);
        await Task.Delay(20); // give any hypothetical background retry a chance to fire

        Assert.Equal(1, service.RetryCallCount);
        Assert.Equal(AdministratorRetryPhase.OperationTimedOut, controller.State.Phase);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-18T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    private static ScanResult OriginalResult() =>
        ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed) with { ScanId = ScanId };

    private static ElevatedScanRetryWorkflowResult AppliedResult() =>
        new(ElevatedScanRetryWorkflowOutcome.Applied, OriginalResult(), ElevatedScanRetryEligibilityOutcome.Eligible,
            ElevatedScannerLauncherOutcome.Completed, ElevatedReconciliationOutcome.Applied,
            CombinedResultFixture(), 500, 300, 1, 1, 0, "elevated-retry.applied");

    private static ElevatedScanRetryWorkflowResult TerminalResult(ElevatedScanRetryWorkflowOutcome outcome) =>
        new(outcome, OriginalResult(), ElevatedScanRetryEligibilityOutcome.Eligible,
            ElevatedScannerLauncherOutcome.Denied, null, null, null, null, 1, 0, 1, "elevated-retry." + outcome);

    private static ElevatedReconciliationResult CombinedResultFixture()
    {
        var attempt = new ElevatedRetryAttempt(Guid.NewGuid(), ScanId, "drive-fingerprint-ux", new string('a', ElevatedScanRetryProtocol.MinNonceLength),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ElevatedScannerLauncherOutcome.Completed, ElevatedScanRetryOutcome.Completed,
            1, 1, 0, 500, 300, [], true);
        return new ElevatedReconciliationResult(ElevatedReconciliationOutcome.Applied, OriginalResult(), attempt, [], [], 1500, 1300, AccountingConsistency.Consistent);
    }

    private sealed class FakeService(ElevatedScanRetryAvailability availability) : IElevatedScanRetryService
    {
        public Task? Gate { get; set; }
        public ElevatedScanRetryWorkflowResult? NextResult { get; set; }
        public Exception? ExceptionToThrow { get; set; }
        public bool CompleteOnThreadPool { get; set; }
        public int RetryCallCount { get; private set; }
        public ScanResult? LastRetryArgument { get; private set; }

        public static FakeService Evaluating(ElevatedScanRetryAvailability availability) => new(availability);

        public ElevatedScanRetryAvailability Evaluate(ScanResult originalResult) => availability;

        public async Task<ElevatedScanRetryWorkflowResult> RetryAsync(ScanResult originalResult, CancellationToken cancellationToken)
        {
            RetryCallCount++;
            LastRetryArgument = originalResult;
            if (Gate is { } gate) await gate.ConfigureAwait(false);
            // Mirrors the real ElevatedScannerLauncher/coordinator path, which always completes its IPC exchange
            // on a thread-pool continuation (see AdministratorRetryController.RunAsync's ConfigureAwait(false)) —
            // never on whatever thread originally called RunAsync.
            if (CompleteOnThreadPool) await Task.Run(() => { }, CancellationToken.None).ConfigureAwait(false);
            if (ExceptionToThrow is { } exception) throw exception;
            return NextResult!;
        }
    }
}
