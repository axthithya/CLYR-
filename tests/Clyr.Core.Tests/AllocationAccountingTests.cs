using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>Phase 7.2.2/7.2.3: allocated-size and hard-link-aware accounting aggregation in ScanCoordinator.</summary>
public sealed class AllocationAccountingTests
{
    [Fact]
    public async Task AllocatedBytesAreSummedSeparatelyFromLogicalBytes()
    {
        var fs = new FakeFileSystem(new()
        {
            ["C:\\"] = [File("C:\\a.bin", logical: 100, allocated: 4096), File("C:\\b.bin", logical: 200, allocated: 4096)]
        });
        var result = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);

        Assert.Equal(300, result.LogicalBytesObserved);
        Assert.NotNull(result.Allocation);
        Assert.Equal(8192, result.Allocation!.AllocatedBytesObserved);
    }

    [Fact]
    public async Task HardLinkedFilesAreCountedOnceInUniqueAllocatedBytesButBothVisibleInLogicalBytes()
    {
        // Two visible paths, same underlying physical content (same FileIdentity) — logical bytes counts both
        // paths (namespace size), but unique allocated bytes must count the physical allocation only once.
        var fs = new FakeFileSystem(new()
        {
            ["C:\\"] = [File("C:\\a.bin", logical: 500, allocated: 4096, identity: 1), File("C:\\b.bin", logical: 500, allocated: 4096, identity: 1)]
        });
        var result = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);

        Assert.Equal(1000, result.LogicalBytesObserved);
        Assert.Equal(8192, result.Allocation!.AllocatedBytesObserved);
        Assert.Equal(4096, result.Allocation.UniqueAllocatedBytesObserved);
        Assert.Equal(1, result.Allocation.VisibleHardLinkEntries);
        Assert.Equal(1, result.Allocation.UniqueFileIdentities);
        Assert.True(result.Allocation.Consistency.HasFlag(AccountingConsistency.HardLinkAdjusted));
    }

    [Fact]
    public async Task FilesWithoutIdentityAreNeverTreatedAsHardLinkedToEachOther()
    {
        var fs = new FakeFileSystem(new()
        {
            ["C:\\"] = [File("C:\\a.bin", logical: 100, allocated: 4096, identity: null), File("C:\\b.bin", logical: 100, allocated: 4096, identity: null)]
        });
        var result = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);

        Assert.Equal(0, result.Allocation!.VisibleHardLinkEntries);
        Assert.Equal(8192, result.Allocation.UniqueAllocatedBytesObserved);
    }

    [Fact]
    public async Task UnavailableAllocatedSizeIsCountedAsUnavailableNeverInventedAsZeroOrLogicalSize()
    {
        var fs = new FakeFileSystem(new()
        {
            ["C:\\"] = [File("C:\\a.bin", logical: 12345, allocated: null)]
        });
        var result = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);

        Assert.Equal(0, result.Allocation!.AllocatedBytesObserved);
        Assert.Equal(1, result.Allocation.FilesWithUnavailableAllocatedSize);
        Assert.True(result.Allocation.Consistency.HasFlag(AccountingConsistency.AllocatedDataIncomplete));
    }

    [Fact]
    public async Task SparseAndCompressedFilesAreCountedSeparately()
    {
        var fs = new FakeFileSystem(new()
        {
            ["C:\\"] = [
                new("C:\\sparse.bin", 1_000_000, EntryTraits.Sparse, AllocatedBytes: 4096),
                new("C:\\compressed.bin", 1_000_000, EntryTraits.Compressed, AllocatedBytes: 200_000)
            ]
        });
        var result = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);

        Assert.Equal(1, result.Allocation!.SparseFileCount);
        Assert.Equal(1, result.Allocation.CompressedFileCount);
    }

    private static FileSystemEntry File(string path, long logical, long? allocated, ulong? identity = null) =>
        new(path, logical, EntryTraits.None, allocated, identity);

    private sealed class FakeDrives : IDriveDiscovery
    {
        public IReadOnlyList<DriveSummary> Discover() => [new("C:\\", "Fixture", "NTFS", DriveKind.Fixed, true, true, true, "Supported", 10_000_000_000, 5_000_000_000, 5_000_000_000)];
    }

    private sealed class FakeFileSystem(Dictionary<string, FileSystemEntry[]> entries) : IFileSystemEnumerator
    {
        public IEnumerable<FileSystemEntry> Enumerate(string directory) => entries.TryGetValue(directory, out var values) ? values : [];
    }
}
