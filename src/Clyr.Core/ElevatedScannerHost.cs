using System.Runtime.Versioning;
using Clyr.Contracts;

namespace Clyr.Core;

/// <summary>How one one-shot elevated-scan hosting attempt ended. This is deliberately separate from
/// <see cref="ElevatedScanRetryResponse.Outcome"/>: <see cref="ResponseSent"/> only says a typed response frame
/// was successfully written to the pipe — the response's own <c>Outcome</c> (which may be
/// <c>Completed</c>, <c>PartiallyCompleted</c>, <c>ValidationRejected</c>, <c>Cancelled</c>, or <c>Failed</c>)
/// is the caller's real source of truth for what happened during the retry itself.</summary>
public enum ElevatedScannerHostOutcome
{
    /// <summary>One request was read and one typed response was successfully written and sent — regardless of
    /// what that response's own <see cref="ElevatedScanRetryResponse.Outcome"/> says.</summary>
    ResponseSent,
    /// <summary>No client connected, or a client connected but never sent a complete request, within the
    /// configured bounded timeout.</summary>
    ConnectionOrRequestTimeout,
    /// <summary>The frame itself could not be trusted (malformed JSON, an out-of-bound declared length) at a
    /// point where no response could be safely constructed and sent.</summary>
    ProtocolFailure,
    /// <summary>The caller's own <see cref="CancellationToken"/> was cancelled before a response could be sent.</summary>
    Cancelled,
    /// <summary>An unexpected failure outside every other case above.</summary>
    UnexpectedFailure
}

public sealed record ElevatedScannerHostResult(ElevatedScannerHostOutcome Outcome, ElevatedScanRetryResponse? Response);

/// <summary>
/// The small composition/orchestration layer that connects the already-committed, independently reviewed
/// pieces — the bounded one-shot named-pipe transport (<c>ElevatedScanIpcTransport</c>) and the pure read-only
/// metadata retry engine (<see cref="ElevatedMetadataRetryEngine"/>) — for exactly one elevated-scan retry
/// attempt. This class contains no scanning logic, no protocol logic, and no filesystem access of its own; it
/// only wires one call to the other and translates the outcome into a typed result a caller (in production,
/// <c>Clyr.ElevatedScanner</c>'s <c>Program.Main</c>) can map to a process exit code. Waits for exactly one
/// client, processes exactly one request, sends exactly one response, and returns — there is no listening loop,
/// no retry loop, and nothing here ever remains resident afterward.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ElevatedScannerHost
{
    public static async Task<ElevatedScannerHostResult> RunOneShotAsync(string pipeName, ElevatedMetadataRetryEngine engine,
        ElevatedScanIpcServerTimeouts timeouts, IClock clock, CancellationToken cancellationToken)
    {
        try
        {
            // ElevatedScanIpcTransport.RunOneShotAsync returns exactly the response it sent — including a
            // ValidationRejected or ProtocolRejected response it built and sent without ever invoking the
            // delegate below — so this never has to duplicate that validation/rejection logic to know what was
            // actually sent on the wire.
            var response = await ElevatedScanIpcTransport.RunOneShotAsync(pipeName, timeouts, clock,
                (request, requestCancellationToken) =>
                {
                    // The IPC layer has already independently validated this request (see
                    // ElevatedScanIpcTransport.RunOneShotAsync) before ever calling this delegate; the engine
                    // re-validates it again on its own before enumerating anything — two independent checks,
                    // neither one trusting the other's result.
                    return engine.RetryAsync(request, requestCancellationToken);
                }, cancellationToken).ConfigureAwait(false);
            return new(ElevatedScannerHostOutcome.ResponseSent, response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(ElevatedScannerHostOutcome.Cancelled, null);
        }
        catch (OperationCanceledException)
        {
            // Cancelled, but not by the caller's own token — this can only be one of the transport's internal
            // per-phase timeouts (connection or request-read) firing.
            return new(ElevatedScannerHostOutcome.ConnectionOrRequestTimeout, null);
        }
        catch (ElevatedScanIpcFrameException)
        {
            return new(ElevatedScannerHostOutcome.ProtocolFailure, null);
        }
        catch (Exception)
        {
            // A bounded, final safety net: whatever this was (pipe security setup, an unexpected I/O failure,
            // or anything else), the caller gets a documented outcome and exit code — never an unhandled
            // exception, and never a raw exception message carried forward.
            return new(ElevatedScannerHostOutcome.UnexpectedFailure, null);
        }
    }
}
