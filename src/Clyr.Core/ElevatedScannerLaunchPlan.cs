namespace Clyr.Core;

/// <summary>Where the trusted co-located helper executable is expected to live. The only production
/// implementation reads <see cref="AppContext.BaseDirectory"/> — this exists as an interface purely so tests
/// can supply an in-memory value without touching the real filesystem.</summary>
public interface ITrustedApplicationBaseDirectory
{
    string BaseDirectory { get; }
}

/// <summary>Reads the real process's own base directory — never a caller-supplied, environment-variable, or
/// configuration-file value.</summary>
public sealed class ProcessTrustedApplicationBaseDirectory : ITrustedApplicationBaseDirectory
{
    public string BaseDirectory { get; } = AppContext.BaseDirectory;
}

/// <summary>
/// A narrow, read-only file-metadata abstraction — exactly what
/// <see cref="ElevatedScannerExecutableResolver"/> needs to validate the trusted helper candidate, and nothing
/// more. Deliberately has no write, delete, move, rename, or attribute-mutation method; broadening it with one
/// would be a safety-boundary violation for this launch-preparation code, which must never touch disk beyond
/// reading these three facts.
/// </summary>
public interface IElevatedScannerFileProbe
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    /// <summary>True only if <paramref name="path"/> itself carries the reparse-point attribute. Reads the
    /// attribute of the path itself — Win32's <c>GetFileAttributesW</c> (which this is built on) never follows
    /// a reparse point to resolve it, so this can never be tricked into inspecting a redirected target.</summary>
    bool IsReparsePoint(string path);
}

/// <summary>Real, read-only filesystem-backed implementation of <see cref="IElevatedScannerFileProbe"/>. Every
/// method here only reads metadata; none of them can write, delete, move, rename, or mutate anything.</summary>
public sealed class FileSystemElevatedScannerFileProbe : IElevatedScannerFileProbe
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }
}

/// <summary>Every way resolving or building a launch plan can end. <see cref="Ready"/> is the only outcome that
/// carries a usable <see cref="ElevatedScannerLaunchPlan"/>; every other value is an expected, typed rejection —
/// none of these is ever thrown as an exception.</summary>
public enum ElevatedScannerLaunchPlanOutcome
{
    Ready,
    TrustedBaseDirectoryUnavailable,
    HelperMissing,
    HelperPathInvalid,
    HelperIsDirectory,
    HelperOutsideTrustedDirectory,
    HelperReparsePointRejected,
    PipeNameGenerationFailed,
    InvalidLaunchPlan
}

/// <summary>
/// An immutable, fully-prepared plan to launch the elevated scanner — data only, never an action. Contains
/// exactly three fields and nothing else: no drive path, scan root, execution ID, nonce, command, script, or any
/// other caller-supplied data ever reaches this type. <see cref="BootstrapArgument"/> is always exactly
/// <c>--pipe=&lt;PipeName&gt;</c>, the single argument <c>Clyr.ElevatedScanner</c>'s bootstrap parser accepts.
/// </summary>
public sealed record ElevatedScannerLaunchPlan(string ExecutablePath, string PipeName, string BootstrapArgument);

/// <summary>A typed result rather than a thrown exception — a launch plan that cannot be prepared (missing
/// helper, untrusted directory shape, and so on) is an expected, routine outcome to report, not an exceptional
/// program state.</summary>
public sealed record ElevatedScannerLaunchPlanResult(ElevatedScannerLaunchPlanOutcome Outcome, ElevatedScannerLaunchPlan? Plan)
{
    public bool IsReady => Outcome == ElevatedScannerLaunchPlanOutcome.Ready;
    public static ElevatedScannerLaunchPlanResult Success(ElevatedScannerLaunchPlan plan) => new(ElevatedScannerLaunchPlanOutcome.Ready, plan);
    public static ElevatedScannerLaunchPlanResult Failure(ElevatedScannerLaunchPlanOutcome outcome) => new(outcome, null);
}

/// <summary>
/// Resolves the one trusted, co-located <c>Clyr.ElevatedScanner.exe</c> path. This never searches PATH, the
/// registry, or any folder other than the trusted base directory; never accepts a caller-supplied path,
/// filename, or directory; and never follows a reparse point to validate whatever it points at. Every check
/// here is read-only and fails closed — an ambiguous or unexpected condition is always a rejection, never a
/// best-effort acceptance.
/// </summary>
public static class ElevatedScannerExecutableResolver
{
    public const string HelperFileName = "Clyr.ElevatedScanner.exe";

    /// <summary>Combines only the trusted base directory with the fixed helper filename, validates the result
    /// with Windows path semantics (absolute, non-UNC, non-device, no traversal, no alternate data stream, no
    /// ambiguous trailing character, contained within the trusted directory, matching filename, not a reparse
    /// point, not a directory, and actually present), and returns the validated absolute path only on
    /// <see cref="ElevatedScannerLaunchPlanOutcome.Ready"/>.</summary>
    public static ElevatedScannerLaunchPlanOutcome TryResolve(ITrustedApplicationBaseDirectory trustedDirectory,
        IElevatedScannerFileProbe probe, out string executablePath)
    {
        executablePath = string.Empty;
        var raw = (trustedDirectory.BaseDirectory ?? string.Empty).Replace('/', '\\');

        if (!IsDriveAbsolute(raw) || IsUncOrDevicePath(raw))
            return ElevatedScannerLaunchPlanOutcome.TrustedBaseDirectoryUnavailable;
        if (HasTraversalSegment(raw) || HasAlternateDataStream(raw) || HasAmbiguousTrailingCharacter(raw))
            return ElevatedScannerLaunchPlanOutcome.TrustedBaseDirectoryUnavailable;

        var trustedBase = raw.TrimEnd('\\');
        var candidate = trustedBase + "\\" + HelperFileName;

        if (!IsDriveAbsolute(candidate) || IsUncOrDevicePath(candidate))
            return ElevatedScannerLaunchPlanOutcome.HelperPathInvalid;
        if (!HasExpectedFileName(candidate))
            return ElevatedScannerLaunchPlanOutcome.HelperPathInvalid;
        if (!IsContainedWithin(candidate, trustedBase))
            return ElevatedScannerLaunchPlanOutcome.HelperOutsideTrustedDirectory;

        // Reparse-point check first: never proceed to ask a probe about "the file" if the entry itself is a
        // redirection — the real target, if any, is never inspected or followed.
        if (probe.IsReparsePoint(candidate)) return ElevatedScannerLaunchPlanOutcome.HelperReparsePointRejected;
        if (probe.DirectoryExists(candidate)) return ElevatedScannerLaunchPlanOutcome.HelperIsDirectory;
        if (!probe.FileExists(candidate)) return ElevatedScannerLaunchPlanOutcome.HelperMissing;

        executablePath = candidate;
        return ElevatedScannerLaunchPlanOutcome.Ready;
    }

    private static bool IsDriveAbsolute(string value) => value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] == '\\';
    private static bool IsUncOrDevicePath(string value) => value.Length >= 2 && value[0] == '\\' && value[1] == '\\';
    private static string[] SplitSegments(string value) => value.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
    private static bool HasTraversalSegment(string value) => SplitSegments(value).Any(segment => segment is "." or "..");
    private static bool HasAlternateDataStream(string value) => SplitSegments(value).Skip(1).Any(segment => segment.Contains(':'));
    private static bool HasAmbiguousTrailingCharacter(string value) => SplitSegments(value).Any(segment => segment.EndsWith(' ') || segment.EndsWith('.'));

    /// <summary>True only when every one of <paramref name="trustedDirectory"/>'s path segments is an exact,
    /// case-insensitive prefix of <paramref name="candidatePath"/>'s segments — a real segment-by-segment
    /// comparison, never a raw string <c>StartsWith</c>, which a sibling directory sharing a text prefix (for
    /// example <c>C:\CLYR2</c> against a trusted <c>C:\CLYR</c>) would otherwise pass incorrectly.</summary>
    internal static bool IsContainedWithin(string candidatePath, string trustedDirectory)
    {
        var candidateSegments = SplitSegments(candidatePath);
        var trustedSegments = SplitSegments(trustedDirectory);
        return candidateSegments.Length > trustedSegments.Length
            && candidateSegments.Take(trustedSegments.Length).SequenceEqual(trustedSegments, StringComparer.OrdinalIgnoreCase);
    }

    internal static bool HasExpectedFileName(string path) => string.Equals(Path.GetFileName(path), HelperFileName, StringComparison.Ordinal);
}

/// <summary>
/// Builds a complete, immutable <see cref="ElevatedScannerLaunchPlan"/> from the trusted resolver plus a freshly
/// generated, validated pipe name — data preparation only. Nothing here launches a process, opens a pipe,
/// connects a client, or triggers UAC; see Phase 7.2.6F2 (not part of this task) for actually acting on a plan
/// this produces.
/// </summary>
public static class ElevatedScannerLaunchPlanBuilder
{
    public static ElevatedScannerLaunchPlanResult Build(ITrustedApplicationBaseDirectory trustedDirectory, IElevatedScannerFileProbe probe)
    {
        var outcome = ElevatedScannerExecutableResolver.TryResolve(trustedDirectory, probe, out var executablePath);
        if (outcome != ElevatedScannerLaunchPlanOutcome.Ready) return ElevatedScannerLaunchPlanResult.Failure(outcome);

        var pipeName = ElevatedScanPipeName.New();
        if (!ElevatedScanPipeName.IsValid(pipeName))
            return ElevatedScannerLaunchPlanResult.Failure(ElevatedScannerLaunchPlanOutcome.PipeNameGenerationFailed);

        var bootstrapArgument = "--pipe=" + pipeName;
        // Self-verifying: the plan's own bootstrap argument must round-trip through the same strict parser
        // Clyr.ElevatedScanner itself uses, rather than trusting that string concatenation above produced
        // something valid.
        if (!ElevatedScannerBootstrapArguments.TryParse([bootstrapArgument]).IsValid)
            return ElevatedScannerLaunchPlanResult.Failure(ElevatedScannerLaunchPlanOutcome.InvalidLaunchPlan);

        return ElevatedScannerLaunchPlanResult.Success(new ElevatedScannerLaunchPlan(executablePath, pipeName, bootstrapArgument));
    }
}
