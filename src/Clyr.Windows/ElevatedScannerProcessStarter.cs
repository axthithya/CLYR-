using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Clyr.Core;

namespace Clyr.Windows;

/// <summary>
/// The single reviewed process-launch boundary for <c>Clyr.ElevatedScanner.exe</c>. This is the only production
/// file in the entire codebase permitted to contain <c>Process.Start</c>, <c>ProcessStartInfo</c>,
/// <c>Verb = "runas"</c>, or <c>UseShellExecute = true</c> for this feature (see the corresponding repository
/// safety test). It launches only the exact path carried by an already-built <see cref="ElevatedScannerLaunchPlan"/>
/// — never a caller-supplied path, filename, or argument list — and passes exactly one argument: the plan's own
/// <c>--pipe=&lt;name&gt;</c> bootstrap argument. Provides no generic executable launching, no arbitrary
/// argument launching, no shell-command launching, no process search, no PATH lookup, and no retry loop.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsElevatedScannerProcessStarter : IElevatedScannerProcessStarter
{
    /// <summary>Win32 <c>ERROR_CANCELLED</c> — the code Windows reports when the user declines the UAC prompt.</summary>
    private const int ErrorCancelled = 1223;

    public ElevatedProcessStartResult Start(ElevatedScannerLaunchPlan plan)
    {
        // Immediately-before-launch revalidation: even though this plan was already built by the trusted,
        // independently reviewed ElevatedScannerLaunchPlanBuilder, this is the one place a process is actually
        // started, so every check is re-run here rather than trusted on the strength of where the plan came
        // from. Every failure fails closed as LaunchFailed — never a best-effort launch.
        if (!string.Equals(Path.GetFileName(plan.ExecutablePath), ElevatedScannerExecutableResolver.HelperFileName, StringComparison.Ordinal))
            return new(ElevatedProcessStartOutcome.LaunchFailed);
        if (!Path.IsPathFullyQualified(plan.ExecutablePath) || plan.ExecutablePath.StartsWith("\\\\", StringComparison.Ordinal))
            return new(ElevatedProcessStartOutcome.LaunchFailed);

        var trustedBaseDirectory = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var candidateDirectory = Path.GetDirectoryName(plan.ExecutablePath);
        if (!string.Equals(candidateDirectory, trustedBaseDirectory, StringComparison.OrdinalIgnoreCase))
            return new(ElevatedProcessStartOutcome.LaunchFailed);

        try
        {
            if (Directory.Exists(plan.ExecutablePath)) return new(ElevatedProcessStartOutcome.LaunchFailed);
            if (!File.Exists(plan.ExecutablePath)) return new(ElevatedProcessStartOutcome.LaunchFailed);
            if ((File.GetAttributes(plan.ExecutablePath) & FileAttributes.ReparsePoint) != 0)
                return new(ElevatedProcessStartOutcome.LaunchFailed);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(ElevatedProcessStartOutcome.LaunchFailed);
        }

        if (!ElevatedScanPipeName.IsValid(plan.PipeName)) return new(ElevatedProcessStartOutcome.LaunchFailed);
        if (!string.Equals(plan.BootstrapArgument, "--pipe=" + plan.PipeName, StringComparison.Ordinal))
            return new(ElevatedProcessStartOutcome.LaunchFailed);
        if (!ElevatedScannerBootstrapArguments.TryParse([plan.BootstrapArgument]).IsValid)
            return new(ElevatedProcessStartOutcome.LaunchFailed);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = plan.ExecutablePath,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                ArgumentList = { plan.BootstrapArgument }
            }
        };

        try
        {
            return process.Start() ? new(ElevatedProcessStartOutcome.Started) : new(ElevatedProcessStartOutcome.LaunchFailed);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            // The user declined the UAC prompt — an ordinary, expected outcome, never retried automatically
            // and never treated as though the (not-yet-attempted) scan itself failed.
            return new(ElevatedProcessStartOutcome.Denied);
        }
        catch (Win32Exception)
        {
            // Any other launch failure (elevation unavailable in this session, the executable rejected by
            // policy, and so on) is bounded and typed — never the raw exception message.
            return new(ElevatedProcessStartOutcome.LaunchFailed);
        }
    }
}
