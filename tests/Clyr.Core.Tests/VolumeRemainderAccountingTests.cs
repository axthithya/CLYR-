using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>Phase 7.2.4: honest, non-fabricated volume-level remainder accounting.</summary>
public sealed class VolumeRemainderAccountingTests
{
    [Fact]
    public void ConsistentAccountingReconcilesExactly()
    {
        // accounted bytes + unresolved remainder = drive used bytes, when the bases are comparable.
        var allocation = Allocation(allocatedBytes: 700, uniqueAllocatedBytes: 700);
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 700, driveUsed: 1000, allocation: allocation);
        var summary = VolumeRemainderAccounting.Summarize(result);

        Assert.Equal(300, summary.UnresolvedRemainderBytes);
        Assert.Equal(1000, summary.UniqueAllocatedBytes!.Value + summary.UnresolvedRemainderBytes!.Value);
        Assert.Equal(AccountingConsistency.Consistent, summary.Consistency);
        Assert.Contains("Not observed by this scan", summary.Explanations);
    }

    [Fact]
    public void UnresolvedRemainderIsNeverNegativeWhenBasesAreComparable()
    {
        var allocation = Allocation(allocatedBytes: 100, uniqueAllocatedBytes: 100);
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 100, driveUsed: 100, allocation: allocation);
        var summary = VolumeRemainderAccounting.Summarize(result);

        Assert.Equal(0, summary.UnresolvedRemainderBytes);
        Assert.True(summary.UnresolvedRemainderBytes >= 0);
    }

    [Fact]
    public void UniqueAllocatedExceedingDriveUsedSuppressesTheRemainderRatherThanGoingNegative()
    {
        // A basis mismatch (not silently clamped to zero and not left implying negative "extra" storage).
        var allocation = Allocation(allocatedBytes: 5000, uniqueAllocatedBytes: 5000);
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 5000, driveUsed: 3000, allocation: allocation);
        var summary = VolumeRemainderAccounting.Summarize(result);

        Assert.Null(summary.UnresolvedRemainderBytes);
        Assert.True(summary.Consistency.HasFlag(AccountingConsistency.AccountingBasisMismatch));
        Assert.Contains("Accounting-basis difference", summary.Explanations);
        // No invalid/impossible percentage-shaped figure is exposed once the bases don't reconcile.
        Assert.DoesNotContain("Not observed by this scan", summary.Explanations);
    }

    [Fact]
    public void AllocatedAccountingUnavailableLeavesTheRemainderUnavailableRatherThanGuessed()
    {
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 700, driveUsed: 1000, allocation: null);
        var summary = VolumeRemainderAccounting.Summarize(result);

        Assert.Null(summary.UniqueAllocatedBytes);
        Assert.Null(summary.ObservedAllocatedBytes);
        Assert.Null(summary.UnresolvedRemainderBytes);
    }

    [Fact]
    public void HardLinkAdjustedConsistencyIsCarriedThroughFromAllocationAccounting()
    {
        var allocation = new AllocationAccounting(AllocatedBytesObserved: 800, UniqueAllocatedBytesObserved: 400,
            FilesWithUnavailableAllocatedSize: 0, SparseFileCount: 0, CompressedFileCount: 0,
            VisibleHardLinkEntries: 1, UniqueFileIdentities: 1, Consistency: AccountingConsistency.HardLinkAdjusted);
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 800, driveUsed: 1000, allocation: allocation);
        var summary = VolumeRemainderAccounting.Summarize(result);

        Assert.True(summary.Consistency.HasFlag(AccountingConsistency.HardLinkAdjusted));
        Assert.Equal(600, summary.UnresolvedRemainderBytes);
    }

    [Fact]
    public void ChangedDuringScanConsistencyIsCarriedThroughAndExplained()
    {
        var allocation = new AllocationAccounting(AllocatedBytesObserved: 100, UniqueAllocatedBytesObserved: 100,
            FilesWithUnavailableAllocatedSize: 0, SparseFileCount: 0, CompressedFileCount: 0,
            VisibleHardLinkEntries: 0, UniqueFileIdentities: 1, Consistency: AccountingConsistency.ChangedDuringScan);
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 100, driveUsed: 1000, allocation: allocation);
        var summary = VolumeRemainderAccounting.Summarize(result);

        Assert.True(summary.Consistency.HasFlag(AccountingConsistency.ChangedDuringScan));
        Assert.Contains("Data changed while scanning", summary.Explanations);
    }

    [Fact]
    public void PermissionLimitedResultIsFlaggedAndExplained()
    {
        var allocation = Allocation(allocatedBytes: 100, uniqueAllocatedBytes: 100);
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 100, driveUsed: 1000, inaccessible: 3, allocation: allocation);
        var summary = VolumeRemainderAccounting.Summarize(result);

        Assert.Equal(3, summary.InaccessibleRootCount);
        Assert.True(summary.Consistency.HasFlag(AccountingConsistency.PermissionLimited));
        Assert.Contains("Permission-limited", summary.Explanations);
    }

    [Fact]
    public void NoDriveCapacityOrFreeIsShownAsUnavailableRatherThanZeroWhenNoDriveSupplied()
    {
        var allocation = Allocation(allocatedBytes: 100, uniqueAllocatedBytes: 100);
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 100, driveUsed: 1000, allocation: allocation);
        var summary = VolumeRemainderAccounting.Summarize(result);

        Assert.Null(summary.DriveCapacityBytes);
        Assert.Null(summary.DriveFreeBytes);
    }

    [Fact]
    public void SupplyingADriveSummaryProvidesRealMeasuredCapacityAndFreeNeverFabricated()
    {
        var allocation = Allocation(allocatedBytes: 100, uniqueAllocatedBytes: 100);
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 100, driveUsed: 1000, allocation: allocation);
        var drive = new DriveSummary("C:\\", "Fixture", "NTFS", DriveKind.Fixed, true, true, true, "Supported", 5000, 1000, 4000);
        var summary = VolumeRemainderAccounting.Summarize(result, drive);

        Assert.Equal(5000, summary.DriveCapacityBytes);
        Assert.Equal(4000, summary.DriveFreeBytes);
        Assert.Equal(1000, summary.DriveUsedBytes);
    }

    [Fact]
    public void NoExplanationEverClaimsTheRemainderIsReclaimable()
    {
        var allocation = Allocation(allocatedBytes: 700, uniqueAllocatedBytes: 700);
        var result = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 700, driveUsed: 1000, inaccessible: 2, allocation: allocation);
        var summary = VolumeRemainderAccounting.Summarize(result);

        Assert.NotEmpty(summary.Explanations);
        Assert.All(summary.Explanations, explanation =>
        {
            Assert.DoesNotContain("reclaim", explanation, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("safe to delete", explanation, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("unknown file", explanation, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("junk", explanation, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void NoNumericValueIsFabricatedForVolumeManagedOrFilesystemInternalStorage()
    {
        // The model exposes only measured/derived numbers (capacity, used, free, logical, allocated, unique
        // allocated, unresolved remainder) — there is no field purporting to be an exact NTFS-metadata,
        // restore-point, shadow-copy, or reserved-space byte count anywhere on the record.
        var properties = typeof(VolumeRemainderSummary).GetProperties().Select(p => p.Name).ToArray();
        foreach (var forbidden in new[] { "NtfsMetadata", "SystemVolumeInformation", "RestorePoint", "ShadowCopy", "ReservedStorage", "VolumeManagedBytes" })
            Assert.DoesNotContain(forbidden, properties);
    }

    private static AllocationAccounting Allocation(long allocatedBytes, long uniqueAllocatedBytes) =>
        new(allocatedBytes, uniqueAllocatedBytes, FilesWithUnavailableAllocatedSize: 0, SparseFileCount: 0,
            CompressedFileCount: 0, VisibleHardLinkEntries: 0, UniqueFileIdentities: 1, Consistency: AccountingConsistency.Consistent);
}
