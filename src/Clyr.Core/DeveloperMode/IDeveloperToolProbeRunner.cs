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
/// Resolves a tool's executable through trusted mechanisms only: a fixed canonical, non-user-writable
/// installation location per tool — never a PATH lookup, which is order-sensitive and can be shadowed by an
/// earlier user-writable directory. Never resolves a script (.bat/.cmd/.ps1), never resolves a reparse point or
/// a relative path, never accepts a path supplied by a caller, and never resolves ambiguously — if more than
/// one distinct candidate exists under the trusted location, this returns <see langword="null"/> rather than
/// picking one.
/// </summary>
public interface IDeveloperToolExecutableLocator
{
    DeveloperToolExecutableCandidate? Locate(DeveloperToolDescriptor descriptor);
}
