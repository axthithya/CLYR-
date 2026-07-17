using Clyr.Contracts;

namespace Clyr.Core;

/// <summary>Coverage quality bands, purely a display aid over <see cref="ScanAccountingSummary.AccountedPercentage"/>.
/// A drive with no comparable used-bytes basis (or a scan that observed essentially nothing of it) is treated as
/// <see cref="Insufficient"/> — the same label used for a genuinely low percentage — since neither case supports
/// a confident claim about drive coverage.</summary>
public enum ScanQuality { Excellent, Good, Partial, Insufficient }

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
    AccountingConsistency Consistency = AccountingConsistency.Consistent);

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

        return new(accountedPercentage, classificationPercentage, QualityFor(accountedPercentage),
            classifiedBytes, unclassifiedBytes, result.UnaccountedBytes, consistency);
    }

    public static ScanQuality QualityFor(double? accountedPercentage) => accountedPercentage switch
    {
        null => ScanQuality.Insufficient,
        >= ExcellentCoverageThreshold => ScanQuality.Excellent,
        >= GoodCoverageThreshold => ScanQuality.Good,
        >= InsufficientCoverageThreshold => ScanQuality.Partial,
        _ => ScanQuality.Insufficient
    };
}
