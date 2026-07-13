namespace Clyr.Contracts;

public enum ScanMode { Quick, Deep }
public enum ScanStatus { NotStarted, Preparing, Scanning, Cancelling, Cancelled, Completed, CompletedWithWarnings, Failed }
public enum DriveKind { Fixed, Removable, Network, Optical, Ram, Unknown }
public enum MeasurementPrecision { Exact, LowerBound, Estimated, Unavailable }
public enum ScanIssueKind { AccessDenied, EntryChanged, ReparseSkipped, CloudPlaceholder, Unsupported, ResourceLimit, Unexpected }
public enum ExtensionFamily { Documents, Images, Video, Audio, Archives, Executables, SourceCode, Data, Other, NoExtension }

public sealed record DriveSummary(string Root, string Label, string FileSystem, DriveKind Kind, bool IsReady,
    bool IsSystemVolume, bool IsSupported, string SupportReason, long? CapacityBytes, long? UsedBytes, long? FreeBytes);
public sealed record ScanRequest(string Root, ScanMode Mode, int? TopCount = null);
public sealed record ScanProgress(ScanStatus Status, TimeSpan Elapsed, long FilesObserved, long DirectoriesObserved,
    long LogicalBytesObserved, long SkippedEntries, string CurrentPath, string Message);
public sealed record RankedPath(string DisplayPath, long LogicalBytes, long FileCount, MeasurementPrecision Precision);
public sealed record ExtensionSummary(ExtensionFamily Family, long LogicalBytes, long FileCount);
public sealed record ScanIssueSummary(ScanIssueKind Kind, string Code, long Count, string SafeDetail);
public sealed record ScanCoverage(long FilesObserved, long DirectoriesObserved, long InaccessibleEntries,
    long ReparsePointsSkipped, long CloudPlaceholdersObserved, long ChangedEntries, long OtherSkippedEntries,
    bool ContentBytesRead, bool ReparsePointsFollowed, bool CloudFilesHydrated);

public sealed record ScanResult(Guid ScanId, ScanStatus Status, ScanMode Mode, string Root, string FileSystem,
    DateTimeOffset StartedAt, DateTimeOffset EndedAt, long LogicalBytesObserved, long? DriveUsedBytes,
    long? UnaccountedBytes, MeasurementPrecision Precision, string AccountingNote, ScanCoverage Coverage,
    IReadOnlyList<RankedPath> TopLevelDirectories, IReadOnlyList<RankedPath> LargestDirectories,
    IReadOnlyList<RankedPath> LargestFiles, IReadOnlyList<ExtensionSummary> ExtensionFamilies,
    IReadOnlyList<ScanIssueSummary> Issues, string? FailureCode, string? FailureMessage)
{
    public bool IsPartial => Status is ScanStatus.Cancelled or ScanStatus.CompletedWithWarnings;
}
