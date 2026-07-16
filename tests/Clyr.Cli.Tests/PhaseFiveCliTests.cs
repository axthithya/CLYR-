using Clyr.Cli;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Rules;

namespace Clyr.Cli.Tests;

public sealed class PhaseFiveCliTests
{
    [Fact]
    public void CandidatesCreateShowValidateAndDiscardStayDryRunOnly()
    {
        var snapshotStore = new PlanSnapshotStore();
        var planStore = new InMemoryCleanupPlanStore();
        var app = Create(snapshotStore, planStore);
        var output = new StringWriter();
        Assert.Equal(0, app.Run(["plan", "candidates", "--snapshot", snapshotStore.Snapshot.Id.ToString(), "--json"],
            output, TextWriter.Null));
        Assert.Contains("dry-run-eligible", output.ToString(), StringComparison.Ordinal);
        var findingId = CleanupCandidateFactory.FromSnapshot(snapshotStore.Snapshot).Single().FindingId;
        output.GetStringBuilder().Clear();
        Assert.Equal(0, app.Run(["plan", "create", "--snapshot", snapshotStore.Snapshot.Id.ToString(),
            "--finding", findingId, "--json"], output, TextWriter.Null));
        Assert.Contains("execution-not-available-in-phase5", output.ToString(), StringComparison.Ordinal);
        var plan = Assert.Single(planStoreValues(planStore, output.ToString()));
        output.GetStringBuilder().Clear();
        Assert.Equal(0, app.Run(["plan", "show", plan.Id.ToString(), "--json"], output, TextWriter.Null));
        Assert.Contains(plan.Digest, output.ToString(), StringComparison.Ordinal);
        output.GetStringBuilder().Clear();
        Assert.Equal(0, app.Run(["plan", "validate", plan.Id.ToString()], output, TextWriter.Null));
        Assert.Contains("Valid", output.ToString(), StringComparison.Ordinal);
        output.GetStringBuilder().Clear();
        Assert.Equal(0, app.Run(["plan", "discard", plan.Id.ToString()], output, TextWriter.Null));
        Assert.Null(planStore.Find(plan.Id));
    }

    [Fact]
    public void ExportWritesOnlyExplicitPrivacySafeReport()
    {
        var snapshotStore = new PlanSnapshotStore();
        var planStore = new InMemoryCleanupPlanStore();
        var app = Create(snapshotStore, planStore);
        var findingId = CleanupCandidateFactory.FromSnapshot(snapshotStore.Snapshot).Single().FindingId;
        var created = new StringWriter();
        Assert.Equal(0, app.Run(["plan", "create", "--snapshot", snapshotStore.Snapshot.Id.ToString(),
            "--finding", findingId, "--json"], created, TextWriter.Null));
        var plan = Assert.Single(planStoreValues(planStore, created.ToString()));
        var path = Path.Combine(Path.GetTempPath(), "clyr-plan-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.Equal(0, app.Run(["plan", "export", plan.Id.ToString(), "--output", path],
                TextWriter.Null, TextWriter.Null));
            var report = File.ReadAllText(path);
            Assert.True(CleanupPlanReportExporter.Validate(report));
            Assert.DoesNotContain("Users", report, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData("execute")]
    [InlineData("apply")]
    [InlineData("clean")]
    [InlineData("delete")]
    public void MutationCommandsDoNotExist(string command)
    {
        var error = new StringWriter();
        Assert.Equal(2, Create(new PlanSnapshotStore(), new InMemoryCleanupPlanStore())
            .Run(["plan", command], TextWriter.Null, error));
        Assert.Contains("Usage:", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ProtectedFindingAndInvalidIdsFailClosed()
    {
        var store = new PlanSnapshotStore(protectedFinding: true);
        var app = Create(store, new InMemoryCleanupPlanStore());
        var candidate = CleanupCandidateFactory.FromSnapshot(store.Snapshot).Single();
        Assert.Equal(CleanupEligibility.Protected, candidate.Eligibility);
        Assert.Equal(2, app.Run(["plan", "create", "--snapshot", store.Snapshot.Id.ToString(),
            "--finding", candidate.FindingId], TextWriter.Null, TextWriter.Null));
        Assert.Equal(2, app.Run(["plan", "show", "invalid"], TextWriter.Null, TextWriter.Null));
    }

    private static IEnumerable<CleanupPlan> planStoreValues(InMemoryCleanupPlanStore store, string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var id = new CleanupPlanId(Guid.Parse(document.RootElement.GetProperty("planId").GetString()!));
        var plan = store.Find(id);
        return plan is null ? [] : [plan];
    }

    private static CliApplication Create(PlanSnapshotStore snapshots, InMemoryCleanupPlanStore plans)
    {
        var environment = new EnvironmentFixture();
        var schema = File.ReadAllText(Path.Combine(Root(), "rules", "schemas", "rule.schema.json"));
        return new(environment, new DemoDataService(), new RuleValidator(schema),
            new PrivacyRedactor(environment), "test", new Drives(), new Scanner(),
            new ScanReportExporter(), null, snapshots, plans);
    }

    private static string Root() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private sealed class EnvironmentFixture : IEnvironmentInfo
    {
        public string UserName => "Private";
        public string UserProfilePath => "C:/Users/Private";
        public string OperatingSystem => "Windows";
        public string Architecture => "X64";
    }
    private sealed class Drives : IDriveDiscovery { public IReadOnlyList<DriveSummary> Discover() => []; }
    private sealed class Scanner : IScanService
    {
        public Task<ScanResult> ScanAsync(ScanRequest request, IProgress<ScanProgress>? progress,
            CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class PlanSnapshotStore : ISnapshotStore
    {
        public PlanSnapshotStore(bool protectedFinding = false)
        {
            var finding = protectedFinding
                ? new SnapshotFinding("windows.system32", "1", StorageCategory.WindowsSystemManaged,
                    FindingConfidence.Confirmed, FindingStatus.Protected, 100, 2)
                : new SnapshotFinding("developer.npm.cache", "1.1.0", StorageCategory.DeveloperCache,
                    FindingConfidence.High, FindingStatus.Informational, 100, 2);
            Snapshot = new(Guid.NewGuid(), Guid.NewGuid(), 1, "test", DateTimeOffset.UtcNow,
                ScanMode.Quick, SnapshotState.Complete,
                new("drive", DriveIdentityQuality.Stable, "C:" + (char)92, "NTFS", 1000, 500, 500),
                100, 100, 0, 0, new(2, 1, 0, 0, 0, 0, 0, false, false, false),
                "clyr.builtin", "1.1.0", "digest", [], [finding], []);
        }
        public StorageSnapshot Snapshot { get; }
        public Task<SnapshotSaveResult> SaveAsync(StorageSnapshot value, CancellationToken token = default) =>
            Task.FromResult(new SnapshotSaveResult(false, null, "disabled", "disabled"));
        public Task<IReadOnlyList<SnapshotSummary>> ListAsync(int limit = 100, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<SnapshotSummary>>([]);
        public Task<StorageSnapshot?> GetAsync(Guid id, CancellationToken token = default) =>
            Task.FromResult(id == Snapshot.Id ? Snapshot : null);
        public Task<bool> DeleteAsync(Guid id, CancellationToken token = default) => Task.FromResult(false);
        public Task<int> ClearAsync(CancellationToken token = default) => Task.FromResult(0);
        public Task<HistorySettings> GetSettingsAsync(CancellationToken token = default) =>
            Task.FromResult(HistorySettings.Default);
        public Task SetSettingsAsync(HistorySettings settings, CancellationToken token = default) => Task.CompletedTask;
    }
}

