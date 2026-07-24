using Clyr.Cli;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Core.Execution;
using Clyr.Persistence;
using Clyr.Rules;

namespace Clyr.Cli.Tests;

/// <summary>Every test class that writes stale fixtures directly into the CLI's real resolved trusted root
/// (%LocalAppData%\Clyr\Temp — the CLI has no override hook, unlike the WinUI path) shares this xunit collection,
/// so xunit never runs them concurrently with each other; two tests racing to scan/delete files in the same real
/// folder at once could otherwise pick up each other's fixture and report a spurious partial result.</summary>
[CollectionDefinition("ClyrOwnedTempRoot", DisableParallelization = true)]
public sealed class ClyrOwnedTempRootTestGroup;

/// <summary>
/// Exercises 'plan execute' and 'execution *' against the CLI's own resolved trusted root
/// (%LocalAppData%\Clyr\Temp) — the only root these commands ever touch — using synthetic files this test
/// creates and removes itself. No real user, system, browser, or project path is ever involved.
/// </summary>
[Collection("ClyrOwnedTempRoot")]
public sealed class Phase6ExecutionCliTests
{
    [Fact]
    public void PlanExecuteRemovesOnlyTheStaleBuiltInArtifactAndPersistsAReceipt()
    {
        var staleFile = WriteStaleFixture();
        try
        {
            using var database = new TemporaryDatabase();
            var receiptStore = new SqliteExecutionReceiptStore(database.Path);
            var snapshotStore = new EmptySnapshotStore();
            var planStore = new InMemoryCleanupPlanStore();
            var app = Create(snapshotStore, planStore, receiptStore);

            var created = new StringWriter();
            Assert.Equal(0, app.Run(["plan", "create", "--snapshot", snapshotStore.Snapshot.Id.ToString(),
                "--finding", "builtin:clyr-owned-temp-artifacts", "--json"], created, TextWriter.Null));
            var plan = Assert.Single(planStoreValues(planStore, created.ToString()));

            var executed = new StringWriter();
            var digestPrefix = plan.Digest[..12];
            var code = app.Run(["plan", "execute", plan.Id.ToString(), "--confirm-digest", digestPrefix, "--json"], executed, TextWriter.Null);

            Assert.Equal(0, code);
            Assert.Contains("\"finalState\": \"completed\"", executed.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(staleFile));

            var list = new StringWriter();
            Assert.Equal(0, app.Run(["execution", "list"], list, TextWriter.Null));
            Assert.Contains("Completed", list.ToString(), StringComparison.Ordinal);
        }
        finally { if (File.Exists(staleFile)) File.Delete(staleFile); }
    }

    [Fact]
    public void PlanExecuteRejectsWrongDigestAndReplayedPlan()
    {
        var staleFile = WriteStaleFixture();
        try
        {
            using var database = new TemporaryDatabase();
            var receiptStore = new SqliteExecutionReceiptStore(database.Path);
            var snapshotStore = new EmptySnapshotStore();
            var planStore = new InMemoryCleanupPlanStore();
            var app = Create(snapshotStore, planStore, receiptStore);

            var created = new StringWriter();
            app.Run(["plan", "create", "--snapshot", snapshotStore.Snapshot.Id.ToString(),
                "--finding", "builtin:clyr-owned-temp-artifacts", "--json"], created, TextWriter.Null);
            var plan = Assert.Single(planStoreValues(planStore, created.ToString()));

            var wrongDigest = new StringWriter();
            Assert.Equal(2, app.Run(["plan", "execute", plan.Id.ToString(), "--confirm-digest", "000000000000"], wrongDigest, wrongDigest));

            var firstRun = app.Run(["plan", "execute", plan.Id.ToString(), "--confirm-digest", plan.Digest[..12]], TextWriter.Null, TextWriter.Null);
            var replay = new StringWriter();
            var secondRun = app.Run(["plan", "execute", plan.Id.ToString(), "--confirm-digest", plan.Digest[..12]], TextWriter.Null, replay);

            Assert.Equal(1, secondRun);
            Assert.Contains("plan.consumed", replay.ToString(), StringComparison.Ordinal);
        }
        finally { if (File.Exists(staleFile)) File.Delete(staleFile); }
    }

    [Theory]
    [InlineData(["plan", "execute", "11111111-1111-1111-1111-111111111111"])]
    public void PlanExecuteWithoutConfirmDigestIsRejected(params string[] args)
    {
        var app = Create(new EmptySnapshotStore(), new InMemoryCleanupPlanStore(), null);
        var error = new StringWriter();
        Assert.Equal(2, app.Run(args, TextWriter.Null, error));
        Assert.Contains("--confirm-digest", error.ToString(), StringComparison.Ordinal);
    }

    private static string WriteStaleFixture()
    {
        var root = ClyrOwnedTempArtifactScanner.ResolveTrustedRoot();
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "cli-test-fixture-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(path, "synthetic stale scratch data written by a test");
        var old = DateTime.UtcNow.AddDays(-30);
        File.SetLastWriteTimeUtc(path, old);
        File.SetCreationTimeUtc(path, old);
        return path;
    }

    private static IEnumerable<CleanupPlan> planStoreValues(InMemoryCleanupPlanStore store, string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var id = new CleanupPlanId(Guid.Parse(document.RootElement.GetProperty("planId").GetString()!));
        var plan = store.Find(id);
        return plan is null ? [] : [plan];
    }

    private static CliApplication Create(EmptySnapshotStore snapshots, InMemoryCleanupPlanStore plans, IExecutionReceiptStore? receipts)
    {
        var environment = new EnvironmentFixture();
        var schema = File.ReadAllText(Path.Combine(Root(), "rules", "schemas", "rule.schema.json"));
        return new(environment, new DemoDataService(), new RuleValidator(schema),
            new PrivacyRedactor(environment), "test", new Drives(), new Scanner(),
            new ScanReportExporter(), null, snapshots, plans, receipts);
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
    private sealed class EmptySnapshotStore : ISnapshotStore
    {
        public EmptySnapshotStore() => Snapshot = new(Guid.NewGuid(), Guid.NewGuid(), 1, "test", DateTimeOffset.UtcNow,
            ScanMode.Quick, SnapshotState.Complete, new("drive", DriveIdentityQuality.Stable, "C:" + (char)92, "NTFS", 1000, 500, 500),
            100, 100, 0, 0, new(2, 1, 0, 0, 0, 0, 0, false, false, false), "clyr.builtin", "1.1.0", "digest", [], [], []);
        public StorageSnapshot Snapshot { get; }
        public Task<SnapshotSaveResult> SaveAsync(StorageSnapshot value, CancellationToken token = default) =>
            Task.FromResult(new SnapshotSaveResult(false, null, "disabled", "disabled"));
        public Task<IReadOnlyList<SnapshotSummary>> ListAsync(int limit = 100, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<SnapshotSummary>>([]);
        public Task<StorageSnapshot?> GetAsync(Guid id, CancellationToken token = default) =>
            Task.FromResult(id == Snapshot.Id ? Snapshot : null);
        public Task<bool> DeleteAsync(Guid id, CancellationToken token = default) => Task.FromResult(false);
        public Task<int> ClearAsync(CancellationToken token = default) => Task.FromResult(0);
        public Task<HistorySettings> GetSettingsAsync(CancellationToken token = default) => Task.FromResult(HistorySettings.Default);
        public Task SetSettingsAsync(HistorySettings settings, CancellationToken token = default) => Task.CompletedTask;
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clyr-cli-receipts-" + Guid.NewGuid().ToString("N") + ".db");
        public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
    }
}
