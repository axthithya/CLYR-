using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clyr.Contracts;

// Exposes the framing primitives (internal, not public) to Clyr.Core.Tests only, so the bounded-length-prefix
// logic can be exercised directly against small in-memory streams instead of only through real named pipes.
[assembly: InternalsVisibleTo("Clyr.Core.Tests")]

namespace Clyr.Core;

/// <summary>
/// A typed, bounded protocol-framing failure — malformed JSON, a declared frame length outside the configured
/// bound, or a collection (roots/diagnostics) exceeding its protocol-level maximum. Distinct from
/// <see cref="OperationCanceledException"/> (timeout/cancellation) and from
/// <c>ElevatedScanRetryValidationResult</c> (a well-formed request whose contents the pure validator rejects) —
/// this exception means the bytes on the wire could not be trusted as a well-formed message at all.
/// </summary>
public sealed class ElevatedScanIpcFrameException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Generates and validates the random, single-use named-pipe name for one elevated scan retry request. The name
/// carries no meaning beyond "this one request, right now" — see <see cref="ElevatedScanPipeNameFormat"/> for the
/// exact fixed shape (<c>clyr-elevated-scan-&lt;32 lowercase hex characters&gt;</c>, 128 bits of entropy). Never
/// embeds a drive path, username, scan path, execution ID, or any other identifying information.
/// </summary>
public static class ElevatedScanPipeName
{
    public static string New() =>
        ElevatedScanPipeNameFormat.Prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>True only for a name matching the exact expected shape: the fixed prefix followed by exactly
    /// <see cref="ElevatedScanPipeNameFormat.RandomHexLength"/> lowercase hexadecimal characters — nothing more,
    /// nothing less. This rejects empty names, path separators, whitespace, the wrong prefix, and oversized names
    /// as a side effect of requiring an exact character-class match over an exact total length.</summary>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.Length != ElevatedScanPipeNameFormat.ExpectedTotalLength) return false;
        if (!name.StartsWith(ElevatedScanPipeNameFormat.Prefix, StringComparison.Ordinal)) return false;
        var suffix = name.AsSpan(ElevatedScanPipeNameFormat.Prefix.Length);
        foreach (var character in suffix)
            if (character is not ((>= '0' and <= '9') or (>= 'a' and <= 'f'))) return false;
        return true;
    }
}

/// <summary>
/// Strict, bounded, closed-contract JSON framing for the elevated scan retry IPC protocol. Every type serialized
/// here is a sealed record with concrete properties (see <see cref="ElevatedScanRetryRequest"/>/
/// <see cref="ElevatedScanRetryResponse"/>); System.Text.Json's default reflection contract has no polymorphic
/// <c>$type</c> discriminator support unless explicitly opted into, and this code never does. Closure is enforced
/// on every axis System.Text.Json supports: unknown JSON properties — top-level or nested — are rejected
/// (<see cref="JsonUnmappedMemberHandling.Disallow"/>); duplicate property names are rejected rather than the
/// last one silently winning (<see cref="JsonSerializerOptions.AllowDuplicateProperties"/> = <see langword="false"/>);
/// and every enum is transmitted as a closed set of named strings via <see cref="JsonStringEnumConverter"/> with
/// <c>allowIntegerValues: false</c>, so neither a raw integer nor an unrecognized name is ever accepted. Every
/// failure of any of these checks surfaces as a <see cref="JsonException"/>, which <see cref="Parse{T}"/> turns
/// into the same typed <see cref="ElevatedScanIpcFrameException"/> used for every other malformed-frame case —
/// the <see cref="Enum.IsDefined{TEnum}(TEnum)"/> checks below are deliberate defense-in-depth, not the primary
/// enforcement mechanism.
/// </summary>
public static class ElevatedScanIpcSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        MaxDepth = 12,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static byte[] SerializeRequest(ElevatedScanRetryRequest request) =>
        EnforceBound(JsonSerializer.SerializeToUtf8Bytes(request, Options), ElevatedScanRetryProtocol.MaxRequestFrameBytes, "request");

    public static byte[] SerializeResponse(ElevatedScanRetryResponse response) =>
        EnforceBound(JsonSerializer.SerializeToUtf8Bytes(response, Options), ElevatedScanRetryProtocol.MaxResponseFrameBytes, "response");

    public static ElevatedScanRetryRequest DeserializeRequest(byte[] bytes)
    {
        EnforceBound(bytes, ElevatedScanRetryProtocol.MaxRequestFrameBytes, "request");
        var request = Parse<ElevatedScanRetryRequest>(bytes, "request");
        if (!Enum.IsDefined(request.Operation)) throw Malformed("request");
        if (!request.PermissionLimitedRoots.IsDefaultOrEmpty)
        {
            if (request.PermissionLimitedRoots.Length > ElevatedScanRetryProtocol.MaxRoots)
                throw new ElevatedScanIpcFrameException("ipc.roots-exceed-bound", "The request declares more roots than the protocol allows.");
            foreach (var root in request.PermissionLimitedRoots)
                if (!Enum.IsDefined(root.ReasonCode)) throw Malformed("request");
        }
        return request;
    }

    public static ElevatedScanRetryResponse DeserializeResponse(byte[] bytes)
    {
        EnforceBound(bytes, ElevatedScanRetryProtocol.MaxResponseFrameBytes, "response");
        var response = Parse<ElevatedScanRetryResponse>(bytes, "response");
        if (!Enum.IsDefined(response.Outcome)) throw Malformed("response");
        if (!response.BoundedDiagnostics.IsDefaultOrEmpty && response.BoundedDiagnostics.Length > ElevatedScanRetryProtocol.MaxDiagnosticCount)
            throw new ElevatedScanIpcFrameException("ipc.diagnostics-exceed-bound", "The response declares more diagnostics than the protocol allows.");
        // Phase 7.2.6G2: bounded the same way the request's own root list already is — a response cannot
        // declare more per-root results than the protocol permits roots at all.
        if (!response.RootResults.IsDefaultOrEmpty && response.RootResults.Length > ElevatedScanRetryProtocol.MaxRoots)
            throw new ElevatedScanIpcFrameException("ipc.root-results-exceed-bound", "The response declares more per-root results than the protocol allows.");
        return response;
    }

    private static T Parse<T>(byte[] bytes, string kind)
    {
        try { return JsonSerializer.Deserialize<T>(bytes, Options) ?? throw Malformed(kind); }
        catch (JsonException) { throw Malformed(kind); }
    }

    /// <summary>Rejects data above <paramref name="maxBytes"/> before any further parsing is attempted — the
    /// caller has already allocated <paramref name="data"/> itself, but no larger buffer or JSON document tree is
    /// ever built from bytes that fail this check.</summary>
    internal static byte[] EnforceBound(byte[] data, int maxBytes, string kind) => data.Length <= maxBytes
        ? data : throw new ElevatedScanIpcFrameException($"ipc.{kind}-too-large", $"The {kind} frame exceeds the bounded size limit.");

    private static ElevatedScanIpcFrameException Malformed(string kind) =>
        new($"ipc.{kind}-malformed", $"The {kind} could not be parsed as a well-formed, closed-contract message.");
}

public sealed record ElevatedScanIpcServerTimeouts(TimeSpan Connection, TimeSpan RequestRead, TimeSpan Operation, TimeSpan ResponseWrite)
{
    public static ElevatedScanIpcServerTimeouts Default { get; } =
        new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));
}

public sealed record ElevatedScanIpcClientTimeouts(TimeSpan Connect, TimeSpan Write, TimeSpan Read)
{
    public static ElevatedScanIpcClientTimeouts Default { get; } =
        new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
}

/// <summary>
/// Security note: <see cref="ElevatedScanRequestManifest.Digest"/> proves a request's fields were not tampered
/// with in transit — it is <em>not</em>, by itself, authorization to scan anything. Whatever eventually hosts
/// <see cref="ElevatedScanIpcTransport.RunOneShotAsync"/> for real must independently re-validate the operation,
/// the drive, root containment, the original-scan relationship, the protocol version, the nonce, the expiry, and
/// the manifest structure against its own live state — exactly what
/// <c>ElevatedScanRetryValidator.Validate</c> already does, and what this transport calls on every request before
/// ever invoking the supplied handler. A caller who can recompute a matching digest has proven the message is
/// intact, nothing more.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ElevatedScanIpcTransport
{
    /// <summary>
    /// Server side, connection-scoped: validate the pipe name, accept exactly one connection, read exactly one
    /// bounded request frame, independently re-validate it with <c>ElevatedScanRetryValidator</c>, invoke
    /// <paramref name="handleRequest"/> only if that validation passes, write exactly one bounded response frame,
    /// then return that same response. There is no listening loop and no second accepted connection. Never
    /// enumerates files, validates real drives, performs scanning, launches a process, writes a file, or uses
    /// the network — the pipe itself is the only I/O surface this method touches. The returned response is
    /// exactly the bytes that were sent on the wire — including a <c>ValidationRejected</c> or
    /// <c>ProtocolRejected</c> response built here without ever invoking <paramref name="handleRequest"/> — so a
    /// caller that needs to know what was actually sent (for example, to decide its own process exit code) does
    /// not have to duplicate this method's validation and rejection logic to reconstruct it.
    /// </summary>
    public static async Task<ElevatedScanRetryResponse> RunOneShotAsync(string pipeName, ElevatedScanIpcServerTimeouts timeouts, IClock clock,
        Func<ElevatedScanRetryRequest, CancellationToken, Task<ElevatedScanRetryResponse>> handleRequest,
        CancellationToken cancellationToken)
    {
        if (!ElevatedScanPipeName.IsValid(pipeName))
            throw new ArgumentException("The pipe name is not a validly generated elevated-scan pipe name.", nameof(pipeName));

        var security = new PipeSecurity();
        var currentUser = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("The current Windows user identity is unavailable.");
        security.SetAccessRule(new PipeAccessRule(currentUser, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        using var server = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, ElevatedScanRetryProtocol.MaxRequestFrameBytes, ElevatedScanRetryProtocol.MaxResponseFrameBytes, security);

        await WithTimeout(timeouts.Connection, server.WaitForConnectionAsync, cancellationToken).ConfigureAwait(false);

        var startedAtUtc = clock.UtcNow;
        ElevatedScanRetryResponse response;
        try
        {
            var requestBytes = await WithTimeout(timeouts.RequestRead,
                ct => ReadFrameAsync(server, ElevatedScanRetryProtocol.MaxRequestFrameBytes, ct), cancellationToken).ConfigureAwait(false);
            var request = ElevatedScanIpcSerializer.DeserializeRequest(requestBytes);
            var validation = ElevatedScanRetryValidator.Validate(request, clock.UtcNow);
            response = validation.IsValid
                ? await WithTimeout(timeouts.Operation, ct => handleRequest(request, ct), cancellationToken).ConfigureAwait(false)
                : Rejected(request.ProtocolVersion, request.Nonce, ElevatedScanRetryOutcome.ValidationRejected, startedAtUtc, clock.UtcNow, validation.Outcome.ToString());
        }
        catch (ElevatedScanIpcFrameException exception)
        {
            // The frame itself could not be trusted (malformed JSON, an out-of-bound declared length, or an
            // out-of-bound collection) — there is no reliable original nonce to echo back, so an empty one is
            // used and documented; the client only ever accepts this outcome without a nonce match.
            response = Rejected(ElevatedScanRetryProtocol.Version, string.Empty, ElevatedScanRetryOutcome.ProtocolRejected, startedAtUtc, clock.UtcNow, exception.Code);
        }

        // No WaitForPipeDrain here: it is synchronous, has no timeout or cancellation parameter, and blocks
        // until a connected peer has read every byte — a peer that simply never reads (by accident or by
        // design) would keep this one-shot server alive forever. The bounded, cancellable WriteFrameAsync
        // above (write + FlushAsync, both under timeouts.ResponseWrite) is the only completion guarantee this
        // method makes; once the OS has buffered the response, the server disposes the pipe and exits
        // regardless of whether the peer ever reads it.
        var responseBytes = ElevatedScanIpcSerializer.SerializeResponse(response);
        await WithTimeout(timeouts.ResponseWrite,
            ct => WriteFrameAsync(server, responseBytes, ElevatedScanRetryProtocol.MaxResponseFrameBytes, ct), cancellationToken).ConfigureAwait(false);
        return response;
    }

    /// <summary>
    /// Client side: connects once to an already-validated pipe name, sends one request frame, reads one response
    /// frame, and independently checks that the response's protocol version and nonce match the request before
    /// returning it — a mismatch on either is treated as untrustworthy and rejected, except a
    /// <see cref="ElevatedScanRetryOutcome.ProtocolRejected"/> response (which never carries the original nonce;
    /// see <see cref="RunOneShotAsync"/>). Never launches a helper process and never connects to a caller-supplied
    /// name that fails <see cref="ElevatedScanPipeName.IsValid"/>.
    /// </summary>
    public static async Task<ElevatedScanRetryResponse> SendRequestAsync(string pipeName, ElevatedScanRetryRequest request,
        ElevatedScanIpcClientTimeouts timeouts, CancellationToken cancellationToken)
    {
        if (!ElevatedScanPipeName.IsValid(pipeName))
            throw new ArgumentException("The pipe name is not a validly generated elevated-scan pipe name.", nameof(pipeName));

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await WithTimeout(timeouts.Connect, client.ConnectAsync, cancellationToken).ConfigureAwait(false);

        var requestBytes = ElevatedScanIpcSerializer.SerializeRequest(request);
        await WithTimeout(timeouts.Write,
            ct => WriteFrameAsync(client, requestBytes, ElevatedScanRetryProtocol.MaxRequestFrameBytes, ct), cancellationToken).ConfigureAwait(false);

        var responseBytes = await WithTimeout(timeouts.Read,
            ct => ReadFrameAsync(client, ElevatedScanRetryProtocol.MaxResponseFrameBytes, ct), cancellationToken).ConfigureAwait(false);
        var response = ElevatedScanIpcSerializer.DeserializeResponse(responseBytes);

        if (response.ProtocolVersion != request.ProtocolVersion)
            throw new ElevatedScanIpcFrameException("ipc.response-protocol-mismatch", "The response protocol version does not match the request.");
        if (response.Outcome != ElevatedScanRetryOutcome.ProtocolRejected && !string.Equals(response.Nonce, request.Nonce, StringComparison.Ordinal))
            throw new ElevatedScanIpcFrameException("ipc.response-nonce-mismatch", "The response nonce does not match the request nonce.");

        return response;
    }

    private static ElevatedScanRetryResponse Rejected(int protocolVersion, string nonce, ElevatedScanRetryOutcome outcome,
        DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc, string diagnosticCode) =>
        new(protocolVersion, nonce, outcome, startedAtUtc, completedAtUtc, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [diagnosticCode]);

    private static async Task WithTimeout(TimeSpan timeout, Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        await action(cts.Token).ConfigureAwait(false);
    }

    private static async Task<T> WithTimeout<T>(TimeSpan timeout, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        return await action(cts.Token).ConfigureAwait(false);
    }

    internal static async Task<byte[]> ReadFrameAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[4];
        await ReadExactAsync(stream, lengthBuffer, cancellationToken).ConfigureAwait(false);
        var length = BitConverter.ToInt32(lengthBuffer);
        if (length <= 0 || length > maxBytes)
            throw new ElevatedScanIpcFrameException("ipc.frame-length-out-of-bounds", "The IPC frame length is out of bounds.");
        var buffer = new byte[length];
        await ReadExactAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
        return buffer;
    }

    internal static async Task WriteFrameAsync(Stream stream, byte[] payload, int maxBytes, CancellationToken cancellationToken)
    {
        if (payload.Length > maxBytes) throw new ElevatedScanIpcFrameException("ipc.frame-too-large", "The IPC frame exceeds the bounded size limit.");
        var length = BitConverter.GetBytes(payload.Length);
        await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("The IPC channel closed before a complete message was received.");
            offset += read;
        }
    }
}
