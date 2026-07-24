using System.Collections.Immutable;
using Clyr.Contracts;

namespace Clyr.Core.Execution;

public sealed record ExecutionOutcome(ExecutionState State, ImmutableArray<ExecutionItemResult> Items, ExecutionReceipt Receipt);

/// <summary>
/// Executes only exact, already-planned targets belonging to the Phase 6 built-in allowlist. Views and view models
/// never call deletion APIs directly — this is the single narrow surface that does. Every target is independently
/// re-probed on disk immediately before deletion; nothing about the plan's original observation is trusted at
/// execution time. This type never launches a child process and never elevates.
/// <para/>
/// Crash-recovery correction: a durable "Started" record (see <see cref="IExecutionReceiptStore.BeginAsync"/>) is
/// written — using the same <see cref="ExecutionId"/> the terminal receipt will later complete — before any
/// mutation can occur, and only after the authorization token is validated and consumed. If that durable write
/// fails, execution stops immediately: nothing is deleted, and the caller sees a safe rejection rather than a
/// crash-and-hope. If persisting the terminal record afterward fails, the already-true outcome is still returned
/// (nothing about what actually happened on disk is hidden), but with a warning that the durable trail could not
/// be completed — the Started row remains, exactly as an interrupted execution would look, until a future launch's
/// startup reconciliation (<see cref="IExecutionReceiptStore.ReconcileInterruptedAsync"/>) resolves it.
/// </summary>
public sealed class NonElevatedCleanupExecutor(IExecutionTokenService tokenService, IClock clock, IExecutionReceiptStore receiptStore)
{
    public async Task<ExecutionOutcome> ExecuteAsync(CleanupPlan plan, IReadOnlyList<string> selectedItemIds, ExecutionToken token,
        ExecutionSessionId sessionId, string windowsUserSid, string applicationVersion,
        string? trustedRootOverride, CancellationToken cancellationToken, IProgress<ExecutionItemResult>? progress = null)
    {
        var startedAtUtc = clock.UtcNow;
        var rejectionId = new ExecutionId(Guid.NewGuid());

        var validated = tokenService.Validate(token, plan, sessionId, windowsUserSid, startedAtUtc);
        if (!validated.IsSuccess)
            return Rejected(rejectionId, plan, sessionId, windowsUserSid, startedAtUtc, applicationVersion, validated.Error!.Message);

        if (!string.Equals(plan.Digest, CleanupPlanCanonicalizer.Digest(plan), StringComparison.Ordinal))
            return Rejected(rejectionId, plan, sessionId, windowsUserSid, startedAtUtc, applicationVersion, "The plan digest no longer matches its contents.");
        if (plan.Expiry.IsExpired(startedAtUtc))
            return Rejected(rejectionId, plan, sessionId, windowsUserSid, startedAtUtc, applicationVersion, "The plan has expired.");

        // Durable replay protection: even across a restart (which clears every in-memory attempted-plan guard),
        // the exact same plan identity or digest must never be presented to the executor a second time.
        if (await receiptStore.HasRecordForPlanAsync(plan.Id, plan.Digest, cancellationToken).ConfigureAwait(false))
            return Rejected(rejectionId, plan, sessionId, windowsUserSid, startedAtUtc, applicationVersion, "This plan has already been used for an execution attempt.");

        if (!tokenService.Consume(token.TokenId))
            return Rejected(rejectionId, plan, sessionId, windowsUserSid, startedAtUtc, applicationVersion, "The execution token has already been used.");

        // The token is now single-use consumed — it can never authorize a second attempt, whatever happens next.
        var executionId = new ExecutionId(Guid.NewGuid());
        var orderedItems = plan.Items
            .Where(item => selectedItemIds.Contains(item.ItemId, StringComparer.Ordinal))
            .OrderBy(item => item.ItemId, StringComparer.Ordinal)
            .ToImmutableArray();
        var approvedSummary = new ExecutionSummary(orderedItems.Length, 0, 0, 0,
            orderedItems.Sum(item => item.Targets.Sum(target => target.LogicalBytes)), 0, 0, 0);
        var startReceipt = BuildReceipt(executionId, plan, sessionId, windowsUserSid, applicationVersion, startedAtUtc,
            null, ExecutionState.Running, false, approvedSummary, null, null,
            ImmutableArray<ExecutionItemResult>.Empty.ToBuilder());

        try
        {
            await receiptStore.BeginAsync(startReceipt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // No durable proof this execution began can be written — refuse to mutate anything. The token is
            // already burned (one-time by design); the user must build and confirm a fresh plan to try again,
            // never an automatic retry of this one.
            return Rejected(executionId, plan, sessionId, windowsUserSid, startedAtUtc, applicationVersion,
                "CLYR could not durably record that this execution was starting, so no files were changed.");
        }

        var trustedRoot = trustedRootOverride ?? ClyrOwnedTempArtifactScanner.ResolveTrustedRoot();
        var freeBefore = TryGetFreeBytes(trustedRoot);

        var results = ImmutableArray.CreateBuilder<ExecutionItemResult>();
        var cancelled = false;

        foreach (var item in orderedItems)
        {
            if (cancellationToken.IsCancellationRequested) { cancelled = true; break; }

            var eligible = ExecutionEligibilityValidator.ValidateItemForExecution(item);
            if (!eligible.IsSuccess)
            {
                foreach (var target in item.Targets)
                {
                    var skipped = new ExecutionItemResult(item.ItemId, target.TargetId, ExecutionItemOutcome.SkippedProtected,
                        eligible.Error!.Code, eligible.Error.Message, null);
                    results.Add(skipped);
                    progress?.Report(skipped);
                }
                continue;
            }
            var capability = eligible.Value!;

            foreach (var target in item.Targets)
            {
                if (cancellationToken.IsCancellationRequested) { cancelled = true; break; }
                var result = ExecuteTarget(item.ItemId, target, capability, trustedRoot);
                results.Add(result);
                progress?.Report(result);
            }
            if (cancelled) break;
        }

        var freeAfter = TryGetFreeBytes(trustedRoot);
        var summary = Summarize(results, plan.Items);
        var finalState = DetermineState(cancelled, orderedItems.Length, results, summary);

        var receipt = BuildReceipt(executionId, plan, sessionId, windowsUserSid, applicationVersion, startedAtUtc,
            clock.UtcNow, finalState, cancelled, summary, freeBefore, freeAfter, results);

        try
        {
            await receiptStore.CompleteAsync(executionId, receipt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Mutation already happened and the Started record already durably proves that — preserved exactly
            // as-is for a future launch's reconciliation to find. The true outcome is still returned (never
            // silently relabeled), with an explicit warning that its terminal record could not be completed.
            receipt = AppendWarning(receipt,
                "The final outcome could not be durably recorded. CLYR will show this as an unresolved execution the next time it starts.");
            return new ExecutionOutcome(finalState, results.ToImmutable(), receipt);
        }

        return new ExecutionOutcome(finalState, results.ToImmutable(), receipt);
    }

    private ExecutionItemResult ExecuteTarget(string itemId, CleanupTarget target, ExecutionCapability capability, string trustedRoot) =>
        ExecutionTargetProcessor.Process(clock, itemId, target.TargetId, target.CanonicalPath, target.LogicalBytes,
            target.LastWriteAtUtc, target.IsReparsePoint, trustedRoot, capability.MinimumAge);

    private static long? TryGetFreeBytes(string root)
    {
        try { return new DriveInfo(Path.GetPathRoot(root) ?? root).AvailableFreeSpace; }
        catch (Exception ex) when (ex is IOException or ArgumentException or DriveNotFoundException) { return null; }
    }

    private static ExecutionSummary Summarize(ImmutableArray<ExecutionItemResult>.Builder results, ImmutableArray<CleanupPlanItem> planItems)
    {
        var byTarget = planItems.SelectMany(item => item.Targets.Select(target => (item.ItemId, target)))
            .ToDictionary(pair => (pair.ItemId, pair.target.TargetId), pair => pair.target.LogicalBytes);
        long removedBytes = 0, skippedBytes = 0, failedBytes = 0, plannedBytes = 0;
        int removed = 0, skipped = 0, failed = 0;
        foreach (var result in results)
        {
            var bytes = byTarget.GetValueOrDefault((result.ItemId, result.TargetId));
            plannedBytes += bytes;
            switch (result.Outcome)
            {
                case ExecutionItemOutcome.Removed: removed++; removedBytes += result.RemovedLogicalBytes ?? bytes; break;
                case ExecutionItemOutcome.Failed: failed++; failedBytes += bytes; break;
                default: skipped++; skippedBytes += bytes; break;
            }
        }
        return new(results.Count, removed, skipped, failed, plannedBytes, removedBytes, skippedBytes, failedBytes);
    }

    private static ExecutionState DetermineState(bool cancelled, int totalItems, ImmutableArray<ExecutionItemResult>.Builder results, ExecutionSummary summary)
    {
        if (cancelled) return summary.RemovedCount > 0 ? ExecutionState.PartiallyCompleted : ExecutionState.Cancelled;
        if (totalItems == 0) return ExecutionState.Completed;
        if (summary.FailedCount > 0 && summary.RemovedCount == 0 && summary.SkippedCount == 0) return ExecutionState.Failed;
        if (summary.FailedCount > 0 || (summary.SkippedCount > 0 && summary.RemovedCount > 0)) return ExecutionState.PartiallyCompleted;
        if (summary.RemovedCount == 0 && summary.SkippedCount > 0) return ExecutionState.Completed;
        return ExecutionState.Completed;
    }

    /// <summary>Every rejection reached before the token is successfully consumed — nothing has been authorized,
    /// so nothing is durably recorded; the ephemeral receipt returned here exists only to explain the refusal to
    /// the caller.</summary>
    private ExecutionOutcome Rejected(ExecutionId executionId, CleanupPlan plan, ExecutionSessionId sessionId,
        string windowsUserSid, DateTimeOffset startedAtUtc, string applicationVersion, string reason)
    {
        var summary = ExecutionSummary.Empty;
        var receipt = BuildReceipt(executionId, plan, sessionId, windowsUserSid, applicationVersion, startedAtUtc, clock.UtcNow,
            ExecutionState.Rejected, false, summary, null, null, ImmutableArray<ExecutionItemResult>.Empty.ToBuilder(), [reason]);
        return new(ExecutionState.Rejected, ImmutableArray<ExecutionItemResult>.Empty, receipt);
    }

    private static ExecutionReceipt BuildReceipt(ExecutionId executionId, CleanupPlan plan, ExecutionSessionId sessionId,
        string windowsUserSid, string applicationVersion, DateTimeOffset startedAtUtc, DateTimeOffset? completedAtUtc,
        ExecutionState finalState, bool cancelled, ExecutionSummary summary, long? freeBefore, long? freeAfter,
        ImmutableArray<ExecutionItemResult>.Builder results, ImmutableArray<string>? extraWarnings = null)
    {
        var categories = results.GroupBy(result => result.Outcome.ToString())
            .ToImmutableDictionary(group => group.Key, group => group.Count());
        var warnings = new List<string>
        {
            "Removed logical bytes and observed free-space change are reported separately; other processes may change free space concurrently.",
            "This receipt does not include unrestricted raw file paths."
        };
        if (extraWarnings is not null) warnings.AddRange(extraWarnings);
        var actionIds = plan.Items.Select(item => item.Action.SourceRuleId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        var driveFingerprint = PrivacyFingerprint(plan.Binding.DriveIdentity);
        var userFingerprint = PrivacyFingerprint(windowsUserSid);
        var digestReceipt = new ExecutionReceipt(1, executionId, plan.Id, plan.Digest, applicationVersion,
            plan.Binding.SourceRulePackVersion, driveFingerprint, startedAtUtc,
            completedAtUtc, finalState, cancelled, false, summary, freeBefore, freeAfter,
            freeBefore.HasValue && freeAfter.HasValue ? freeAfter - freeBefore : null,
            categories, [.. warnings],
            ["Physical/allocated bytes are not reported; only validated logical bytes.",
             "Hard-link reference counting is not independently verified in this executor."],
            plan.Binding.PrivacyMode, string.Empty, plan.Binding.SourceScanId, plan.Binding.EvidenceStateId,
            actionIds, sessionId.Value, userFingerprint);
        var digest = ExecutionReceiptCanonicalizer.Digest(digestReceipt);
        return new ExecutionReceipt(1, executionId, plan.Id, plan.Digest, applicationVersion,
            plan.Binding.SourceRulePackVersion, driveFingerprint, startedAtUtc,
            completedAtUtc, finalState, cancelled, false, summary, freeBefore, freeAfter,
            freeBefore.HasValue && freeAfter.HasValue ? freeAfter - freeBefore : null,
            categories, [.. warnings],
            ["Physical/allocated bytes are not reported; only validated logical bytes.",
             "Hard-link reference counting is not independently verified in this executor."],
            plan.Binding.PrivacyMode, digest, plan.Binding.SourceScanId, plan.Binding.EvidenceStateId,
            actionIds, sessionId.Value, userFingerprint);
    }

    private static ExecutionReceipt AppendWarning(ExecutionReceipt receipt, string warning) => new(
        receipt.SchemaVersion, receipt.ExecutionId, receipt.SourcePlanId, receipt.SourcePlanDigest, receipt.ApplicationVersion,
        receipt.RulePackVersion, receipt.DriveIdentityFingerprint, receipt.StartedAtUtc, receipt.CompletedAtUtc, receipt.FinalState,
        receipt.Cancelled, receipt.ElevationUsed, receipt.Summary, receipt.DriveFreeBytesBefore, receipt.DriveFreeBytesAfter,
        receipt.ObservedFreeSpaceDeltaBytes, receipt.OutcomeCategories, [.. receipt.Warnings, warning], receipt.Limitations,
        receipt.PrivacyMode, receipt.Digest, receipt.SourceScanId, receipt.EvidenceStateId, receipt.ActionIds,
        receipt.ExecutionSessionId, receipt.WindowsUserSidFingerprint);

    private static string PrivacyFingerprint(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
