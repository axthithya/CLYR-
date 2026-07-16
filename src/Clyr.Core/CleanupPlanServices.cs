using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clyr.Contracts;

namespace Clyr.Core;

public sealed class CleanupDryRunResolver
{
    public static DryRunResult Resolve(CleanupPlan plan, PlanValidationResult validation)
    {
        var items = plan.Items.Select(item => new DryRunItemResult(
            item.ItemId, item.Action.ActionType, item.Impact,
            item.Targets.Select(target => target.DisplayLocation).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray(),
            item.Targets.Count(target => target.State == TargetState.Inaccessible),
            item.Targets.Count(target => target.State == TargetState.Protected),
            item.Targets.Count(target => target.IsCloudPlaceholder),
            item.Targets.Count(target => target.State == TargetState.Changed),
            item.Targets.Count(target => target.State == TargetState.Missing),
            item.Action.ActionType == CleanupActionType.ReviewFiles
                || item.Targets.Any(target => target.State == TargetState.ReviewRequired),
            item.Action.RequiresElevation, item.Action.Rollback,
            ExecutionAvailability.ExecutionNotAvailableInPhase5)).ToImmutableArray();
        return new(plan.Id, validation.Status, plan.TotalImpact, items,
            ["Potential logical bytes affected are not guaranteed recovered space.",
             "Files can change; allocation, hard links, compression, placeholders, locks, metadata, and cache recreation are not fully predictable."],
            ExecutionAvailability.ExecutionNotAvailableInPhase5);
    }
}

public interface ICleanupPlanStore
{
    void Save(CleanupPlan plan);
    CleanupPlan? Find(CleanupPlanId id);
    bool Discard(CleanupPlanId id);
}

public sealed class InMemoryCleanupPlanStore : ICleanupPlanStore
{
    private const int Capacity = 16;
    private readonly Dictionary<CleanupPlanId, CleanupPlan> plans = [];
    private readonly Queue<CleanupPlanId> order = [];
    public void Save(CleanupPlan plan)
    {
        if (plans.ContainsKey(plan.Id)) return;
        plans.Add(plan.Id, plan);
        order.Enqueue(plan.Id);
        while (order.Count > Capacity) plans.Remove(order.Dequeue());
    }
    public CleanupPlan? Find(CleanupPlanId id) => plans.GetValueOrDefault(id);
    public bool Discard(CleanupPlanId id) => plans.Remove(id);
}

public interface ICleanupExecutor
{
    ValueTask<CleanupExecutionResult> GetAvailabilityAsync(CleanupPlan plan, CancellationToken cancellationToken = default);
}

public sealed class PhaseFiveDisabledCleanupExecutor : ICleanupExecutor
{
    public ValueTask<CleanupExecutionResult> GetAvailabilityAsync(CleanupPlan plan, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new CleanupExecutionResult(false, "ExecutionNotAvailableInPhase5",
            "Dry-run only — no files will be changed. Execution is not available in Phase 5.",
            ExecutionAvailability.ExecutionNotAvailableInPhase5));
}

public sealed class CleanupPlanReportExporter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Serialize(CleanupPlan plan, PlanValidationResult validation)
    {
        var report = new
        {
            schemaVersion = 1,
            reportType = "clyr-cleanup-plan-dry-run",
            applicationVersion = plan.ApplicationVersion,
            planId = plan.Id.ToString(),
            digest = plan.Digest,
            createdAtUtc = plan.Expiry.CreatedAtUtc,
            expiresAtUtc = plan.Expiry.ExpiresAtUtc,
            sourceSnapshotId = plan.Binding.SourceSnapshotId,
            sourceDriveFingerprint = PrivacyFingerprint(plan.Binding.DriveIdentity),
            rulePackVersion = plan.Binding.SourceRulePackVersion,
            candidates = plan.Items.Select(item => new
            {
                item.FindingId,
                item.Title,
                eligibility = item.Eligibility.ToString(),
                risk = item.Risk.ToString(),
                confidence = item.Confidence.ToString(),
                logicalBytes = item.Impact.ObservedLogicalBytes,
                itemCount = item.Impact.ItemCount,
                uncertainty = item.Impact.Uncertainty,
                item.Consequence,
                rollbackCapability = item.Action.Rollback.ToString(),
                affectedRoot = item.Action.AllowedRootIdentity
            }),
            protectedValidation = validation.ProtectedPathViolations.Length == 0 ? "passed" : "failed",
            staleStatus = validation.Status.ToString(),
            executionAvailability = plan.ExecutionAvailability.ToString(),
            privacyMode = plan.Binding.PrivacyMode,
            limitations = plan.Warnings,
            rawPathsIncluded = false,
            userNamesIncluded = false,
            fileContentsIncluded = false
        };
        return JsonSerializer.Serialize(report, Options);
    }

    public static bool Validate(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > 4_194_304) return false;
        try
        {
            using var document = JsonDocument.Parse(json,
                new() { MaxDepth = 32, CommentHandling = JsonCommentHandling.Disallow });
            var root = document.RootElement;
            return root.TryGetProperty("schemaVersion", out var schema) && schema.GetInt32() == 1
                && root.TryGetProperty("reportType", out var type)
                && type.GetString() == "clyr-cleanup-plan-dry-run"
                && root.TryGetProperty("digest", out var digest) && digest.GetString()?.Length == 64
                && root.TryGetProperty("executionAvailability", out var availability)
                && availability.GetString() == nameof(ExecutionAvailability.ExecutionNotAvailableInPhase5)
                && root.TryGetProperty("rawPathsIncluded", out var paths) && !paths.GetBoolean();
        }
        catch (JsonException) { return false; }
    }

    private static string PrivacyFingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

