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
    long ClassifiedObservedBytes, long UnclassifiedObservedBytes, long? UnaccountedDriveBytes);

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
        // Never an impossible percentage: logical observed bytes can exceed drive-used bytes (hard links,
        // sparse files, or a basis difference between the two measurements), so the numerator is clamped to
        // the drive-used basis rather than allowed to produce more than 100%.
        double? accountedPercentage = driveUsed is > 0
            ? Math.Clamp(Math.Min(result.LogicalBytesObserved, driveUsed.Value) * 100d / driveUsed.Value, 0, 100)
            : null;

        var classifiedBytes = result.Classification?.Coverage.ClassifiedBytes ?? 0;
        var unclassifiedBytes = result.Classification?.Coverage.UnknownBytes ?? Math.Max(0, result.LogicalBytesObserved - classifiedBytes);
        var observedForClassification = classifiedBytes + unclassifiedBytes;
        double? classificationPercentage = observedForClassification > 0 ? classifiedBytes * 100d / observedForClassification : null;

        return new(accountedPercentage, classificationPercentage, QualityFor(accountedPercentage),
            classifiedBytes, unclassifiedBytes, result.UnaccountedBytes);
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
