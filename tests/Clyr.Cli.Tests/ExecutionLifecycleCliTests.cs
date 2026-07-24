using Clyr.Cli;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Core.Execution;
using Clyr.Persistence;
using Clyr.Rules;

namespace Clyr.Cli.Tests;

/// <summary>
/// Phase 6 crash-recovery correction: a durable "Started" execution record left behind by a crashed process must
/// be detected and reported — never silently resumed, never guessed successful — the next time the CLI runs
/// against the same history database. Uses only a disposable temporary SQLite file this test creates and deletes
/// itself; no real cleanup target or CLYR-owned temp folder is touched.
/// </summary>
public sealed class ExecutionLifecycleCliTests
{
    [Fact]
    public void StartupReconciliationMarksAnUnresolvedStartedRowAsInterruptedOnTheNextInvocation()
    {
        using var database = new TemporaryDatabase();
        var executionId = new ExecutionId(Guid.NewGuid());
        SeedUnresolvedStartedRow(database.Path, executionId);

        var app = Create(database.Path);
        var statusOutput = new StringWriter();
        var code = app.Run(["execution", "status", executionId.ToString()], statusOutput, TextWriter.Null);

        Assert.Equal(1, code); // never a success exit code for an unresolved execution
        Assert.Contains("Interrupted", statusOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("Run a new Drive Analysis before creating another cleanup plan.", statusOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void StartupReconciliationNeverMarksAnUnresolvedRowSuccessful()
    {
        using var database = new TemporaryDatabase();
        var executionId = new ExecutionId(Guid.NewGuid());
        SeedUnresolvedStartedRow(database.Path, executionId);

        var app = Create(database.Path);
        var receiptOutput = new StringWriter();
        app.Run(["execution", "receipt", executionId.ToString()], receiptOutput, TextWriter.Null);

        Assert.DoesNotContain("\"finalState\": \"completed\"", receiptOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"finalState\": \"interrupted\"", receiptOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ListedHistoryShowsTheInterruptedGuidanceInline()
    {
        using var database = new TemporaryDatabase();
        var executionId = new ExecutionId(Guid.NewGuid());
        SeedUnresolvedStartedRow(database.Path, executionId);

        var app = Create(database.Path);
        var listOutput = new StringWriter();
        Assert.Equal(0, app.Run(["execution", "list"], listOutput, TextWriter.Null));

        Assert.Contains("Interrupted", listOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("CLYR found an execution that started but did not record a final result.", listOutput.ToString(), StringComparison.Ordinal);
    }

    private static void SeedUnresolvedStartedRow(string databasePath, ExecutionId executionId)
    {
        // Uses the real production store, exactly as NonElevatedCleanupExecutor would, so this row is genuinely
        // indistinguishable from one a real crashed process would have left behind.
        var store = new SqliteExecutionReceiptStore(databasePath);
        var start = new ExecutionReceipt(1, executionId, new CleanupPlanId(Guid.NewGuid()), new string('a', 64), "test-app-1",
            "1.1.0", new string('b', 64), new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), null, ExecutionState.Running,
            false, false, new ExecutionSummary(1, 0, 0, 0, 100, 0, 0, 0), null, null, null,
            System.Collections.Immutable.ImmutableDictionary<string, int>.Empty, [], [], "support-safe", string.Empty,
            Guid.NewGuid(), "evidence-fixture", ["builtin.clyr-owned-temp-artifacts"], Guid.NewGuid(), new string('d', 64));
        store.BeginAsync(start).GetAwaiter().GetResult();
    }

    private static CliApplication Create(string databasePath)
    {
        var environment = new EnvironmentFixture();
        var schema = File.ReadAllText(Path.Combine(Root(), "rules", "schemas", "rule.schema.json"));
        var receiptStore = new SqliteExecutionReceiptStore(databasePath);
        return new(environment, new DemoDataService(), new RuleValidator(schema),
            new PrivacyRedactor(environment), "test", new Drives(), new Scanner(),
            new ScanReportExporter(), null, new EmptySnapshotStore(), new InMemoryCleanupPlanStore(), receiptStore);
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
        public Task<SnapshotSaveResult> SaveAsync(StorageSnapshot value, CancellationToken token = default) =>
            Task.FromResult(new SnapshotSaveResult(false, null, "disabled", "disabled"));
        public Task<IReadOnlyList<SnapshotSummary>> ListAsync(int limit = 100, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<SnapshotSummary>>([]);
        public Task<StorageSnapshot?> GetAsync(Guid id, CancellationToken token = default) => Task.FromResult<StorageSnapshot?>(null);
        public Task<bool> DeleteAsync(Guid id, CancellationToken token = default) => Task.FromResult(false);
        public Task<int> ClearAsync(CancellationToken token = default) => Task.FromResult(0);
        public Task<HistorySettings> GetSettingsAsync(CancellationToken token = default) => Task.FromResult(HistorySettings.Default);
        public Task SetSettingsAsync(HistorySettings settings, CancellationToken token = default) => Task.CompletedTask;
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clyr-cli-lifecycle-" + Guid.NewGuid().ToString("N") + ".db");
        public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
    }
}
