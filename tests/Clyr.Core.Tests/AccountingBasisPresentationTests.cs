using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>
/// Phase (post-Administrator-Retry accounting and presentation correction): a real successful retry pushed
/// combined logical bytes slightly over the drive's physical used-bytes basis (hard links, sparse files,
/// compression, filesystem-managed storage), which the presentation layer then rendered as a negative "Not
/// observed" value and a misleading "Limited coverage" badge. These tests lock in the corrected accounting model:
/// coverage becomes unavailable (never negative, never fake, never "Limited coverage") without ever treating the
/// retry as a failure.
/// </summary>
public sealed class AccountingBasisPresentationTests
{
    [Fact]
    public void CompatibleAccountingProducesNormalCoverageAndNonNegativeNotObserved()
    {
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 2500, driveUsed: 3000);
        var summary = ScanAccounting.Summarize(result);

        Assert.Equal(2500d * 100d / 3000, summary.AccountedPercentage);
        Assert.Equal(ScanQuality.Good, summary.Quality);
        Assert.Equal(500, summary.UnaccountedDriveBytes);
        Assert.Equal(500, summary.PresentableUnaccountedDriveBytes);
        Assert.False(summary.Consistency.HasFlag(AccountingConsistency.LogicalExceedsDriveUsed));
    }

    [Fact]
    public void LogicalObservedGreaterThanDriveUsedProducesTheConsistencyFlag()
    {
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 276_010_000_000, driveUsed: 275_920_000_000);
        var summary = ScanAccounting.Summarize(result);
        Assert.True(summary.Consistency.HasFlag(AccountingConsistency.LogicalExceedsDriveUsed));
    }

    [Fact]
    public void LogicalOverPhysicalNeverProducesANegativeUserFacingUnobservedValue()
    {
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 276_010_000_000, driveUsed: 275_920_000_000);
        var summary = ScanAccounting.Summarize(result);
        Assert.True(summary.UnaccountedDriveBytes < 0); // raw diagnostic value stays negative, by design
        Assert.Null(summary.PresentableUnaccountedDriveBytes); // never shown to a user as a negative number
    }

    [Fact]
    public void LogicalOverPhysicalProducesUnavailableCoverage()
    {
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 276_010_000_000, driveUsed: 275_920_000_000);
        var summary = ScanAccounting.Summarize(result);
        Assert.Null(summary.AccountedPercentage);
    }

    [Fact]
    public void LogicalOverPhysicalDoesNotProduceLimitedCoverage()
    {
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 276_010_000_000, driveUsed: 275_920_000_000);
        var summary = ScanAccounting.Summarize(result);
        Assert.Equal(ScanQuality.AccountingBasisDiffers, summary.Quality);
        Assert.NotEqual(ScanQuality.Partial, summary.Quality);
        Assert.NotEqual(ScanQuality.Insufficient, summary.Quality);
    }

    [Fact]
    public void LogicalOverPhysicalDoesNotProduce100PercentCoverage()
    {
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 276_010_000_000, driveUsed: 275_920_000_000);
        var summary = ScanAccounting.Summarize(result);
        Assert.True(summary.AccountedPercentage is null or <= 100);
    }

    [Fact]
    public void LogicalOverPhysicalDoesNotSilentlyPresentZeroBytesAsKnownUnobservedStorage()
    {
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 276_010_000_000, driveUsed: 275_920_000_000);
        var summary = ScanAccounting.Summarize(result);
        // Must be genuinely absent (null / "Not available"), never silently floored to a false 0 B.
        Assert.Null(summary.PresentableUnaccountedDriveBytes);
        Assert.NotEqual(0, summary.PresentableUnaccountedDriveBytes.GetValueOrDefault(-1));
    }

    [Fact]
    public void CompatibleAllocatedAccountingRemainsSeparatelyAvailable()
    {
        var allocation = new AllocationAccounting(200_000_000_000, 190_000_000_000, 0, 2, 5, 0, 900_000, AccountingConsistency.Consistent);
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 276_010_000_000, driveUsed: 275_920_000_000, allocation: allocation);
        var summary = ScanAccounting.Summarize(result);
        // Logical-vs-physical coverage is unavailable, but the allocated figures remain intact and readable —
        // logical and physical bases are never mixed together into one number.
        Assert.Null(summary.AccountedPercentage);
        Assert.Equal(200_000_000_000, result.Allocation!.AllocatedBytesObserved);
        Assert.Equal(190_000_000_000, result.Allocation.UniqueAllocatedBytesObserved);
    }

    [Fact]
    public void LogicalAndPhysicalValuesRemainSeparatelyAvailableOnTheResultItself()
    {
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 276_010_000_000, driveUsed: 275_920_000_000);
        Assert.Equal(276_010_000_000, result.LogicalBytesObserved);
        Assert.Equal(275_920_000_000, result.DriveUsedBytes);
    }

    [Fact]
    public void QualityForDefaultsToInsufficientWithoutConsistencyInformation()
    {
        // Callers (such as History) that cannot supply AccountingConsistency fall back to the older, coarser
        // Insufficient behavior for a null percentage — never AccountingBasisDiffers without evidence for it.
        Assert.Equal(ScanQuality.Insufficient, ScanAccounting.QualityFor(null));
        Assert.Equal(ScanQuality.AccountingBasisDiffers, ScanAccounting.QualityFor(null, AccountingConsistency.LogicalExceedsDriveUsed));
    }

    [Fact]
    public void ExportNeverEmitsANegativeUnaccountedBytesValue()
    {
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 276_010_000_000, driveUsed: 275_920_000_000);
        Assert.True(result.UnaccountedBytes < 0); // the raw value stays negative internally, by design

        var json = new ScanReportExporter().Serialize(result);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var unaccounted = document.RootElement.GetProperty("scan").GetProperty("unaccountedBytes");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, unaccounted.ValueKind);
    }

    [Fact]
    public void ExportPreservesAccountingConsistencyAndSchemaCompatibility()
    {
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 276_010_000_000, driveUsed: 275_920_000_000)
            with
        { Allocation = new AllocationAccounting(0, 0, 0, 0, 0, 0, 0, AccountingConsistency.LogicalExceedsDriveUsed) };
        var json = new ScanReportExporter().Serialize(result);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        var consistency = document.RootElement.GetProperty("scan").GetProperty("allocation").GetProperty("consistency").GetString();
        Assert.Contains("logical-exceeds-drive-used", consistency, StringComparison.OrdinalIgnoreCase);
    }
}
