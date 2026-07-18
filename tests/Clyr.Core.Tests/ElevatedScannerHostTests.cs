using System.Collections.Immutable;
using System.IO.Pipes;
using System.Runtime.Versioning;
using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

[CollectionDefinition("ElevatedScannerHostSequential", DisableParallelization = true)]
public sealed class ElevatedScannerHostSequentialMarker;

/// <summary>Phase 7.2.6E: the composition wiring between the bounded IPC transport and the read-only retry
/// engine. Real named pipes, but entirely in-process — no separate process, no elevation, no real filesystem or
/// drive access. Every server here is handed a tiny in-memory fake <see cref="IFileSystemEnumerator"/>.</summary>
[Collection("ElevatedScannerHostSequential")]
[SupportedOSPlatform("windows")]
public sealed class ElevatedScannerHostTests
{
    private static readonly Guid ScanId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private const string DriveIdentity = "drive-fingerprint-host";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-17T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private static readonly ElevatedScanIpcServerTimeouts FastServerTimeouts =
        new(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4));
    private static readonly ElevatedScanIpcClientTimeouts FastClientTimeouts =
        new(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4));

    [Fact]
    public async Task ValidRequestProducesOneTypedResponse()
    {
        const string root = "C:\\Data\\Alpha";
        var fs = new FakeFileSystem(directory => directory == root ? [] : throw Unexpected(directory));
        var pipeName = ElevatedScanPipeName.New();
        var serverTask = ElevatedScannerHost.RunOneShotAsync(pipeName, Engine(fs), FastServerTimeouts, new FixedClock(Now), CancellationToken.None);

        var request = BuildRequest([RootFixture(root)]);
        var response = await ElevatedScanIpcTransport.SendRequestAsync(pipeName, request, FastClientTimeouts, CancellationToken.None);
        var hostResult = await serverTask;

        Assert.Equal(ElevatedScannerHostOutcome.ResponseSent, hostResult.Outcome);
        Assert.NotNull(hostResult.Response);
        Assert.Equal(ElevatedScanRetryOutcome.Completed, response.Outcome);
    }

    [Fact]
    public async Task ResponseProtocolVersionAndNonceMatchTheRequest()
    {
        const string root = "C:\\Data\\Alpha";
        var fs = new FakeFileSystem(directory => directory == root ? [] : throw Unexpected(directory));
        var pipeName = ElevatedScanPipeName.New();
        var serverTask = ElevatedScannerHost.RunOneShotAsync(pipeName, Engine(fs), FastServerTimeouts, new FixedClock(Now), CancellationToken.None);

        var request = BuildRequest([RootFixture(root)]);
        var response = await ElevatedScanIpcTransport.SendRequestAsync(pipeName, request, FastClientTimeouts, CancellationToken.None);
        await serverTask;

        Assert.Equal(request.ProtocolVersion, response.ProtocolVersion);
        Assert.Equal(request.Nonce, response.Nonce);
    }

    [Fact]
    public async Task InvalidRequestInvokesTheMetadataProviderZeroTimes()
    {
        var fs = new FakeFileSystem(directory => throw Unexpected(directory));
        var pipeName = ElevatedScanPipeName.New();
        var serverTask = ElevatedScannerHost.RunOneShotAsync(pipeName, Engine(fs), FastServerTimeouts, new FixedClock(Now), CancellationToken.None);

        var invalidRequest = BuildRequest([RootFixture("C:\\Data\\Alpha")]) with { ExpiresAtUtc = Now.AddMinutes(-1) };
        var response = await ElevatedScanIpcTransport.SendRequestAsync(pipeName, invalidRequest, FastClientTimeouts, CancellationToken.None);
        await serverTask;

        Assert.Equal(ElevatedScanRetryOutcome.ValidationRejected, response.Outcome);
        Assert.Empty(fs.EnumeratedDirectories);
    }

    [Fact]
    public async Task ValidationRejectedResponseIsSentSuccessfully()
    {
        var fs = new FakeFileSystem(directory => throw Unexpected(directory));
        var pipeName = ElevatedScanPipeName.New();
        var serverTask = ElevatedScannerHost.RunOneShotAsync(pipeName, Engine(fs), FastServerTimeouts, new FixedClock(Now), CancellationToken.None);

        var invalidRequest = BuildRequest([RootFixture("C:\\Data\\Alpha")]) with { ExpiresAtUtc = Now.AddMinutes(-1) };
        await ElevatedScanIpcTransport.SendRequestAsync(pipeName, invalidRequest, FastClientTimeouts, CancellationToken.None);
        var hostResult = await serverTask;

        Assert.Equal(ElevatedScannerHostOutcome.ResponseSent, hostResult.Outcome);
        Assert.Equal(ElevatedScanRetryOutcome.ValidationRejected, hostResult.Response!.Outcome);
    }

    [Fact]
    public async Task PartiallyCompletedResponseIsSentSuccessfully()
    {
        const string root = "C:\\Data\\Alpha";
        var fs = new FakeFileSystem(_ => throw new UnauthorizedAccessException("denied"));
        var pipeName = ElevatedScanPipeName.New();
        var serverTask = ElevatedScannerHost.RunOneShotAsync(pipeName, Engine(fs), FastServerTimeouts, new FixedClock(Now), CancellationToken.None);

        var request = BuildRequest([RootFixture(root)]);
        var response = await ElevatedScanIpcTransport.SendRequestAsync(pipeName, request, FastClientTimeouts, CancellationToken.None);
        var hostResult = await serverTask;

        Assert.Equal(ElevatedScannerHostOutcome.ResponseSent, hostResult.Outcome);
        Assert.Equal(ElevatedScanRetryOutcome.PartiallyCompleted, response.Outcome);
    }

    [Fact]
    public async Task OperationTimeoutResponseIsSentSuccessfully()
    {
        // A very short Operation timeout (bounded, deterministic) plus one small Thread.Sleep between two
        // yielded entries — by the time the second entry is recorded and the engine loop re-checks
        // cancellation, the Operation-phase timeout has already fired. Phase 7.2.6H2E: the engine itself still
        // cooperatively returns Cancelled (it cannot tell its own deadline apart from real external
        // cancellation), but ElevatedScanIpcTransport.RunOneShotAsync now correctly relabels this as
        // ElevatedScanRetryOutcome.TimedOut, since the caller's own token (CancellationToken.None here) was
        // never itself cancelled — only this transport's internal Operation-budget timer fired.
        const string root = "C:\\Data\\Alpha";
        var fs = new FakeFileSystem(directory => directory == root ? SlowSequence() : throw Unexpected(directory));
        var shortOperationTimeouts = FastServerTimeouts with { Operation = TimeSpan.FromMilliseconds(50) };
        var pipeName = ElevatedScanPipeName.New();
        var serverTask = ElevatedScannerHost.RunOneShotAsync(pipeName, Engine(fs), shortOperationTimeouts, new FixedClock(Now), CancellationToken.None);

        var request = BuildRequest([RootFixture(root)]);
        var response = await ElevatedScanIpcTransport.SendRequestAsync(pipeName, request, FastClientTimeouts, CancellationToken.None);
        var hostResult = await serverTask;

        Assert.Equal(ElevatedScannerHostOutcome.ResponseSent, hostResult.Outcome);
        Assert.Equal(ElevatedScanRetryOutcome.TimedOut, response.Outcome);
    }

    [Fact]
    public async Task FatalEngineResultReturnsABoundedFailedResponse()
    {
        var fs = new FakeFileSystem(_ => throw new NotSupportedException("internal-secret-detail-should-not-leak"));
        var pipeName = ElevatedScanPipeName.New();
        var serverTask = ElevatedScannerHost.RunOneShotAsync(pipeName, Engine(fs), FastServerTimeouts, new FixedClock(Now), CancellationToken.None);

        var request = BuildRequest([RootFixture("C:\\Data\\Alpha")]);
        var response = await ElevatedScanIpcTransport.SendRequestAsync(pipeName, request, FastClientTimeouts, CancellationToken.None);
        var hostResult = await serverTask;

        Assert.Equal(ElevatedScannerHostOutcome.ResponseSent, hostResult.Outcome);
        Assert.Equal(ElevatedScanRetryOutcome.Failed, response.Outcome);
        Assert.All(response.BoundedDiagnostics, item => Assert.DoesNotContain("internal-secret-detail-should-not-leak", item, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ServerProcessesExactlyOneRequestAndExits()
    {
        const string root = "C:\\Data\\Alpha";
        var fs = new FakeFileSystem(directory => directory == root ? [] : throw Unexpected(directory));
        var pipeName = ElevatedScanPipeName.New();
        var serverTask = ElevatedScannerHost.RunOneShotAsync(pipeName, Engine(fs), FastServerTimeouts, new FixedClock(Now), CancellationToken.None);

        var request = BuildRequest([RootFixture(root)]);
        await ElevatedScanIpcTransport.SendRequestAsync(pipeName, request, FastClientTimeouts, CancellationToken.None);
        var hostResult = await serverTask;

        Assert.Equal(ElevatedScannerHostOutcome.ResponseSent, hostResult.Outcome);
        Assert.Equal(1, fs.EnumeratedDirectories.Count(item => item == root));
    }

    [Fact]
    public async Task ConnectionTimeoutReturnsTheDocumentedHostOutcome()
    {
        var fs = new FakeFileSystem(directory => throw Unexpected(directory));
        var pipeName = ElevatedScanPipeName.New();
        var shortConnectionTimeouts = FastServerTimeouts with { Connection = TimeSpan.FromMilliseconds(150) };

        var hostResult = await ElevatedScannerHost.RunOneShotAsync(pipeName, Engine(fs), shortConnectionTimeouts, new FixedClock(Now), CancellationToken.None);

        Assert.Equal(ElevatedScannerHostOutcome.ConnectionOrRequestTimeout, hostResult.Outcome);
        Assert.Null(hostResult.Response);
    }

    [Fact]
    public async Task CancellationStopsTheHost()
    {
        var fs = new FakeFileSystem(directory => throw Unexpected(directory));
        var pipeName = ElevatedScanPipeName.New();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var hostResult = await ElevatedScannerHost.RunOneShotAsync(pipeName, Engine(fs), FastServerTimeouts, new FixedClock(Now), cts.Token);

        Assert.Equal(ElevatedScannerHostOutcome.Cancelled, hostResult.Outcome);
        Assert.Null(hostResult.Response);
        Assert.Empty(fs.EnumeratedDirectories);
    }

    [Fact]
    public async Task NoSecondRequestIsAccepted()
    {
        const string root = "C:\\Data\\Alpha";
        var fs = new FakeFileSystem(directory => directory == root ? [] : throw Unexpected(directory));
        var pipeName = ElevatedScanPipeName.New();
        var serverTask = ElevatedScannerHost.RunOneShotAsync(pipeName, Engine(fs), FastServerTimeouts, new FixedClock(Now), CancellationToken.None);

        var request = BuildRequest([RootFixture(root)]);
        await ElevatedScanIpcTransport.SendRequestAsync(pipeName, request, FastClientTimeouts, CancellationToken.None);
        await serverTask; // the host has already returned exactly once and holds no further listener.

        using var secondClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondClient.ConnectAsync(cts.Token));
    }

    private static ElevatedMetadataRetryEngine Engine(IFileSystemEnumerator fs) => new(fs, new FixedClock(Now));

    private static InvalidOperationException Unexpected(string directory) => new($"Unexpected directory enumerated: {directory}");

    private static IEnumerable<FileSystemEntry> SlowSequence()
    {
        yield return new FileSystemEntry("C:\\Data\\Alpha\\file0.txt", 100, EntryTraits.None);
        Thread.Sleep(150);
        yield return new FileSystemEntry("C:\\Data\\Alpha\\file1.txt", 200, EntryTraits.None);
    }

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
        public List<string> EnumeratedDirectories { get; } = [];
        public IEnumerable<FileSystemEntry> Enumerate(string directory)
        {
            EnumeratedDirectories.Add(directory);
            return enumerate(directory);
        }
    }
}
