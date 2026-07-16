using System.Collections.Immutable;
using Clyr.Contracts;

namespace Clyr.Core.Execution;

public sealed record ExecutionOutcome(ExecutionState State, ImmutableArray<ExecutionItemResult> Items, ExecutionReceipt Receipt);

/// <summary>
/// Executes only exact, already-planned targets belonging to the Phase 6 built-in allowlist. Views and view models
/// never call deletion APIs directly — this is the single narrow surface that does. Every target is independently
/// re-probed on disk immediately before deletion; nothing about the plan's original observation is trusted at
/// execution time. This type never launches a child process and never elevates.
/// </summary>
public sealed class NonElevatedCleanupExecutor(IExecutionTokenService tokenService, IClock clock)
{
    public ExecutionOutcome Execute(CleanupPlan plan, IReadOnlyList<string> selectedItemIds, ExecutionToken token,
        ExecutionSessionId sessionId, string windowsUserSid, string applicationVersion,
        string? trustedRootOverride, CancellationToken cancellationToken)
    {
        var startedAtUtc = clock.UtcNow;
        var executionId = new ExecutionId(Guid.NewGuid());

        var validated = tokenService.Validate(token, plan, sessionId, windowsUserSid, startedAtUtc);
        if (!validated.IsSuccess)
            return Rejected(executionId, plan, startedAtUtc, applicationVersion, validated.Error!.Message);

        if (!string.Equals(plan.Digest, CleanupPlanCanonicalizer.Digest(plan), StringComparison.Ordinal))
            return Rejected(executionId, plan, startedAtUtc, applicationVersion, "The plan digest no longer matches its contents.");
        if (plan.Expiry.IsExpired(startedAtUtc))
            return Rejected(executionId, plan, startedAtUtc, applicationVersion, "The plan has expired.");
        if (!tokenService.Consume(token.TokenId))
            return Rejected(executionId, plan, startedAtUtc, applicationVersion, "The execution token has already been used.");

        var trustedRoot = trustedRootOverride ?? ClyrOwnedTempArtifactScanner.ResolveTrustedRoot();
        var freeBefore = TryGetFreeBytes(trustedRoot);

        var orderedItems = plan.Items
            .Where(item => selectedItemIds.Contains(item.ItemId, StringComparer.Ordinal))
            .OrderBy(item => item.ItemId, StringComparer.Ordinal)
            .ToImmutableArray();

        var results = ImmutableArray.CreateBuilder<ExecutionItemResult>();
        var cancelled = false;

        foreach (var item in orderedItems)
        {
            if (cancellationToken.IsCancellationRequested) { cancelled = true; break; }

            var eligible = ExecutionEligibilityValidator.ValidateItemForExecution(item);
            if (!eligible.IsSuccess)
            {
                foreach (var target in item.Targets)
                    results.Add(new(item.ItemId, target.TargetId, ExecutionItemOutcome.SkippedProtected,
                        eligible.Error!.Code, eligible.Error.Message, null));
                continue;
            }
            var capability = eligible.Value!;

            foreach (var target in item.Targets)
            {
                if (cancellationToken.IsCancellationRequested) { cancelled = true; break; }
                results.Add(ExecuteTarget(item.ItemId, target, capability, trustedRoot));
            }
            if (cancelled) break;
        }

        var freeAfter = TryGetFreeBytes(trustedRoot);
        var summary = Summarize(results, plan.Items);
        var finalState = DetermineState(cancelled, orderedItems.Length, results, summary);

        var receipt = BuildReceipt(executionId, plan, applicationVersion, startedAtUtc, clock.UtcNow, finalState,
            cancelled, summary, freeBefore, freeAfter, results);

        return new ExecutionOutcome(finalState, results.ToImmutable(), receipt);
    }

    private ExecutionItemResult ExecuteTarget(string itemId, CleanupTarget target, ExecutionCapability capability, string trustedRoot)
    {
        var validation = WindowsPathSafetyValidator.Validate(target.CanonicalPath ?? string.Empty, trustedRoot, target.IsReparsePoint);
        if (!validation.IsValid)
        {
            var outcome = validation.Code switch
            {
                "path.reparse" => ExecutionItemOutcome.SkippedReparsePoint,
                _ when validation.IsProtected => ExecutionItemOutcome.SkippedProtected,
                _ => ExecutionItemOutcome.SkippedOutsideApprovedRoot
            };
            return new(itemId, target.TargetId, outcome, validation.Code, validation.Message, null);
        }

        var path = validation.CanonicalPath!;
        if (!File.Exists(path))
            return new(itemId, target.TargetId, ExecutionItemOutcome.NotFound, "target.not-found", "The target no longer exists.", null);

        FileInfo info;
        try { info = new FileInfo(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(itemId, target.TargetId, ExecutionItemOutcome.SkippedAccessDenied, "target.probe-failed", "The target could not be re-probed.", null);
        }

        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            return new(itemId, target.TargetId, ExecutionItemOutcome.SkippedReparsePoint, "target.reparse", "The target became a reparse point.", null);
        if (IsCloudPlaceholder(info.Attributes))
            return new(itemId, target.TargetId, ExecutionItemOutcome.SkippedCloudPlaceholder, "target.cloud-placeholder", "The target became a cloud placeholder.", null);

        var stillStale = clock.UtcNow - info.LastWriteTimeUtc >= capability.MinimumAge;
        var identityMatches = info.Length == target.LogicalBytes && info.LastWriteTimeUtc == target.LastWriteAtUtc;
        if (!identityMatches || !stillStale)
            return new(itemId, target.TargetId, ExecutionItemOutcome.SkippedChanged, "target.changed", "The target's identity or age no longer matches the validated plan.", null);

        try
        {
            File.Delete(path);
            return new(itemId, target.TargetId, ExecutionItemOutcome.Removed, "target.removed", "The target was removed.", info.Length);
        }
        catch (UnauthorizedAccessException)
        {
            return new(itemId, target.TargetId, ExecutionItemOutcome.SkippedAccessDenied, "target.access-denied", "The target could not be removed without elevated or forced access.", null);
        }
        catch (IOException)
        {
            return new(itemId, target.TargetId, ExecutionItemOutcome.SkippedLocked, "target.locked", "The target is in use or locked.", null);
        }
        catch (Exception ex)
        {
            return new(itemId, target.TargetId, ExecutionItemOutcome.Failed, "target.failed", ex.GetType().Name, null);
        }
    }

    private static bool IsCloudPlaceholder(FileAttributes attributes)
    {
        const FileAttributes recallOnOpen = (FileAttributes)0x00040000;
        const FileAttributes recallOnDataAccess = (FileAttributes)0x00400000;
        return (attributes & (FileAttributes.Offline | recallOnOpen | recallOnDataAccess)) != 0;
    }

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

    private ExecutionOutcome Rejected(ExecutionId executionId, CleanupPlan plan, DateTimeOffset startedAtUtc, string applicationVersion, string reason)
    {
        var summary = ExecutionSummary.Empty;
        var receipt = BuildReceipt(executionId, plan, applicationVersion, startedAtUtc, clock.UtcNow, ExecutionState.Rejected,
            false, summary, null, null, ImmutableArray<ExecutionItemResult>.Empty.ToBuilder(), [reason]);
        return new(ExecutionState.Rejected, ImmutableArray<ExecutionItemResult>.Empty, receipt);
    }

    private static ExecutionReceipt BuildReceipt(ExecutionId executionId, CleanupPlan plan, string applicationVersion,
        DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc, ExecutionState finalState, bool cancelled,
        ExecutionSummary summary, long? freeBefore, long? freeAfter, ImmutableArray<ExecutionItemResult>.Builder results,
        ImmutableArray<string>? extraWarnings = null)
    {
        var categories = results.GroupBy(result => result.Outcome.ToString())
            .ToImmutableDictionary(group => group.Key, group => group.Count());
        var warnings = new List<string>
        {
            "Removed logical bytes and observed free-space change are reported separately; other processes may change free space concurrently.",
            "This receipt does not include unrestricted raw file paths."
        };
        if (extraWarnings is not null) warnings.AddRange(extraWarnings);
        var digestReceipt = new ExecutionReceipt(1, executionId, plan.Id, plan.Digest, applicationVersion,
            plan.Binding.SourceRulePackVersion, PrivacyFingerprint(plan.Binding.DriveIdentity), startedAtUtc,
            completedAtUtc, finalState, cancelled, false, summary, freeBefore, freeAfter,
            freeBefore.HasValue && freeAfter.HasValue ? freeAfter - freeBefore : null,
            categories, [.. warnings],
            ["Physical/allocated bytes are not reported; only validated logical bytes.",
             "Hard-link reference counting is not independently verified in this executor."],
            plan.Binding.PrivacyMode, string.Empty);
        var digest = ExecutionReceiptCanonicalizer.Digest(digestReceipt);
        return new ExecutionReceipt(1, executionId, plan.Id, plan.Digest, applicationVersion,
            plan.Binding.SourceRulePackVersion, PrivacyFingerprint(plan.Binding.DriveIdentity), startedAtUtc,
            completedAtUtc, finalState, cancelled, false, summary, freeBefore, freeAfter,
            freeBefore.HasValue && freeAfter.HasValue ? freeAfter - freeBefore : null,
            categories, [.. warnings],
            ["Physical/allocated bytes are not reported; only validated logical bytes.",
             "Hard-link reference counting is not independently verified in this executor."],
            plan.Binding.PrivacyMode, digest);
    }

    private static string PrivacyFingerprint(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
