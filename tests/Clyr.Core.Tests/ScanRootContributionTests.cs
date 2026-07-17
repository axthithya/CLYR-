using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>Phase 7.2.6G2: bounded, per-top-level-root accounting recorded by Deep Analysis. No test here
/// touches a real drive — every fixture is a tiny in-memory fake <see cref="IFileSystemEnumerator"/>.</summary>
public sealed class ScanRootContributionTests
{
    [Fact]
    public async Task FullScanRecordsACompletedRootContribution()
    {
        var fs = new FakeFileSystem(directory => directory switch
        {
            "C:\\" => [new FileSystemEntry("C:\\Alpha", 0, EntryTraits.Directory)],
            "C:\\Alpha" => [new FileSystemEntry("C:\\Alpha\\a.txt", 100, EntryTraits.None)],
            _ => throw Unexpected(directory)
        });

        var result = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);

        var contribution = Assert.Single(result.RootContributions);
        Assert.Equal(ScanRootEnumerationState.Completed, contribution.EnumerationState);
        Assert.Equal(100, contribution.LogicalBytesObserved);
    }

    [Fact]
    public async Task RootInaccessibleBeforeEnumerationRecordsZeroByteInaccessibleAtRoot()
    {
        var fs = new FakeFileSystem(directory => directory switch
        {
            "C:\\" => [new FileSystemEntry("C:\\Beta", 0, EntryTraits.Directory)],
            "C:\\Beta" => throw new UnauthorizedAccessException("denied"),
            _ => throw Unexpected(directory)
        });

        var result = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);

        var contribution = Assert.Single(result.RootContributions);
        Assert.Equal(ScanRootEnumerationState.InaccessibleAtRoot, contribution.EnumerationState);
        Assert.Equal(0, contribution.LogicalBytesObserved);
    }

    [Fact]
    public async Task PartiallyScannedRootRecordsPartiallyObservedAndTruthfulBytes()
    {
        var fs = new FakeFileSystem(directory => directory switch
        {
            "C:\\" => [new FileSystemEntry("C:\\Gamma", 0, EntryTraits.Directory)],
            "C:\\Gamma" =>
            [
                new FileSystemEntry("C:\\Gamma\\a.txt", 50, EntryTraits.None),
                new FileSystemEntry("C:\\Gamma\\Sub", 0, EntryTraits.Directory)
            ],
            "C:\\Gamma\\Sub" => throw new UnauthorizedAccessException("denied"),
            _ => throw Unexpected(directory)
        });

        var result = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);

        var contribution = Assert.Single(result.RootContributions);
        Assert.Equal(ScanRootEnumerationState.PartiallyObserved, contribution.EnumerationState);
        Assert.Equal(50, contribution.LogicalBytesObserved);
        Assert.True(contribution.InaccessibleEntryCount > 0);
    }

    [Fact]
    public async Task ParentAndChildContributionsAreNotIndependentlyDoubleCounted()
    {
        var fs = new FakeFileSystem(directory => directory switch
        {
            "C:\\" => [new FileSystemEntry("C:\\Delta", 0, EntryTraits.Directory)],
            "C:\\Delta" => [new FileSystemEntry("C:\\Delta\\Child", 0, EntryTraits.Directory)],
            "C:\\Delta\\Child" => [new FileSystemEntry("C:\\Delta\\Child\\a.txt", 30, EntryTraits.None)],
            _ => throw Unexpected(directory)
        });

        var result = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);

        // Only the top-level root gets its own record — the nested "Child" directory is folded into Delta's
        // own total, never recorded as a second, independently-additive contribution.
        var contribution = Assert.Single(result.RootContributions);
        Assert.Equal("C:\\Delta", contribution.RootPath);
        Assert.Equal(30, contribution.LogicalBytesObserved);
    }

    [Fact]
    public async Task RootIdentityIsDeterministic()
    {
        var fs = new FakeFileSystem(directory => directory switch
        {
            "C:\\" => [new FileSystemEntry("C:\\Epsilon", 0, EntryTraits.Directory)],
            "C:\\Epsilon" => [],
            _ => throw Unexpected(directory)
        });

        var first = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);
        var second = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);

        Assert.Equal(first.RootContributions[0].CanonicalRootIdentity, second.RootContributions[0].CanonicalRootIdentity);
        Assert.Equal("C:\\EPSILON", first.RootContributions[0].CanonicalRootIdentity);
    }

    [Fact]
    public async Task ContributionCollectionIsBounded()
    {
        const int topLevelCount = ScanRootContributionLimits.MaxContributions + 10;
        var fs = new FakeFileSystem(directory =>
        {
            if (directory == "C:\\")
                return Enumerable.Range(0, topLevelCount).Select(index => new FileSystemEntry($"C:\\Dir{index}", 0, EntryTraits.Directory));
            return [];
        });

        var result = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);

        Assert.True(result.RootContributions.Count <= ScanRootContributionLimits.MaxContributions);
    }

    private static InvalidOperationException Unexpected(string directory) => new($"Unexpected directory enumerated: {directory}");

    private sealed class FakeDrives : IDriveDiscovery
    {
        public IReadOnlyList<DriveSummary> Discover() => [new("C:\\", "Fixture", "NTFS", DriveKind.Fixed, true, true, true, "Supported", 1_000_000, 500_000, 500_000)];
    }

    private sealed class FakeFileSystem(Func<string, IEnumerable<FileSystemEntry>> enumerate) : IFileSystemEnumerator
    {
        public IEnumerable<FileSystemEntry> Enumerate(string directory) => enumerate(directory);
    }
}
