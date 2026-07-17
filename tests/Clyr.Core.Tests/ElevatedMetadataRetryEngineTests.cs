using System.Collections.Immutable;
using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

[CollectionDefinition("ElevatedMetadataRetryEngineSequential", DisableParallelization = true)]
public sealed class ElevatedMetadataRetryEngineSequentialMarker;

/// <summary>Phase 7.2.6C: the pure, in-process metadata retry engine. Every fixture here is an in-memory fake
/// <see cref="IFileSystemEnumerator"/> — no real filesystem, no drive, no process, no IPC.</summary>
[Collection("ElevatedMetadataRetryEngineSequential")]
public sealed class ElevatedMetadataRetryEngineTests
{
    private static readonly Guid ScanId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private const string DriveIdentity = "drive-fingerprint-engine";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-17T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task ValidRequestScansExactlyTheRequestedRoot()
    {
        const string root = "C:\\Data\\Alpha";
        var fs = new FakeFileSystem(directory => directory == root ? [] : throw Unexpected(directory));
        var engine = Engine(fs);

        var response = await engine.RetryAsync(BuildRequest([RootFixture(root)]), CancellationToken.None);

        Assert.Equal(ElevatedScanRetryOutcome.Completed, response.Outcome);
        Assert.Equal([root], fs.EnumeratedDirectories);
    }

    [Fact]
    public async Task TwoValidatedRootsAreEachProcessedOnce()
    {
        const string rootA = "C:\\Data\\Alpha";
        const string rootB = "C:\\Data\\Beta";
        var fs = new FakeFileSystem(directory => directory is rootA or rootB ? [] : throw Unexpected(directory));
        var engine = Engine(fs);

        var response = await engine.RetryAsync(BuildRequest([RootFixture(rootA), RootFixture(rootB)]), CancellationToken.None);

        Assert.Equal(ElevatedScanRetryOutcome.Completed, response.Outcome);
        Assert.Equal(2, response.RootsCompleted);
        Assert.Equal(1, fs.EnumeratedDirectories.Count(item => item == rootA));
        Assert.Equal(1, fs.EnumeratedDirectories.Count(item => item == rootB));
    }

    [Fact]
    public async Task InvalidRequestCallsTheProviderZeroTimes()
    {
        var fs = new FakeFileSystem(directory => throw Unexpected(directory));
        var engine = Engine(fs);
        var invalidRequest = BuildRequest([RootFixture("C:\\Data\\Alpha")]) with { ExpiresAtUtc = Now.AddMinutes(-1) };

        var response = await engine.RetryAsync(invalidRequest, CancellationToken.None);

        Assert.Equal(ElevatedScanRetryOutcome.ValidationRejected, response.Outcome);
        Assert.Empty(fs.EnumeratedDirectories);
    }

    [Fact]
    public async Task RootOutsideTheManifestIsNeverEnumerated()
    {
        const string included = "C:\\Data\\Alpha";
        const string excluded = "C:\\Data\\Outside";
        var fs = new FakeFileSystem(directory => directory == included ? [] : throw Unexpected(directory));
        var engine = Engine(fs);

        await engine.RetryAsync(BuildRequest([RootFixture(included)]), CancellationToken.None);

        Assert.DoesNotContain(excluded, fs.EnumeratedDirectories);
    }

    [Fact]
    public async Task AccessDeniedRootRemainsInRootsStillInaccessible()
    {
        const string root = "C:\\Data\\Alpha";
        var fs = new FakeFileSystem(directory => throw new UnauthorizedAccessException("denied"));
        var engine = Engine(fs);

        var response = await engine.RetryAsync(BuildRequest([RootFixture(root)]), CancellationToken.None);

        Assert.Equal(ElevatedScanRetryOutcome.PartiallyCompleted, response.Outcome);
        Assert.Equal(1, response.RootsAttempted);
        Assert.Equal(0, response.RootsCompleted);
        Assert.Equal(1, response.RootsStillInaccessible);
    }

    [Fact]
    public async Task OtherRootsContinueAfterOneAccessDeniedRoot()
    {
        const string deniedRoot = "C:\\Data\\Alpha";
        const string okRoot = "C:\\Data\\Beta";
        var fs = new FakeFileSystem(directory => directory == deniedRoot
            ? throw new UnauthorizedAccessException("denied")
            : directory == okRoot ? [] : throw Unexpected(directory));
        var engine = Engine(fs);

        var response = await engine.RetryAsync(BuildRequest([RootFixture(deniedRoot), RootFixture(okRoot)]), CancellationToken.None);

        Assert.Equal(1, response.RootsCompleted);
        Assert.Equal(1, response.RootsStillInaccessible);
        Assert.Contains(okRoot, fs.EnumeratedDirectories);
    }

    [Fact]
    public async Task ReparsePointTargetIsNotTraversed()
    {
        const string root = "C:\\Data\\Alpha";
        const string loopTarget = "C:\\Data\\Alpha\\Loop";
        var fs = new FakeFileSystem(directory => directory switch
        {
            root => [new FileSystemEntry(loopTarget, 0, EntryTraits.Directory | EntryTraits.ReparsePoint)],
            loopTarget => throw new InvalidOperationException("A reparse-point target must never be enumerated."),
            _ => throw Unexpected(directory)
        });
        var engine = Engine(fs);

        var response = await engine.RetryAsync(BuildRequest([RootFixture(root)]), CancellationToken.None);

        Assert.Equal(ElevatedScanRetryOutcome.Completed, response.Outcome);
        Assert.DoesNotContain(loopTarget, fs.EnumeratedDirectories);
    }

    [Fact]
    public async Task LoopIsPrevented()
    {
        const string root = "C:\\Data\\Alpha";
        const string sub = "C:\\Data\\Alpha\\Sub";
        // Sub contains a reparse point pointing straight back at the root — if that were ever followed, the
        // walk would never terminate.
        var fs = new FakeFileSystem(directory => directory switch
        {
            root => [new FileSystemEntry(sub, 0, EntryTraits.Directory)],
            sub => [new FileSystemEntry(root, 0, EntryTraits.Directory | EntryTraits.ReparsePoint)],
            _ => throw Unexpected(directory)
        });
        var engine = Engine(fs);

        var response = await engine.RetryAsync(BuildRequest([RootFixture(root)]), CancellationToken.None);

        Assert.Equal(ElevatedScanRetryOutcome.Completed, response.Outcome);
        Assert.Equal(1, fs.EnumeratedDirectories.Count(item => item == root));
    }

    [Fact]
    public async Task LogicalBytesAreAggregatedCorrectly()
    {
        const string root = "C:\\Data\\Alpha";
        var fs = new FakeFileSystem(directory => directory == root
            ? [File("a.txt", 100), File("b.txt", 200), File("c.txt", 300)]
            : throw Unexpected(directory));
        var engine = Engine(fs);

        var response = await engine.RetryAsync(BuildRequest([RootFixture(root)]), CancellationToken.None);

        Assert.Equal(3, response.FilesExamined);
        Assert.Equal(600, response.LogicalBytesObserved);
    }

    [Fact]
    public async Task AllocatedBytesAreAggregatedCorrectly()
    {
        const string root = "C:\\Data\\Alpha";
        var fs = new FakeFileSystem(directory => directory == root
            ? [File("a.txt", 100, allocated: 50, identity: 1), File("b.txt", 200, allocated: 60, identity: 2)]
            : throw Unexpected(directory));
        var engine = Engine(fs);

        var response = await engine.RetryAsync(BuildRequest([RootFixture(root)]), CancellationToken.None);

        Assert.Equal(110, response.AllocatedBytesObserved);
        Assert.Equal(110, response.UniqueAllocatedBytesObserved);
    }

    [Fact]
    public async Task HardLinkedAllocatedBytesAreDeduplicated()
    {
        const string root = "C:\\Data\\Alpha";
        var fs = new FakeFileSystem(directory => directory == root
            ? [File("a.txt", 100, allocated: 100, identity: 7), File("b.txt", 100, allocated: 100, identity: 7)]
            : throw Unexpected(directory));
        var engine = Engine(fs);

        var response = await engine.RetryAsync(BuildRequest([RootFixture(root)]), CancellationToken.None);

        Assert.Equal(200, response.AllocatedBytesObserved);
        Assert.Equal(100, response.UniqueAllocatedBytesObserved);
        Assert.Equal(1, response.HardLinkEntriesDetected);
    }

    [Fact]
    public async Task AllocationUnavailableMetadataIsHandledHonestly()
    {
        const string root = "C:\\Data\\Alpha";
        var fs = new FakeFileSystem(directory => directory == root
            ? [File("a.txt", 100, allocated: null), File("b.txt", 100, allocated: null)]
            : throw Unexpected(directory));
        var engine = Engine(fs);

        var response = await engine.RetryAsync(BuildRequest([RootFixture(root)]), CancellationToken.None);

        Assert.Equal(0, response.AllocatedBytesObserved);
        Assert.Equal(0, response.UniqueAllocatedBytesObserved);
        // Both unavailable-allocation occurrences aggregate into a single diagnostic entry, not two.
        var matching = response.BoundedDiagnostics.Where(item => item.StartsWith("allocation.unavailable", StringComparison.Ordinal)).ToArray();
        Assert.Single(matching);
        Assert.Contains("count=2", matching[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiagnosticsAreAggregatedAndCapped()
    {
        // Distinct stable identifiers so each root produces its own diagnostic key rather than collapsing into
        // one shared redacted-path label (see SafeRootLabel's fallback for roots with no stable identifier).
        var roots = ImmutableArray.Create(
            RootFixture("C:\\Data\\Alpha", "root-alpha"), RootFixture("C:\\Data\\Beta", "root-beta"), RootFixture("C:\\Data\\Gamma", "root-gamma"));
        var fs = new FakeFileSystem(directory => throw new UnauthorizedAccessException("denied: " + directory));
        var engine = Engine(fs);
        var request = BuildRequest(roots, maximumDiagnosticCount: 2);

        var response = await engine.RetryAsync(request, CancellationToken.None);

        Assert.Equal(3, response.RootsStillInaccessible);
        Assert.Equal(2, response.BoundedDiagnostics.Length);
    }

    [Fact]
    public async Task CancellationReturnsPartialCountersAndStopsTraversal()
    {
        const string root = "C:\\Data\\Alpha";
        using var cts = new CancellationTokenSource();
        var fs = new FakeFileSystem(directory => directory == root ? CancelingSequence(cts) : throw Unexpected(directory));
        var engine = Engine(fs);

        var response = await engine.RetryAsync(BuildRequest([RootFixture(root)]), cts.Token);

        Assert.Equal(ElevatedScanRetryOutcome.Cancelled, response.Outcome);
        // Cancellation is checked once per loop iteration, before the next metadata request is made — the item
        // already in flight when Cancel() was observed is still recorded truthfully, but no further item (the
        // sequence's third entry) is ever requested.
        Assert.Equal(2, response.FilesExamined);
        Assert.Equal(0, response.RootsCompleted);
        Assert.Equal(0, response.RootsStillInaccessible);
    }

    [Fact]
    public async Task ProviderFatalErrorReturnsBoundedFailedResponse()
    {
        const string root = "C:\\Data\\Alpha";
        var fs = new FakeFileSystem(_ => throw new NotSupportedException("internal-secret-detail-should-not-leak"));
        var engine = Engine(fs);

        var response = await engine.RetryAsync(BuildRequest([RootFixture(root)]), CancellationToken.None);

        Assert.Equal(ElevatedScanRetryOutcome.Failed, response.Outcome);
        Assert.All(response.BoundedDiagnostics, item => Assert.DoesNotContain("internal-secret-detail-should-not-leak", item, StringComparison.Ordinal));
        Assert.Contains(response.BoundedDiagnostics, item => item.StartsWith("engine.unexpected-provider-failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResponseProtocolVersionAndNonceMatchTheRequest()
    {
        const string root = "C:\\Data\\Alpha";
        var fs = new FakeFileSystem(directory => directory == root ? [] : throw Unexpected(directory));
        var engine = Engine(fs);
        var request = BuildRequest([RootFixture(root)]);

        var response = await engine.RetryAsync(request, CancellationToken.None);

        Assert.Equal(request.ProtocolVersion, response.ProtocolVersion);
        Assert.Equal(request.Nonce, response.Nonce);
    }

    private static ElevatedMetadataRetryEngine Engine(IFileSystemEnumerator fs) => new(fs, new FixedClock(Now));

    private static InvalidOperationException Unexpected(string directory) => new($"Unexpected directory enumerated: {directory}");

    private static FileSystemEntry File(string name, long logicalBytes, long? allocated = 0, ulong? identity = null) =>
        new("C:\\Data\\Alpha\\" + name, logicalBytes, EntryTraits.None, allocated, identity);

    private static IEnumerable<FileSystemEntry> CancelingSequence(CancellationTokenSource cts)
    {
        yield return new FileSystemEntry("C:\\Data\\Alpha\\file0.txt", 100, EntryTraits.None);
        cts.Cancel();
        yield return new FileSystemEntry("C:\\Data\\Alpha\\file1.txt", 200, EntryTraits.None);
    }

    private static PermissionLimitedRoot RootFixture(string path, string? stableRootIdentifier = null) =>
        new(path, ScanId, DriveIdentity, stableRootIdentifier, PermissionLimitedReasonCode.AccessDenied);

    private static ElevatedScanRetryRequest BuildRequest(ImmutableArray<PermissionLimitedRoot> roots, int maximumDiagnosticCount = 16)
    {
        var manifest = ElevatedScanManifestBuilder.Build(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots, ScanId, DriveIdentity, roots);
        return new ElevatedScanRetryRequest(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots,
            new string('a', ElevatedScanRetryProtocol.MinNonceLength), Now, Now.AddMinutes(1), ScanId, DriveIdentity,
            manifest.Value!.Digest, roots, maximumDiagnosticCount);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FakeFileSystem(Func<string, IEnumerable<FileSystemEntry>> enumerate) : IFileSystemEnumerator
    {
        public List<string> EnumeratedDirectories { get; } = [];
        public IEnumerable<FileSystemEntry> Enumerate(string directory)
        {
            EnumeratedDirectories.Add(directory);
            return enumerate(directory);
        }
    }
}
