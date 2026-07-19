using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Clyr.Contracts;

namespace Clyr.Core;

public static class CleanupPlanningConstants
{
    public const int PlanSchemaVersion = 1;
    public const string CategoryRegistryVersion = "1";
    public const string ApplicationCompatibilityVersion = "phase5-v1";
    public const int MaximumPlanItems = 128;
    public const int MaximumTargets = 1024;
    public static readonly TimeSpan MaximumPlanAge = TimeSpan.FromMinutes(10);
}

public sealed class CleanupCandidateFactory
{
    private static readonly HashSet<string> DryRunRules = new(StringComparer.Ordinal)
    {
        "developer.gradle.cache", "developer.maven.cache", "developer.npm.cache",
        "developer.nuget.packages", "developer.playwright.cache", "developer.pnpm.store"
    };

    public static IReadOnlyList<CleanupCandidate> FromScan(ScanResult result) =>
        result.Classification?.Findings.Select(finding => Create(
            finding.Id, finding.Title, finding.RuleId, finding.RuleVersion, finding.PackVersion,
            finding.Category, finding.Status, finding.Confidence, finding.LogicalBytes, finding.FileCount,
            result.FileSystem, result.Coverage.InaccessibleEntries > 0)).OrderBy(item => item.Title, StringComparer.Ordinal).ToArray() ?? [];

    public static IReadOnlyList<CleanupCandidate> FromSnapshot(StorageSnapshot snapshot) =>
        snapshot.Findings.Select(finding => Create(
            SnapshotFindingId(snapshot.Id, finding.RuleId), Humanize(finding.RuleId), finding.RuleId,
            finding.RuleVersion, snapshot.RulePackVersion, finding.Category, finding.Status, finding.Confidence,
            finding.LogicalBytes, finding.FileCount, snapshot.Drive.FileSystem,
            snapshot.Coverage.InaccessibleEntries > 0)).OrderBy(item => item.Title, StringComparer.Ordinal).ToArray();

    private static CleanupCandidate Create(string findingId, string title, string ruleId, string ruleVersion,
        string rulePackVersion, StorageCategory category, FindingStatus status, FindingConfidence confidence,
        long logicalBytes, long itemCount, string fileSystem, bool partial)
    {
        var eligibility = Decide(ruleId, category, status, confidence, fileSystem, partial);
        var risk = Risk(eligibility, category);
        var rootIdentity = RootIdentity(ruleId);
        var consequence = Consequence(ruleId, category, eligibility);
        var action = eligibility is CleanupEligibility.DryRunEligible or CleanupEligibility.ManualReviewOnly
            ? new ActionDefinition(
                eligibility == CleanupEligibility.DryRunEligible ? CleanupActionType.ReportOnly : CleanupActionType.ReviewFiles,
                1, ruleId, ruleVersion, rulePackVersion, rootIdentity, "canonical-component-containment-v1",
                false, eligibility == CleanupEligibility.ManualReviewOnly ? RollbackCapability.Manual : RollbackCapability.None,
                [consequence.PossibleOutcome], ["approved-root", "protected-path", "scan-binding", "metadata-identity"],
                ExecutionAvailability.ExecutionNotAvailableInPhase5,
                eligibility == CleanupEligibility.DryRunEligible
                    ? "Metadata may be included in an integrity-checked dry-run plan; no files can be changed."
                    : "Review metadata only. CLYR cannot determine whether user-controlled data is still needed.",
                risk, confidence, CleanupPlanningConstants.MaximumPlanAge)
            : null;
        var uncertainty = "Observed logical metadata may differ from physical allocation or future recovered space.";
        if (partial) uncertainty += " The source analysis was partial.";
        return new(findingId, title, category, eligibility, EligibilityReason(eligibility), action,
            new(Math.Max(0, itemCount), Math.Max(0, logicalBytes), null, uncertainty), risk,
            confidence, consequence, ImmutableArray<CleanupTarget>.Empty);
    }

    private static CleanupEligibility Decide(string ruleId, StorageCategory category, FindingStatus status,
        FindingConfidence confidence, string fileSystem, bool partial)
    {
        if (status == FindingStatus.Protected || ProtectedCategory(category)) return CleanupEligibility.Protected;
        if (!string.Equals(fileSystem, "NTFS", StringComparison.OrdinalIgnoreCase)) return CleanupEligibility.Unsupported;
        if (confidence is FindingConfidence.Unknown or FindingConfidence.Low) return CleanupEligibility.InsufficientEvidence;
        if (category == StorageCategory.BrowserCache) return CleanupEligibility.InsufficientEvidence;
        if (DryRunRules.Contains(ruleId)) return CleanupEligibility.DryRunEligible;
        if (category is StorageCategory.UserDownloads or StorageCategory.UserDocuments or StorageCategory.UserMedia
            or StorageCategory.ArchivesInstallers or StorageCategory.DeveloperDependencies or StorageCategory.BuildOutput
            or StorageCategory.Logs or StorageCategory.CrashDumpsDiagnostics or StorageCategory.TemporaryFiles)
            return CleanupEligibility.ManualReviewOnly;
        if (category == StorageCategory.Unknown) return CleanupEligibility.InsufficientEvidence;
        return CleanupEligibility.NotEligible;
    }

    private static bool ProtectedCategory(StorageCategory category) => category is
        StorageCategory.WindowsSystemManaged or StorageCategory.WindowsUpdateServicing or
        StorageCategory.RestoreRecovery or StorageCategory.Containers or StorageCategory.VirtualMachines or
        StorageCategory.Wsl or StorageCategory.AndroidSdkEmulators or StorageCategory.CloudSync;

    private static RiskLevel Risk(CleanupEligibility eligibility, StorageCategory category) => eligibility switch
    {
        CleanupEligibility.Protected or CleanupEligibility.Unsupported => RiskLevel.Prohibited,
        CleanupEligibility.ManualReviewOnly => RiskLevel.High,
        CleanupEligibility.DryRunEligible when category == StorageCategory.BrowserCache => RiskLevel.Low,
        CleanupEligibility.DryRunEligible => RiskLevel.Medium,
        _ => RiskLevel.Informational
    };

    private static string EligibilityReason(CleanupEligibility value) => value switch
    {
        CleanupEligibility.DryRunEligible => "Eligible for an integrity-checked metadata-only dry-run plan.",
        CleanupEligibility.ManualReviewOnly => "Review only. CLYR cannot determine whether this data is still needed.",
        CleanupEligibility.Protected => "Protected by CLYR. This location cannot be added to a cleanup plan.",
        CleanupEligibility.Unsupported => "The source or filesystem is unsupported for safe planning.",
        CleanupEligibility.InsufficientEvidence => "The available metadata is not strong enough to create a plan item.",
        _ => "This finding is explanatory and is not a cleanup-plan candidate."
    };

    private static CleanupConsequence Consequence(string ruleId, StorageCategory category, CleanupEligibility eligibility)
    {
        if (category == StorageCategory.BrowserCache)
            return new("Temporary browser assets", "Browsers cache downloaded assets to improve page loading.",
                "A future action could make sites load more slowly on first use while the browser recreates data.",
                true, "Network usage may temporarily increase as assets are downloaded again.",
                "The browser may need to be closed in a future execution phase.", "Credential and session stores are excluded.",
                "Direct cache cleanup may have no rollback.", "CLYR cannot verify browser state or future cache recreation.");
        if (category == StorageCategory.DeveloperCache)
            return new("Developer tool cache", "Development tools retain downloaded packages and generated cache data.",
                "A future tool-owned action could require packages to be restored or rebuilt.", true,
                "Package downloads may increase.", "Builds may be slower until caches are recreated.",
                "Project source and credentials are not candidates.", "Rollback depends on the owning tool and is not promised.",
                "CLYR cannot verify offline package availability or active tool processes.");
        if (eligibility == CleanupEligibility.Protected)
            return new("Protected or system-managed data", "Windows or an application manages this location.",
                "No cleanup action is permitted.", false, "Unknown.", "Modification could damage the system or application.",
                "Credentials, databases, and user state may be present.", "No rollback is offered.",
                "Size never overrides the protected policy.");
        if (eligibility == CleanupEligibility.ManualReviewOnly)
            return new(Humanize(category.ToString()), "This data may have been created or chosen by the user.",
                "A future choice could remove data that is still important.", false, "Unknown.",
                "Applications or projects may stop working.", "Personal or authored data may be affected.",
                "Manual review is required; rollback is not guaranteed.", "CLYR cannot infer intent from path or size.");
        return new(Humanize(ruleId), "The finding explains observed storage.", "No action is available.",
            false, "Unknown.", "Unknown.", "Unknown.", "No rollback is applicable.",
            "Additional evidence is required before planning.");
    }

    private static string RootIdentity(string ruleId) => ruleId switch
    {
        "browser.chrome.cache" => "known-folder:local-app-data/chrome/cache",
        "browser.edge.cache" => "known-folder:local-app-data/edge/cache",
        "browser.firefox.cache" => "known-folder:local-app-data/firefox/cache",
        "developer.npm.cache" => "known-folder:local-app-data/npm-cache",
        "developer.nuget.packages" => "known-folder:user-profile/nuget-packages",
        "developer.gradle.cache" => "known-folder:user-profile/gradle-cache",
        "developer.maven.cache" => "known-folder:user-profile/maven-cache",
        "developer.pnpm.store" => "known-folder:local-app-data/pnpm-store",
        "developer.playwright.cache" => "known-folder:local-app-data/playwright-cache",
        _ => "source-finding:review-only"
    };

    private static string SnapshotFindingId(Guid id, string ruleId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{id:D}|{ruleId}"))).ToLowerInvariant()[..24];

    private static string Humanize(string value) => DisplayNames.FromDottedIdentifier(value);
}

