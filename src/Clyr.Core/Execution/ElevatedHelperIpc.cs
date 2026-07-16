using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Clyr.Contracts;

namespace Clyr.Core.Execution;

/// <summary>
/// One-shot, current-user-restricted named-pipe transport between CLYR and the elevated helper. The pipe name
/// is random per request and never reused; the server accepts exactly one connection, reads exactly one bounded
/// frame, writes exactly one bounded frame, and exits. There is no listening service and no persistent endpoint.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ElevatedHelperIpc
{
    public static string NewPipeName() => "Clyr.Helper." + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>Server side, connection-scoped: accept a connection, read the request, then write the response on the same pipe.</summary>
    public static async Task RunOneShotAsync(string pipeName, TimeSpan timeout,
        Func<HelperRequest, CancellationToken, Task<HelperResponse>> handleRequest, CancellationToken cancellationToken)
    {
        var security = new PipeSecurity();
        var currentUser = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("The current Windows user identity is unavailable.");
        security.SetAccessRule(new PipeAccessRule(currentUser, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        using var server = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, HelperProtocol.MaxMessageBytes, HelperProtocol.MaxMessageBytes, security);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        await server.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);
        var requestBytes = await ReadFrameAsync(server, cts.Token).ConfigureAwait(false);
        var request = HelperIpcSerializer.DeserializeRequest(requestBytes);
        var response = await handleRequest(request, cts.Token).ConfigureAwait(false);
        await WriteFrameAsync(server, HelperIpcSerializer.SerializeResponse(response), cts.Token).ConfigureAwait(false);
        server.WaitForPipeDrain();
    }

    /// <summary>Client side: runs inside the main CLYR process. Connects once, sends one request, reads one response.</summary>
    public static async Task<HelperResponse> SendRequestAsync(string pipeName, HelperRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        await client.ConnectAsync(cts.Token).ConfigureAwait(false);
        await WriteFrameAsync(client, HelperIpcSerializer.SerializeRequest(request), cts.Token).ConfigureAwait(false);
        var responseBytes = await ReadFrameAsync(client, cts.Token).ConfigureAwait(false);
        return HelperIpcSerializer.DeserializeResponse(responseBytes);
    }

    private static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        if (payload.Length > HelperProtocol.MaxMessageBytes) throw new InvalidDataException("Frame exceeds the bounded size limit.");
        var length = BitConverter.GetBytes(payload.Length);
        await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[4];
        await ReadExactAsync(stream, lengthBuffer, cancellationToken).ConfigureAwait(false);
        var length = BitConverter.ToInt32(lengthBuffer);
        if (length <= 0 || length > HelperProtocol.MaxMessageBytes)
            throw new InvalidDataException("The IPC frame length is out of bounds.");
        var buffer = new byte[length];
        await ReadExactAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
        return buffer;
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
