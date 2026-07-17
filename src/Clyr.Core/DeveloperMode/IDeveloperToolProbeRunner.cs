using Clyr.Contracts;

namespace Clyr.Core.DeveloperMode;

/// <summary>
/// The single narrow surface anywhere in production CLYR that may launch a developer tool's own executable —
/// always non-elevated, always <c>UseShellExecute = false</c>, always a fixed executable path plus a fixed,
/// adapter-chosen argument list. There is no overload that accepts a raw command string.
/// </summary>
public interface IDeveloperToolProbeRunner
{
    Task<DeveloperToolProbeResult> RunAsync(DeveloperToolProbeRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Resolves a tool's executable through trusted mechanisms only: a bounded PATH lookup for an exact, known
/// filename, and a bounded search of well-known installation roots. Never resolves a script (.bat/.cmd/.ps1),
/// never resolves a reparse point, and never accepts a path supplied by a caller.
/// </summary>
public interface IDeveloperToolExecutableLocator
{
    DeveloperToolExecutableCandidate? Locate(DeveloperToolDescriptor descriptor);
}
