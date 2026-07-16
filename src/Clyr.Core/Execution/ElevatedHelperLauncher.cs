using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Clyr.Contracts;

namespace Clyr.Core.Execution;

public enum ElevationOutcome { Completed, Denied, TimedOut, LaunchFailed }
public sealed record ElevationResult(ElevationOutcome Outcome, HelperResponse? Response);

/// <summary>
/// The single, tightly controlled process launch permitted anywhere in production CLYR: a normal Windows UAC
/// elevation of the known, co-located helper executable, passing only a freshly generated pipe name as its sole
/// bootstrap argument. No arbitrary executable path, command line, or argument list ever reaches this method —
/// the real request is transferred only afterward, over the authenticated IPC channel established in
/// <see cref="ElevatedHelperIpc"/>. No action in the current allowlist requires this path; it exists so the
/// architecture is ready if a future action does, without elevating the main CLYR process itself.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ElevatedHelperLauncher
{
    private const string HelperFileName = "Clyr.ElevatedHelper.exe";

    public static string ResolveHelperExecutablePath() => Path.Combine(AppContext.BaseDirectory, HelperFileName);

    public static async Task<ElevationResult> RunAsync(HelperRequest request, string helperExecutablePath,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(helperExecutablePath) || !File.Exists(helperExecutablePath))
            return new(ElevationOutcome.LaunchFailed, null);

        var pipeName = ElevatedHelperIpc.NewPipeName();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = helperExecutablePath,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                ArgumentList = { pipeName }
            }
        };

        try
        {
            if (!process.Start()) return new(ElevationOutcome.LaunchFailed, null);
        }
        catch (Win32Exception)
        {
            // The UAC prompt was declined, or elevation is unavailable in this session. This is a normal,
            // expected outcome — never retried automatically, never treated as an error.
            return new(ElevationOutcome.Denied, null);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            var response = await ElevatedHelperIpc.SendRequestAsync(pipeName, request, timeout, cts.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new(ElevationOutcome.Completed, response);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new(ElevationOutcome.TimedOut, null);
        }
        catch (IOException)
        {
            TryKill(process);
            return new(ElevationOutcome.LaunchFailed, null);
        }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }
}
