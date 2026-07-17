namespace Clyr.Contracts;

public enum ScanMode { Quick, Deep }
public enum ScanStatus { NotStarted, Preparing, Scanning, Cancelling, Cancelled, Completed, CompletedWithWarnings, Failed }
public enum DriveKind { Fixed, Removable, Network, Optical, Ram, Unknown }
public enum MeasurementPrecision { Exact, LowerBound, Estimated, Unavailable }
public enum ScanIssueKind { AccessDenied, EntryChanged, ReparseSkipped, CloudPlaceholder, Unsupported, ResourceLimit, Unexpected }
public enum ExtensionFamily { Documents, Images, Video, Audio, Archives, Executables, SourceCode, Data, Other, NoExtension }

/// <summary>How seriously a diagnostic should be treated by the UI. <see cref="Information"/> and
/// <see cref="PolicyBoundary"/> describe expected, by-design behavior (a reparse point intentionally not
/// followed; Quick Analysis's own time/item/depth budget being reached) and must never, on their own, make a
/// scan look like it failed. The remaining values describe real coverage gaps worth surfacing as warnings.</summary>
public enum ScanIssueSeverity { Information, PolicyBoundary, AccessWarning, DataChanged, PermissionLimited, Fatal }

public sealed record DriveSummary(string Root, string Label, string FileSystem, DriveKind Kind, bool IsReady,
    bool IsSystemVolume, bool IsSupported, string SupportReason, long? CapacityBytes, long? UsedBytes, long? FreeBytes);
public sealed record ScanRequest(string Root, ScanMode Mode, int? TopCount = null, bool ContinueFromCheckpoint = false);
public sealed record ScanProgress(ScanStatus Status, TimeSpan Elapsed, long FilesObserved, long DirectoriesObserved,
    long LogicalBytesObserved, long SkippedEntries, string CurrentPath, string Message,
    long InaccessibleEntries = 0, long ReparsePointsSkipped = 0, long WarningCount = 0);
public sealed record RankedPath(string DisplayPath, long LogicalBytes, long FileCount, MeasurementPrecision Precision);
public sealed record ExtensionSummary(ExtensionFamily Family, long LogicalBytes, long FileCount);
public sealed record ScanIssueSummary(ScanIssueKind Kind, string Code, long Count, string SafeDetail, ScanIssueSeverity Severity = ScanIssueSeverity.AccessWarning);

/// <summary>A CLYR-owned, bounded snapshot of a Quick Analysis run that ended at a policy boundary (time or item
/// budget) rather than exhaustion, so "Continue Quick Analysis" can resume from where it left off instead of
/// restarting at the drive root. Contains only aggregate counters and trusted pending-directory paths already
/// produced by CLYR's own scan of this drive — never file contents, never another execution's data.</summary>
public sealed record ScanCheckpoint(string Root, ScanMode Mode, int PolicyVersion, DateTimeOffset OriginalStartedAtUtc,
    DateTimeOffset SavedAtUtc, long FilesObserved, long DirectoriesObserved, long LogicalBytesObserved,
    IReadOnlyList<string> PendingDirectories);
public sealed record ScanCoverage(long FilesObserved, long DirectoriesObserved, long InaccessibleEntries,
    long ReparsePointsSkipped, long CloudPlaceholdersObserved, long ChangedEntries, long OtherSkippedEntries,
    bool ContentBytesRead, bool ReparsePointsFollowed, bool CloudFilesHydrated);

public sealed record ScanResult(Guid ScanId, ScanStatus Status, ScanMode Mode, string Root, string FileSystem,
    DateTimeOffset StartedAt, DateTimeOffset EndedAt, long LogicalBytesObserved, long? DriveUsedBytes,
    long? UnaccountedBytes, MeasurementPrecision Precision, string AccountingNote, ScanCoverage Coverage,
    IReadOnlyList<RankedPath> TopLevelDirectories, IReadOnlyList<RankedPath> LargestDirectories,
    IReadOnlyList<RankedPath> LargestFiles, IReadOnlyList<ExtensionSummary> ExtensionFamilies,
    IReadOnlyList<ScanIssueSummary> Issues, string? FailureCode, string? FailureMessage,
    ClassificationResult? Classification = null)
{
    public bool IsPartial => Status is ScanStatus.Cancelled or ScanStatus.CompletedWithWarnings;
}
