using System.Collections.Immutable;
using Clyr.Contracts;
using Clyr.Persistence;

namespace Clyr.Persistence.Tests;

public sealed class ExecutionReceiptStoreTests
{
    [Fact]
    public async Task RoundTripPreservesAccountingAndDigest()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteExecutionReceiptStore(database.Path);
        var receipt = Create(ExecutionState.Completed);

        await store.SaveAsync(receipt);
        var loaded = await store.GetAsync(receipt.ExecutionId);

        Assert.NotNull(loaded);
        Assert.Equal(receipt.Digest, loaded.Digest);
        Assert.Equal(receipt.Summary, loaded.Summary);
        Assert.Equal(receipt.FinalState, loaded.FinalState);
        Assert.Equal(receipt.DriveFreeBytesBefore, loaded.DriveFreeBytesBefore);
        Assert.Equal(receipt.ObservedFreeSpaceDeltaBytes, loaded.ObservedFreeSpaceDeltaBytes);
    }

    [Fact]
    public async Task TerminalReceiptCannotBeOverwritten()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteExecutionReceiptStore(database.Path);
        var receipt = Create(ExecutionState.Completed);
        await store.SaveAsync(receipt);

        var attempt = () => store.SaveAsync(receipt);
        var exception = await Assert.ThrowsAsync<ExecutionReceiptStoreException>(attempt);
        Assert.Equal("receipt.immutable", exception.Code);
    }

    [Fact]
    public async Task ListOrdersNewestFirstAndDiscardRemovesOnlyOneRow()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteExecutionReceiptStore(database.Path);
        var first = Create(ExecutionState.Completed);
        var second = Create(ExecutionState.Failed, startedAtUtc: first.StartedAtUtc + TimeSpan.FromMinutes(1));
        await store.SaveAsync(first);
        await store.SaveAsync(second);

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
        await store.SaveAsync(abandoned);

        var reconciled = await store.ReconcileInterruptedAsync(TimeSpan.FromMinutes(5), startedAtUtc + TimeSpan.FromHours(1));

        Assert.Equal(1, reconciled);
        var loaded = await store.GetAsync(abandoned.ExecutionId);
        Assert.Equal(ExecutionState.Interrupted, loaded!.FinalState);
    }

    [Fact]
    public async Task PersistedReceiptContainsNoRawFilePaths()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteExecutionReceiptStore(database.Path);
        var receipt = Create(ExecutionState.Completed);
        await store.SaveAsync(receipt);

        var rawFile = File.ReadAllText(database.Path, System.Text.Encoding.Latin1);
        Assert.DoesNotContain(@"C:\Users", rawFile, StringComparison.OrdinalIgnoreCase);
    }

    private static ExecutionReceipt Create(ExecutionState state, DateTimeOffset? startedAtUtc = null) =>
        Create(state, startedAtUtc ?? new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero));

    private static ExecutionReceipt Create(ExecutionState state, DateTimeOffset startedAtUtc) => new(
        1, new ExecutionId(Guid.NewGuid()), new CleanupPlanId(Guid.NewGuid()), new string('a', 64), "test-app-1",
        "1.1.0", new string('b', 64), startedAtUtc,
        state is ExecutionState.Running ? null : startedAtUtc + TimeSpan.FromMinutes(5),
        state, false, false, new ExecutionSummary(2, 2, 0, 0, 200, 200, 0, 0), 1_000_000_000, 1_000_000_200, 200,
        ImmutableDictionary<string, int>.Empty.Add("Removed", 2), ["warning"], ["limitation"], "support-safe", new string('c', 64));

    private sealed class TemporaryDatabase : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clyr-receipts-" + Guid.NewGuid().ToString("N") + ".db");
        public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
    }
}
