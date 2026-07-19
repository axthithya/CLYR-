using Clyr.Cli;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Core.DeveloperMode;
using Clyr.Rules;

namespace Clyr.Cli.Tests;

public sealed class Phase7DeveloperModeCliTests
{
    [Fact]
    public void ToolsListsTheFullClosedRegistryWithoutRequiringASnapshot()
    {
        var app = Create(new DeveloperSnapshotStore(), new InMemoryCleanupPlanStore());
        var output = new StringWriter();
        Assert.Equal(0, app.Run(["developer", "tools", "--json"], output, TextWriter.Null));
        Assert.Contains("Docker Desktop", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Node.js / npm", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ScanReportsEveryRegisteredToolAndSurfacesTheSnapshotFinding()
    {
        var store = new DeveloperSnapshotStore();
        var app = Create(store, new InMemoryCleanupPlanStore());
        var output = new StringWriter();
        Assert.Equal(0, app.Run(["developer", "scan", "--snapshot", store.Snapshot.Id.ToString(), "--json"], output, TextWriter.Null));
        var text = output.ToString();
        Assert.Equal(DeveloperToolRegistry.Descriptors.Length,
            System.Text.Json.JsonDocument.Parse(text).RootElement.GetArrayLength());
        Assert.Contains("\"node-npm\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Developer npm Cache", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowRequiresAKnownToolIdAndAValidSnapshot()
    {
        var store = new DeveloperSnapshotStore();
        var app = Create(store, new InMemoryCleanupPlanStore());
        Assert.Equal(2, app.Run(["developer", "show", "not-a-real-tool", "--snapshot", store.Snapshot.Id.ToString()],
            TextWriter.Null, TextWriter.Null));
        var output = new StringWriter();
        Assert.Equal(0, app.Run(["developer", "show", "NodeNpm", "--snapshot", store.Snapshot.Id.ToString()], output, TextWriter.Null));
        Assert.Contains("NodeNpm", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void FindingsListsTheSnapshotDerivedDeveloperFinding()
    {
        var store = new DeveloperSnapshotStore();
        var app = Create(store, new InMemoryCleanupPlanStore());
        var output = new StringWriter();
        Assert.Equal(0, app.Run(["developer", "findings", "--snapshot", store.Snapshot.Id.ToString(), "--json"], output, TextWriter.Null));
        Assert.Contains("Developer npm Cache", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PlanBuildsAnIntegrityCheckedPlanThroughTheSameBuilderAsEveryOtherFinding()
    {
        var store = new DeveloperSnapshotStore();
        var planStore = new InMemoryCleanupPlanStore();
        var app = Create(store, planStore);
        var findingId = CleanupCandidateFactory.FromSnapshot(store.Snapshot).Single().FindingId;
        var output = new StringWriter();
        Assert.Equal(0, app.Run(["developer", "plan", findingId, "--snapshot", store.Snapshot.Id.ToString(), "--json"], output, TextWriter.Null));
        using var document = System.Text.Json.JsonDocument.Parse(output.ToString());
        var planId = new CleanupPlanId(Guid.Parse(document.RootElement.GetProperty("planId").GetString()!));
        var plan = planStore.Find(planId);
        Assert.NotNull(plan);
        Assert.Single(plan!.Items);
    }

    [Fact]
    public void PlanFailsClosedOnAnUnknownFindingId()
    {
        var store = new DeveloperSnapshotStore();
        var app = Create(store, new InMemoryCleanupPlanStore());
        Assert.Equal(1, app.Run(["developer", "plan", "not-a-real-finding", "--snapshot", store.Snapshot.Id.ToString()],
            TextWriter.Null, TextWriter.Null));
    }

    [Fact]
    public void CapabilitiesTruthfullyReportsNoExecutableDeveloperAction()
    {
        var app = Create(new DeveloperSnapshotStore(), new InMemoryCleanupPlanStore());
        var output = new StringWriter();
        Assert.Equal(0, app.Run(["developer", "capabilities"], output, TextWriter.Null));
        Assert.Contains("No developer-tool action is currently enabled for execution.", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DoctorReportsPerToolDiscoveryStatusWithoutRequiringASnapshot()
    {
        var app = Create(new DeveloperSnapshotStore(), new InMemoryCleanupPlanStore());
        var output = new StringWriter();
        Assert.Equal(0, app.Run(["developer", "doctor", "--json"], output, TextWriter.Null));
        Assert.Equal(DeveloperToolRegistry.Descriptors.Length,
            System.Text.Json.JsonDocument.Parse(output.ToString()).RootElement.GetArrayLength());
    }

    [Theory]
    [InlineData("run")]
    [InlineData("install")]
    [InlineData("prune")]
    [InlineData("clean")]
    [InlineData("execute")]
    public void MutationAndExecutionSubcommandsDoNotExist(string command)
    {
        var error = new StringWriter();
        Assert.Equal(2, Create(new DeveloperSnapshotStore(), new InMemoryCleanupPlanStore())
            .Run(["developer", command], TextWriter.Null, error));
        Assert.Contains("Usage:", error.ToString(), StringComparison.Ordinal);
    }

    private static CliApplication Create(ISnapshotStore snapshots, ICleanupPlanStore plans)
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

    private sealed class DeveloperSnapshotStore : ISnapshotStore
    {
        public DeveloperSnapshotStore()
        {
            var finding = new SnapshotFinding("developer.npm.cache", "1.1.0", StorageCategory.DeveloperCache,
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
