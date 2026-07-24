using System.Collections.Immutable;

namespace Clyr.Contracts;

public enum ExecutionState
{
    Pending, AwaitingConsent, Validating, Rejected, AwaitingElevation, Running,
    Cancelling, Completed, PartiallyCompleted, Cancelled, Interrupted, UnknownOutcome, Failed
}

public enum ExecutionItemOutcome
{
    Removed, NotFound, SkippedChanged, SkippedLocked, SkippedProtected, SkippedReparsePoint,
    SkippedCloudPlaceholder, SkippedHardLink, SkippedIdentityMismatch, SkippedOutsideApprovedRoot,
    SkippedAccessDenied, Cancelled, Failed
}

public enum ExecutionDiagnosticSeverity { Information, Warning, Error }

public readonly record struct ExecutionSessionId
{
    public ExecutionSessionId(Guid value)
    {
        if (value == Guid.Empty) throw new ArgumentException("An execution session ID cannot be empty.", nameof(value));
        Value = value;
    }
    public Guid Value { get; }
    public override string ToString() => Value.ToString("D");
}

public readonly record struct ExecutionId
{
    public ExecutionId(Guid value)
    {
        if (value == Guid.Empty) throw new ArgumentException("An execution ID cannot be empty.", nameof(value));
        Value = value;
    }
    public Guid Value { get; }
    public override string ToString() => Value.ToString("D");
}

public sealed record ExecutionCapability(
    string ActionId, string Title, bool Enabled, RiskLevel Risk, string TrustedRootIdentity,
    TimeSpan MinimumAge, int MaxItems, long MaxTotalBytes, bool RequiresElevation, string Explanation);

public sealed record ExecutionPolicy(int SchemaVersion, ImmutableArray<ExecutionCapability> EnabledActions);

public sealed record ExecutionConsent(bool Confirmed, DateTimeOffset ConfirmedAtUtc, string ConfirmationTextDigest);

public sealed class ExecutionToken
{
    public ExecutionToken(Guid tokenId, CleanupPlanId planId, string planDigest, ExecutionSessionId applicationSessionId,
        string windowsUserSid, string driveIdentity, ImmutableArray<string> actionIds,
        DateTimeOffset issuedAtUtc, DateTimeOffset expiresAtUtc, string nonce)
    {
        if (tokenId == Guid.Empty) throw new ArgumentException("A token ID cannot be empty.", nameof(tokenId));
        if (string.IsNullOrWhiteSpace(planDigest)) throw new ArgumentException("A token requires a plan digest.", nameof(planDigest));
        if (string.IsNullOrWhiteSpace(nonce) || nonce.Length < 32) throw new ArgumentException("A token requires a strong nonce.", nameof(nonce));
        if (actionIds.IsDefaultOrEmpty) throw new ArgumentException("A token requires at least one action ID.", nameof(actionIds));
        if (expiresAtUtc <= issuedAtUtc) throw new ArgumentException("A token must expire after it is issued.", nameof(expiresAtUtc));
        TokenId = tokenId;
        PlanId = planId;
        PlanDigest = planDigest;
        ApplicationSessionId = applicationSessionId;
        WindowsUserSid = windowsUserSid;
        DriveIdentity = driveIdentity;
        ActionIds = actionIds;
        IssuedAtUtc = issuedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Nonce = nonce;
    }

    public Guid TokenId { get; }
    public CleanupPlanId PlanId { get; }
    public string PlanDigest { get; }
    public ExecutionSessionId ApplicationSessionId { get; }
    public string WindowsUserSid { get; }
    public string DriveIdentity { get; }
    public ImmutableArray<string> ActionIds { get; }
    public DateTimeOffset IssuedAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public string Nonce { get; }
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAtUtc;
}

public sealed record ExecutionRequest(
    CleanupPlanId PlanId, string PlanDigestConfirmation, ImmutableArray<string> SelectedItemIds, ExecutionConsent Consent);

public sealed record ExecutionItemResult(
    string ItemId, string TargetId, ExecutionItemOutcome Outcome, string Code, string Message, long? RemovedLogicalBytes);

public sealed record ExecutionDiagnostic(string Code, ExecutionDiagnosticSeverity Severity, string Message, string? ItemId = null);

public sealed record ExecutionSummary(
    int TotalItems, int RemovedCount, int SkippedCount, int FailedCount,
    long PlannedLogicalBytes, long RemovedLogicalBytes, long SkippedLogicalBytes, long FailedLogicalBytes)
{
    public static ExecutionSummary Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);
}

public sealed record ExecutionCancellationResult(bool Requested, bool Honored, bool PartialResultsPreserved);

public sealed record ExecutionReceiptSummary(
    ExecutionId ExecutionId, CleanupPlanId SourcePlanId, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc,
    ExecutionState FinalState, int RemovedCount, int SkippedCount, int FailedCount, long RemovedLogicalBytes);

public sealed class ExecutionReceipt
{
    public ExecutionReceipt(int schemaVersion, ExecutionId executionId, CleanupPlanId sourcePlanId, string sourcePlanDigest,
        string applicationVersion, string rulePackVersion, string driveIdentityFingerprint,
        DateTimeOffset startedAtUtc, DateTimeOffset? completedAtUtc, ExecutionState finalState, bool cancelled,
        bool elevationUsed, ExecutionSummary summary, long? driveFreeBytesBefore, long? driveFreeBytesAfter,
        long? observedFreeSpaceDeltaBytes, ImmutableDictionary<string, int> outcomeCategories,
        ImmutableArray<string> warnings, ImmutableArray<string> limitations, string privacyMode, string digest,
        Guid sourceScanId, string evidenceStateId, ImmutableArray<string> actionIds,
        Guid executionSessionId, string windowsUserSidFingerprint)
    {
        SchemaVersion = schemaVersion;
        ExecutionId = executionId;
        SourcePlanId = sourcePlanId;
        SourcePlanDigest = sourcePlanDigest;
        ApplicationVersion = applicationVersion;
        RulePackVersion = rulePackVersion;
        DriveIdentityFingerprint = driveIdentityFingerprint;
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
        FinalState = finalState;
        Cancelled = cancelled;
        ElevationUsed = elevationUsed;
        Summary = summary;
        DriveFreeBytesBefore = driveFreeBytesBefore;
        DriveFreeBytesAfter = driveFreeBytesAfter;
        ObservedFreeSpaceDeltaBytes = observedFreeSpaceDeltaBytes;
        OutcomeCategories = outcomeCategories;
        Warnings = warnings;
        Limitations = limitations;
        PrivacyMode = privacyMode;
        Digest = digest;
        SourceScanId = sourceScanId;
        EvidenceStateId = evidenceStateId;
        ActionIds = actionIds;
        ExecutionSessionId = executionSessionId;
        WindowsUserSidFingerprint = windowsUserSidFingerprint;
    }

    public int SchemaVersion { get; }
    public ExecutionId ExecutionId { get; }
    public CleanupPlanId SourcePlanId { get; }
    public string SourcePlanDigest { get; }
    public string ApplicationVersion { get; }
    public string RulePackVersion { get; }
    public string DriveIdentityFingerprint { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset? CompletedAtUtc { get; }
    public ExecutionState FinalState { get; }
    public bool Cancelled { get; }
    public bool ElevationUsed { get; }
    public ExecutionSummary Summary { get; }
    public long? DriveFreeBytesBefore { get; }
    public long? DriveFreeBytesAfter { get; }
    public long? ObservedFreeSpaceDeltaBytes { get; }
    public ImmutableDictionary<string, int> OutcomeCategories { get; }
    public ImmutableArray<string> Warnings { get; }
    public ImmutableArray<string> Limitations { get; }
    public string PrivacyMode { get; }
    public string Digest { get; }

    /// <summary>The completed analysis this execution's plan was built from — <see cref="Guid.Empty"/> when the
    /// plan had no analysis result at all (a plan built only from the always-live CLYR-owned-temp-artifact
    /// candidate). Durable crash recovery and replay protection (see <c>IExecutionReceiptStore.BeginAsync</c>)
    /// need this independently of <see cref="SourcePlanId"/>, which is a fresh random identity per plan build.</summary>
    public Guid SourceScanId { get; }

    /// <summary>The plan's bound evidence-state identity (see <c>Clyr.Core.EvidenceState</c>) at the moment this
    /// execution began — never a raw path, always an opaque content digest.</summary>
    public string EvidenceStateId { get; }

    /// <summary>The built-in action rule IDs this execution was authorized for — the same set bound into the
    /// execution token (see <c>IExecutionTokenService.Issue</c>), recorded durably for audit.</summary>
    public ImmutableArray<string> ActionIds { get; }

    /// <summary>The application session this execution ran under (see <see cref="ExecutionSessionId"/> the type,
    /// not this property) — never persisted across a restart on its own, but recorded here so a durable receipt
    /// can be correlated back to the session that authorized it.</summary>
    public Guid ExecutionSessionId { get; }

    /// <summary>A one-way privacy-safe fingerprint of the Windows user SID this execution ran as — the same
    /// fingerprinting convention already used for <see cref="DriveIdentityFingerprint"/>. Never the raw SID.</summary>
    public string WindowsUserSidFingerprint { get; }
}
