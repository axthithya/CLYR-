using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

public sealed class CleanupSecurityTests
{
    [Theory]
    [InlineData("C:/safe/cache/item.tmp", "C:/safe/cache", false, true, "path.valid")]
    [InlineData("C:/safe/cache2/item.tmp", "C:/safe/cache", false, false, "path.outside-root")]
    [InlineData("C:/safe/cache/../Windows/x", "C:/safe/cache", false, false, "path.traversal")]
    [InlineData("C:/safe/x:stream", "C:/safe", false, false, "path.ads")]
    [InlineData("C:/safe/x", "C:/safe", true, false, "path.reparse")]
    [InlineData("C:/Windows/System32/x", "C:/", false, false, "path.protected")]
    [InlineData("C:/safe/FILE~1.tmp", "C:/safe", false, false, "path.ambiguous")]
    [InlineData("%TEMP%/x", "C:/safe", false, false, "path.environment")]
    public void PathSafetyIsComponentAwareAndFailClosed(string path, string root, bool reparse, bool valid, string code)
    {
        var result = WindowsPathSafetyValidator.Validate(path, root, reparse);
        Assert.Equal(valid, result.IsValid);
        Assert.Equal(code, result.Code);
    }

    [Fact]
    public void NetworkAndDeviceNamespacesAreRejected()
    {
        var separator = ((char)92).ToString();
        foreach (var path in new[]
        {
            separator + separator + "server" + separator + "share",
            separator + separator + "?" + separator + "C:" + separator + "safe",
            separator + separator + "." + separator + "C:" + separator + "safe"
        })
            Assert.Equal("path.namespace", WindowsPathSafetyValidator.Validate(path, "C:/safe", false).Code);
    }

    [Fact]
    public void ValidationDetectsExpiryStalenessAndDigestTampering()
    {
        var plan = PlanningFixtures.Plan([PlanningFixtures.Candidate("a", 100)], ["a"]);
        Assert.True(CleanupPlanValidator.Validate(plan, PlanningFixtures.Context(plan)).IsValid);
        var expired = PlanningFixtures.Context(plan) with { NowUtc = plan.Expiry.ExpiresAtUtc };
        Assert.Equal(CleanupPlanStatus.Expired, CleanupPlanValidator.Validate(plan, expired).Status);
        var stale = PlanningFixtures.Context(plan) with { DriveIdentity = "different" };
        Assert.Equal(CleanupPlanStatus.Stale, CleanupPlanValidator.Validate(plan, stale).Status);
        var forged = new CleanupPlan(plan.SchemaVersion, plan.Id, plan.ApplicationVersion, plan.Binding,
            plan.Expiry, plan.Items, plan.TotalImpact, plan.Risk, plan.Confidence, plan.Warnings,
            plan.ExecutionAvailability, new string('0', 64));
        Assert.Contains(CleanupPlanValidator.Validate(forged, PlanningFixtures.Context(forged)).Diagnostics,
            item => item.Code == "plan.digest");
    }

    [Fact]
    public async Task DisabledExecutorNeverOffersMutation()
    {
        var plan = PlanningFixtures.Plan([PlanningFixtures.Candidate("a", 1)], ["a"]);
        var result = await new PhaseFiveDisabledCleanupExecutor().GetAvailabilityAsync(plan);
        Assert.False(result.Available);
        Assert.Equal("ExecutionNotAvailableInPhase5", result.Code);
    }

    [Fact]
    public void DryRunSeparatesLogicalImpactFromUnavailablePhysicalBytes()
    {
        var plan = PlanningFixtures.Plan([PlanningFixtures.Candidate("a", 100)], ["a"]);
        var validation = CleanupPlanValidator.Validate(plan, PlanningFixtures.Context(plan));
        var result = CleanupDryRunResolver.Resolve(plan, validation);
        Assert.Null(result.TotalImpact.EstimatedPhysicalBytes);
        Assert.Contains("not guaranteed recovered space", string.Join(" ", result.Limitations), StringComparison.Ordinal);
    }

    [Fact]
    public void ExportIsPrivacySafeVersionedAndValid()
    {
        var plan = PlanningFixtures.Plan([PlanningFixtures.Candidate("a", 100)], ["a"]);
        var json = CleanupPlanReportExporter.Serialize(plan,
            CleanupPlanValidator.Validate(plan, PlanningFixtures.Context(plan)));
        Assert.True(CleanupPlanReportExporter.Validate(json));
        Assert.DoesNotContain("C:/Users", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains((char)34 + "rawPathsIncluded" + (char)34 + ": false", json, StringComparison.Ordinal);
        Assert.False(CleanupPlanReportExporter.Validate("{" + (char)34 + "schemaVersion" + (char)34 + ":999}"));
    }

    [Fact]
    public void MemoryStoreIsBoundedAndDiscardTouchesOnlyPlanRecords()
    {
        var store = new InMemoryCleanupPlanStore();
        var plans = Enumerable.Range(0, 17)
            .Select(index =>
            {
                var id = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return PlanningFixtures.Plan([PlanningFixtures.Candidate(id, index)], [id]);
            })
            .ToArray();
        foreach (var plan in plans) store.Save(plan);
        Assert.Null(store.Find(plans[0].Id));
        Assert.NotNull(store.Find(plans[^1].Id));
        Assert.True(store.Discard(plans[^1].Id));
    }

    [Fact]
    public void NegativeAndOverflowingAccountingFailsClosedOrSaturates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EstimatedImpact(-1, 1, null, "invalid").Validate());
        var first = PlanningFixtures.Candidate("a", long.MaxValue);
        var second = PlanningFixtures.Candidate("b", long.MaxValue);
        Assert.Equal(long.MaxValue, PlanningFixtures.Plan([first, second], ["a", "b"]).TotalImpact.ObservedLogicalBytes);
    }
}

