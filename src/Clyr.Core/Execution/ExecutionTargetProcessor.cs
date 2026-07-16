using Clyr.Contracts;

namespace Clyr.Core.Execution;

/// <summary>
/// The single place production code re-probes a target on disk and deletes it. Both the non-elevated executor
/// and the elevated helper call this — each from its own process, against its own live filesystem view — so
/// "independent revalidation" means independent execution, not merely independent code paths.
/// </summary>
public static class ExecutionTargetProcessor
{
    public static ExecutionItemResult Process(IClock clock, string itemId, string targetId, string? canonicalPath,
        long expectedLogicalBytes, DateTimeOffset? expectedLastWriteAtUtc, bool planTimeIsReparsePoint,
        string trustedRoot, TimeSpan minimumAge)
    {
        var validation = WindowsPathSafetyValidator.Validate(canonicalPath ?? string.Empty, trustedRoot, planTimeIsReparsePoint);
        if (!validation.IsValid)
        {
            var outcome = validation.Code switch
            {
                "path.reparse" => ExecutionItemOutcome.SkippedReparsePoint,
                _ when validation.IsProtected => ExecutionItemOutcome.SkippedProtected,
                _ => ExecutionItemOutcome.SkippedOutsideApprovedRoot
            };
            return new(itemId, targetId, outcome, validation.Code, validation.Message, null);
        }

        var path = validation.CanonicalPath!;
        if (!File.Exists(path))
            return new(itemId, targetId, ExecutionItemOutcome.NotFound, "target.not-found", "The target no longer exists.", null);

        FileInfo info;
        try { info = new FileInfo(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(itemId, targetId, ExecutionItemOutcome.SkippedAccessDenied, "target.probe-failed", "The target could not be re-probed.", null);
        }

        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            return new(itemId, targetId, ExecutionItemOutcome.SkippedReparsePoint, "target.reparse", "The target became a reparse point.", null);
        if (IsCloudPlaceholder(info.Attributes))
            return new(itemId, targetId, ExecutionItemOutcome.SkippedCloudPlaceholder, "target.cloud-placeholder", "The target became a cloud placeholder.", null);

        var stillStale = clock.UtcNow - info.LastWriteTimeUtc >= minimumAge;
        var identityMatches = info.Length == expectedLogicalBytes && info.LastWriteTimeUtc == expectedLastWriteAtUtc;
        if (!identityMatches || !stillStale)
            return new(itemId, targetId, ExecutionItemOutcome.SkippedChanged, "target.changed", "The target's identity or age no longer matches the validated plan.", null);

        try
        {
            File.Delete(path);
            return new(itemId, targetId, ExecutionItemOutcome.Removed, "target.removed", "The target was removed.", info.Length);
        }
        catch (UnauthorizedAccessException)
        {
            return new(itemId, targetId, ExecutionItemOutcome.SkippedAccessDenied, "target.access-denied", "The target could not be removed without elevated or forced access.", null);
        }
        catch (IOException)
        {
            return new(itemId, targetId, ExecutionItemOutcome.SkippedLocked, "target.locked", "The target is in use or locked.", null);
        }
        catch (Exception ex)
        {
            return new(itemId, targetId, ExecutionItemOutcome.Failed, "target.failed", ex.GetType().Name, null);
        }
    }

    private static bool IsCloudPlaceholder(FileAttributes attributes)
    {
        const FileAttributes recallOnOpen = (FileAttributes)0x00040000;
        const FileAttributes recallOnDataAccess = (FileAttributes)0x00400000;
        return (attributes & (FileAttributes.Offline | recallOnOpen | recallOnDataAccess)) != 0;
    }
}
