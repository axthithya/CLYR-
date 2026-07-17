using Clyr.Contracts;

namespace Clyr.Core.DeveloperMode;

/// <summary>
/// Default trusted executable discovery: a bounded PATH lookup for one of the descriptor's exact known
/// filenames, falling back to a depth-bounded search of well-known Windows installation roots. Only files with
/// an <c>.exe</c> extension that are not reparse points are ever returned — scripts, batch files, and ambiguous
/// path-shadowed replacements are rejected by construction.
/// </summary>
public sealed class TrustedExecutableLocator : IDeveloperToolExecutableLocator
{
    private static readonly string[] KnownRoots =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
    ];

    public DeveloperToolExecutableCandidate? Locate(DeveloperToolDescriptor descriptor)
    {
        if (!descriptor.RequiresProbe || descriptor.TrustedExecutableNames.IsDefaultOrEmpty) return null;
        foreach (var name in descriptor.TrustedExecutableNames)
        {
            if (!string.Equals(Path.GetExtension(name), ".exe", StringComparison.OrdinalIgnoreCase)) continue;
            var onPath = LocateOnPath(name);
            if (onPath is not null) return onPath;
            foreach (var root in KnownRoots)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                var found = SearchKnownRoot(root, name);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static DeveloperToolExecutableCandidate? LocateOnPath(string exeName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try { candidate = Path.Combine(directory.Trim(), exeName); }
            catch (ArgumentException) { continue; }
            if (IsTrustedExecutable(candidate)) return new(Path.GetFullPath(candidate), null, null, "PATH");
        }
        return null;
    }

    private static DeveloperToolExecutableCandidate? SearchKnownRoot(string root, string exeName)
    {
        if (!Directory.Exists(root)) return null;
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 4,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (var file in Directory.EnumerateFiles(root, exeName, options))
                if (IsTrustedExecutable(file)) return new(Path.GetFullPath(file), null, null, "known-folder:" + root);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    private static bool IsTrustedExecutable(string path)
    {
        if (!File.Exists(path)) return false;
        if (!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)) return false;
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
