using System.Collections.Immutable;
using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

public sealed class CleanupPlanningTests
{
    [Theory]
    [InlineData("developer.npm.cache", StorageCategory.DeveloperCache, FindingStatus.Informational, FindingConfidence.High, CleanupEligibility.DryRunEligible)]
    [InlineData("user.downloads", StorageCategory.UserDownloads, FindingStatus.Review, FindingConfidence.High, CleanupEligibility.ManualReviewOnly)]
    [InlineData("windows.system32", StorageCategory.WindowsSystemManaged, FindingStatus.Protected, FindingConfidence.Confirmed, CleanupEligibility.Protected)]
    [InlineData("unknown", StorageCategory.Unknown, FindingStatus.Unknown, FindingConfidence.Unknown, CleanupEligibility.InsufficientEvidence)]
    [InlineData("browser.chrome.cache", StorageCategory.BrowserCache, FindingStatus.Informational, FindingConfidence.High, CleanupEligibility.InsufficientEvidence)]
    public void EligibilityFailsClosed(string rule, StorageCategory category, FindingStatus status,
        FindingConfidence confidence, CleanupEligibility expected)
    {
        var snapshot = Snapshot([new(rule, "1", category, confidence, status, 100, 2)]);
        Assert.Equal(expected, Assert.Single(CleanupCandidateFactory.FromSnapshot(snapshot)).Eligibility);
    }

    [Fact]
    public void UnsupportedFilesystemOverridesEligibleRule()
    {
        var snapshot = Snapshot([new("developer.npm.cache", "1", StorageCategory.DeveloperCache,
            FindingConfidence.High, FindingStatus.Informational, 100, 2)]);
        snapshot = snapshot with { Drive = snapshot.Drive with { FileSystem = "ReFS" } };
        Assert.Equal(CleanupEligibility.Unsupported, Assert.Single(CleanupCandidateFactory.FromSnapshot(snapshot)).Eligibility);
    }

    [Fact]
    public void ProtectedStatusOverridesOtherwiseEligibleRule()
    {
        var snapshot = Snapshot([new("developer.npm.cache", "1", StorageCategory.DeveloperCache,
            FindingConfidence.High, FindingStatus.Protected, 100, 2)]);
        Assert.Equal(CleanupEligibility.Protected, Assert.Single(CleanupCandidateFactory.FromSnapshot(snapshot)).Eligibility);
    }

    [Fact]
    public void PlanIsImmutableOrderedBoundAndIntegrityChecked()
    {
        var candidates = new[] { PlanningFixtures.Candidate("b", 200), PlanningFixtures.Candidate("a", 100) };
        var plan = PlanningFixtures.Plan(candidates, ["b", "a"]);
        Assert.Equal(["a", "b"], plan.Items.Select(item => item.FindingId));
        Assert.Equal(300, plan.TotalImpact.ObservedLogicalBytes);
        Assert.Equal(6, plan.TotalImpact.ItemCount);
        Assert.Equal(64, plan.Digest.Length);
        Assert.Equal(plan.Digest, CleanupPlanCanonicalizer.Digest(plan));
        Assert.Equal(typeof(ImmutableArray<CleanupPlanItem>), plan.Items.GetType());
        Assert.Equal(ExecutionAvailability.ExecutionNotAvailableInPhase5, plan.ExecutionAvailability);
    }

    [Fact]
    public void ChangedSelectionCreatesNewIdentityAndDigest()
    {
        var candidates = new[] { PlanningFixtures.Candidate("a", 100), PlanningFixtures.Candidate("b", 200) };
        var first = PlanningFixtures.Plan(candidates, ["a"]);
        var second = PlanningFixtures.Plan(candidates, ["a", "b"]);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.Binding.ItemSelectionIdentity, second.Binding.ItemSelectionIdentity);
        Assert.NotEqual(first.Digest, second.Digest);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("protected")]
    public void InvalidSelectionIsRejectedAtomically(string kind)
    {
        var candidate = kind == "protected" ? PlanningFixtures.Candidate("a", 1) with
        { Eligibility = CleanupEligibility.Protected, Action = null, Risk = RiskLevel.Prohibited }
            : PlanningFixtures.Candidate("a", 1);
        var selected = kind switch { "empty" => Array.Empty<string>(), "duplicate" => ["a", "a"], _ => ["a"] };
        Assert.Throws<InvalidOperationException>(() => PlanningFixtures.Plan([candidate], selected));
    }

    private static StorageSnapshot Snapshot(IReadOnlyList<SnapshotFinding> findings) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, "test", DateTimeOffset.UtcNow, ScanMode.Quick,
            SnapshotState.Complete, new("drive", DriveIdentityQuality.Stable, "C:" + (char)92, "NTFS", 1000, 500, 500),
            100, 100, 0, 0, new(10, 2, 0, 0, 0, 0, 0, false, false, false),
            "clyr.builtin", "1.1.0", "digest", [], findings, []);
}

internal static class PlanningFixtures
{
    public static CleanupCandidate Candidate(string id, long bytes)
    {
        var consequence = new CleanupConsequence("Cache", "Speeds repeat work.", "May be recreated.", true,
            "Downloads may increase.", "Restart may be required.", "Sessions are excluded.",
            "No rollback is promised.", "Application state is unknown.");
        var action = new ActionDefinition(CleanupActionType.ReportOnly, 1, "developer.npm.cache", "1",
            "1.1", "known-folder:local-app-data/npm-cache", "canonical-component-containment-v1", false,
            RollbackCapability.None, ["May be recreated."], ["approved-root", "protected-path"],
            ExecutionAvailability.ExecutionNotAvailableInPhase5, "Dry-run metadata only.", RiskLevel.Medium,
            FindingConfidence.High, TimeSpan.FromMinutes(10));
        return new(id, "Cache " + id, StorageCategory.DeveloperCache, CleanupEligibility.DryRunEligible,
            "Eligible for review.", action, new(3, Math.Max(0, bytes), null, "Logical metadata only."),
            RiskLevel.Medium, FindingConfidence.High, consequence, []);
    }

    public static CleanupPlan Plan(IReadOnlyList<CleanupCandidate> candidates, IReadOnlyList<string> selected) =>
        CleanupPlanBuilder.Create(new(Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"), "drive", "clyr.builtin", "1.1.0",
            "pack-digest", "test", "support-safe",
            new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero), candidates, selected));

    public static PlanValidationContext Context(CleanupPlan plan) => new(plan.Expiry.CreatedAtUtc,
        plan.Binding.SourceScanId, plan.Binding.SourceSnapshotId, plan.Binding.DriveIdentity,
        plan.Binding.SourceRulePackId, plan.Binding.SourceRulePackVersion, plan.Binding.SourceRulePackDigest,
        plan.Binding.CategoryRegistryVersion, plan.Binding.ApplicationCompatibilityVersion,
        plan.Binding.PrivacyMode, ImmutableDictionary<string, CleanupTarget>.Empty);
}

