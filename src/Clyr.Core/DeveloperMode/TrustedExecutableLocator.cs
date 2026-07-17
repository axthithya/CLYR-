using Clyr.Contracts;

namespace Clyr.Core.DeveloperMode;

/// <summary>
/// Trusted executable discovery for the two probe-capable developer tools (Docker, WSL). This deliberately
/// never consults <c>PATH</c>: PATH order is attacker- or environment-influenceable (a project folder, a
/// temporary directory, or any other user-writable directory earlier on PATH could shadow the real tool with a
/// same-named executable), so trust is anchored to a small, fixed set of canonical, non-user-writable locations
/// instead of "whatever resolves first." Each tool resolves to at most one candidate; if more than one distinct
/// executable is found under a tool's trusted root, discovery is treated as ambiguous and returns
/// <see langword="null"/> rather than guessing — the caller (<see cref="DeveloperToolRegistry"/>) then reports
/// the tool as not reliably discovered instead of probing an unverified binary.
/// </summary>
public sealed class TrustedExecutableLocator : IDeveloperToolExecutableLocator
{
    private readonly string? system32Root;
    private readonly string? programFilesRoot;

    /// <param name="system32Root">
    /// Overrides the trusted root WSL must resolve under. Defaults to the real <c>%SystemRoot%\System32</c>.
    /// Exposed only so tests can stand in a private, disposable directory for a fake "canonical location" —
    /// production callers should never pass this.
    /// </param>
    /// <param name="programFilesRoot">
    /// Overrides the trusted root Docker Desktop must resolve under. Defaults to the real
    /// <c>%ProgramFiles%</c>. Same test-only purpose as <paramref name="system32Root"/>.
    /// </param>
    public TrustedExecutableLocator(string? system32Root = null, string? programFilesRoot = null)
    {
        this.system32Root = system32Root ?? Environment.GetFolderPath(Environment.SpecialFolder.System);
        this.programFilesRoot = programFilesRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    }

    public DeveloperToolExecutableCandidate? Locate(DeveloperToolDescriptor descriptor)
    {
        if (!descriptor.RequiresProbe || descriptor.TrustedExecutableNames.IsDefaultOrEmpty) return null;
        var exeName = descriptor.TrustedExecutableNames[0];
        if (!string.Equals(Path.GetExtension(exeName), ".exe", StringComparison.OrdinalIgnoreCase)) return null;
        return descriptor.Id switch
        {
            DeveloperToolId.Wsl => LocateWsl(exeName),
            DeveloperToolId.Docker => LocateDocker(exeName),
            _ => null
        };
    }

    /// <summary>
    /// The real <c>wsl.exe</c> ships only inside <c>System32</c> as part of Windows itself — that single
    /// location, not PATH, is WSL's entire trust boundary.
    /// </summary>
    private DeveloperToolExecutableCandidate? LocateWsl(string exeName)
    {
        if (string.IsNullOrWhiteSpace(system32Root)) return null;
        var candidate = Path.Combine(system32Root, exeName);
        return TrustedExecutableValidation.IsTrustedExecutableFile(candidate) ? BuildCandidate(candidate, "trusted-system32") : null;
    }

    /// <summary>
    /// Docker Desktop's CLI ships under its own Program Files installation tree
    /// (<c>%ProgramFiles%\Docker\Docker\resources\bin\docker.exe</c> by default). The search is bounded to that
    /// tree only — never <c>%LocalAppData%</c> or any other user-writable location — and depth-limited so an
    /// attacker-planted nested copy elsewhere under Program Files cannot be reached. If the tree contains more
    /// than one <c>docker.exe</c>, the result is ambiguous by design and this returns <see langword="null"/>.
    /// </summary>
    private DeveloperToolExecutableCandidate? LocateDocker(string exeName)
    {
        if (string.IsNullOrWhiteSpace(programFilesRoot)) return null;
        var root = Path.Combine(programFilesRoot, "Docker");
        if (!Directory.Exists(root)) return null;

        var found = new List<string>();
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 6,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (var file in Directory.EnumerateFiles(root, exeName, options))
                if (TrustedExecutableValidation.IsTrustedExecutableFile(file)) found.Add(Path.GetFullPath(file));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        // Deduplicate by real path before judging ambiguity — the same physical file reachable through more
        // than one enumerated path is not a conflicting candidate.
        var distinct = found.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return distinct.Length == 1 ? BuildCandidate(distinct[0], "trusted-program-files:Docker") : null;
    }

    private static DeveloperToolExecutableCandidate BuildCandidate(string path, string discoverySource)
    {
        var normalized = Path.GetFullPath(path);
        return new(normalized, TrustedExecutableValidation.TryGetFileVersion(normalized),
            TrustedExecutableValidation.TryGetPublisher(normalized), discoverySource);
    }
}
