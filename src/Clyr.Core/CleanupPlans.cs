using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clyr.Contracts;

namespace Clyr.Core;

public sealed record PlanCreationRequest(
    Guid SourceScanId, Guid? SourceSnapshotId, string DriveIdentity, string RulePackId,
    string RulePackVersion, string RulePackDigest, string ApplicationVersion, string PrivacyMode,
    string EvidenceStateId, DateTimeOffset CreatedAtUtc, IReadOnlyList<CleanupCandidate> Candidates,
    IReadOnlyList<string> SelectedFindingIds);

public sealed class CleanupPlanBuilder
{
    public static CleanupPlan Create(PlanCreationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var selectedIds = request.SelectedFindingIds.ToArray();
        if (selectedIds.Length is 0 or > CleanupPlanningConstants.MaximumPlanItems)
            throw new InvalidOperationException("A plan requires between 1 and 128 selected findings.");
        if (selectedIds.Distinct(StringComparer.Ordinal).Count() != selectedIds.Length)
            throw new InvalidOperationException("Duplicate finding selections are not permitted.");
        var candidates = request.Candidates.ToDictionary(item => item.FindingId, StringComparer.Ordinal);
        var selected = selectedIds.Select(id => candidates.TryGetValue(id, out var candidate)
            ? candidate : throw new InvalidOperationException("A selected finding is unavailable."))
            .OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        if (selected.Any(item => item.Eligibility != CleanupEligibility.DryRunEligible || item.Action is null))
            throw new InvalidOperationException("Only dry-run-eligible findings can become plan items.");
        if (selected.Sum(item => item.Targets.Length) > CleanupPlanningConstants.MaximumTargets)
            throw new InvalidOperationException("The plan exceeds the bounded target limit.");

        var items = selected.Select(item => new CleanupPlanItem(
            StableId($"{item.FindingId}|{item.Action!.ActionType}|{item.Action.AllowedRootIdentity}"),
            item.FindingId, item.Title, item.Eligibility, item.Action, item.Impact.Validate(), item.Risk,
            item.Confidence, item.Consequence, item.Targets.OrderBy(target => target.TargetId, StringComparer.Ordinal).ToImmutableArray()))
            .ToImmutableArray();
        long? physical = items.All(item => item.Impact.EstimatedPhysicalBytes.HasValue)
            ? Total(items.Select(item => item.Impact.EstimatedPhysicalBytes!.Value)) : null;
        var binding = new PlanBinding(request.SourceScanId, request.SourceSnapshotId, request.DriveIdentity,
            request.RulePackId, request.RulePackVersion, request.RulePackDigest,
            CleanupPlanningConstants.CategoryRegistryVersion, CleanupPlanningConstants.ApplicationCompatibilityVersion,
            request.PrivacyMode, request.EvidenceStateId, StableId(string.Join("|", items.Select(item => item.FindingId))),
            items.Select(item => item.Action.AllowedRootIdentity).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray());
        var created = request.CreatedAtUtc.ToUniversalTime();
        var expiry = new PlanExpiry(created, created.Add(items.Min(item => item.Action.MaximumPlanAge)));
        var impact = new EstimatedImpact(Total(items.Select(item => item.Impact.ItemCount)),
            Total(items.Select(item => item.Impact.ObservedLogicalBytes)), physical,
            "Potential logical bytes affected are based on observed metadata and are not guaranteed recovered space.");
        var shell = new CleanupPlan(CleanupPlanningConstants.PlanSchemaVersion, new(Guid.NewGuid()),
            request.ApplicationVersion, binding, expiry, items, impact, items.Max(item => item.Risk),
            items.Min(item => item.Confidence),
            ["Dry-run only — no files will be changed.",
             "Allocation, hard links, compression, cloud state, locks, and cache recreation can change outcomes."],
            ExecutionAvailability.ExecutionNotAvailableInPhase5, string.Empty);
        return Copy(shell, CleanupPlanCanonicalizer.Digest(shell));
    }

    private static CleanupPlan Copy(CleanupPlan plan, string digest) => new(plan.SchemaVersion, plan.Id,
        plan.ApplicationVersion, plan.Binding, plan.Expiry, plan.Items, plan.TotalImpact, plan.Risk,
        plan.Confidence, plan.Warnings, plan.ExecutionAvailability, digest);
    private static string StableId(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];
    private static long Total(IEnumerable<long> values)
    {
        long total = 0;
        foreach (var value in values)
            try { total = checked(total + Math.Max(0, value)); }
            catch (OverflowException) { return long.MaxValue; }
        return total;
    }
}

public static class CleanupPlanCanonicalizer
{
    public static string Digest(CleanupPlan plan) =>
        Convert.ToHexString(SHA256.HashData(CanonicalBytes(plan))).ToLowerInvariant();

    public static byte[] CanonicalBytes(CleanupPlan plan)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", plan.SchemaVersion);
            writer.WriteString("planId", plan.Id.ToString());
            writer.WriteString("applicationVersion", plan.ApplicationVersion);
            WriteBinding(writer, plan.Binding);
            writer.WriteString("createdAtUtc", plan.Expiry.CreatedAtUtc.ToUniversalTime());
            writer.WriteString("expiresAtUtc", plan.Expiry.ExpiresAtUtc.ToUniversalTime());
            writer.WriteStartArray("items");
            foreach (var item in plan.Items.OrderBy(value => value.ItemId, StringComparer.Ordinal)) WriteItem(writer, item);
            writer.WriteEndArray();
            WriteImpact(writer, "totalImpact", plan.TotalImpact);
            writer.WriteString("risk", plan.Risk.ToString());
            writer.WriteString("confidence", plan.Confidence.ToString());
            WriteStrings(writer, "warnings", plan.Warnings);
            writer.WriteString("executionAvailability", plan.ExecutionAvailability.ToString());
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void WriteBinding(Utf8JsonWriter writer, PlanBinding value)
    {
        writer.WriteStartObject("binding");
        writer.WriteString("sourceScanId", value.SourceScanId);
        if (value.SourceSnapshotId.HasValue) writer.WriteString("sourceSnapshotId", value.SourceSnapshotId.Value);
        else writer.WriteNull("sourceSnapshotId");
        writer.WriteString("driveIdentity", value.DriveIdentity);
        writer.WriteString("sourceRulePackId", value.SourceRulePackId);
        writer.WriteString("sourceRulePackVersion", value.SourceRulePackVersion);
        writer.WriteString("sourceRulePackDigest", value.SourceRulePackDigest);
        writer.WriteString("categoryRegistryVersion", value.CategoryRegistryVersion);
        writer.WriteString("applicationCompatibilityVersion", value.ApplicationCompatibilityVersion);
        writer.WriteString("privacyMode", value.PrivacyMode);
        writer.WriteString("evidenceStateId", value.EvidenceStateId);
        writer.WriteString("itemSelectionIdentity", value.ItemSelectionIdentity);
        WriteStrings(writer, "targetRootIdentities", value.TargetRootIdentities);
        writer.WriteEndObject();
    }

    private static void WriteItem(Utf8JsonWriter writer, CleanupPlanItem value)
    {
        writer.WriteStartObject();
        writer.WriteString("itemId", value.ItemId);
        writer.WriteString("findingId", value.FindingId);
        writer.WriteString("title", value.Title);
        writer.WriteString("eligibility", value.Eligibility.ToString());
        WriteAction(writer, value.Action);
        WriteImpact(writer, "impact", value.Impact);
        writer.WriteString("risk", value.Risk.ToString());
        writer.WriteString("confidence", value.Confidence.ToString());
        WriteConsequence(writer, value.Consequence);
        writer.WriteStartArray("targets");
        foreach (var target in value.Targets) WriteTarget(writer, target);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteAction(Utf8JsonWriter writer, ActionDefinition value)
    {
        writer.WriteStartObject("action");
        writer.WriteString("actionType", value.ActionType.ToString());
        writer.WriteNumber("schemaVersion", value.SchemaVersion);
        writer.WriteString("sourceRuleId", value.SourceRuleId);
        writer.WriteString("sourceRuleVersion", value.SourceRuleVersion);
        writer.WriteString("sourceRulePackVersion", value.SourceRulePackVersion);
        writer.WriteString("allowedRootIdentity", value.AllowedRootIdentity);
        writer.WriteString("pathContainmentPolicy", value.PathContainmentPolicy);
        writer.WriteBoolean("requiresElevation", value.RequiresElevation);
        writer.WriteString("rollback", value.Rollback.ToString());
        writer.WriteString("executionAvailability", value.ExecutionAvailability.ToString());
        writer.WriteString("explanation", value.Explanation);
        writer.WriteString("risk", value.Risk.ToString());
        writer.WriteString("confidence", value.Confidence.ToString());
        writer.WriteNumber("maximumPlanAgeSeconds", checked((long)value.MaximumPlanAge.TotalSeconds));
        WriteStrings(writer, "expectedSideEffects", value.ExpectedSideEffects);
        WriteStrings(writer, "validationRequirements", value.ValidationRequirements);
        if (value.TrustedToolIdentifier is null) writer.WriteNull("trustedToolIdentifier");
        else writer.WriteString("trustedToolIdentifier", value.TrustedToolIdentifier);
        writer.WriteStartObject("typedArguments");
        foreach (var pair in value.TypedArguments ?? ImmutableDictionary<string, string>.Empty)
            writer.WriteString(pair.Key, pair.Value);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteImpact(Utf8JsonWriter writer, string name, EstimatedImpact value)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("itemCount", value.ItemCount);
        writer.WriteNumber("observedLogicalBytes", value.ObservedLogicalBytes);
        if (value.EstimatedPhysicalBytes.HasValue) writer.WriteNumber("estimatedPhysicalBytes", value.EstimatedPhysicalBytes.Value);
        else writer.WriteNull("estimatedPhysicalBytes");
        writer.WriteString("uncertainty", value.Uncertainty);
        writer.WriteEndObject();
    }

    private static void WriteConsequence(Utf8JsonWriter writer, CleanupConsequence value)
    {
        writer.WriteStartObject("consequence");
        writer.WriteString("dataDescription", value.DataDescription);
        writer.WriteString("whyItExists", value.WhyItExists);
        writer.WriteString("possibleOutcome", value.PossibleOutcome);
        writer.WriteBoolean("canRegenerate", value.CanRegenerate);
        writer.WriteString("networkImpact", value.NetworkImpact);
        writer.WriteString("applicationImpact", value.ApplicationImpact);
        writer.WriteString("sessionImpact", value.SessionImpact);
        writer.WriteString("rollbackStatement", value.RollbackStatement);
        writer.WriteString("unknowns", value.Unknowns);
        writer.WriteEndObject();
    }

    private static void WriteTarget(Utf8JsonWriter writer, CleanupTarget value)
    {
        writer.WriteStartObject();
        writer.WriteString("targetId", value.TargetId);
        writer.WriteString("approvedRootIdentity", value.ApprovedRootIdentity);
        writer.WriteString("displayLocation", value.DisplayLocation);
        if (value.CanonicalPath is null) writer.WriteNull("canonicalPath"); else writer.WriteString("canonicalPath", value.CanonicalPath);
        writer.WriteString("volumeIdentity", value.VolumeIdentity);
        if (value.StableFileIdentity is null) writer.WriteNull("stableFileIdentity"); else writer.WriteString("stableFileIdentity", value.StableFileIdentity);
        writer.WriteNumber("logicalBytes", value.LogicalBytes);
        if (value.CreatedAtUtc.HasValue) writer.WriteString("createdAtUtc", value.CreatedAtUtc.Value.ToUniversalTime()); else writer.WriteNull("createdAtUtc");
        if (value.LastWriteAtUtc.HasValue) writer.WriteString("lastWriteAtUtc", value.LastWriteAtUtc.Value.ToUniversalTime()); else writer.WriteNull("lastWriteAtUtc");
        writer.WriteString("attributes", value.Attributes);
        writer.WriteBoolean("isReparsePoint", value.IsReparsePoint);
        writer.WriteBoolean("isCloudPlaceholder", value.IsCloudPlaceholder);
        writer.WriteString("state", value.State.ToString());
        writer.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values) writer.WriteStringValue(value);
        writer.WriteEndArray();
    }
}

