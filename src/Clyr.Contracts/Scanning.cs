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

/// <summary>
/// Why a scan's accounting numbers may not be directly comparable, or may not add up the way a naive reading
/// would expect. Multiple flags can apply to the same result at once — this is a bitset, not a single verdict —
/// because these conditions are independent of each other. <see cref="Consistent"/> (zero) means none applied:
/// the numbers can be compared and displayed at face value.
/// </summary>
[Flags]
public enum AccountingConsistency
{
    Consistent = 0,
    /// <summary>Observed logical bytes exceeded the drive's reported used-bytes basis (possible with hard
    /// links, sparse files, or simply two different measurements taken at different basis). The accounted
    /// percentage is suppressed (not silently clamped to 100%) whenever this is set.</summary>
    LogicalExceedsDriveUsed = 1,
    /// <summary>One or more files' allocated size could not be read, so allocated-byte totals are a lower
    /// bound, not a complete figure.</summary>
    AllocatedDataIncomplete = 2,
    /// <summary>Unique-allocation totals were adjusted downward because hard-linked files sharing the same
    /// physical content were detected and de-duplicated.</summary>
    HardLinkAdjusted = 4,
    /// <summary>The filesystem changed while the scan was running (entries appeared, disappeared, or were
    /// renamed), so the final totals reflect a moving target rather than one frozen instant.</summary>
    ChangedDuringScan = 8,
    /// <summary>Two figures being compared were measured on different bases (e.g., logical vs. allocated, or a
    /// volume-level figure vs. a file-tree figure) and are not safe to subtract or divide against each other
    /// without qualification.</summary>
    AccountingBasisMismatch = 16,
    /// <summary>One or more roots were inaccessible to this scan (permission denied), so any remainder figure
    /// may include storage this scan simply could not see, not storage that is somehow unaccounted-for junk.</summary>
    PermissionLimited = 32,
    /// <summary>A unique-allocated-bytes figure was combined across two independent scans (for example, an
    /// original non-elevated scan and a later elevated permission-limited-root retry) without being able to
    /// prove no hard link spans both — the same physical content could have already been counted once by one
    /// scan and again by the other. Any "unique" total under this flag is qualified or unavailable, never a
    /// falsely exact global figure.</summary>
    CrossScanIdentityReconciliationUnavailable = 64
}

/// <summary>
/// Read-only, real-Windows-API-derived allocation and hard-link accounting for a finished scan. Logical bytes
/// (namespace size) and allocated bytes (real on-disk consumption, accounting for sparse/compressed storage)
/// are kept strictly separate and never mixed into one number — see <see cref="ScanResult.LogicalBytesObserved"/>
/// for the logical total. <see cref="UniqueAllocatedBytesObserved"/> excludes duplicate hard-link references so
/// it is never presented as though every visible path were independently reclaimable.
/// </summary>
public sealed record AllocationAccounting(
    long AllocatedBytesObserved, long UniqueAllocatedBytesObserved, long FilesWithUnavailableAllocatedSize,
    long SparseFileCount, long CompressedFileCount, long VisibleHardLinkEntries, long UniqueFileIdentities,
    AccountingConsistency Consistency);

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

/// <summary>How one recorded scan root ended. <see cref="InaccessibleAtRoot"/> means the root itself could
/// never be opened at all (zero observed bytes, by construction) — a later elevated retry that completes this
/// root may safely add its bytes outright. <see cref="PartiallyObserved"/> means the root opened but the scan
/// hit at least one failure somewhere within it, so it carries a truthful but incomplete contribution that a
/// later retry must subtract before adding its own. <see cref="Completed"/> means the root needs no retry at
/// all.</summary>
public enum ScanRootEnumerationState { Completed, PartiallyObserved, InaccessibleAtRoot, Cancelled, Failed }

/// <summary>
/// Phase 7.2.6G2: one bounded, immutable per-root accounting record — never an unrestricted per-directory
/// inventory. <see cref="ScanResult.RootContributions"/> holds these only for top-level scan roots (the same
/// depth-1 directories already ranked in <see cref="ScanResult.TopLevelDirectories"/>), capped at
/// <see cref="ScanRootContributionLimits.MaxContributions"/> entries, so an elevated retry's result reconciler
/// can tell exactly what a specific permission-limited root already contributed to the original scan — without
/// this, reconciliation cannot safely add elevated bytes without risking double-counting.
/// <see cref="CanonicalRootIdentity"/> is the normalized root path (see <c>ElevatedScanManifestBuilder.NormalizePath</c>)
/// used to correlate this record against a later <c>PermissionLimitedRoot</c>/<c>ElevatedRootRetryResult</c>.
/// </summary>
public sealed record ScanRootContribution(
    string CanonicalRootIdentity, string? StableRootIdentifier, string RootPath,
    ScanRootEnumerationState EnumerationState, long FilesExamined, long DirectoriesExamined,
    long LogicalBytesObserved, long AllocatedBytesObserved, long UniqueAllocatedBytesObservedWithinRoot,
    long HardLinkEntriesDetected, long AllocationUnavailableCount, long SparseFileCount, long CompressedFileCount,
    long InaccessibleEntryCount, long ReparsePointsSkipped, long DiagnosticCount);

public static class ScanRootContributionLimits
{
    /// <summary>Matches <c>ElevatedScanRetryProtocol.MaxRoots</c> — the same bound the elevated retry manifest
    /// already enforces, since these records exist specifically to make that retry's reconciliation safe.</summary>
    public const int MaxContributions = 64;
}

public sealed record ScanResult(Guid ScanId, ScanStatus Status, ScanMode Mode, string Root, string FileSystem,
    DateTimeOffset StartedAt, DateTimeOffset EndedAt, long LogicalBytesObserved, long? DriveUsedBytes,
    long? UnaccountedBytes, MeasurementPrecision Precision, string AccountingNote, ScanCoverage Coverage,
    IReadOnlyList<RankedPath> TopLevelDirectories, IReadOnlyList<RankedPath> LargestDirectories,
    IReadOnlyList<RankedPath> LargestFiles, IReadOnlyList<ExtensionSummary> ExtensionFamilies,
    IReadOnlyList<ScanIssueSummary> Issues, string? FailureCode, string? FailureMessage,
    ClassificationResult? Classification = null, AllocationAccounting? Allocation = null,
    IReadOnlyList<ScanRootContribution>? RootContributions = null)
{
    /// <summary>Never <see langword="null"/> regardless of how this record was constructed — a missing/default
    /// value here always normalizes to empty rather than requiring every caller to null-check.</summary>
    public IReadOnlyList<ScanRootContribution> RootContributions { get; init; } = RootContributions ?? [];
    public bool IsPartial => Status is ScanStatus.Cancelled or ScanStatus.CompletedWithWarnings;
}
