using Clyr.Core;
using Clyr.Windows;

namespace Clyr.ElevatedScanner;

/// <summary>
/// Phase 7.2.6E: the minimal composition root. This parses the single strict <c>--pipe=&lt;name&gt;</c>
/// argument (see <see cref="ElevatedScannerBootstrapArguments"/>, Clyr.Core), constructs the approved read-only
/// dependencies (the real Windows metadata enumerator and the pure retry engine), hands them to
/// <see cref="ElevatedScannerHost"/> for exactly one request/response cycle, and maps the result to one of the
/// documented exit codes below. No scanning, protocol, or orchestration logic lives here — all of it is in
/// already-reviewed, independently testable components this file only wires together. Never prints anything
/// (no path, request payload, nonce, manifest data, or exception detail ever reaches this process's own
/// console), and never launches another process.
/// </summary>
internal static class Program
{
    /// <summary>Small, stable, documented exit codes. 0 means only that one typed response was successfully
    /// sent — the scan's own outcome (<c>Completed</c>, <c>PartiallyCompleted</c>, <c>ValidationRejected</c>,
    /// <c>Cancelled</c>, or <c>Failed</c>) lives inside that response and is the caller's real source of truth,
    /// never this process's exit code alone.</summary>
    internal static class ExitCode
    {
        public const int Success = 0;
        /// <summary>The command line itself was malformed: zero arguments, more than one argument, or an
        /// argument that was not the exact <c>--pipe=</c> switch.</summary>
        public const int InvalidArguments = 2;
        /// <summary>The <c>--pipe=</c> switch was present but its value was empty or failed
        /// <see cref="ElevatedScanPipeName.IsValid"/>.</summary>
        public const int InvalidPipeName = 3;
        /// <summary>No client connected, or a client connected but never completed a request, within the
        /// bounded timeout.</summary>
        public const int ConnectionOrRequestTimeout = 11;
        /// <summary>The frame itself could not be trusted at a point where no response could be sent.</summary>
        public const int ProtocolFailure = 12;
        /// <summary>The host's own cancellation token was cancelled before a response could be sent.</summary>
        public const int Cancelled = 13;
        /// <summary>An unexpected failure outside every other documented case.</summary>
        public const int UnexpectedHostFailure = 14;
    }

    private static int Main(string[] args)
    {
        var parsed = ElevatedScannerBootstrapArguments.TryParse(args);
        if (parsed.Outcome is ElevatedScannerBootstrapOutcome.EmptyPipeName or ElevatedScannerBootstrapOutcome.InvalidPipeName)
            return ExitCode.InvalidPipeName;
        if (!parsed.IsValid) return ExitCode.InvalidArguments;

        var clock = new SystemClock();
        var engine = new ElevatedMetadataRetryEngine(new WindowsFileSystemEnumerator(), clock);
        using var cancellation = new CancellationTokenSource();
        var result = ElevatedScannerHost
            .RunOneShotAsync(parsed.PipeName!, engine, ElevatedScanIpcServerTimeouts.Default, clock, cancellation.Token)
            .GetAwaiter().GetResult();

        return result.Outcome switch
        {
            ElevatedScannerHostOutcome.ResponseSent => ExitCode.Success,
            ElevatedScannerHostOutcome.ConnectionOrRequestTimeout => ExitCode.ConnectionOrRequestTimeout,
            ElevatedScannerHostOutcome.ProtocolFailure => ExitCode.ProtocolFailure,
            ElevatedScannerHostOutcome.Cancelled => ExitCode.Cancelled,
            _ => ExitCode.UnexpectedHostFailure
        };
    }
}
