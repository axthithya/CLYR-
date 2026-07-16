namespace Clyr.Core;

public sealed record PathValidationResult(bool IsValid, string? CanonicalPath, string Code, string Message, bool IsProtected);

public sealed class WindowsPathSafetyValidator
{
    private static readonly string[] ProtectedSegments =
    [
        "windows", "system32", "winsxs", "installer", "recovery", "system volume information",
        "$recycle.bin", "boot", "efi", "docker", "wsl", "virtual machines", "programdata"
    ];
    private static readonly string[] ProtectedFiles =
    [
        "pagefile.sys", "swapfile.sys", "hiberfil.sys", "sam", "security", "system", "ntuser.dat"
    ];

    public static PathValidationResult Validate(string path, string approvedRoot, bool isReparsePoint)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(approvedRoot))
            return Invalid("path.empty", "A path and approved root are required.");
        if (isReparsePoint) return Invalid("path.reparse", "Reparse points, junctions, symlinks, and mount points are rejected.");
        if (IsNetworkOrDevicePath(path))
            return Invalid("path.namespace", "UNC and device namespaces are not supported.");
        if (path.Contains('%', StringComparison.Ordinal))
            return Invalid("path.environment", "Environment-variable expansion is allowed only through trusted known-folder identities.");
        path = path.Replace('/', (char)92);
        approvedRoot = approvedRoot.Replace('/', (char)92);
        if (!IsDriveAbsolute(path) || !IsDriveAbsolute(approvedRoot))
            return Invalid("path.relative", "Only absolute local drive paths are accepted.");
        var pathSegments = Split(path);
        var rootSegments = Split(approvedRoot);
        if (pathSegments.Any(segment => segment is "." or ".."))
            return Invalid("path.traversal", "Relative traversal components are rejected.");
        if (pathSegments.Any(segment => segment.EndsWith(' ') || segment.EndsWith('.') || segment.Contains('~')))
            return Invalid("path.ambiguous", "Trailing dots, trailing spaces, and ambiguous 8.3 aliases are rejected.");
        if (pathSegments.Skip(1).Any(segment => segment.Contains(':')))
            return Invalid("path.ads", "Alternate data streams are rejected.");
        if (!Contains(pathSegments, rootSegments))
            return Invalid("path.outside-root", "The target is outside the approved root.");
        var canonical = Canonical(pathSegments);
        if (IsProtected(pathSegments))
            return new(false, canonical, "path.protected", "Protected paths override every eligibility decision.", true);
        return new(true, canonical, "path.valid", "The target is canonically contained by the approved root.", false);
    }

    private static bool IsNetworkOrDevicePath(string value) => value.Length >= 2 && value[0] == '\\' && value[1] == '\\';
    private static PathValidationResult Invalid(string code, string message) => new(false, null, code, message, false);
    private static bool IsDriveAbsolute(string value) => value.Length >= 3 && char.IsAsciiLetter(value[0])
        && value[1] == ':' && value[2] == '\\';
    private static string[] Split(string value) => value.Split('\\', StringSplitOptions.RemoveEmptyEntries);
    private static bool Contains(string[] path, string[] root) => path.Length >= root.Length
        && path.Take(root.Length).SequenceEqual(root, StringComparer.OrdinalIgnoreCase);
    private static string Canonical(string[] segments) =>
        char.ToUpperInvariant(segments[0][0]) + ":" + '\\' + string.Join('\\', segments.Skip(1));
    private static bool IsProtected(string[] segments)
    {
        var names = segments.Skip(1).ToArray();
        return names.Any(segment => ProtectedSegments.Contains(segment, StringComparer.OrdinalIgnoreCase))
            || names.Any(segment => ProtectedFiles.Contains(segment, StringComparer.OrdinalIgnoreCase))
            || names.Any(segment => segment.Contains("credential", StringComparison.OrdinalIgnoreCase)
                || segment.Contains("password", StringComparison.OrdinalIgnoreCase)
                || segment.Contains("game save", StringComparison.OrdinalIgnoreCase)
                || segment.EndsWith(".vhd", StringComparison.OrdinalIgnoreCase)
                || segment.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase)
                || segment.EndsWith(".vmdk", StringComparison.OrdinalIgnoreCase)
                || segment.EndsWith(".ost", StringComparison.OrdinalIgnoreCase)
                || segment.EndsWith(".pst", StringComparison.OrdinalIgnoreCase)
                || segment.EndsWith(".db", StringComparison.OrdinalIgnoreCase));
    }
}

