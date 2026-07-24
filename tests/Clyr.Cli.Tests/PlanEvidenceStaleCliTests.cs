using Clyr.Cli;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Core.Execution;
using Clyr.Persistence;
using Clyr.Rules;

namespace Clyr.Cli.Tests;

/// <summary>
/// Phase 6 safety correction: 'plan execute' must reject a plan whose bound analysis evidence has changed since
/// creation — even though the plan's digest, ScanId, drive, and rule pack all still match — the exact gap the
/// audit found for Administrator Retry. Exercises the CLI's own resolved trusted root
/// (%LocalAppData%\Clyr\Temp) using synthetic files this test creates and removes itself, exactly like the
/// existing Phase6ExecutionCliTests.
/// </summary>
[Collection("ClyrOwnedTempRoot")]
public sealed class PlanEvidenceStaleCliTests
{
    [Fact]
    public void PlanExecuteRejectsAPlanWhoseSnapshotEvidenceChangedSinceItWasCreated()
    {
        var staleFile = WriteStaleFixture();
        try
        {
            using var database = new TemporaryDatabase();
            var receiptStore = new SqliteExecutionReceiptStore(database.Path);
            var snapshotStore = new MutableSnapshotStore();
            var planStore = new InMemoryCleanupPlanStore();
            var app = Create(snapshotStore, planStore, receiptStore);

            var created = new StringWriter();
            Assert.Equal(0, app.Run(["plan", "create", "--snapshot", snapshotStore.Snapshot.Id.ToString(),
                "--finding", "builtin:clyr-owned-temp-artifacts", "--json"], created, TextWriter.Null));
            var plan = Assert.Single(PlanStoreValues(planStore, created.ToString()));

            // The analysis evidence changes after the plan was created (its ScanId, drive, and rule pack all
            // stay exactly the same — only the observed evidence itself changes, exactly like an Administrator
            // Retry enrichment).
            snapshotStore.MutateEvidence();

            var executed = new StringWriter();
            var error = new StringWriter();
            var code = app.Run(["plan", "execute", plan.Id.ToString(), "--confirm-digest", plan.Digest[..12]], executed, error);

            Assert.Equal(1, code);
            Assert.Contains("plan.stale", error.ToString(), StringComparison.Ordinal);
            Assert.True(File.Exists(staleFile), "The stale fixture file must not be removed by a rejected execution.");
        }
        finally { if (File.Exists(staleFile)) File.Delete(staleFile); }
    }

    [Fact]
    public void PlanExecuteStillSucceedsWhenEvidenceHasNotChanged()
    {
        var staleFile = WriteStaleFixture();
        try
        {
            using var database = new TemporaryDatabase();
            var receiptStore = new SqliteExecutionReceiptStore(database.Path);
            var snapshotStore = new MutableSnapshotStore();
            var planStore = new InMemoryCleanupPlanStore();
            var app = Create(snapshotStore, planStore, receiptStore);

            var created = new StringWriter();
            app.Run(["plan", "create", "--snapshot", snapshotStore.Snapshot.Id.ToString(),
                "--finding", "builtin:clyr-owned-temp-artifacts", "--json"], created, TextWriter.Null);
            var plan = Assert.Single(PlanStoreValues(planStore, created.ToString()));

            var executed = new StringWriter();
            var code = app.Run(["plan", "execute", plan.Id.ToString(), "--confirm-digest", plan.Digest[..12], "--json"], executed, TextWriter.Null);

            Assert.Equal(0, code);
            Assert.False(File.Exists(staleFile));
        }
        finally { if (File.Exists(staleFile)) File.Delete(staleFile); }
    }

    private static string WriteStaleFixture()
    {
        var root = ClyrOwnedTempArtifactScanner.ResolveTrustedRoot();
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "cli-evidence-test-fixture-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(path, "synthetic stale scratch data written by a test");
        var old = DateTime.UtcNow.AddDays(-30);
        File.SetLastWriteTimeUtc(path, old);
        File.SetCreationTimeUtc(path, old);
        return path;
    }

    private static IEnumerable<CleanupPlan> PlanStoreValues(InMemoryCleanupPlanStore store, string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var id = new CleanupPlanId(Guid.Parse(document.RootElement.GetProperty("planId").GetString()!));
        var plan = store.Find(id);
        return plan is null ? [] : [plan];
    }

    private static CliApplication Create(MutableSnapshotStore snapshots, InMemoryCleanupPlanStore plans, IExecutionReceiptStore? receipts)
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

    /// <summary>Unlike a real <see cref="ISnapshotStore"/>, this deliberately allows the same snapshot ID to
    /// return different evidence across calls — exactly what happens in production when Administrator Retry's
    /// background <c>ResultsViewModel.PersistEnrichedResultAsync</c> overwrites the same History row a plan was
    /// already bound to.</summary>
    private sealed class MutableSnapshotStore : ISnapshotStore
    {
        public MutableSnapshotStore() => Snapshot = Build(logicalBytes: 100, unknownBytes: 0);

        public StorageSnapshot Snapshot { get; private set; }

        public void MutateEvidence() => Snapshot = Snapshot with
        {
            LogicalBytesObserved = 900,
            UnknownBytes = 800,
            Categories = [new(StorageCategory.DeveloperCache, 900, 40, MeasurementPrecision.Estimated, FindingStatus.Informational)],
        };

        private static StorageSnapshot Build(long logicalBytes, long unknownBytes) => new(Guid.NewGuid(), Guid.NewGuid(), 1, "test",
            DateTimeOffset.UtcNow, ScanMode.Quick, SnapshotState.Complete,
            new("drive", DriveIdentityQuality.Stable, "C:" + (char)92, "NTFS", 1000, 500, 500),
            logicalBytes, logicalBytes - unknownBytes, unknownBytes, 0, new(2, 1, 0, 0, 0, 0, 0, false, false, false),
            "clyr.builtin", "1.1.0", "digest", [], [], []);

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
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clyr-cli-evidence-" + Guid.NewGuid().ToString("N") + ".db");
        public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
    }
}
