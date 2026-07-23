using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Core.Execution;

namespace Clyr.Cli;

public sealed partial class CliApplication
{
    private static readonly JsonSerializerOptions PlanJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    private int Plans(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (snapshotStore is null || cleanupPlanStore is null)
        {
            error.WriteLine("plan.unavailable: Cleanup planning is unavailable.");
            return 3;
        }
        try
        {
            if (args.Count >= 2 && args[1] == "candidates") return PlanCandidates(args, output, error);
            if (args.Count >= 2 && args[1] == "create") return PlanCreate(args, output, error);
            if (args.Count >= 3 && args[1] == "show") return PlanShow(args, output, error);
            if (args.Count >= 3 && args[1] == "validate") return PlanValidate(args, output, error);
            if (args.Count >= 3 && args[1] == "export") return PlanExport(args, output, error);
            if (args.Count == 3 && args[1] == "discard") return PlanDiscard(args[2], output, error);
            if (args.Count >= 3 && args[1] == "execute") return PlanExecute(args, output, error);
            error.WriteLine("Usage: clyr plan candidates --snapshot <id> [--json] | create --snapshot <id> --finding <id> [--finding <id>] [--json] | show <plan-id> [--json] | validate <plan-id> [--json] | export <plan-id> --output <path> | discard <plan-id> | execute <plan-id> --confirm-digest <prefix> [--json]");
            return 2;
        }
        catch (InvalidOperationException exception)
        {
            error.WriteLine("plan.invalid: " + exception.Message);
            return 2;
        }
        catch (IOException exception)
        {
            error.WriteLine("plan.export-failed: " + redactor.Redact(exception.Message));
            return 3;
        }
    }

    private int PlanCandidates(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!TryOption(args, "--snapshot", out var value) || !Guid.TryParse(value, out var id))
            return Usage(error, "A valid --snapshot <id> is required.");
        var snapshot = snapshotStore!.GetAsync(id).GetAwaiter().GetResult();
        if (snapshot is null) return Missing(error, "Snapshot not found.");
        var candidates = CandidatesFor(snapshot);
        if (args.Contains("--json", StringComparer.Ordinal))
            output.WriteLine(JsonSerializer.Serialize(candidates.Select(SafeCandidate), PlanJson));
        else
            foreach (var item in candidates)
                output.WriteLine($"{item.FindingId} {item.Eligibility} {item.Risk} observed={item.Impact.ObservedLogicalBytes} {item.Title} — {item.EligibilityReason}");
        return 0;
    }

    private int PlanCreate(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!TryOption(args, "--snapshot", out var value) || !Guid.TryParse(value, out var id))
            return Usage(error, "A valid --snapshot <id> is required.");
        var findings = Options(args, "--finding");
        if (findings.Length == 0) return Usage(error, "At least one --finding <id> is required.");
        var snapshot = snapshotStore!.GetAsync(id).GetAwaiter().GetResult();
        if (snapshot is null) return Missing(error, "Snapshot not found.");
        var candidates = CandidatesFor(snapshot);
        var plan = CleanupPlanBuilder.Create(new(snapshot.ScanId, snapshot.Id, snapshot.Drive.Fingerprint,
            snapshot.RulePackId, snapshot.RulePackVersion, snapshot.RulePackDigest, version,
            "support-safe", EvidenceState.ForSnapshot(snapshot), DateTimeOffset.UtcNow, candidates, findings));
        cleanupPlanStore!.Save(plan);
        if (args.Contains("--json", StringComparer.Ordinal)) output.WriteLine(SafePlanJson(plan));
        else
        {
            output.WriteLine($"Integrity-checked cleanup plan {plan.Id}");
            output.WriteLine($"Digest: {plan.Digest}");
            output.WriteLine($"Potential logical bytes affected: {plan.TotalImpact.ObservedLogicalBytes}; items: {plan.TotalImpact.ItemCount}");
            var executable = plan.Items.Any(item => item.Action.ExecutionAvailability == ExecutionAvailability.Phase6BuiltInExecutable);
            output.WriteLine(executable
                ? "Status: dry-run plan. One or more items are eligible for Phase 6 built-in execution via 'plan execute'."
                : "Status: Dry-run only — no files will be changed. ExecutionNotAvailableInPhase5.");
        }
        return 0;
    }

    private int PlanShow(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!TryPlanId(args[2], out var id)) return Usage(error, "A valid plan ID is required.");
        var plan = cleanupPlanStore!.Find(id);
        if (plan is null) return Missing(error, "Plan not found or no longer held in this process.");
        output.WriteLine(SafePlanJson(plan));
        return 0;
    }

    private int PlanValidate(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!TryPlanId(args[2], out var id)) return Usage(error, "A valid plan ID is required.");
        var plan = cleanupPlanStore!.Find(id);
        if (plan is null) return Missing(error, "Plan not found or no longer held in this process.");
        var snapshot = plan.Binding.SourceSnapshotId.HasValue
            ? snapshotStore!.GetAsync(plan.Binding.SourceSnapshotId.Value).GetAwaiter().GetResult() : null;
        var result = Validate(plan, snapshot);
        if (args.Contains("--json", StringComparer.Ordinal)) output.WriteLine(JsonSerializer.Serialize(result, PlanJson));
        else output.WriteLine($"{result.Status}: digest={plan.Digest}; execution={plan.ExecutionAvailability}");
        foreach (var diagnostic in result.Diagnostics) error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
        return result.IsValid ? 0 : 1;
    }

    private int PlanExport(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!TryPlanId(args[2], out var id) || !TryOption(args, "--output", out var path))
            return Usage(error, "A valid plan ID and --output <path> are required.");
        var plan = cleanupPlanStore!.Find(id);
        if (plan is null) return Missing(error, "Plan not found or no longer held in this process.");
        var snapshot = plan.Binding.SourceSnapshotId.HasValue
            ? snapshotStore!.GetAsync(plan.Binding.SourceSnapshotId.Value).GetAwaiter().GetResult() : null;
        var report = CleanupPlanReportExporter.Serialize(plan, Validate(plan, snapshot));
        File.WriteAllText(path!, report);
        output.WriteLine($"Privacy-safe dry-run report exported. Digest: {plan.Digest}");
        return 0;
    }

    private int PlanDiscard(string value, TextWriter output, TextWriter error)
    {
        if (!TryPlanId(value, out var id)) return Usage(error, "A valid plan ID is required.");
        if (!cleanupPlanStore!.Discard(id)) return Missing(error, "Plan not found.");
        output.WriteLine("In-memory dry-run plan record discarded. No user or system file was changed.");
        return 0;
    }

    private static List<CleanupCandidate> CandidatesFor(StorageSnapshot snapshot)
    {
        var candidates = CleanupCandidateFactory.FromSnapshot(snapshot).ToList();
        var builtIn = ClyrOwnedTempArtifactScanner.Scan(new SystemClock());
        if (builtIn is not null) candidates.Add(builtIn);
        return candidates;
    }

    private static PlanValidationResult Validate(CleanupPlan plan, StorageSnapshot? snapshot)
    {
        // Always re-fetches (the caller already re-reads the snapshot fresh from the store by ID), so this
        // reflects the snapshot's current content — never the plan's own binding echoed back at itself, which
        // would always report "current" regardless of what actually changed since the plan was created.
        var context = new PlanValidationContext(DateTimeOffset.UtcNow,
            snapshot?.ScanId ?? Guid.Empty, snapshot?.Id, snapshot?.Drive.Fingerprint ?? string.Empty,
            snapshot?.RulePackId ?? string.Empty, snapshot?.RulePackVersion ?? string.Empty,
            snapshot?.RulePackDigest ?? string.Empty, CleanupPlanningConstants.CategoryRegistryVersion,
            CleanupPlanningConstants.ApplicationCompatibilityVersion, plan.Binding.PrivacyMode,
            snapshot is null ? EvidenceState.NoResult : EvidenceState.ForSnapshot(snapshot),
            CurrentTargets());
        return CleanupPlanValidator.Validate(plan, context);
    }

    /// <summary>The one real source of live per-target identity in this CLI (the classification-derived items
    /// never carry real targets at all — see ADR-0009 — so an empty result for them is correct, not a gap).
    /// Without this, <see cref="CleanupPlanValidator.Validate"/> would treat every real CLYR-owned-temp-artifact
    /// target as "changed" purely because no current identity was supplied to compare against, which would make
    /// 'plan execute' reject a perfectly current plan.</summary>
    private static ImmutableDictionary<string, CleanupTarget> CurrentTargets()
    {
        var builtIn = ClyrOwnedTempArtifactScanner.Scan(new SystemClock());
        return builtIn is null
            ? ImmutableDictionary<string, CleanupTarget>.Empty
            : builtIn.Targets.ToImmutableDictionary(target => target.TargetId);
    }

    private static object SafeCandidate(CleanupCandidate item) => new
    {
        item.FindingId,
        item.Title,
        item.Category,
        item.Eligibility,
        item.EligibilityReason,
        item.Impact,
        item.Risk,
        item.Confidence,
        item.Consequence,
        action = item.Action is null ? null : new
        {
            item.Action.ActionType,
            item.Action.AllowedRootIdentity,
            item.Action.RequiresElevation,
            item.Action.Rollback,
            item.Action.ExecutionAvailability
        }
    };

    private static string SafePlanJson(CleanupPlan plan) => JsonSerializer.Serialize(new
    {
        schemaVersion = plan.SchemaVersion,
        planId = plan.Id.ToString(),
        plan.ApplicationVersion,
        plan.Expiry,
        plan.TotalImpact,
        plan.Risk,
        plan.Confidence,
        plan.Warnings,
        plan.ExecutionAvailability,
        plan.Digest,
        binding = new
        {
            plan.Binding.SourceScanId,
            plan.Binding.SourceSnapshotId,
            plan.Binding.SourceRulePackId,
            plan.Binding.SourceRulePackVersion,
            plan.Binding.CategoryRegistryVersion,
            plan.Binding.ApplicationCompatibilityVersion,
            plan.Binding.PrivacyMode,
            plan.Binding.ItemSelectionIdentity,
            plan.Binding.TargetRootIdentities
        },
        items = plan.Items.Select(item => new
        {
            item.ItemId,
            item.FindingId,
            item.Title,
            item.Impact,
            item.Risk,
            item.Confidence,
            item.Consequence,
            item.Action.ActionType,
            item.Action.Rollback,
            item.Action.ExecutionAvailability
        })
    }, PlanJson);

    private static bool TryOption(IReadOnlyList<string> args, string name, out string? value)
    {
        var index = args.IndexOf(name);
        value = index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
        return value is not null;
    }
    private static string[] Options(IReadOnlyList<string> args, string name) =>
        args.Select((value, index) => (value, index))
            .Where(item => item.value == name && item.index + 1 < args.Count)
            .Select(item => args[item.index + 1]).ToArray();
    private static bool TryPlanId(string value, out CleanupPlanId id)
    {
        if (Guid.TryParse(value, out var parsed) && parsed != Guid.Empty) { id = new(parsed); return true; }
        id = default; return false;
    }
    private static int Usage(TextWriter error, string message) { error.WriteLine("plan.usage: " + message); return 2; }
    private static int Missing(TextWriter error, string message) { error.WriteLine("plan.not-found: " + message); return 1; }
}

internal static class ArgumentListExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
            if (string.Equals(values[index], value, StringComparison.Ordinal)) return index;
        return -1;
    }
}

