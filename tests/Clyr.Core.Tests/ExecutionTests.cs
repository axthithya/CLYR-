using System.Collections.Immutable;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Core.Execution;

namespace Clyr.Core.Tests;

public sealed class ExecutionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "clyr-exec-test-" + Guid.NewGuid().ToString("N"));
    private readonly MutableClock clock = new(new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero));
    private const string UserSid = "S-1-5-21-1111111111-2222222222-3333333333-1001";

    public ExecutionTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }

    [Fact]
    public void ScannerFindsOnlyStaleFilesInsideTrustedRootAndIgnoresFreshOnes()
    {
        WriteStale("old1.tmp");
        WriteStale("old2.tmp");
        File.WriteAllText(Path.Combine(root, "fresh.tmp"), "recent");

        var candidate = ClyrOwnedTempArtifactScanner.Scan(clock, root);

        Assert.NotNull(candidate);
        Assert.Equal(2, candidate!.Targets.Length);
        Assert.Equal(CleanupEligibility.DryRunEligible, candidate.Eligibility);
        Assert.Equal(RiskLevel.Low, candidate.Risk);
        Assert.Equal(CleanupActionType.TrustedBuiltInCleanup, candidate.Action!.ActionType);
        Assert.Equal(ExecutionAvailability.Phase6BuiltInExecutable, candidate.Action.ExecutionAvailability);
        Assert.DoesNotContain(candidate.Targets, target => target.DisplayLocation.Contains("fresh", StringComparison.Ordinal));
    }

    [Fact]
    public void ScannerReturnsNullWhenNothingQualifies()
    {
        File.WriteAllText(Path.Combine(root, "fresh.tmp"), "recent");
        Assert.Null(ClyrOwnedTempArtifactScanner.Scan(clock, root));
        Assert.Null(ClyrOwnedTempArtifactScanner.Scan(clock, Path.Combine(root, "does-not-exist")));
    }

    [Fact]
    public void HappyPathRemovesExactStaleFilesAndProducesVerifiableReceipt()
    {
        WriteStale("old1.tmp");
        WriteStale("old2.tmp");
        var freshPath = Path.Combine(root, "fresh.tmp");
        File.WriteAllText(freshPath, "recent");

        var (plan, tokenService) = BuildPlan();
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock);

        var outcome = executor.Execute(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);

        Assert.Equal(ExecutionState.Completed, outcome.State);
        Assert.Equal(2, outcome.Items.Length);
        Assert.All(outcome.Items, item => Assert.Equal(ExecutionItemOutcome.Removed, item.Outcome));
        Assert.Equal(2, outcome.Receipt.Summary.RemovedCount);
        Assert.True(File.Exists(freshPath));
        Assert.False(Directory.EnumerateFiles(root, "old*").Any());
        Assert.Equal(64, outcome.Receipt.Digest.Length);
        Assert.Equal(outcome.Receipt.Digest, ExecutionReceiptCanonicalizer.Digest(outcome.Receipt));
    }

    [Fact]
    public void TokenCannotBeReplayedAfterConsumption()
    {
        WriteStale("old1.tmp");
        var (plan, tokenService) = BuildPlan();
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock);

        var first = executor.Execute(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);
        Assert.Equal(ExecutionState.Completed, first.State);

        var replay = executor.Execute(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);
        Assert.Equal(ExecutionState.Rejected, replay.State);
        Assert.Empty(replay.Items);
    }

    [Fact]
    public void ExpiredTokenIsRejected()
    {
        WriteStale("old1.tmp");
        var (plan, tokenService) = BuildPlan();
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        clock.Advance(TimeSpan.FromMinutes(5));
        var executor = new NonElevatedCleanupExecutor(tokenService, clock);

        var result = executor.Execute(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);
        Assert.Equal(ExecutionState.Rejected, result.State);
        Assert.True(File.Exists(Path.Combine(root, "old1.tmp")));
    }

    [Fact]
    public void TamperedPlanDigestIsRejected()
    {
        WriteStale("old1.tmp");
        var (plan, tokenService) = BuildPlan();
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var tampered = new CleanupPlan(plan.SchemaVersion, plan.Id, plan.ApplicationVersion, plan.Binding, plan.Expiry,
            plan.Items, plan.TotalImpact, plan.Risk, plan.Confidence, plan.Warnings, plan.ExecutionAvailability, new string('0', 64));
        var executor = new NonElevatedCleanupExecutor(tokenService, clock);

        var result = executor.Execute(tampered, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);
        Assert.Equal(ExecutionState.Rejected, result.State);
        Assert.True(File.Exists(Path.Combine(root, "old1.tmp")));
    }

    [Fact]
    public void TokenBoundToWrongSessionOrUserIsRejected()
    {
        WriteStale("old1.tmp");
        var (plan, tokenService) = BuildPlan();
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock);

        var wrongSession = executor.Execute(plan, [plan.Items[0].ItemId], token, new ExecutionSessionId(Guid.NewGuid()), UserSid, "test-app-1", root, CancellationToken.None);
        Assert.Equal(ExecutionState.Rejected, wrongSession.State);

        var wrongUser = executor.Execute(plan, [plan.Items[0].ItemId], token, sessionId, "S-1-5-21-9-9-9-9999", "test-app-1", root, CancellationToken.None);
        Assert.Equal(ExecutionState.Rejected, wrongUser.State);
        Assert.True(File.Exists(Path.Combine(root, "old1.tmp")));
    }

    [Fact]
    public void TargetOutsideApprovedRootIsSkippedNotDeleted()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), "clyr-outside-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(outsidePath, "should never be touched");
        try
        {
            var candidate = ManualCandidateFor(outsidePath, reparse: false, size: new FileInfo(outsidePath).Length,
                lastWrite: clock.UtcNow - TimeSpan.FromDays(30));
            var (plan, tokenService) = BuildPlan(candidate);
            var sessionId = new ExecutionSessionId(Guid.NewGuid());
            var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
            var executor = new NonElevatedCleanupExecutor(tokenService, clock);

            var result = executor.Execute(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);

            Assert.Equal(ExecutionItemOutcome.SkippedOutsideApprovedRoot, Assert.Single(result.Items).Outcome);
            Assert.True(File.Exists(outsidePath));
        }
        finally { File.Delete(outsidePath); }
    }

    [Fact]
    public void TargetClaimingReparseAtPlanTimeIsSkipped()
    {
        WriteStale("old1.tmp");
        var path = Path.Combine(root, "old1.tmp");
        var candidate = ManualCandidateFor(path, reparse: true, size: new FileInfo(path).Length, lastWrite: clock.UtcNow - TimeSpan.FromDays(30));
        var (plan, tokenService) = BuildPlan(candidate);
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock);

        var result = executor.Execute(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);

        Assert.Equal(ExecutionItemOutcome.SkippedReparsePoint, Assert.Single(result.Items).Outcome);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void TargetChangedAfterPlanningIsSkippedNotForced()
    {
        WriteStale("old1.tmp");
        var path = Path.Combine(root, "old1.tmp");
        var (plan, tokenService) = BuildPlan();
        File.WriteAllText(path, "content changed after the plan was created");
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock);

        var result = executor.Execute(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);

        Assert.Equal(ExecutionItemOutcome.SkippedChanged, Assert.Single(result.Items).Outcome);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void MissingTargetReportsNotFoundRatherThanFailing()
    {
        WriteStale("old1.tmp");
        var path = Path.Combine(root, "old1.tmp");
        var (plan, tokenService) = BuildPlan();
        File.Delete(path);
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock);

        var result = executor.Execute(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);

        Assert.Equal(ExecutionItemOutcome.NotFound, Assert.Single(result.Items).Outcome);
        Assert.Equal(ExecutionState.Completed, result.State);
    }

    [Fact]
    public void CancellationBeforeStartProducesCancelledStateWithNoDeletions()
    {
        WriteStale("old1.tmp");
        var path = Path.Combine(root, "old1.tmp");
        var (plan, tokenService) = BuildPlan();
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = executor.Execute(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, cts.Token);

        Assert.Equal(ExecutionState.Cancelled, result.State);
        Assert.Empty(result.Items);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void HighRiskOrNonBuiltInItemsNeverExecuteEvenIfSelected()
    {
        WriteStale("old1.tmp");
        var builtIn = ClyrOwnedTempArtifactScanner.Scan(clock, root)!;
        var manualReview = PlanningFixtures.Candidate("manual", 100) with { Eligibility = CleanupEligibility.DryRunEligible };
        var (plan, _) = BuildPlan(candidates: [builtIn, manualReview], selected: [builtIn.FindingId, manualReview.FindingId]);
        var manualItem = plan.Items.Single(item => item.FindingId == manualReview.FindingId);

        var eligibility = ExecutionEligibilityValidator.ValidateItemForExecution(manualItem);

        Assert.False(eligibility.IsSuccess);
        Assert.Equal("execution.risk", eligibility.Error!.Code);
    }

    private void WriteStale(string name)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, "stale scratch data");
        var old = clock.UtcNow - TimeSpan.FromDays(30);
        File.SetLastWriteTimeUtc(path, old.UtcDateTime);
        File.SetCreationTimeUtc(path, old.UtcDateTime);
    }

    private (CleanupPlan Plan, IExecutionTokenService TokenService) BuildPlan(CleanupCandidate? extra = null) =>
        BuildPlan(candidates: extra is null ? [ClyrOwnedTempArtifactScanner.Scan(clock, root)!] : [extra],
            selected: extra is null ? [ClyrOwnedTempArtifactScanner.Scan(clock, root)!.FindingId] : [extra.FindingId]);

    private (CleanupPlan Plan, IExecutionTokenService TokenService) BuildPlan(IReadOnlyList<CleanupCandidate> candidates, IReadOnlyList<string> selected)
    {
        var plan = CleanupPlanBuilder.Create(new(Guid.NewGuid(), null, "drive-fixture", "clyr.builtin", "1.1.0",
            "pack-digest", "test", "support-safe", clock.UtcNow, candidates, selected));
        return (plan, new ExecutionTokenService());
    }

    private static CleanupCandidate ManualCandidateFor(string path, bool reparse, long size, DateTimeOffset lastWrite)
    {
        var capability = BuiltInExecutionActions.ClyrOwnedTempArtifacts;
        var action = new ActionDefinition(CleanupActionType.TrustedBuiltInCleanup, 1, capability.ActionId, "1",
            "builtin-1", capability.TrustedRootIdentity, "canonical-component-containment-v1", false,
            RollbackCapability.None, ["removed"], ["approved-root"], ExecutionAvailability.Phase6BuiltInExecutable,
            capability.Explanation, RiskLevel.Low, FindingConfidence.High, TimeSpan.FromMinutes(10));
        var consequence = new CleanupConsequence("scratch", "scratch", "removed", true, "none", "none", "none", "none", "none");
        var target = new CleanupTarget("target-1", capability.TrustedRootIdentity, "<redacted>", path, "C:",
            null, size, lastWrite, lastWrite, "Normal", reparse, false, TargetState.Observed);
        return new("manual:target", "Manual target", StorageCategory.TemporaryFiles, CleanupEligibility.DryRunEligible,
            "test", action, new(1, size, null, "test"), RiskLevel.Low, FindingConfidence.High, consequence, [target]);
    }

    private sealed class MutableClock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = start;
        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
