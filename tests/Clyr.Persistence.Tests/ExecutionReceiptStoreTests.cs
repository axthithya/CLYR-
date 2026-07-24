using System.Collections.Immutable;
using Clyr.Contracts;
using Clyr.Core.Execution;
using Clyr.Persistence;
using Microsoft.Data.Sqlite;

namespace Clyr.Persistence.Tests;

public sealed class ExecutionReceiptStoreTests
{
    [Fact]
    public async Task BeginThenCompletePreservesAccountingAndDigest()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteExecutionReceiptStore(database.Path);
        var start = Create(ExecutionState.Running);
        await store.BeginAsync(start);
        var final = Terminal(start, ExecutionState.Completed);

        await store.CompleteAsync(final.ExecutionId, final);
        var loaded = await store.GetAsync(final.ExecutionId);

        Assert.NotNull(loaded);
        Assert.Equal(final.Digest, loaded.Digest);
        Assert.Equal(final.Summary, loaded.Summary);
        Assert.Equal(final.FinalState, loaded.FinalState);
        Assert.Equal(final.DriveFreeBytesBefore, loaded.DriveFreeBytesBefore);
        Assert.Equal(final.ObservedFreeSpaceDeltaBytes, loaded.ObservedFreeSpaceDeltaBytes);
        Assert.Equal(final.SourceScanId, loaded.SourceScanId);
        Assert.Equal(final.EvidenceStateId, loaded.EvidenceStateId);
        Assert.Equal(final.ActionIds.AsEnumerable(), loaded.ActionIds.AsEnumerable());
        Assert.Equal(final.ExecutionSessionId, loaded.ExecutionSessionId);
        Assert.Equal(final.WindowsUserSidFingerprint, loaded.WindowsUserSidFingerprint);
    }

    [Fact]
    public async Task DuplicateBeginFailsSafely()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteExecutionReceiptStore(database.Path);
        var start = Create(ExecutionState.Running);
        await store.BeginAsync(start);

        var exception = await Assert.ThrowsAsync<ExecutionReceiptStoreException>(() => store.BeginAsync(start));
        Assert.Equal("receipt.duplicate-begin", exception.Code);
    }

    [Fact]
    public async Task UnknownCompletionFailsClosed()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteExecutionReceiptStore(database.Path);
        var final = Create(ExecutionState.Completed);

        var exception = await Assert.ThrowsAsync<ExecutionReceiptStoreException>(() => store.CompleteAsync(final.ExecutionId, final));
        Assert.Equal("receipt.unknown-execution", exception.Code);
    }

    [Fact]
    public async Task IdenticalRepeatedCompletionIsIdempotent()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteExecutionReceiptStore(database.Path);
        var start = Create(ExecutionState.Running);
        await store.BeginAsync(start);
        var final = Terminal(start, ExecutionState.Completed);
        await store.CompleteAsync(final.ExecutionId, final);

        await store.CompleteAsync(final.ExecutionId, final); // must not throw

        var loaded = await store.GetAsync(final.ExecutionId);
        Assert.Equal(ExecutionState.Completed, loaded!.FinalState);
    }

    [Fact]
    public async Task ConflictingRepeatedCompletionIsRejected()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteExecutionReceiptStore(database.Path);
        var start = Create(ExecutionState.Running);
        await store.BeginAsync(start);
        var final = Terminal(start, ExecutionState.Completed);
        await store.CompleteAsync(final.ExecutionId, final);

        var conflicting = Terminal(start, ExecutionState.Failed);
        var exception = await Assert.ThrowsAsync<ExecutionReceiptStoreException>(() => store.CompleteAsync(final.ExecutionId, conflicting));
        Assert.Equal("receipt.immutable", exception.Code);
    }

    [Fact]
    public async Task CompletionCannotSilentlyRetargetAnotherPlanOrDigest()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteExecutionReceiptStore(database.Path);
        var start = Create(ExecutionState.Running);
        await store.BeginAsync(start);
        var wrongPlan = Terminal(start, ExecutionState.Completed);
        var mismatched = new ExecutionReceipt(wrongPlan.SchemaVersion, wrongPlan.ExecutionId, new CleanupPlanId(Guid.NewGuid()),
            wrongPlan.SourcePlanDigest, wrongPlan.ApplicationVersion, wrongPlan.RulePackVersion, wrongPlan.DriveIdentityFingerprint,
            wrongPlan.StartedAtUtc, wrongPlan.CompletedAtUtc, wrongPlan.FinalState, wrongPlan.Cancelled, wrongPlan.ElevationUsed,
            wrongPlan.Summary, wrongPlan.DriveFreeBytesBefore, wrongPlan.DriveFreeBytesAfter, wrongPlan.ObservedFreeSpaceDeltaBytes,
            wrongPlan.OutcomeCategories, wrongPlan.Warnings, wrongPlan.Limitations, wrongPlan.PrivacyMode, wrongPlan.Digest,
            wrongPlan.SourceScanId, wrongPlan.EvidenceStateId, wrongPlan.ActionIds, wrongPlan.ExecutionSessionId, wrongPlan.WindowsUserSidFingerprint);

        var exception = await Assert.ThrowsAsync<ExecutionReceiptStoreException>(() => store.CompleteAsync(start.ExecutionId, mismatched));
        Assert.Equal("receipt.completion-mismatch", exception.Code);
    }

    [Fact]
    public async Task HasRecordForPlanDetectsAnExistingPlanButNeverANewOne()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteExecutionReceiptStore(database.Path);
        var start = Create(ExecutionState.Running);
        await store.BeginAsync(start);

        Assert.True(await store.HasRecordForPlanAsync(start.SourcePlanId, start.SourcePlanDigest));
        Assert.False(await store.HasRecordForPlanAsync(new CleanupPlanId(Guid.NewGuid()), new string('9', 64)));
    }

    [Fact]
    public async Task ListOrdersNewestFirstAndDiscardRemovesOnlyOneRow()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteExecutionReceiptStore(database.Path);
        var firstStart = Create(ExecutionState.Running);
        var secondStart = Create(ExecutionState.Running, startedAtUtc: firstStart.StartedAtUtc + TimeSpan.FromMinutes(1));
        await store.BeginAsync(firstStart);
        var first = Terminal(firstStart, ExecutionState.Completed);
        await store.CompleteAsync(first.ExecutionId, first);
        await store.BeginAsync(secondStart);
        var second = Terminal(secondStart, ExecutionState.Failed);
        await store.CompleteAsync(second.ExecutionId, second);

        var listed = await store.ListAsync();
        Assert.Equal(2, listed.Count);
        Assert.Equal(second.ExecutionId, listed[0].ExecutionId);

        Assert.True(await store.DiscardAsync(first.ExecutionId));
        Assert.Single(await store.ListAsync());
    }

    [Fact]
    public async Task ReconcileMarksStaleInFlightRowsAsInterruptedNeverCompleted()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteExecutionReceiptStore(database.Path);
        var startedAtUtc = new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero);
        var abandoned = Create(ExecutionState.Running, startedAtUtc: startedAtUtc);
        await store.BeginAsync(abandoned);

        var reconciled = await store.ReconcileInterruptedAsync(TimeSpan.FromMinutes(5), startedAtUtc + TimeSpan.FromHours(1));

        Assert.Equal(1, reconciled);
        var loaded = await store.GetAsync(abandoned.ExecutionId);
        Assert.Equal(ExecutionState.Interrupted, loaded!.FinalState);
        Assert.NotNull(loaded.CompletedAtUtc);
    }

    [Fact]
    public async Task PersistedReceiptContainsNoRawFilePaths()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteExecutionReceiptStore(database.Path);
        var start = Create(ExecutionState.Running);
        await store.BeginAsync(start);
        await store.CompleteAsync(start.ExecutionId, Terminal(start, ExecutionState.Completed));

        var rawFile = File.ReadAllText(database.Path, System.Text.Encoding.Latin1);
        Assert.DoesNotContain(@"C:\Users", rawFile, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExistingReceiptsWrittenUnderSchemaThreeSurviveMigrationToSchemaFourWithSafeDefaults()
    {
        using var database = new TemporaryDatabase();
        var legacyId = Guid.NewGuid();
        CreateLegacySchemaThreeDatabaseWithOneReceipt(database.Path, legacyId);

        // Constructing the real store runs Migrate() — this is the exact production upgrade path.
        var store = new SqliteExecutionReceiptStore(database.Path);
        var loaded = await store.GetAsync(new ExecutionId(legacyId));

        Assert.NotNull(loaded);
        Assert.Equal(ExecutionState.Completed, loaded!.FinalState);
        Assert.Equal(2, loaded.Summary.RemovedCount);
        // Safe, non-identifying defaults for columns that did not exist when this row was written — never
        // inferred or backfilled from anything that could be wrong.
        Assert.Equal(Guid.Empty, loaded.SourceScanId);
        Assert.Equal(string.Empty, loaded.EvidenceStateId);
        Assert.Empty(loaded.ActionIds);
        Assert.Equal(Guid.Empty, loaded.ExecutionSessionId);
        Assert.Equal(string.Empty, loaded.WindowsUserSidFingerprint);

        var listed = await store.ListAsync();
        Assert.Contains(listed, item => item.ExecutionId.Value == legacyId);
    }

    private static void CreateLegacySchemaThreeDatabaseWithOneReceipt(string path, Guid executionId)
    {
        Clyr.Persistence.SqliteRuntime.Initialize();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE SchemaInfo (Version INTEGER NOT NULL);
            INSERT INTO SchemaInfo(Version) VALUES (3);
            CREATE TABLE ExecutionReceipt (
                Id TEXT PRIMARY KEY, SchemaVersion INTEGER NOT NULL, SourcePlanId TEXT NOT NULL,
                SourcePlanDigest TEXT NOT NULL, ApplicationVersion TEXT NOT NULL, RulePackVersion TEXT NOT NULL,
                DriveIdentityFingerprint TEXT NOT NULL, StartedAtUtc TEXT NOT NULL, CompletedAtUtc TEXT,
                FinalState TEXT NOT NULL, Cancelled INTEGER NOT NULL, ElevationUsed INTEGER NOT NULL,
                TotalItems INTEGER NOT NULL, RemovedCount INTEGER NOT NULL, SkippedCount INTEGER NOT NULL,
                FailedCount INTEGER NOT NULL, PlannedLogicalBytes INTEGER NOT NULL, RemovedLogicalBytes INTEGER NOT NULL,
                SkippedLogicalBytes INTEGER NOT NULL, FailedLogicalBytes INTEGER NOT NULL,
                DriveFreeBytesBefore INTEGER, DriveFreeBytesAfter INTEGER, ObservedFreeSpaceDeltaBytes INTEGER,
                OutcomeCategoriesJson TEXT NOT NULL, WarningsJson TEXT NOT NULL, LimitationsJson TEXT NOT NULL,
                PrivacyMode TEXT NOT NULL, Digest TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        command.CommandText = """
            INSERT INTO ExecutionReceipt VALUES ($id,1,$plan,$digest,'test-app-1','1.1.0',$drive,$start,$end,'Completed',
                0,0,2,2,0,0,200,200,0,0,1000000000,1000000200,200,'{"Removed":2}','["warning"]','["limitation"]','support-safe',$receiptdigest);
            """;
        command.Parameters.AddWithValue("$id", executionId.ToString());
        command.Parameters.AddWithValue("$plan", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$digest", new string('a', 64));
        command.Parameters.AddWithValue("$drive", new string('b', 64));
        command.Parameters.AddWithValue("$start", "2026-07-16T00:00:00.0000000+00:00");
        command.Parameters.AddWithValue("$end", "2026-07-16T00:05:00.0000000+00:00");
        command.Parameters.AddWithValue("$receiptdigest", new string('c', 64));
        command.ExecuteNonQuery();
    }

    private static ExecutionReceipt Create(ExecutionState state, DateTimeOffset? startedAtUtc = null) =>
        Create(state, startedAtUtc ?? new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero));

    private static ExecutionReceipt Create(ExecutionState state, DateTimeOffset startedAtUtc) => new(
        1, new ExecutionId(Guid.NewGuid()), new CleanupPlanId(Guid.NewGuid()), new string('a', 64), "test-app-1",
        "1.1.0", new string('b', 64), startedAtUtc,
        state is ExecutionState.Running ? null : startedAtUtc + TimeSpan.FromMinutes(5),
        state, false, false,
        state is ExecutionState.Running ? new ExecutionSummary(2, 0, 0, 0, 200, 0, 0, 0) : new ExecutionSummary(2, 2, 0, 0, 200, 200, 0, 0),
        state is ExecutionState.Running ? null : 1_000_000_000, state is ExecutionState.Running ? null : 1_000_000_200,
        state is ExecutionState.Running ? null : 200,
        state is ExecutionState.Running ? ImmutableDictionary<string, int>.Empty : ImmutableDictionary<string, int>.Empty.Add("Removed", 2),
        ["warning"], ["limitation"], "support-safe", new string('c', 64), Guid.NewGuid(), "evidence-fixture",
        ["builtin.clyr-owned-temp-artifacts"], Guid.NewGuid(), new string('d', 64));

    /// <summary>Builds the terminal receipt that would complete <paramref name="start"/> — same identity fields,
    /// different (freshly recomputed) digest and terminal data, exactly like <c>NonElevatedCleanupExecutor</c>
    /// does between its Begin and Complete calls.</summary>
    private static ExecutionReceipt Terminal(ExecutionReceipt start, ExecutionState finalState) => new(
        start.SchemaVersion, start.ExecutionId, start.SourcePlanId, start.SourcePlanDigest, start.ApplicationVersion,
        start.RulePackVersion, start.DriveIdentityFingerprint, start.StartedAtUtc, start.StartedAtUtc + TimeSpan.FromMinutes(5),
        finalState, false, false, new ExecutionSummary(2, 2, 0, 0, 200, 200, 0, 0), 1_000_000_000, 1_000_000_200, 200,
        ImmutableDictionary<string, int>.Empty.Add("Removed", 2), start.Warnings, start.Limitations, start.PrivacyMode,
        TerminalDigestFor(start.ExecutionId, finalState), start.SourceScanId, start.EvidenceStateId, start.ActionIds,
        start.ExecutionSessionId, start.WindowsUserSidFingerprint);

    /// <summary>A distinct, deterministic 64-hex-char stand-in digest per (execution, final state) — real digests
    /// come from <c>ExecutionReceiptCanonicalizer</c>, but these tests only need two different terminal receipts
    /// for the same execution to reliably produce two different digest values, exactly like the real canonicalizer
    /// would for genuinely different terminal data.</summary>
    private static string TerminalDigestFor(ExecutionId executionId, ExecutionState finalState) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(executionId + "|" + finalState))).ToLowerInvariant();

    private sealed class TemporaryDatabase : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clyr-receipts-" + Guid.NewGuid().ToString("N") + ".db");
        public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
    }
}
