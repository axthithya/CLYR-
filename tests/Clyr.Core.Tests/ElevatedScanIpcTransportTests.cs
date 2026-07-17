using System.Collections.Immutable;
using System.IO.Pipes;
using System.Runtime.Versioning;
using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

[CollectionDefinition("ElevatedScanIpcSequential", DisableParallelization = true)]
public sealed class ElevatedScanIpcSequentialMarker;

/// <summary>Phase 7.2.6B: the in-process, one-shot named-pipe transport. Real named pipes, but entirely
/// in-process, current-user-only, and sequential — no separate process, no elevation, no filesystem/drive access.
/// Every server here is handed a tiny in-memory fake handler; none of them enumerate files or scan anything.</summary>
[Collection("ElevatedScanIpcSequential")]
[SupportedOSPlatform("windows")]
public sealed class ElevatedScanIpcTransportTests
{
    private static readonly Guid ScanId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private const string DriveIdentity = "drive-fingerprint-transport";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-17T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private static readonly ElevatedScanIpcServerTimeouts FastServerTimeouts =
        new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    private static readonly ElevatedScanIpcClientTimeouts FastClientTimeouts =
        new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

    [Fact]
    public async Task ServerExitsPromptlyWhenThePeerNeverReadsTheResponse()
    {
        // Regression test for the removed, unbounded WaitForPipeDrain() call: previously a peer that connected,
        // sent a request, and then simply never read the response could keep this one-shot server alive with no
        // timeout at all. The server must now complete (write + dispose) on its own, well within a couple of
        // seconds, regardless of whether anyone ever reads what it wrote.
        var pipeName = ElevatedScanPipeName.New();
        var serverTask = ElevatedScanIpcTransport.RunOneShotAsync(pipeName, FastServerTimeouts, new FixedClock(Now),
            (received, _) => Task.FromResult(CompletedResponse(received)), CancellationToken.None);

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(CancellationToken.None);
        var requestBytes = ElevatedScanIpcSerializer.SerializeRequest(ValidRequest());
        await ElevatedScanIpcTransport.WriteFrameAsync(client, requestBytes, ElevatedScanRetryProtocol.MaxRequestFrameBytes, CancellationToken.None);
        // Deliberately never read the response.

        var completed = await Task.WhenAny(serverTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(serverTask, completed);
        await serverTask; // rethrow if it actually failed, rather than just having finished
    }

    [Fact]
    public void NoUnboundedWaitForPipeDrainCallRemains()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Clyr.Core", "ElevatedScanIpc.cs"));
        // Checks for the call syntax specifically (not just the bare word), since the surrounding code
        // deliberately documents, in prose, why WaitForPipeDrain is not called here.
        Assert.DoesNotContain(".WaitForPipeDrain(", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneRequestProducesOneResponse()
    {
        // Also proves the current-user PipeSecurity descriptor does not block the same user from connecting.
        var pipeName = ElevatedScanPipeName.New();
        var request = ValidRequest();
        var serverTask = ElevatedScanIpcTransport.RunOneShotAsync(pipeName, FastServerTimeouts, new FixedClock(Now),
            (received, _) => Task.FromResult(CompletedResponse(received)), CancellationToken.None);
        var response = await ElevatedScanIpcTransport.SendRequestAsync(pipeName, request, FastClientTimeouts, CancellationToken.None);
        await serverTask;

        Assert.Equal(ElevatedScanRetryOutcome.Completed, response.Outcome);
        Assert.Equal(request.Nonce, response.Nonce);
        Assert.Equal(request.ProtocolVersion, response.ProtocolVersion);
    }

    [Fact]
    public async Task SecondConnectionAfterOneShotIsNotAccepted()
    {
        var pipeName = ElevatedScanPipeName.New();
        var request = ValidRequest();
        var serverTask = ElevatedScanIpcTransport.RunOneShotAsync(pipeName, FastServerTimeouts, new FixedClock(Now),
            (received, _) => Task.FromResult(CompletedResponse(received)), CancellationToken.None);
        await ElevatedScanIpcTransport.SendRequestAsync(pipeName, request, FastClientTimeouts, CancellationToken.None);
        await serverTask; // the server has already returned exactly once and holds no further listener.

        using var secondClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondClient.ConnectAsync(cts.Token));
    }

    [Fact]
    public async Task CancellationBeforeConnectionThrows()
    {
        var pipeName = ElevatedScanPipeName.New();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ElevatedScanIpcTransport.SendRequestAsync(pipeName, ValidRequest(), FastClientTimeouts, cts.Token));
    }

    [Fact]
    public async Task ConnectionTimeoutWhenNoServerIsListening()
    {
        var pipeName = ElevatedScanPipeName.New();
        var shortTimeouts = FastClientTimeouts with { Connect = TimeSpan.FromMilliseconds(150) };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ElevatedScanIpcTransport.SendRequestAsync(pipeName, ValidRequest(), shortTimeouts, CancellationToken.None));
    }

    [Fact]
    public async Task RequestReadTimeoutFiresWhenTheClientConnectsButNeverSends()
    {
        var pipeName = ElevatedScanPipeName.New();
        var shortServerTimeouts = FastServerTimeouts with { RequestRead = TimeSpan.FromMilliseconds(150) };
        var serverTask = ElevatedScanIpcTransport.RunOneShotAsync(pipeName, shortServerTimeouts, new FixedClock(Now),
            (received, _) => Task.FromResult(CompletedResponse(received)), CancellationToken.None);

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serverTask);
    }

    [Fact]
    public async Task OperationTimeoutFiresWhenTheHandlerHangs()
    {
        var pipeName = ElevatedScanPipeName.New();
        var shortServerTimeouts = FastServerTimeouts with { Operation = TimeSpan.FromMilliseconds(150) };
        var serverTask = ElevatedScanIpcTransport.RunOneShotAsync(pipeName, shortServerTimeouts, new FixedClock(Now),
            async (received, ct) => { await Task.Delay(Timeout.Infinite, ct); return CompletedResponse(received); },
            CancellationToken.None);
        var clientTask = ElevatedScanIpcTransport.SendRequestAsync(pipeName, ValidRequest(), FastClientTimeouts, CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serverTask);
        // Once the server aborts, the pipe closes underneath the client — its pending read fails promptly
        // rather than waiting out its own multi-second timeout.
        await Assert.ThrowsAnyAsync<Exception>(() => clientTask);
    }

    [Fact]
    public async Task ResponseWriteFailureIsHandledWhenTheClientDisconnectsBeforeReadingTheResponse()
    {
        var pipeName = ElevatedScanPipeName.New();
        var serverTask = ElevatedScanIpcTransport.RunOneShotAsync(pipeName, FastServerTimeouts, new FixedClock(Now),
            (received, _) => Task.FromResult(CompletedResponse(received)), CancellationToken.None);

        using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(CancellationToken.None);
            var requestBytes = ElevatedScanIpcSerializer.SerializeRequest(ValidRequest());
            await ElevatedScanIpcTransport.WriteFrameAsync(client, requestBytes, ElevatedScanRetryProtocol.MaxRequestFrameBytes, CancellationToken.None);
            // Disconnect immediately without ever reading the response.
        }

        // The server's response write must fail cleanly (and the awaited task complete) rather than hang.
        await Assert.ThrowsAnyAsync<Exception>(() => serverTask);
    }

    [Fact]
    public async Task MalformedRequestBytesReturnAProtocolRejectedResponse()
    {
        var pipeName = ElevatedScanPipeName.New();
        var serverTask = ElevatedScanIpcTransport.RunOneShotAsync(pipeName, FastServerTimeouts, new FixedClock(Now),
            (received, _) => Task.FromResult(CompletedResponse(received)), CancellationToken.None);

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(CancellationToken.None);
        var garbage = "{ not valid json"u8.ToArray();
        await ElevatedScanIpcTransport.WriteFrameAsync(client, garbage, ElevatedScanRetryProtocol.MaxRequestFrameBytes, CancellationToken.None);
        var responseBytes = await ElevatedScanIpcTransport.ReadFrameAsync(client, ElevatedScanRetryProtocol.MaxResponseFrameBytes, CancellationToken.None);
        var response = ElevatedScanIpcSerializer.DeserializeResponse(responseBytes);
        await serverTask;

        Assert.Equal(ElevatedScanRetryOutcome.ProtocolRejected, response.Outcome);
        Assert.Equal(string.Empty, response.Nonce);
    }

    [Fact]
    public async Task ResponseNonceMismatchIsRejectedByTheClient()
    {
        var pipeName = ElevatedScanPipeName.New();
        var request = ValidRequest();
        var serverTask = ElevatedScanIpcTransport.RunOneShotAsync(pipeName, FastServerTimeouts, new FixedClock(Now),
            (received, _) => Task.FromResult(CompletedResponse(received) with { Nonce = new string('z', ElevatedScanRetryProtocol.MinNonceLength) }),
            CancellationToken.None);

        await Assert.ThrowsAsync<ElevatedScanIpcFrameException>(() =>
            ElevatedScanIpcTransport.SendRequestAsync(pipeName, request, FastClientTimeouts, CancellationToken.None));
        await serverTask;
    }

    [Fact]
    public async Task ValidationRejectedOutcomeIsReturnedForAnInvalidRequestWithoutInvokingTheHandler()
    {
        var pipeName = ElevatedScanPipeName.New();
        var invalidRequest = ValidRequest() with { ExpiresAtUtc = Now.AddMinutes(-1) }; // already expired
        var handlerInvoked = false;
        var serverTask = ElevatedScanIpcTransport.RunOneShotAsync(pipeName, FastServerTimeouts, new FixedClock(Now),
            (received, _) => { handlerInvoked = true; return Task.FromResult(CompletedResponse(received)); },
            CancellationToken.None);
        var response = await ElevatedScanIpcTransport.SendRequestAsync(pipeName, invalidRequest, FastClientTimeouts, CancellationToken.None);
        await serverTask;

        Assert.Equal(ElevatedScanRetryOutcome.ValidationRejected, response.Outcome);
        Assert.False(handlerInvoked);
    }

    private static ElevatedScanRetryRequest ValidRequest()
    {
        var roots = ImmutableArray.Create(new PermissionLimitedRoot("C:\\Data\\Alpha", ScanId, DriveIdentity, "root-1", PermissionLimitedReasonCode.AccessDenied));
        var manifest = ElevatedScanManifestBuilder.Build(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots, ScanId, DriveIdentity, roots);
        return new ElevatedScanRetryRequest(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots,
            new string('a', ElevatedScanRetryProtocol.MinNonceLength), Now, Now.AddMinutes(1), ScanId, DriveIdentity,
            manifest.Value!.Digest, roots, 16);
    }

    private static ElevatedScanRetryResponse CompletedResponse(ElevatedScanRetryRequest request) =>
        new(request.ProtocolVersion, request.Nonce, ElevatedScanRetryOutcome.Completed, Now, Now.AddSeconds(1),
            request.PermissionLimitedRoots.Length, request.PermissionLimitedRoots.Length, 0, 10, 2, 1000, 800, 800, 0, 0, 0, []);

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
