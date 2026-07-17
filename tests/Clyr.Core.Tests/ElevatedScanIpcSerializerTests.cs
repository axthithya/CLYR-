using System.Collections.Immutable;
using System.Runtime.Versioning;
using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>Phase 7.2.6B: the bounded, closed-contract JSON codec and its raw length-prefix framing. Every test
/// here is a small in-memory byte array — no real pipe, process, or filesystem access.</summary>
public sealed class ElevatedScanIpcSerializerTests
{
    private static readonly Guid ScanId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string DriveIdentity = "drive-fingerprint-ipc";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-17T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void ValidRequestRoundTrips()
    {
        var request = ValidRequest();
        var roundTripped = ElevatedScanIpcSerializer.DeserializeRequest(ElevatedScanIpcSerializer.SerializeRequest(request));

        Assert.Equal(request.ProtocolVersion, roundTripped.ProtocolVersion);
        Assert.Equal(request.Operation, roundTripped.Operation);
        Assert.Equal(request.Nonce, roundTripped.Nonce);
        Assert.Equal(request.CreatedAtUtc, roundTripped.CreatedAtUtc);
        Assert.Equal(request.ExpiresAtUtc, roundTripped.ExpiresAtUtc);
        Assert.Equal(request.OriginalScanExecutionId, roundTripped.OriginalScanExecutionId);
        Assert.Equal(request.DriveIdentity, roundTripped.DriveIdentity);
        Assert.Equal(request.PermissionLimitedManifestDigest, roundTripped.PermissionLimitedManifestDigest);
        Assert.Equal(request.MaximumDiagnosticCount, roundTripped.MaximumDiagnosticCount);
        // ImmutableArray<T>'s own Equals compares the backing array by reference, not element-by-element, so a
        // freshly deserialized array is never "record-equal" to the original even with identical contents —
        // SequenceEqual is the correct comparison here.
        Assert.True(request.PermissionLimitedRoots.SequenceEqual(roundTripped.PermissionLimitedRoots));
    }

    [Fact]
    public void ValidResponseRoundTrips()
    {
        var response = ValidResponse();
        var roundTripped = ElevatedScanIpcSerializer.DeserializeResponse(ElevatedScanIpcSerializer.SerializeResponse(response));

        Assert.Equal(response.ProtocolVersion, roundTripped.ProtocolVersion);
        Assert.Equal(response.Nonce, roundTripped.Nonce);
        Assert.Equal(response.Outcome, roundTripped.Outcome);
        Assert.Equal(response.StartedAtUtc, roundTripped.StartedAtUtc);
        Assert.Equal(response.CompletedAtUtc, roundTripped.CompletedAtUtc);
        Assert.Equal(response.RootsAttempted, roundTripped.RootsAttempted);
        Assert.Equal(response.LogicalBytesObserved, roundTripped.LogicalBytesObserved);
        Assert.True(response.BoundedDiagnostics.SequenceEqual(roundTripped.BoundedDiagnostics));
    }

    [Fact]
    public void MalformedJsonThrowsATypedFrameException()
    {
        var bytes = "{ not valid json"u8.ToArray();
        Assert.Throws<ElevatedScanIpcFrameException>(() => ElevatedScanIpcSerializer.DeserializeRequest(bytes));
    }

    [Fact]
    public void RequestFrameAboveTheMaximumIsRejected()
    {
        var bytes = new byte[ElevatedScanRetryProtocol.MaxRequestFrameBytes + 1];
        Assert.Throws<ElevatedScanIpcFrameException>(() => ElevatedScanIpcSerializer.DeserializeRequest(bytes));
    }

    [Fact]
    public void ResponseFrameAboveTheMaximumIsRejected()
    {
        var bytes = new byte[ElevatedScanRetryProtocol.MaxResponseFrameBytes + 1];
        Assert.Throws<ElevatedScanIpcFrameException>(() => ElevatedScanIpcSerializer.DeserializeResponse(bytes));
    }

    [Fact]
    public void ExcessiveDiagnosticCountIsRejected()
    {
        var diagnostics = Enumerable.Range(0, ElevatedScanRetryProtocol.MaxDiagnosticCount + 1).Select(index => "d" + index).ToImmutableArray();
        var response = ValidResponse() with { BoundedDiagnostics = diagnostics };
        var bytes = ElevatedScanIpcSerializer.SerializeResponse(response);
        Assert.Throws<ElevatedScanIpcFrameException>(() => ElevatedScanIpcSerializer.DeserializeResponse(bytes));
    }

    [Fact]
    public void ExcessiveRootCountIsRejectedByTheCodec()
    {
        var roots = Enumerable.Range(0, ElevatedScanRetryProtocol.MaxRoots + 1)
            .Select(index => new PermissionLimitedRoot($"C:\\Data\\Root{index}", ScanId, DriveIdentity, null, PermissionLimitedReasonCode.AccessDenied))
            .ToImmutableArray();
        var request = ValidRequest() with { PermissionLimitedRoots = roots };
        var bytes = ElevatedScanIpcSerializer.SerializeRequest(request);
        Assert.Throws<ElevatedScanIpcFrameException>(() => ElevatedScanIpcSerializer.DeserializeRequest(bytes));
    }

    [Fact]
    public void TrailingSecondFrameIsRejected()
    {
        var first = ElevatedScanIpcSerializer.SerializeRequest(ValidRequest());
        var second = ElevatedScanIpcSerializer.SerializeRequest(ValidRequest());
        var concatenated = first.Concat(second).ToArray();
        Assert.Throws<ElevatedScanIpcFrameException>(() => ElevatedScanIpcSerializer.DeserializeRequest(concatenated));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task TruncatedLengthPrefixThrowsRatherThanHanging()
    {
        using var stream = new MemoryStream([1, 2]); // only 2 of the 4 length-prefix bytes are present
        await Assert.ThrowsAsync<EndOfStreamException>(() => ElevatedScanIpcTransport.ReadFrameAsync(stream, 1024, CancellationToken.None));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task NegativeDeclaredLengthIsRejected()
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(-1));
        await Assert.ThrowsAsync<ElevatedScanIpcFrameException>(() => ElevatedScanIpcTransport.ReadFrameAsync(stream, 1024, CancellationToken.None));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task DeclaredLengthAboveTheBoundIsRejectedBeforeAllocatingTheBuffer()
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(int.MaxValue));
        await Assert.ThrowsAsync<ElevatedScanIpcFrameException>(() => ElevatedScanIpcTransport.ReadFrameAsync(stream, 1024, CancellationToken.None));
    }

    private static ElevatedScanRetryRequest ValidRequest()
    {
        var roots = ImmutableArray.Create(new PermissionLimitedRoot("C:\\Data\\Alpha", ScanId, DriveIdentity, "root-1", PermissionLimitedReasonCode.AccessDenied));
        var manifest = ElevatedScanManifestBuilder.Build(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots, ScanId, DriveIdentity, roots);
        return new ElevatedScanRetryRequest(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots,
            new string('a', ElevatedScanRetryProtocol.MinNonceLength), Now, Now.AddMinutes(1), ScanId, DriveIdentity,
            manifest.Value!.Digest, roots, 16);
    }

    private static ElevatedScanRetryResponse ValidResponse() =>
        new(ElevatedScanRetryProtocol.Version, new string('a', ElevatedScanRetryProtocol.MinNonceLength), ElevatedScanRetryOutcome.Completed,
            Now, Now.AddSeconds(2), 1, 1, 0, 10, 2, 1000, 800, 800, 0, 0, 0, ["diagnostic-1"]);
}
