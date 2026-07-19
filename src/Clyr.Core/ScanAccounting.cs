using Clyr.Contracts;

namespace Clyr.Core;

/// <summary>Coverage quality bands, purely a display aid over <see cref="ScanAccountingSummary.AccountedPercentage"/>.
/// A drive with no comparable used-bytes basis at all (or a scan that observed essentially nothing of it) is
/// treated as <see cref="Insufficient"/> — a genuinely low or absent percentage. <see cref="AccountingBasisDiffers"/>
/// is a distinct condition: a percentage could not be computed specifically because logical (namespace) bytes
/// legitimately exceed the drive's physical used-bytes basis (hard links, sparse files, compression) — this is
/// never a coverage problem and must never be presented as "Limited coverage" or "Insufficient coverage".</summary>
public enum ScanQuality { Excellent, Good, Partial, Insufficient, AccountingBasisDiffers }

/// <summary>
/// Truthful, separated accounting derived from a finished <see cref="ScanResult"/>. Two independent bases are
/// kept apart on purpose: "how much of the whole drive did this scan account for" (<see cref="AccountedPercentage"/>)
/// versus "of what this scan actually observed, how much did the rule engine classify"
/// (<see cref="ClassificationPercentage"/>). Classification coverage must never be read as, or displayed as,
/// total drive coverage — a scan that classified 100% of what little it observed can still have accounted for
/// almost none of the drive.
/// </summary>
public sealed record ScanAccountingSummary(
    double? AccountedPercentage, double? ClassificationPercentage, ScanQuality Quality,
    long ClassifiedObservedBytes, long UnclassifiedObservedBytes, long? UnaccountedDriveBytes,
    AccountingConsistency Consistency = AccountingConsistency.Consistent)
{
    /// <summary>
    /// The presentation-safe not-observed figure — every normal UI surface (Results, Overview, Scan, History,
    /// exports) must read this for display, never <see cref="UnaccountedDriveBytes"/> directly.
    /// <see cref="UnaccountedDriveBytes"/> is deliberately never clamped at its own source (Phase 7.2.5 — a
    /// negative value there is a real, meaningful internal signal that logical bytes exceed the drive-used
    /// basis), but a negative byte count is never a valid "amount of unobserved storage" to show a user — this
    /// property is <see langword="null"/> ("Not available") whenever <see cref="UnaccountedDriveBytes"/> would be
    /// negative, rather than ever formatting a negative number or silently flooring it to a misleading 0 B.
    /// </summary>
    public long? PresentableUnaccountedDriveBytes => UnaccountedDriveBytes is < 0 ? null : UnaccountedDriveBytes;
}

public static class ScanAccounting
{
    /// <summary>Below this accounted-drive percentage, a Quick (or any) result is labelled "Insufficient
    /// coverage" and Deep Analysis is recommended — see the documented regression example in
    /// <c>ScanAccountingTests</c>.</summary>
    public const double InsufficientCoverageThreshold = 30;
    private const double GoodCoverageThreshold = 70;
    private const double ExcellentCoverageThreshold = 90;

    public static ScanAccountingSummary Summarize(ScanResult result)
    {
        var driveUsed = result.DriveUsedBytes;
        var consistency = result.Allocation?.Consistency ?? AccountingConsistency.Consistent;
        // Phase 7.2.5: no silent clamping. When logical observed bytes exceed the drive-used basis (hard
        // links, sparse files, or a basis difference between the two measurements), a percentage above 100%
        // is not a meaningful "coverage" figure, so it is suppressed (null) rather than floored to a
        // reassuring-looking 100% — the caller must read AccountedPercentage's absence together with
        // Consistency.LogicalExceedsDriveUsed, never assume "null means unavailable for some boring reason."
        double? accountedPercentage = null;
        if (driveUsed is > 0)
        {
            if (result.LogicalBytesObserved > driveUsed.Value) consistency |= AccountingConsistency.LogicalExceedsDriveUsed;
            else accountedPercentage = result.LogicalBytesObserved * 100d / driveUsed.Value;
        }

        var classifiedBytes = result.Classification?.Coverage.ClassifiedBytes ?? 0;
        var unclassifiedBytes = result.Classification?.Coverage.UnknownBytes ?? Math.Max(0, result.LogicalBytesObserved - classifiedBytes);
        var observedForClassification = classifiedBytes + unclassifiedBytes;
        double? classificationPercentage = observedForClassification > 0 ? classifiedBytes * 100d / observedForClassification : null;

        return new(accountedPercentage, classificationPercentage, QualityFor(accountedPercentage, consistency),
            classifiedBytes, unclassifiedBytes, result.UnaccountedBytes, consistency);
    }

    /// <param name="consistency">Optional — when it carries <see cref="AccountingConsistency.LogicalExceedsDriveUsed"/>
    /// and <paramref name="accountedPercentage"/> is null, the result is the distinct <see cref="ScanQuality.AccountingBasisDiffers"/>
    /// rather than <see cref="ScanQuality.Insufficient"/>, so a caller never has to guess why the percentage is
    /// unavailable. Defaults to <see cref="AccountingConsistency.Consistent"/> for callers (such as History, which
    /// has no stored consistency data) that cannot supply it — those callers fall back to the older, coarser
    /// <see cref="ScanQuality.Insufficient"/> behavior for a null percentage.</param>
    public static ScanQuality QualityFor(double? accountedPercentage, AccountingConsistency consistency = AccountingConsistency.Consistent)
    {
        if (accountedPercentage is null)
            return consistency.HasFlag(AccountingConsistency.LogicalExceedsDriveUsed) ? ScanQuality.AccountingBasisDiffers : ScanQuality.Insufficient;
        return accountedPercentage switch
        {
            >= ExcellentCoverageThreshold => ScanQuality.Excellent,
            >= GoodCoverageThreshold => ScanQuality.Good,
            >= InsufficientCoverageThreshold => ScanQuality.Partial,
            _ => ScanQuality.Insufficient
        };
    }
}

/// <summary>
/// Phase 7.2.4: what became of a drive's used space after a scan, kept honestly separate from the file-tree
/// classification coverage in <see cref="ScanAccountingSummary"/>. <see cref="UniqueAllocatedBytes"/> — not
/// logical (namespace) bytes — is the basis compared against <see cref="DriveUsedBytes"/>, because both are
/// real on-disk allocation measurements; logical size is not, and comparing it against drive-used is exactly
/// the basis mismatch <see cref="ScanAccountingSummary"/> already guards against for its own percentage.
/// <para/>
/// Every numeric field here is either read directly through a supported Windows API or a defensible
/// subtraction between two measured figures — never a fabricated estimate of NTFS internals, System Volume
/// Information, restore points, shadow copies, or reserved space. When a figure cannot be measured or safely
/// derived, the field is null, never a guessed number. <see cref="UnresolvedRemainderBytes"/> is an accounting
/// gap, not a claim about what it contains — <see cref="Explanations"/> names the specific reasons (permission
/// limits, a basis mismatch, data changing mid-scan) without ever asserting the remainder is reclaimable.
/// </summary>
public sealed record VolumeRemainderSummary(
    long? DriveCapacityBytes, long? DriveUsedBytes, long? DriveFreeBytes,
    long ObservedLogicalBytes, long? ObservedAllocatedBytes, long? UniqueAllocatedBytes,
    long InaccessibleRootCount, long? UnresolvedRemainderBytes, AccountingConsistency Consistency,
    IReadOnlyList<string> Explanations);

public static class VolumeRemainderAccounting
{
    /// <summary>
    /// Summarizes the drive-used remainder for a finished scan. <paramref name="drive"/> is optional and, when
    /// supplied, provides capacity/free (not carried on <see cref="ScanResult"/> itself) from the same trusted
    /// drive-discovery data already used elsewhere — never re-measured or estimated here.
    /// </summary>
    public static VolumeRemainderSummary Summarize(ScanResult result, DriveSummary? drive = null)
    {
        var driveUsed = drive?.UsedBytes ?? result.DriveUsedBytes;
        var driveCapacity = drive?.CapacityBytes;
        var driveFree = drive?.FreeBytes;
        var observedAllocated = result.Allocation?.AllocatedBytesObserved;
        var uniqueAllocated = result.Allocation?.UniqueAllocatedBytesObserved;
        var consistency = result.Allocation?.Consistency ?? AccountingConsistency.Consistent;
        if (result.Coverage.InaccessibleEntries > 0) consistency |= AccountingConsistency.PermissionLimited;

        // "accounted bytes + unresolved remainder = drive used bytes" only when the two figures share a basis
        // (both are real on-disk allocation). When unique allocated bytes exceeds drive-used — a genuine
        // possibility (hard links resolved differently than the OS's own accounting, or a basis/timing
        // difference) — no remainder is computed at all rather than silently clamped to zero or negative.
        long? unresolved = null;
        if (driveUsed is > 0 && uniqueAllocated is { } allocated)
        {
            if (allocated > driveUsed.Value) consistency |= AccountingConsistency.AccountingBasisMismatch;
            else unresolved = driveUsed.Value - allocated;
        }

        return new(driveCapacity, driveUsed, driveFree, result.LogicalBytesObserved, observedAllocated,
            uniqueAllocated, result.Coverage.InaccessibleEntries, unresolved, consistency, Explain(consistency, unresolved));
    }

    /// <summary>
    /// Short, exact-wording status labels describing an unresolved remainder — never a fabricated number, never
    /// a claim of reclaimability. Callers combine these with the raw byte figures for display; deliberately
    /// returned as plain labels rather than one pre-formatted sentence so a caller never has to parse prose to
    /// extract "was this permission-limited," and so this never embeds a byte-formatting concern that belongs
    /// to the presentation layer.
    /// </summary>
    private static List<string> Explain(AccountingConsistency consistency, long? unresolvedRemainderBytes)
    {
        var explanations = new List<string>();
        if (unresolvedRemainderBytes is not null)
        {
            explanations.Add("Not observed by this scan");
            explanations.Add("Volume-managed or filesystem-managed storage may be included");
        }
        if (consistency.HasFlag(AccountingConsistency.PermissionLimited)) explanations.Add("Permission-limited");
        if (consistency.HasFlag(AccountingConsistency.AccountingBasisMismatch)) explanations.Add("Accounting-basis difference");
        if (consistency.HasFlag(AccountingConsistency.ChangedDuringScan)) explanations.Add("Data changed while scanning");
        if (consistency.HasFlag(AccountingConsistency.AllocatedDataIncomplete)) explanations.Add("Allocated-size data incomplete");
        return explanations;
    }
}
