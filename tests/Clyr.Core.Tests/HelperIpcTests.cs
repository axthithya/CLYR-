using System.Collections.Immutable;
using System.Runtime.Versioning;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Core.Execution;

namespace Clyr.Core.Tests;

public sealed class HelperIpcTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "clyr-helper-test-" + Guid.NewGuid().ToString("N"));
    private readonly MutableClock clock = new(new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero));

    public HelperIpcTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }

    [Fact]
    public void HandlerRemovesOnlyValidatedTargetsAndReportsCompleted()
    {
        var stale = WriteStale("old.tmp");
        var request = BuildRequest([Target("item-1", "target-1", stale, new FileInfo(stale).Length, StaleTime())]);

        var response = ElevatedHelperRequestHandler.Handle(request, clock, "1.0.0-test", CancellationToken.None);

        Assert.Equal(HelperResponseStatus.Completed, response.Status);
        Assert.Equal(ExecutionItemOutcome.Removed, Assert.Single(response.Items).Outcome);
        Assert.False(File.Exists(stale));
    }

    [Fact]
    public void HandlerRejectsUnknownProtocolVersion()
    {
        var stale = WriteStale("old.tmp");
        var request = BuildRequest([Target("item-1", "target-1", stale, 1, StaleTime())]) with { ProtocolVersion = 999 };
        var response = ElevatedHelperRequestHandler.Handle(request, clock, "1.0.0-test", CancellationToken.None);
        Assert.Equal(HelperResponseStatus.Rejected, response.Status);
        Assert.Equal("protocol.version-mismatch", response.RejectionCode);
        Assert.True(File.Exists(stale));
    }

    [Fact]
    public void HandlerRejectsUnknownAction()
    {
        var stale = WriteStale("old.tmp");
        var request = BuildRequest([Target("item-1", "target-1", stale, new FileInfo(stale).Length, StaleTime())]) with { ActionId = "builtin.not-a-real-action" };
        var response = ElevatedHelperRequestHandler.Handle(request, clock, "1.0.0-test", CancellationToken.None);
        Assert.Equal(HelperResponseStatus.Rejected, response.Status);
        Assert.Equal("execution.unknown-action", response.RejectionCode);
    }

    [Fact]
    public void HandlerRejectsRootIdentityMismatch()
    {
        var stale = WriteStale("old.tmp");
        var request = BuildRequest([Target("item-1", "target-1", stale, new FileInfo(stale).Length, StaleTime())]) with { TrustedRootIdentity = "known-folder:local-app-data/other" };
        var response = ElevatedHelperRequestHandler.Handle(request, clock, "1.0.0-test", CancellationToken.None);
        Assert.Equal(HelperResponseStatus.Rejected, response.Status);
        Assert.Equal("execution.root-mismatch", response.RejectionCode);
    }

    [Fact]
    public void HandlerRejectsExpiredToken()
    {
        var stale = WriteStale("old.tmp");
        var request = BuildRequest([Target("item-1", "target-1", stale, new FileInfo(stale).Length, StaleTime())])
            with
        { TokenExpiresAtUtc = clock.UtcNow - TimeSpan.FromMinutes(1) };
        var response = ElevatedHelperRequestHandler.Handle(request, clock, "1.0.0-test", CancellationToken.None);
        Assert.Equal(HelperResponseStatus.Rejected, response.Status);
        Assert.Equal("token.expired", response.RejectionCode);
    }

    [Fact]
    public void HandlerRejectsOversizedManifest()
    {
        var items = Enumerable.Range(0, HelperProtocol.MaxManifestItems + 1)
            .Select(index => Target("item-" + index, "target-" + index, Path.Combine(root, "f" + index), 1, StaleTime()))
            .ToImmutableArray();
        var request = BuildRequest(items);
        var response = ElevatedHelperRequestHandler.Handle(request, clock, "1.0.0-test", CancellationToken.None);
        Assert.Equal(HelperResponseStatus.Rejected, response.Status);
        Assert.Equal("request.manifest-bounds", response.RejectionCode);
    }

    [Fact]
    public void HandlerRejectsTargetOutsideTrustedRootWithoutTouchingIt()
    {
        var outside = Path.Combine(Path.GetTempPath(), "clyr-helper-outside-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(outside, "must never be touched");
        try
        {
            var request = BuildRequest([Target("item-1", "target-1", outside, new FileInfo(outside).Length, StaleTime())]);
            var response = ElevatedHelperRequestHandler.Handle(request, clock, "1.0.0-test", CancellationToken.None);
            Assert.Equal(ExecutionItemOutcome.SkippedOutsideApprovedRoot, Assert.Single(response.Items).Outcome);
            Assert.True(File.Exists(outside));
        }
        finally { File.Delete(outside); }
    }

    [Fact]
    public void HandlerHonorsCancellationBeforeAnyRemoval()
    {
        var stale = WriteStale("old.tmp");
        var request = BuildRequest([Target("item-1", "target-1", stale, new FileInfo(stale).Length, StaleTime())]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var response = ElevatedHelperRequestHandler.Handle(request, clock, "1.0.0-test", cts.Token);
        Assert.Equal(HelperResponseStatus.Cancelled, response.Status);
        Assert.Empty(response.Items);
        Assert.True(File.Exists(stale));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task RealNamedPipeRoundTripDeliversTypedRequestAndResponse()
    {
        var stale = WriteStale("old.tmp");
        var request = BuildRequest([Target("item-1", "target-1", stale, new FileInfo(stale).Length, StaleTime())]);
        var pipeName = ElevatedHelperIpc.NewPipeName();

        var serverTask = ElevatedHelperIpc.RunOneShotAsync(pipeName, TimeSpan.FromSeconds(10),
            (received, ct) => Task.FromResult(ElevatedHelperRequestHandler.Handle(received, clock, "1.0.0-test", ct)),
            CancellationToken.None);
        var response = await ElevatedHelperIpc.SendRequestAsync(pipeName, request, TimeSpan.FromSeconds(10), CancellationToken.None);
        await serverTask;

        Assert.Equal(HelperResponseStatus.Completed, response.Status);
        Assert.Equal(request.RequestId, response.RequestId);
        Assert.Equal(ExecutionItemOutcome.Removed, Assert.Single(response.Items).Outcome);
        Assert.False(File.Exists(stale));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task NamedPipeConnectionTimesOutWhenNoServerIsListening()
    {
        var pipeName = ElevatedHelperIpc.NewPipeName();
        var request = BuildRequest([Target("item-1", "target-1", Path.Combine(root, "x"), 1, StaleTime())]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ElevatedHelperIpc.SendRequestAsync(pipeName, request, TimeSpan.FromMilliseconds(200), CancellationToken.None));
    }

    private string WriteStale(string name)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, "stale scratch data");
        var old = StaleTime();
        File.SetLastWriteTimeUtc(path, old.UtcDateTime);
        return path;
    }

    private DateTimeOffset StaleTime() => clock.UtcNow - TimeSpan.FromDays(30);

    private static HelperTargetManifestItem Target(string itemId, string targetId, string path, long bytes, DateTimeOffset lastWrite) =>
        new(itemId, targetId, path, bytes, lastWrite, IsReparsePoint: false);

    private HelperRequest BuildRequest(ImmutableArray<HelperTargetManifestItem> targets)
    {
        var capability = BuiltInExecutionActions.ClyrOwnedTempArtifacts;
        return new(HelperProtocol.Version, Guid.NewGuid(), new string('a', 32), Guid.NewGuid(),
            "S-1-5-21-1-2-3-1001", "drive-fixture", capability.ActionId, capability.TrustedRootIdentity, root,
            Guid.NewGuid().ToString("D"), new string('0', 64), clock.UtcNow, clock.UtcNow + TimeSpan.FromMinutes(2), targets);
    }

    private sealed class MutableClock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; } = start;
    }
}
