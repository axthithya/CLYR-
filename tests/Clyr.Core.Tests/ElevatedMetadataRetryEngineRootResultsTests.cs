using System.Collections.Immutable;
using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>Phase 7.2.6G2: per-root results populated by the retry engine. No real filesystem, IPC, process, or
/// UAC involved — every fixture is an in-memory fake <see cref="IFileSystemEnumerator"/>.</summary>
public sealed class ElevatedMetadataRetryEngineRootResultsTests
{
    private static readonly Guid ScanId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string DriveIdentity = "drive-fingerprint-root-results";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-18T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task ElevatedRetryProducesOneRootResultPerRequestedRoot()
    {
        const string rootA = "C:\\Data\\Alpha";
        const string rootB = "C:\\Data\\Beta";
        var fs = new FakeFileSystem(directory => directory is rootA or rootB ? [] : throw Unexpected(directory));
        var engine = new ElevatedMetadataRetryEngine(fs, new FixedClock(Now));

        var response = await engine.RetryAsync(BuildRequest([RootFixture(rootA), RootFixture(rootB)]), CancellationToken.None);

        Assert.Equal(2, response.RootResults.Length);
        Assert.Equal(ElevatedScanManifestBuilder.NormalizePath(rootA), response.RootResults[0].CanonicalRootIdentity);
        Assert.Equal(ElevatedScanManifestBuilder.NormalizePath(rootB), response.RootResults[1].CanonicalRootIdentity);
    }

    [Fact]
    public async Task AggregateResponseEqualsRootResultsAggregation()
    {
        const string rootA = "C:\\Data\\Alpha";
        const string rootB = "C:\\Data\\Beta";
        var fs = new FakeFileSystem(directory => directory switch
        {
            rootA => [new FileSystemEntry("C:\\Data\\Alpha\\a.txt", 100, EntryTraits.None, AllocatedBytes: 60, FileIdentity: 1)],
            rootB => [new FileSystemEntry("C:\\Data\\Beta\\b.txt", 200, EntryTraits.None, AllocatedBytes: 70, FileIdentity: 2)],
            _ => throw Unexpected(directory)
        });
        var engine = new ElevatedMetadataRetryEngine(fs, new FixedClock(Now));

        var response = await engine.RetryAsync(BuildRequest([RootFixture(rootA), RootFixture(rootB)]), CancellationToken.None);

        Assert.Equal(response.RootResults.Sum(item => item.FilesExamined), response.FilesExamined);
        Assert.Equal(response.RootResults.Sum(item => item.LogicalBytesObserved), response.LogicalBytesObserved);
        Assert.Equal(response.RootResults.Sum(item => item.AllocatedBytesObserved), response.AllocatedBytesObserved);
        // No hard links in this fixture (each file has its own distinct identity), so the globally unique total
        // and the sum of each root's own within-root unique total agree trivially.
        Assert.Equal(response.RootResults.Sum(item => item.UniqueAllocatedBytesObservedWithinRoot), response.UniqueAllocatedBytesObserved);
    }

    [Fact]
    public async Task CancellationReturnsBoundedPerRootResults()
    {
        const string rootA = "C:\\Data\\Alpha";
        const string rootB = "C:\\Data\\Beta";
        using var cts = new CancellationTokenSource();
        var fs = new FakeFileSystem(directory => directory switch
        {
            rootA => [],
            rootB => CancelingSequence(cts),
            _ => throw Unexpected(directory)
        });
        var engine = new ElevatedMetadataRetryEngine(fs, new FixedClock(Now));

        var response = await engine.RetryAsync(BuildRequest([RootFixture(rootA), RootFixture(rootB)]), cts.Token);

        Assert.Equal(ElevatedScanRetryOutcome.Cancelled, response.Outcome);
        // Bounded: never more than one result per requested root, even when cancellation cuts the attempt short.
        Assert.True(response.RootResults.Length <= 2);
        Assert.Contains(response.RootResults, item => item.Outcome == ElevatedRootRetryOutcome.Cancelled);
    }

    private static IEnumerable<FileSystemEntry> CancelingSequence(CancellationTokenSource cts)
    {
        yield return new FileSystemEntry("C:\\Data\\Beta\\file0.txt", 10, EntryTraits.None);
        cts.Cancel();
        yield return new FileSystemEntry("C:\\Data\\Beta\\file1.txt", 20, EntryTraits.None);
    }

    private static InvalidOperationException Unexpected(string directory) => new($"Unexpected directory enumerated: {directory}");

    private static PermissionLimitedRoot RootFixture(string path) =>
        new(path, ScanId, DriveIdentity, null, PermissionLimitedReasonCode.AccessDenied);

    private static ElevatedScanRetryRequest BuildRequest(ImmutableArray<PermissionLimitedRoot> roots)
    {
        var manifest = ElevatedScanManifestBuilder.Build(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots, ScanId, DriveIdentity, roots);
        return new ElevatedScanRetryRequest(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots,
            new string('a', ElevatedScanRetryProtocol.MinNonceLength), Now, Now.AddMinutes(1), ScanId, DriveIdentity,
            manifest.Value!.Digest, roots, 16);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FakeFileSystem(Func<string, IEnumerable<FileSystemEntry>> enumerate) : IFileSystemEnumerator
    {
        public IEnumerable<FileSystemEntry> Enumerate(string directory) => enumerate(directory);
    }
}
