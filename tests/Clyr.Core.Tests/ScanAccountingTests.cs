using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

public sealed class ScanAccountingTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Fact]
    public void DocumentedRegressionExampleIsLabelledInsufficient()
    {
        // Drive used: 267.61 GiB, Observed: 49.91 GiB, Accounted coverage: 18.7%, Classified observed: 44.13 GiB,
        // Unclassified observed: 5.78 GiB, Unaccounted: 217.70 GiB — the exact real-world example this
        // correction must classify as "Insufficient coverage" and recommend Deep Analysis for.
        var driveUsed = (long)(267.61 * GiB);
        var observed = (long)(44.13 * GiB) + (long)(5.78 * GiB);
        var classification = Classification(classifiedBytes: (long)(44.13 * GiB), unknownBytes: (long)(5.78 * GiB));
        var result = ScanFixtures.Result(ScanMode.Quick, ScanStatus.Completed, observed, driveUsed, classification: classification);

        var summary = ScanAccounting.Summarize(result);

        Assert.Equal(ScanQuality.Insufficient, summary.Quality);
        Assert.NotNull(summary.AccountedPercentage);
        Assert.InRange(summary.AccountedPercentage!.Value, 18.0, 19.0);
        Assert.True(summary.AccountedPercentage < ScanAccounting.InsufficientCoverageThreshold);
        // classifiedObservedBytes + unclassifiedObservedBytes = observedLogicalBytes
        Assert.Equal(observed, summary.ClassifiedObservedBytes + summary.UnclassifiedObservedBytes);
        var expectedUnaccounted = (long)(217.70 * GiB);
        Assert.InRange(summary.UnaccountedDriveBytes!.Value, expectedUnaccounted - (long)(0.01 * GiB), expectedUnaccounted + (long)(0.01 * GiB));
    }

    [Theory]
    [InlineData(95, ScanQuality.Excellent)]
    [InlineData(90, ScanQuality.Excellent)]
    [InlineData(89, ScanQuality.Good)]
    [InlineData(70, ScanQuality.Good)]
    [InlineData(69, ScanQuality.Partial)]
    [InlineData(30, ScanQuality.Partial)]
    [InlineData(29, ScanQuality.Insufficient)]
    [InlineData(0, ScanQuality.Insufficient)]
    public void QualityBandsMatchTheDocumentedThresholds(double accountedPercentage, ScanQuality expected) =>
        Assert.Equal(expected, ScanAccounting.QualityFor(accountedPercentage));

    [Fact]
    public void NoComparableDriveUsedBasisIsTreatedAsInsufficientNeverAFabricatedPercentage()
    {
        var result = ScanFixtures.Result(ScanMode.Quick, ScanStatus.Completed, observed: 5000, driveUsed: null);
        var summary = ScanAccounting.Summarize(result);
        Assert.Null(summary.AccountedPercentage);
        Assert.Equal(ScanQuality.Insufficient, summary.Quality);
    }

    [Fact]
    public void UnaccountedBytesIsNeverNegativeEvenWhenObservedExceedsDriveUsed()
    {
        // Hard links, sparse files, or a basis difference can make logical observed bytes exceed drive-used
        // bytes; the accounted percentage must clamp at 100%, never overflow past it or go negative.
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 5000, driveUsed: 3000);
        var summary = ScanAccounting.Summarize(result);
        Assert.Equal(100, summary.AccountedPercentage);
        Assert.True(summary.UnaccountedDriveBytes is null or >= 0);
    }

    [Fact]
    public void ClassificationPercentageIsSeparateFromAccountedPercentageAndNeverReadAsTotalDriveCoverage()
    {
        // A scan that classified 100% of what little it observed can still have accounted for almost none of
        // the drive — the two percentages must never collapse into one number.
        var classification = Classification(classifiedBytes: 100, unknownBytes: 0);
        var result = ScanFixtures.Result(ScanMode.Quick, ScanStatus.Completed, observed: 100, driveUsed: 1_000_000, classification: classification);
        var summary = ScanAccounting.Summarize(result);
        Assert.Equal(100, summary.ClassificationPercentage);
        Assert.True(summary.AccountedPercentage < 1);
    }

    [Fact]
    public void WithoutClassificationEverythingObservedIsUnclassifiedNeverFabricatedAsClassified()
    {
        var result = ScanFixtures.Result(ScanMode.Quick, ScanStatus.Completed, observed: 500, driveUsed: 1000, classification: null);
        var summary = ScanAccounting.Summarize(result);
        Assert.Equal(0, summary.ClassifiedObservedBytes);
        Assert.Equal(500, summary.UnclassifiedObservedBytes);
    }

    private static ClassificationResult Classification(long classifiedBytes, long unknownBytes) => new(
        [], [], new ClassificationCoverage(0, classifiedBytes, 0, unknownBytes, 0, 0, null),
        new("clyr.builtin", "1.0.0", "digest", RulePackTrust.BuiltIn, true, true, 1, "verified", "Verified", "builtin", "MIT"),
        "test", []);
}
