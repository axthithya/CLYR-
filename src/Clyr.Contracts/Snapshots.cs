namespace Clyr.Contracts;

public enum SnapshotState { Pending, Writing, Complete, Partial, Cancelled, Failed, Incompatible, Corrupted }
public enum DriveIdentityQuality { Stable, Fallback, Unavailable }
public enum SnapshotCompatibility { FullyComparable, ComparableWithWarnings, ClassificationAdjusted, NotComparable }
public enum ComparisonConfidence { High, Medium, Low, Unavailable }
public enum DeltaKind { Increased, Decreased, Unchanged, New, NoLongerPresent, Uncertain, Incomparable }

public sealed record SnapshotDrive(string Fingerprint, DriveIdentityQuality IdentityQuality, string Root,
    string FileSystem, long? CapacityBytes, long? UsedBytes, long? FreeBytes);

public sealed record SnapshotCategory(StorageCategory Category, long LogicalBytes, long FileCount,
    MeasurementPrecision Precision, FindingStatus Status);

public sealed record SnapshotFinding(string RuleId, string RuleVersion, StorageCategory Category,
    FindingConfidence Confidence, FindingStatus Status, long LogicalBytes, long FileCount);

public sealed record StorageSnapshot(Guid Id, Guid ScanId, int SchemaVersion, string ApplicationVersion,
    DateTimeOffset CapturedAtUtc, ScanMode Mode, SnapshotState State, SnapshotDrive Drive,
    long LogicalBytesObserved, long ClassifiedBytes, long UnknownBytes, long? UnaccountedBytes,
    ScanCoverage Coverage, string RulePackId, string RulePackVersion, string RulePackDigest,
    IReadOnlyList<SnapshotCategory> Categories, IReadOnlyList<SnapshotFinding> Findings,
    IReadOnlyList<string> Warnings);

public sealed record SnapshotSummary(Guid Id, DateTimeOffset CapturedAtUtc, SnapshotState State,
    string DriveFingerprint, DriveIdentityQuality IdentityQuality, string Root, string FileSystem,
    ScanMode Mode, long LogicalBytesObserved, long? UsedBytes, long UnknownBytes);

public sealed record SnapshotMetricDelta(string Metric, long? Before, long? After, long? Change,
    DeltaKind Kind, bool IsSignificant);

public sealed record SnapshotCompatibilityResult(SnapshotCompatibility Kind, ComparisonConfidence Confidence,
    IReadOnlyList<string> Warnings);

public sealed record SnapshotComparison(Guid BeforeId, Guid AfterId, DateTimeOffset BeforeUtc,
    DateTimeOffset AfterUtc, SnapshotCompatibilityResult Compatibility,
    IReadOnlyList<SnapshotMetricDelta> Metrics, IReadOnlyList<SnapshotMetricDelta> Categories,
    IReadOnlyList<SnapshotMetricDelta> Findings, IReadOnlyList<string> Insights);

public sealed record HistorySettings(bool IsEnabled, int RetentionPerDrive, bool SavePartial, bool SaveCancelled)
{
    public static HistorySettings Default { get; } = new(true, 20, true, true);
}

public sealed record SnapshotSaveResult(bool Saved, Guid? SnapshotId, string Code, string Message);
