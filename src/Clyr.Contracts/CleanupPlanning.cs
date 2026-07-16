using System.Collections.Immutable;

namespace Clyr.Contracts;

public readonly record struct CleanupPlanId
{
    public CleanupPlanId(Guid value)
    {
        if (value == Guid.Empty) throw new ArgumentException("A cleanup plan ID cannot be empty.", nameof(value));
        Value = value;
    }

    public Guid Value { get; }
    public override string ToString() => Value.ToString("D");
}

public enum CleanupEligibility { NotEligible, ManualReviewOnly, DryRunEligible, Protected, Unsupported, InsufficientEvidence }
public enum CleanupActionType { ReportOnly, ReviewFiles, RecycleFiles, QuarantineFiles, TrustedToolCommand, WindowsSupportedCleanup, MoveKnownFolder, ManualInstructions }
public enum CleanupPlanStatus { Draft, Valid, Stale, Expired, Invalid, Discarded }
public enum RollbackCapability { None, RecycleBinPotential, QuarantinePotential, ToolManaged, Manual, Unknown }
public enum ExecutionAvailability { ExecutionNotAvailableInPhase5 }
public enum PlanDiagnosticSeverity { Information, Warning, Error }
public enum TargetState { Observed, Inaccessible, Protected, CloudPlaceholder, Changed, Missing, ReviewRequired }

public sealed record EstimatedImpact(long ItemCount, long ObservedLogicalBytes, long? EstimatedPhysicalBytes, string Uncertainty)
{
    public EstimatedImpact Validate()
    {
        if (ItemCount < 0 || ObservedLogicalBytes < 0 || EstimatedPhysicalBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(ObservedLogicalBytes), "Impact values cannot be negative.");
        return this;
    }
}

public sealed record CleanupConsequence(
    string DataDescription, string WhyItExists, string PossibleOutcome, bool CanRegenerate,
    string NetworkImpact, string ApplicationImpact, string SessionImpact, string RollbackStatement, string Unknowns);

public sealed record ActionDefinition(
    CleanupActionType ActionType, int SchemaVersion, string SourceRuleId, string SourceRuleVersion,
    string SourceRulePackVersion, string AllowedRootIdentity, string PathContainmentPolicy,
    bool RequiresElevation, RollbackCapability Rollback, ImmutableArray<string> ExpectedSideEffects,
    ImmutableArray<string> ValidationRequirements, ExecutionAvailability ExecutionAvailability,
    string Explanation, RiskLevel Risk, FindingConfidence Confidence, TimeSpan MaximumPlanAge,
    string? TrustedToolIdentifier = null, ImmutableDictionary<string, string>? TypedArguments = null);

public sealed record CleanupTarget(
    string TargetId, string ApprovedRootIdentity, string DisplayLocation, string? CanonicalPath,
    string VolumeIdentity, string? StableFileIdentity, long LogicalBytes, DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? LastWriteAtUtc, string Attributes, bool IsReparsePoint, bool IsCloudPlaceholder, TargetState State);

public sealed record CleanupCandidate(
    string FindingId, string Title, StorageCategory Category, CleanupEligibility Eligibility,
    string EligibilityReason, ActionDefinition? Action, EstimatedImpact Impact, RiskLevel Risk,
    FindingConfidence Confidence, CleanupConsequence Consequence, ImmutableArray<CleanupTarget> Targets);

public sealed record UserSelection(string Identity, ImmutableArray<string> FindingIds);

public sealed record PlanBinding(
    Guid SourceScanId, Guid? SourceSnapshotId, string DriveIdentity, string SourceRulePackId,
    string SourceRulePackVersion, string SourceRulePackDigest, string CategoryRegistryVersion,
    string ApplicationCompatibilityVersion, string PrivacyMode, string ItemSelectionIdentity,
    ImmutableArray<string> TargetRootIdentities);

public sealed record PlanExpiry(DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc)
{
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAtUtc;
}

public sealed record CleanupPlanItem(
    string ItemId, string FindingId, string Title, CleanupEligibility Eligibility, ActionDefinition Action,
    EstimatedImpact Impact, RiskLevel Risk, FindingConfidence Confidence, CleanupConsequence Consequence,
    ImmutableArray<CleanupTarget> Targets);

public sealed record PlanDiagnostic(string Code, PlanDiagnosticSeverity Severity, string Message, string? ItemId = null);
public sealed record ProtectedPathViolation(string ItemId, string TargetId, string PolicyCode, string Message);
public sealed record PlanValidationResult(bool IsValid, CleanupPlanStatus Status,
    ImmutableArray<PlanDiagnostic> Diagnostics, ImmutableArray<ProtectedPathViolation> ProtectedPathViolations);

public sealed record DryRunItemResult(
    string ItemId, CleanupActionType ActionType, EstimatedImpact Impact, ImmutableArray<string> AffectedLocations,
    long InaccessibleItems, long ProtectedItems, long CloudPlaceholders, long ChangedItems, long MissingItems,
    bool RequiresReview, bool RequiresElevation, RollbackCapability Rollback, ExecutionAvailability ExecutionAvailability);

public sealed record DryRunResult(
    CleanupPlanId PlanId, CleanupPlanStatus Status, EstimatedImpact TotalImpact,
    ImmutableArray<DryRunItemResult> Items, ImmutableArray<string> Limitations,
    ExecutionAvailability ExecutionAvailability);

public sealed class CleanupPlan
{
    public CleanupPlan(int schemaVersion, CleanupPlanId id, string applicationVersion, PlanBinding binding,
        PlanExpiry expiry, ImmutableArray<CleanupPlanItem> items, EstimatedImpact totalImpact,
        RiskLevel risk, FindingConfidence confidence, ImmutableArray<string> warnings,
        ExecutionAvailability executionAvailability, string digest)
    {
        SchemaVersion = schemaVersion;
        Id = id;
        ApplicationVersion = applicationVersion;
        Binding = binding;
        Expiry = expiry;
        Items = items;
        TotalImpact = totalImpact;
        Risk = risk;
        Confidence = confidence;
        Warnings = warnings;
        ExecutionAvailability = executionAvailability;
        Digest = digest;
    }

    public int SchemaVersion { get; }
    public CleanupPlanId Id { get; }
    public string ApplicationVersion { get; }
    public PlanBinding Binding { get; }
    public PlanExpiry Expiry { get; }
    public ImmutableArray<CleanupPlanItem> Items { get; }
    public EstimatedImpact TotalImpact { get; }
    public RiskLevel Risk { get; }
    public FindingConfidence Confidence { get; }
    public ImmutableArray<string> Warnings { get; }
    public ExecutionAvailability ExecutionAvailability { get; }
    public string Digest { get; }
}

public sealed record PlanValidationContext(
    DateTimeOffset NowUtc, Guid SourceScanId, Guid? SourceSnapshotId, string DriveIdentity,
    string RulePackId, string RulePackVersion, string RulePackDigest, string CategoryRegistryVersion,
    string ApplicationCompatibilityVersion, string PrivacyMode,
    ImmutableDictionary<string, CleanupTarget> CurrentTargets);

public sealed record CleanupExecutionResult(bool Available, string Code, string Message, ExecutionAvailability Availability);

