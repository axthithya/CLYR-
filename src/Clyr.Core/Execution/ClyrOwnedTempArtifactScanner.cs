using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Clyr.Contracts;

namespace Clyr.Core.Execution;

/// <summary>
/// Discovers exact, bounded candidates for the single enabled Phase 6 built-in action. This is a dedicated
/// app-owned-folder scanner, not the classification rule engine: nothing here can be redirected by a rule pack.
/// </summary>
public static class ClyrOwnedTempArtifactScanner
{
    public static string ResolveTrustedRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clyr", "Temp");

    public static CleanupCandidate? Scan(IClock clock, string? rootOverride = null)
    {
        var capability = BuiltInExecutionActions.ClyrOwnedTempArtifacts;
        var root = rootOverride ?? ResolveTrustedRoot();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;

        var cutoff = clock.UtcNow - capability.MinimumAge;
        var volumeIdentity = Path.GetPathRoot(root)?.ToUpperInvariant() ?? "unknown";
        var targets = ImmutableArray.CreateBuilder<CleanupTarget>();
        long totalBytes = 0;

        foreach (var path in EnumerateCandidateFiles(root))
        {
            if (targets.Count >= capability.MaxItems || totalBytes >= capability.MaxTotalBytes) break;
            FileInfo info;
            try { info = new FileInfo(path); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            if (!info.Exists) continue;
            if (info.LastWriteTimeUtc >= cutoff) continue;

            var validation = WindowsPathSafetyValidator.Validate(path, root, (info.Attributes & FileAttributes.ReparsePoint) != 0);
            if (!validation.IsValid) continue;

            var reparse = (info.Attributes & FileAttributes.ReparsePoint) != 0;
            var cloud = IsCloudPlaceholder(info.Attributes);
            targets.Add(new CleanupTarget(
                TargetId(path), capability.TrustedRootIdentity, RedactedLocation(path, root), path,
                volumeIdentity, null, info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc,
                info.Attributes.ToString(), reparse, cloud, TargetState.Observed));
            totalBytes += info.Length;
        }

        if (targets.Count == 0) return null;

        var action = new ActionDefinition(
            CleanupActionType.TrustedBuiltInCleanup, SchemaVersion: 1, SourceRuleId: capability.ActionId,
            SourceRuleVersion: "1", SourceRulePackVersion: "builtin-1", AllowedRootIdentity: capability.TrustedRootIdentity,
            PathContainmentPolicy: "canonical-component-containment-v1", RequiresElevation: capability.RequiresElevation,
            Rollback: RollbackCapability.None,
            ExpectedSideEffects: ["Stale CLYR-owned temporary scratch files are permanently removed."],
            ValidationRequirements: ["approved-root", "protected-path", "target-identity", "age-threshold", "execution-token"],
            ExecutionAvailability: ExecutionAvailability.Phase6BuiltInExecutable,
            Explanation: capability.Explanation, Risk: capability.Risk, Confidence: FindingConfidence.High,
            MaximumPlanAge: TimeSpan.FromMinutes(10));

        var consequence = new CleanupConsequence(
            "CLYR's own temporary scratch files",
            "CLYR writes short-lived scratch data while exporting reports and staging diagnostics.",
            "Removing it frees disk space; CLYR recreates scratch files automatically the next time it needs them.",
            CanRegenerate: true, NetworkImpact: "None.", ApplicationImpact: "None; this data belongs only to CLYR.",
            SessionImpact: "None.", RollbackStatement: "There is no rollback; files are regenerated automatically as needed.",
            Unknowns: "None known — CLYR is the only writer to this folder.");

        var impact = new EstimatedImpact(targets.Count, totalBytes, null,
            "Logical size only; actual free-space change may differ from removed logical bytes.");

        return new CleanupCandidate(
            "builtin:clyr-owned-temp-artifacts", "CLYR temporary scratch files", StorageCategory.TemporaryFiles,
            CleanupEligibility.DryRunEligible, "Eligible for Phase 6 low-risk built-in execution.", action,
            impact, capability.Risk, FindingConfidence.High, consequence, targets.ToImmutable());
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFiles(root, "*", options).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch (IOException) { yield break; }
        catch (UnauthorizedAccessException) { yield break; }
        foreach (var path in entries) yield return path;
    }

    private static bool IsCloudPlaceholder(FileAttributes attributes)
    {
        const FileAttributes recallOnOpen = (FileAttributes)0x00040000;
        const FileAttributes recallOnDataAccess = (FileAttributes)0x00400000;
        return (attributes & (FileAttributes.Offline | recallOnOpen | recallOnDataAccess)) != 0;
    }

    private static string TargetId(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant()[..24];

    private static string RedactedLocation(string path, string root) =>
        path.Length > root.Length ? "<clyr-temp>" + path[root.Length..] : "<clyr-temp>";
}
