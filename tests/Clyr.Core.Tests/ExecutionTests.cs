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
    public async Task HappyPathRemovesExactStaleFilesAndProducesVerifiableReceipt()
    {
        WriteStale("old1.tmp");
        WriteStale("old2.tmp");
        var freshPath = Path.Combine(root, "fresh.tmp");
        File.WriteAllText(freshPath, "recent");

        var (plan, tokenService, receiptStore) = BuildPlan();
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock, receiptStore);

        var outcome = await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);

        Assert.Equal(ExecutionState.Completed, outcome.State);
        Assert.Equal(2, outcome.Items.Length);
        Assert.All(outcome.Items, item => Assert.Equal(ExecutionItemOutcome.Removed, item.Outcome));
        Assert.Equal(2, outcome.Receipt.Summary.RemovedCount);
        Assert.True(File.Exists(freshPath));
        Assert.False(Directory.EnumerateFiles(root, "old*").Any());
        Assert.Equal(64, outcome.Receipt.Digest.Length);
        Assert.Equal(outcome.Receipt.Digest, ExecutionReceiptCanonicalizer.Digest(outcome.Receipt));
        Assert.Equal(1, receiptStore.BeginCalls);
        Assert.Equal(1, receiptStore.CompleteCalls);
    }

    [Fact]
    public async Task TokenCannotBeReplayedAfterConsumption()
    {
        WriteStale("old1.tmp");
        var (plan, tokenService, receiptStore) = BuildPlan();
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock, receiptStore);

        var first = await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);
        Assert.Equal(ExecutionState.Completed, first.State);

        var replay = await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);
        Assert.Equal(ExecutionState.Rejected, replay.State);
        Assert.Empty(replay.Items);
    }

    [Fact]
    public async Task ExpiredTokenIsRejected()
    {
        WriteStale("old1.tmp");
        var (plan, tokenService, receiptStore) = BuildPlan();
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        clock.Advance(TimeSpan.FromMinutes(5));
        var executor = new NonElevatedCleanupExecutor(tokenService, clock, receiptStore);

        var result = await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);
        Assert.Equal(ExecutionState.Rejected, result.State);
        Assert.True(File.Exists(Path.Combine(root, "old1.tmp")));
        Assert.Equal(0, receiptStore.BeginCalls);
    }

    [Fact]
    public async Task TamperedPlanDigestIsRejected()
    {
        WriteStale("old1.tmp");
        var (plan, tokenService, receiptStore) = BuildPlan();
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var tampered = new CleanupPlan(plan.SchemaVersion, plan.Id, plan.ApplicationVersion, plan.Binding, plan.Expiry,
            plan.Items, plan.TotalImpact, plan.Risk, plan.Confidence, plan.Warnings, plan.ExecutionAvailability, new string('0', 64));
        var executor = new NonElevatedCleanupExecutor(tokenService, clock, receiptStore);

        var result = await executor.ExecuteAsync(tampered, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);
        Assert.Equal(ExecutionState.Rejected, result.State);
        Assert.True(File.Exists(Path.Combine(root, "old1.tmp")));
    }

    [Fact]
    public async Task TokenBoundToWrongSessionOrUserIsRejected()
    {
        WriteStale("old1.tmp");
        var (plan, tokenService, receiptStore) = BuildPlan();
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock, receiptStore);

        var wrongSession = await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, new ExecutionSessionId(Guid.NewGuid()), UserSid, "test-app-1", root, CancellationToken.None);
        Assert.Equal(ExecutionState.Rejected, wrongSession.State);

        var wrongUser = await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, sessionId, "S-1-5-21-9-9-9-9999", "test-app-1", root, CancellationToken.None);
        Assert.Equal(ExecutionState.Rejected, wrongUser.State);
        Assert.True(File.Exists(Path.Combine(root, "old1.tmp")));
    }

    [Fact]
    public async Task TargetOutsideApprovedRootIsSkippedNotDeleted()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), "clyr-outside-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(outsidePath, "should never be touched");
        try
        {
            var candidate = ManualCandidateFor(outsidePath, reparse: false, size: new FileInfo(outsidePath).Length,
                lastWrite: clock.UtcNow - TimeSpan.FromDays(30));
            var (plan, tokenService, receiptStore) = BuildPlan(candidate);
            var sessionId = new ExecutionSessionId(Guid.NewGuid());
            var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
            var executor = new NonElevatedCleanupExecutor(tokenService, clock, receiptStore);

            var result = await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);

            Assert.Equal(ExecutionItemOutcome.SkippedOutsideApprovedRoot, Assert.Single(result.Items).Outcome);
            Assert.True(File.Exists(outsidePath));
        }
        finally { File.Delete(outsidePath); }
    }

    [Fact]
    public async Task TargetClaimingReparseAtPlanTimeIsSkipped()
    {
        WriteStale("old1.tmp");
        var path = Path.Combine(root, "old1.tmp");
        var candidate = ManualCandidateFor(path, reparse: true, size: new FileInfo(path).Length, lastWrite: clock.UtcNow - TimeSpan.FromDays(30));
        var (plan, tokenService, receiptStore) = BuildPlan(candidate);
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock, receiptStore);

        var result = await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);

        Assert.Equal(ExecutionItemOutcome.SkippedReparsePoint, Assert.Single(result.Items).Outcome);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task TargetChangedAfterPlanningIsSkippedNotForced()
    {
        WriteStale("old1.tmp");
        var path = Path.Combine(root, "old1.tmp");
        var (plan, tokenService, receiptStore) = BuildPlan();
        File.WriteAllText(path, "content changed after the plan was created");
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock, receiptStore);

        var result = await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);

        Assert.Equal(ExecutionItemOutcome.SkippedChanged, Assert.Single(result.Items).Outcome);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task MissingTargetReportsNotFoundRatherThanFailing()
    {
        WriteStale("old1.tmp");
        var path = Path.Combine(root, "old1.tmp");
        var (plan, tokenService, receiptStore) = BuildPlan();
        File.Delete(path);
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock, receiptStore);

        var result = await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);

        Assert.Equal(ExecutionItemOutcome.NotFound, Assert.Single(result.Items).Outcome);
        Assert.Equal(ExecutionState.Completed, result.State);
    }

    [Fact]
    public async Task CancellationBeforeStartProducesCancelledStateWithNoDeletions()
    {
        WriteStale("old1.tmp");
        var path = Path.Combine(root, "old1.tmp");
        var (plan, tokenService, receiptStore) = BuildPlan();
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock, receiptStore);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, cts.Token);

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
        var (plan, _, _) = BuildPlan(candidates: [builtIn, manualReview], selected: [builtIn.FindingId, manualReview.FindingId]);
        var manualItem = plan.Items.Single(item => item.FindingId == manualReview.FindingId);

        var eligibility = ExecutionEligibilityValidator.ValidateItemForExecution(manualItem);

        Assert.False(eligibility.IsSuccess);
        Assert.Equal("execution.risk", eligibility.Error!.Code);
    }

    [Fact]
    public async Task StartedIsDurablyStoredBeforeTheFirstMutation()
    {
        WriteStale("old1.tmp");
        var path = Path.Combine(root, "old1.tmp");
        var (plan, tokenService, receiptStore) = BuildPlan();
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        // Observes the store's state from inside Begin, before ExecuteAsync's mutation loop has run at all.
        receiptStore.OnBegin = () => Assert.True(File.Exists(path));
        var executor = new NonElevatedCleanupExecutor(tokenService, clock, receiptStore);

        var outcome = await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);

        Assert.Equal(ExecutionState.Completed, outcome.State);
        var stored = await receiptStore.GetAsync(outcome.Receipt.ExecutionId);
        Assert.NotNull(stored);
        Assert.Equal(outcome.Receipt.ExecutionId, stored!.ExecutionId);
    }

    [Fact]
    public async Task StartStoreFailureCausesZeroMutationsAndNoTokenReplay()
    {
        WriteStale("old1.tmp");
        var path = Path.Combine(root, "old1.tmp");
        var (plan, tokenService, receiptStore) = BuildPlan();
        receiptStore.ThrowOnBegin = true;
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock, receiptStore);

        var outcome = await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);

        Assert.Equal(ExecutionState.Rejected, outcome.State);
        Assert.True(File.Exists(path), "Nothing may be deleted when the durable start record could not be written.");
        Assert.Empty(receiptStore.StoredExecutionIds);

        // The token was already burned (single-use by design) — a second attempt with it must also fail, never
        // silently retry the same authorization.
        receiptStore.ThrowOnBegin = false;
        var retry = await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);
        Assert.Equal(ExecutionState.Rejected, retry.State);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task SameExecutionIdIsUsedFromStartThroughCompletion()
    {
        WriteStale("old1.tmp");
        var (plan, tokenService, receiptStore) = BuildPlan();
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock, receiptStore);

        var outcome = await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);

        var beginId = Assert.Single(receiptStore.BeginExecutionIds);
        var completeId = Assert.Single(receiptStore.CompleteExecutionIds);
        Assert.Equal(beginId, completeId);
        Assert.Equal(beginId, outcome.Receipt.ExecutionId);
    }

    [Fact]
    public async Task CompletionPersistenceFailureKeepsTheTrueOutcomeAndWarnsRatherThanHidingIt()
    {
        WriteStale("old1.tmp");
        var (plan, tokenService, receiptStore) = BuildPlan();
        receiptStore.ThrowOnComplete = true;
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock, receiptStore);

        var outcome = await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);

        Assert.Equal(ExecutionState.Completed, outcome.State); // the true, on-disk outcome — never relabeled
        Assert.False(Directory.EnumerateFiles(root, "old*").Any()); // the mutation genuinely happened
        Assert.Contains(outcome.Receipt.Warnings, warning => warning.Contains("could not be durably recorded", StringComparison.Ordinal));
        var stored = await receiptStore.GetAsync(outcome.Receipt.ExecutionId);
        Assert.Equal(ExecutionState.Running, stored!.FinalState); // the Started row is exactly what remains
    }

    [Fact]
    public async Task SamePlanCannotBeDurablyReplayedAfterARestartEvenWithAFreshInMemoryExecutor()
    {
        WriteStale("old1.tmp");
        var (plan, tokenService, receiptStore) = BuildPlan();
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock, receiptStore);
        await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);

        // Simulates a restart: a brand-new token service and executor (no in-memory attempted-plan state at
        // all), reusing only the same durable receiptStore — as if it were the real on-disk database.
        var freshTokenService = new ExecutionTokenService();
        var freshExecutor = new NonElevatedCleanupExecutor(freshTokenService, clock, receiptStore);
        var freshToken = freshTokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);

        var replay = await freshExecutor.ExecuteAsync(plan, [plan.Items[0].ItemId], freshToken, sessionId, UserSid, "test-app-1", root, CancellationToken.None);

        Assert.Equal(ExecutionState.Rejected, replay.State);
    }

    [Fact]
    public async Task ANewPlanFromNewEvidenceCanStillExecuteNormally()
    {
        WriteStale("old1.tmp");
        var (plan, tokenService, receiptStore) = BuildPlan();
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock, receiptStore);
        var first = await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);
        Assert.Equal(ExecutionState.Completed, first.State);

        // A genuinely new analysis finding a genuinely new stale file — a fresh random PlanId and a different
        // digest — must never be durably blocked by the previous, unrelated plan's record.
        WriteStale("old2.tmp");
        var (secondPlan, secondTokenService, _) = BuildPlan();
        Assert.NotEqual(plan.Id, secondPlan.Id);
        var secondToken = secondTokenService.Issue(secondPlan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var secondExecutor = new NonElevatedCleanupExecutor(secondTokenService, clock, receiptStore);

        var second = await secondExecutor.ExecuteAsync(secondPlan, [secondPlan.Items[0].ItemId], secondToken, sessionId, UserSid, "test-app-1", root, CancellationToken.None);

        Assert.Equal(ExecutionState.Completed, second.State);
    }

    [Fact]
    public async Task NoRawPathIsAddedToTheStartRecord()
    {
        WriteStale("old1.tmp");
        var (plan, tokenService, receiptStore) = BuildPlan();
        var sessionId = new ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(plan, sessionId, UserSid, [BuiltInExecutionActions.ClyrOwnedTempArtifactsId], clock.UtcNow);
        var executor = new NonElevatedCleanupExecutor(tokenService, clock, receiptStore);

        var outcome = await executor.ExecuteAsync(plan, [plan.Items[0].ItemId], token, sessionId, UserSid, "test-app-1", root, CancellationToken.None);

        var started = receiptStore.FirstBeginReceipt;
        Assert.NotNull(started);
        Assert.DoesNotContain(root, started!.DriveIdentityFingerprint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(root, started.EvidenceStateId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(root, started.WindowsUserSidFingerprint, StringComparison.OrdinalIgnoreCase);
    }

    private void WriteStale(string name)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, "stale scratch data");
        var old = clock.UtcNow - TimeSpan.FromDays(30);
        File.SetLastWriteTimeUtc(path, old.UtcDateTime);
        File.SetCreationTimeUtc(path, old.UtcDateTime);
    }

    private (CleanupPlan Plan, IExecutionTokenService TokenService, FakeExecutionReceiptStore ReceiptStore) BuildPlan(CleanupCandidate? extra = null) =>
        BuildPlan(candidates: extra is null ? [ClyrOwnedTempArtifactScanner.Scan(clock, root)!] : [extra],
            selected: extra is null ? [ClyrOwnedTempArtifactScanner.Scan(clock, root)!.FindingId] : [extra.FindingId]);

    private (CleanupPlan Plan, IExecutionTokenService TokenService, FakeExecutionReceiptStore ReceiptStore) BuildPlan(IReadOnlyList<CleanupCandidate> candidates, IReadOnlyList<string> selected)
    {
        var plan = CleanupPlanBuilder.Create(new(Guid.NewGuid(), null, "drive-fixture", "clyr.builtin", "1.1.0",
            "pack-digest", "test", "support-safe", "evidence-fixture", clock.UtcNow, candidates, selected));
        return (plan, new ExecutionTokenService(), new FakeExecutionReceiptStore());
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

/// <summary>
/// Deterministic in-memory double for <see cref="IExecutionReceiptStore"/>, mirroring the real
/// <c>SqliteExecutionReceiptStore</c>'s safety contract exactly (fail-closed duplicate begin, fail-closed unknown
/// completion, identity-mismatch rejection, digest-based idempotent-vs-conflicting completion) so tests can
/// exercise <see cref="NonElevatedCleanupExecutor"/>'s crash-recovery behavior without any real filesystem or
/// SQLite dependency. <see cref="ThrowOnBegin"/>/<see cref="ThrowOnComplete"/> simulate a persistence failure at
/// exactly the two moments that matter: before the first mutation, and after the outcome is already known.
/// </summary>
internal sealed class FakeExecutionReceiptStore : IExecutionReceiptStore
{
    private static readonly HashSet<ExecutionState> TerminalStates =
    [
        ExecutionState.Completed, ExecutionState.PartiallyCompleted, ExecutionState.Cancelled,
        ExecutionState.Failed, ExecutionState.Interrupted, ExecutionState.UnknownOutcome, ExecutionState.Rejected
    ];
    private readonly Dictionary<ExecutionId, ExecutionReceipt> receipts = [];

    public bool ThrowOnBegin { get; set; }
    public bool ThrowOnComplete { get; set; }
    public Action? OnBegin { get; set; }
    public int BeginCalls { get; private set; }
    public int CompleteCalls { get; private set; }
    public List<ExecutionId> BeginExecutionIds { get; } = [];
    public List<ExecutionId> CompleteExecutionIds { get; } = [];
    public IReadOnlyCollection<ExecutionId> StoredExecutionIds => receipts.Keys;
    public ExecutionReceipt? FirstBeginReceipt { get; private set; }

    public Task BeginAsync(ExecutionReceipt startRecord, CancellationToken cancellationToken = default)
    {
        BeginCalls++;
        if (ThrowOnBegin) throw new ExecutionReceiptStoreException("receipt.database-error", "Simulated start-record persistence failure.", new IOException());
        if (!receipts.TryAdd(startRecord.ExecutionId, startRecord))
            throw new ExecutionReceiptStoreException("receipt.duplicate-begin", "An execution record already exists for this execution ID.", new InvalidOperationException());
        FirstBeginReceipt ??= startRecord;
        BeginExecutionIds.Add(startRecord.ExecutionId);
        OnBegin?.Invoke();
        return Task.CompletedTask;
    }

    public Task CompleteAsync(ExecutionId id, ExecutionReceipt finalReceipt, CancellationToken cancellationToken = default)
    {
        CompleteCalls++;
        if (ThrowOnComplete) throw new ExecutionReceiptStoreException("receipt.database-error", "Simulated terminal-record persistence failure.", new IOException());
        if (!id.Equals(finalReceipt.ExecutionId))
            throw new ExecutionReceiptStoreException("receipt.id-mismatch", "The completion target does not match the receipt's own execution ID.", new InvalidOperationException());
        if (!receipts.TryGetValue(id, out var stored))
            throw new ExecutionReceiptStoreException("receipt.unknown-execution", "No started execution record exists for this execution ID.", new InvalidOperationException());
        if (TerminalStates.Contains(stored.FinalState))
        {
            if (string.Equals(stored.Digest, finalReceipt.Digest, StringComparison.Ordinal)) return Task.CompletedTask;
            throw new ExecutionReceiptStoreException("receipt.immutable", "A terminal execution receipt cannot be overwritten.", new InvalidOperationException());
        }
        if (!SameStartIdentity(stored, finalReceipt))
            throw new ExecutionReceiptStoreException("receipt.completion-mismatch",
                "The completing receipt does not match the plan, scan, evidence, drive, session or user this execution started with.", new InvalidOperationException());
        receipts[id] = finalReceipt;
        CompleteExecutionIds.Add(id);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ExecutionReceiptSummary>> ListAsync(int limit = 50, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExecutionReceiptSummary>>(receipts.Values
            .OrderByDescending(receipt => receipt.StartedAtUtc).Take(limit)
            .Select(receipt => new ExecutionReceiptSummary(receipt.ExecutionId, receipt.SourcePlanId, receipt.StartedAtUtc,
                receipt.CompletedAtUtc, receipt.FinalState, receipt.Summary.RemovedCount, receipt.Summary.SkippedCount,
                receipt.Summary.FailedCount, receipt.Summary.RemovedLogicalBytes)).ToArray());

    public Task<ExecutionReceipt?> GetAsync(ExecutionId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(receipts.TryGetValue(id, out var receipt) ? receipt : null);

    public Task<bool> DiscardAsync(ExecutionId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(receipts.Remove(id));

    public Task<bool> HasRecordForPlanAsync(CleanupPlanId planId, string planDigest, CancellationToken cancellationToken = default) =>
        Task.FromResult(receipts.Values.Any(receipt => receipt.SourcePlanId.Equals(planId) || string.Equals(receipt.SourcePlanDigest, planDigest, StringComparison.Ordinal)));

    public Task<int> ReconcileInterruptedAsync(TimeSpan staleAfter, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var stale = receipts.Values.Where(receipt => !receipt.CompletedAtUtc.HasValue && !TerminalStates.Contains(receipt.FinalState)
            && receipt.StartedAtUtc <= nowUtc - staleAfter).ToArray();
        foreach (var receipt in stale) receipts[receipt.ExecutionId] = Interrupted(receipt, nowUtc);
        return Task.FromResult(stale.Length);
    }

    private static ExecutionReceipt Interrupted(ExecutionReceipt receipt, DateTimeOffset nowUtc) => new(
        receipt.SchemaVersion, receipt.ExecutionId, receipt.SourcePlanId, receipt.SourcePlanDigest, receipt.ApplicationVersion,
        receipt.RulePackVersion, receipt.DriveIdentityFingerprint, receipt.StartedAtUtc, nowUtc, ExecutionState.Interrupted,
        receipt.Cancelled, receipt.ElevationUsed, receipt.Summary, receipt.DriveFreeBytesBefore, receipt.DriveFreeBytesAfter,
        receipt.ObservedFreeSpaceDeltaBytes, receipt.OutcomeCategories, receipt.Warnings, receipt.Limitations,
        receipt.PrivacyMode, receipt.Digest, receipt.SourceScanId, receipt.EvidenceStateId, receipt.ActionIds,
        receipt.ExecutionSessionId, receipt.WindowsUserSidFingerprint);

    private static bool SameStartIdentity(ExecutionReceipt started, ExecutionReceipt completing) =>
        started.SourcePlanId.Equals(completing.SourcePlanId)
        && string.Equals(started.SourcePlanDigest, completing.SourcePlanDigest, StringComparison.Ordinal)
        && started.SourceScanId == completing.SourceScanId
        && string.Equals(started.EvidenceStateId, completing.EvidenceStateId, StringComparison.Ordinal)
        && started.ExecutionSessionId == completing.ExecutionSessionId
        && string.Equals(started.WindowsUserSidFingerprint, completing.WindowsUserSidFingerprint, StringComparison.Ordinal);
}
