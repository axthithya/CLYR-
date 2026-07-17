using Clyr.Core;

namespace Clyr.ElevatedScanner;

/// <summary>
/// Phase 7.2.6D: a deliberately nonfunctional bootstrap only. This parses the single strict
/// <c>--pipe=&lt;name&gt;</c> argument (see <see cref="ElevatedScannerBootstrapArguments"/>, Clyr.Core) and
/// exits with one of the documented nonzero <see cref="ExitCode"/> values below. It never opens the named
/// pipe, never invokes the metadata retry engine, never enumerates a filesystem, never launches a process, and
/// never prints anything (so it can never leak a pipe name, path, or other sensitive detail to its own
/// console). It must never return exit code 0 — a caller that sees success here would be wrong, since nothing
/// has actually been attempted yet. Wiring this bootstrap up to real inter-process transport and read-only
/// metadata enumeration is later, out-of-scope work.
/// </summary>
internal static class Program
{
    /// <summary>Small, stable, documented nonzero exit codes for this temporary subphase. None of these is 0,
    /// and none will ever become 0 — a successful elevated retry, once implemented, needs its own distinct
    /// success code, never reusing one of these placeholders.</summary>
    internal static class ExitCode
    {
        /// <summary>The command line itself was malformed: zero arguments, more than one argument, or an
        /// argument that was not the exact <c>--pipe=</c> switch.</summary>
        public const int InvalidArguments = 2;
        /// <summary>The <c>--pipe=</c> switch was present but its value was empty or failed
        /// <see cref="ElevatedScanPipeName.IsValid"/>.</summary>
        public const int InvalidPipeName = 3;
        /// <summary>The bootstrap argument was fully valid, but this subphase intentionally goes no further —
        /// the named pipe is never opened and no scan is ever attempted.</summary>
        public const int TransportNotConnected = 10;
    }

    private static int Main(string[] args)
    {
        var parsed = ElevatedScannerBootstrapArguments.TryParse(args);
        return parsed.Outcome switch
        {
            ElevatedScannerBootstrapOutcome.Valid => ExitCode.TransportNotConnected,
            ElevatedScannerBootstrapOutcome.EmptyPipeName or ElevatedScannerBootstrapOutcome.InvalidPipeName => ExitCode.InvalidPipeName,
            _ => ExitCode.InvalidArguments
        };
    }
}
