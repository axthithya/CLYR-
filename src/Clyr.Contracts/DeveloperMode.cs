using System.Collections.Immutable;

namespace Clyr.Contracts;

/// <summary>
/// Closed, typed identifiers for the Phase 7 developer-tool inventory. Adding a tool means adding a member
/// here and a matching entry in <c>Clyr.Core.DeveloperMode.DeveloperToolRegistry</c> — nothing dynamic, no
/// rule-pack-declared tool identities, no user-supplied identifiers.
/// </summary>
public enum DeveloperToolId
{
    Docker, Wsl, NodeNpm, Pnpm, Yarn, DotNetNuGet, Gradle, Maven, PythonPip, RustCargo,
    FlutterDart, AndroidSdk, Playwright, BuildOutput
}

/// <summary>
/// What CLYR could actually establish about a tool. Absence of one expected folder must never alone produce
/// <see cref="NotInstalled"/> — see the Phase 7 detection contract in docs/PHASE7_DEVELOPER_MODE.md.
/// </summary>
public enum DeveloperToolStatus
{
    FullyDetected, PartiallyDetected, InstalledNoData, NotInstalled, Unavailable,
    PermissionLimited, UnsupportedVersion, ProbeFailed
}

/// <summary>
/// Finer-grained developer-storage vocabulary than <see cref="StorageCategory"/>, used only for Developer Mode
/// display/explanation. The underlying <see cref="CleanupCandidate.Category"/> a finding carries remains the
/// existing shared <see cref="StorageCategory"/> so it keeps working with every existing Phase 5/6 contract;
/// this enum only adds developer-specific precision on top, it never replaces the shared one.
/// </summary>
public enum DeveloperStorageCategory
{
    DownloadCache, PackageCache, PackageStore, InstalledSdk, EmulatorImage, ContainerImage,
    ContainerBuildCache, ContainerVolume, WslVirtualDisk, DependencyDirectory, BuildOutput,
    ToolLogs, TemporaryToolFiles, UserProject, UnknownToolManaged
}

public sealed record DeveloperToolDescriptor(
    DeveloperToolId Id, string DisplayName, ImmutableArray<string> SupportedPlatforms,
    ImmutableArray<string> TrustedExecutableNames, bool RequiresProbe, TimeSpan ProbeTimeout,
    int MaxProbeOutputBytes, string Explanation);

public sealed record DeveloperToolDiagnostic(string Code, string Message);

/// <summary>
/// One tool's aggregated Developer Mode result. <see cref="Candidates"/> reuses the exact Phase 5
/// <see cref="CleanupCandidate"/> model — a Developer Mode finding is a cleanup candidate, sourced differently,
/// so it flows through the same eligibility/plan/execution pipeline as every other finding without a parallel
/// type system.
/// </summary>
public sealed record DeveloperToolReport(
    DeveloperToolId ToolId, DeveloperToolStatus Status, string? DetectedVersion,
    string? ExecutableDiscoverySource, ImmutableArray<CleanupCandidate> Candidates,
    ImmutableArray<DeveloperToolDiagnostic> Diagnostics, long TotalObservedLogicalBytes,
    long? ToolReportedBytes);

/// <summary>
/// A narrow, closed, typed probe request. There is no free-form command string anywhere in this contract —
/// <see cref="Arguments"/> is populated only by an adapter from its own fixed, reviewed argument templates,
/// never from user input, rule-pack data, or configuration.
/// </summary>
public sealed record DeveloperToolProbeRequest(
    DeveloperToolId ToolId, string ExecutablePath, ImmutableArray<string> Arguments,
    TimeSpan Timeout, int MaxOutputBytes);

public sealed record DeveloperToolProbeResult(
    bool Succeeded, string? StandardOutput, bool TimedOut, bool OutputTruncated,
    int? ExitCode, string? ErrorMessage);

public sealed record DeveloperToolExecutableCandidate(
    string NormalizedFullPath, string? Version, string? PublisherOrSignature, string DiscoverySource);
