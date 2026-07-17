using System.Runtime.Versioning;
using Clyr.Contracts;

namespace Clyr.Core;

/// <summary>How the one reviewed process-start attempt ended. <see cref="Started"/> only means the OS accepted
/// the launch request — it says nothing about whether a scan ever completed; that is
/// <see cref="ElevatedScannerLauncherResult"/>'s job.</summary>
public enum ElevatedProcessStartOutcome { Started, Denied, LaunchFailed }

public sealed record ElevatedProcessStartResult(ElevatedProcessStartOutcome Outcome);

/// <summary>
/// The only capability this abstraction exposes is "start the one trusted, already-validated helper described
/// by this plan" — there is no generic executable launching, no arbitrary argument list, no shell-command
/// launching, no process search, no PATH lookup, and no retry loop anywhere behind it. Real production code
/// implementing this (see <c>WindowsElevatedScannerProcessStarter</c> in Clyr.Windows) is the one place in the
/// entire codebase permitted to perform the actual OS-level launch for this feature. Tests always use a fake
/// implementation — none of them ever start a real process.
/// </summary>
public interface IElevatedScannerProcessStarter
{
    ElevatedProcessStartResult Start(ElevatedScannerLaunchPlan plan);
}

/// <summary>Every way one elevated-scan launch attempt can end. A successful process start is not itself
/// success — <see cref="Completed"/> requires the trusted helper to have started, sent exactly one request, and
/// returned exactly one valid typed response. The response's own
/// <see cref="ElevatedScanRetryResponse.Outcome"/> (which may be <c>Completed</c>, <c>PartiallyCompleted</c>,
/// <c>ValidationRejected</c>, <c>Cancelled</c>, or <c>Failed</c>) is the real source of truth for what the scan
/// itself found — this launcher-level outcome only ever says whether that response was obtained at all.</summary>
public enum ElevatedScannerLauncherOutcome
{
    Completed, Denied, Cancelled, HelperMissing, InvalidLaunchPlan, LaunchFailed,
    ConnectionTimedOut, ResponseTimedOut, ProtocolRejected, ValidationRejected, InvalidResponse, Failed
}

public sealed record ElevatedScannerLauncherResult(ElevatedScannerLauncherOutcome Outcome, ElevatedScanRetryResponse? Response);

/// <summary>
/// The app-side orchestration boundary: builds a trusted launch plan, starts the helper through the narrow
/// <see cref="IElevatedScannerProcessStarter"/> abstraction, and exchanges exactly one request/response with it
/// over the existing bounded one-shot IPC client. The public <see cref="RunAsync"/> method accepts only a typed
/// request and a cancellation token — no executable path, filename, directory, launch plan, pipe name, argument
/// array, command, or script can ever reach this class from a caller. Never exposes an operating-system process
/// handle or its start-up configuration to a caller, never retries a launch, and never remains resident after
/// one exchange.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ElevatedScannerLauncher(
    ITrustedApplicationBaseDirectory trustedDirectory,
    IElevatedScannerFileProbe fileProbe,
    IElevatedScannerProcessStarter processStarter,
    ElevatedScanIpcClientTimeouts? timeouts = null)
{
    private readonly ElevatedScanIpcClientTimeouts timeouts = timeouts ?? ElevatedScanIpcClientTimeouts.Default;

    public async Task<ElevatedScannerLauncherResult> RunAsync(ElevatedScanRetryRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return new(ElevatedScannerLauncherOutcome.Cancelled, null);

        var planResult = ElevatedScannerLaunchPlanBuilder.Build(trustedDirectory, fileProbe);
        if (!planResult.IsReady) return new(MapPlanOutcome(planResult.Outcome), null);
        var plan = planResult.Plan!;

        // Defense-in-depth: revalidate the generated pipe name and the exact bootstrap argument again,
        // immediately before acting on them, rather than trusting a value merely because it arrived from the
        // trusted plan builder.
        if (!ElevatedScanPipeName.IsValid(plan.PipeName)
            || !string.Equals(plan.BootstrapArgument, "--pipe=" + plan.PipeName, StringComparison.Ordinal)
            || !ElevatedScannerBootstrapArguments.TryParse([plan.BootstrapArgument]).IsValid)
            return new(ElevatedScannerLauncherOutcome.InvalidLaunchPlan, null);

        if (cancellationToken.IsCancellationRequested) return new(ElevatedScannerLauncherOutcome.Cancelled, null);

        var startResult = processStarter.Start(plan);
        switch (startResult.Outcome)
        {
            case ElevatedProcessStartOutcome.Denied: return new(ElevatedScannerLauncherOutcome.Denied, null);
            case ElevatedProcessStartOutcome.LaunchFailed: return new(ElevatedScannerLauncherOutcome.LaunchFailed, null);
        }

        if (cancellationToken.IsCancellationRequested) return new(ElevatedScannerLauncherOutcome.Cancelled, null);

        try
        {
            var response = await ElevatedScanIpcTransport.SendRequestAsync(plan.PipeName, request, timeouts, cancellationToken).ConfigureAwait(false);
            return new(ElevatedScannerLauncherOutcome.Completed, response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(ElevatedScannerLauncherOutcome.Cancelled, null);
        }
        catch (OperationCanceledException)
        {
            // The client-side connect/write/read timeouts all surface identically here; without instrumenting
            // the (preserved, unmodified) IPC client further, this is reported as a connection timeout — the
            // most common real cause when the helper never opens its pipe server in time.
            return new(ElevatedScannerLauncherOutcome.ConnectionTimedOut, null);
        }
        catch (ElevatedScanIpcFrameException exception) when (exception.Code is "ipc.response-protocol-mismatch" or "ipc.response-nonce-mismatch")
        {
            return new(ElevatedScannerLauncherOutcome.InvalidResponse, null);
        }
        catch (ElevatedScanIpcFrameException)
        {
            return new(ElevatedScannerLauncherOutcome.ProtocolRejected, null);
        }
        catch (Exception)
        {
            // A bounded, final safety net: never an unhandled exception, never a raw exception message
            // carried forward.
            return new(ElevatedScannerLauncherOutcome.Failed, null);
        }
    }

    private static ElevatedScannerLauncherOutcome MapPlanOutcome(ElevatedScannerLaunchPlanOutcome outcome) => outcome switch
    {
        ElevatedScannerLaunchPlanOutcome.HelperMissing => ElevatedScannerLauncherOutcome.HelperMissing,
        _ => ElevatedScannerLauncherOutcome.InvalidLaunchPlan
    };
}
