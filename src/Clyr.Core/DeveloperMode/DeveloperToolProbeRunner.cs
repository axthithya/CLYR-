using System.Diagnostics;
using System.Text;
using Clyr.Contracts;

namespace Clyr.Core.DeveloperMode;

/// <summary>
/// The one reviewed process-launch surface for Developer Mode. Every call is non-elevated
/// (<c>UseShellExecute = false</c>, no <c>Verb</c>), reads no interactive input, launches only the exact
/// executable path and fixed argument list an adapter already resolved, is time-bounded, and truncates output
/// at a fixed byte limit rather than reading an unbounded stream. This class never mutates anything — it only
/// ever asks a tool to report its own status.
/// </summary>
public sealed class DeveloperToolProbeRunner : IDeveloperToolProbeRunner
{
    public async Task<DeveloperToolProbeResult> RunAsync(DeveloperToolProbeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ExecutablePath) || !File.Exists(request.ExecutablePath))
            return new(false, null, false, false, null, "The probe executable could not be resolved.");
        if (!string.Equals(Path.GetExtension(request.ExecutablePath), ".exe", StringComparison.OrdinalIgnoreCase))
            return new(false, null, false, false, null, "Only a trusted .exe target may be probed.");

        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false
        };
        foreach (var argument in request.Arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(request.Timeout);

        try
        {
            if (!process.Start()) return new(false, null, false, false, null, "The probe process failed to start.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new(false, null, false, false, null, "The probe process could not be launched.");
        }

        var output = new StringBuilder();
        var truncated = false;
        var readTask = ReadBoundedAsync(process.StandardOutput, output, request.MaxOutputBytes, linked.Token)
            .ContinueWith(task => truncated = task.Result, TaskScheduler.Default);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            await readTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new(false, output.ToString(), true, truncated, null, "The probe timed out.");
        }

        return new(process.ExitCode == 0, output.ToString(), false, truncated, process.ExitCode, null);
    }

    private static async Task<bool> ReadBoundedAsync(TextReader reader, StringBuilder destination, int maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var totalBytes = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            var chunkBytes = Encoding.UTF8.GetByteCount(buffer, 0, read);
            if (totalBytes + chunkBytes > maxBytes)
            {
                var remaining = Math.Max(0, maxBytes - totalBytes);
                destination.Append(buffer, 0, Math.Min(read, remaining));
                return true;
            }
            destination.Append(buffer, 0, read);
            totalBytes += chunkBytes;
        }
        return false;
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
