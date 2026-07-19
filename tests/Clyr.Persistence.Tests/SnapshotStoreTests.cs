using Clyr.Contracts;
using Clyr.Persistence;

namespace Clyr.Persistence.Tests;

public sealed class SnapshotStoreTests
{
    [Fact]
    public async Task RoundTripUsesNormalizedChildrenAndUtc()
    {
        using var database = new TemporaryDatabase(); var store = new SqliteSnapshotStore(database.Path); var snapshot = Create();
        Assert.True((await store.SaveAsync(snapshot)).Saved);
        var loaded = await store.GetAsync(snapshot.Id);
        Assert.NotNull(loaded); Assert.Equal(snapshot.Categories, loaded.Categories); Assert.Equal(snapshot.Findings, loaded.Findings); Assert.Equal(TimeSpan.Zero, loaded.CapturedAtUtc.Offset);
    }

    [Fact]
    public async Task DuplicateScanSessionIsIdempotent()
    {
        using var database = new TemporaryDatabase(); var store = new SqliteSnapshotStore(database.Path); var snapshot = Create();
        await store.SaveAsync(snapshot); var duplicate = await store.SaveAsync(snapshot with { Id = Guid.NewGuid() });
        Assert.False(duplicate.Saved); Assert.Single(await store.ListAsync());
    }

    [Fact]
    public async Task FailedSnapshotIsNeverStored()
    {
        using var database = new TemporaryDatabase(); var store = new SqliteSnapshotStore(database.Path);
        Assert.False((await store.SaveAsync(Create() with { State = SnapshotState.Failed })).Saved); Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task DeleteAndClearOnlyRemoveSnapshotRows()
    {
        using var database = new TemporaryDatabase(); var store = new SqliteSnapshotStore(database.Path); var first = Create(); var second = Create() with { Id = Guid.NewGuid(), ScanId = Guid.NewGuid() };
        await store.SaveAsync(first); await store.SaveAsync(second); Assert.True(await store.DeleteAsync(first.Id)); Assert.Equal(1, await store.ClearAsync()); Assert.Empty(await store.ListAsync()); Assert.Equal(HistorySettings.Default, await store.GetSettingsAsync());
    }

    [Fact]
    public async Task RetentionKeepsAtLeastTwoComparableBaselines()
    {
        using var database = new TemporaryDatabase(); var store = new SqliteSnapshotStore(database.Path); await store.SetSettingsAsync(new(true, 2, true, true));
        for (var i = 0; i < 3; i++) { var item = Create() with { Id = Guid.NewGuid(), ScanId = Guid.NewGuid(), CapturedAtUtc = DateTimeOffset.UtcNow.AddMinutes(i) }; await store.SaveAsync(item); }
        Assert.Equal(2, (await store.ListAsync()).Count);
    }

    [Fact]
    public async Task UpdateAsyncRefreshesTheExistingRowInPlaceRatherThanInsertingASecondRecord()
    {
        using var database = new TemporaryDatabase(); var store = new SqliteSnapshotStore(database.Path); var original = Create();
        await store.SaveAsync(original);
        var enriched = original with
        {
            LogicalBytesObserved = original.LogicalBytesObserved + 100,
            UnknownBytes = original.UnknownBytes + 100,
            Categories = [new(StorageCategory.DeveloperCache, 100, 1, MeasurementPrecision.Estimated, FindingStatus.Informational)],
            Findings = [new("developer.retry", "1", StorageCategory.DeveloperCache, FindingConfidence.High, FindingStatus.Informational, 100, 1)],
        };
        var result = await store.UpdateAsync(enriched);
        Assert.True(result.Saved);
        Assert.Equal(original.Id, result.SnapshotId); // Same stored record, never a second row.

        var all = await store.ListAsync();
        Assert.Single(all); // Still exactly one Drive Analysis record for this ScanId.
        var loaded = await store.GetAsync(original.Id);
        Assert.NotNull(loaded);
        Assert.Equal(original.ScanId, loaded.ScanId);
        Assert.Equal(original.CapturedAtUtc.ToUniversalTime(), loaded.CapturedAtUtc); // Original capture time preserved.
        Assert.Equal(original.LogicalBytesObserved + 100, loaded.LogicalBytesObserved);
        Assert.Equal(enriched.Categories, loaded.Categories);
        Assert.Equal(enriched.Findings, loaded.Findings);
    }

    [Fact]
    public async Task UpdateAsyncFailsSafelyWhenNoExistingRecordMatchesTheScanId()
    {
        using var database = new TemporaryDatabase(); var store = new SqliteSnapshotStore(database.Path);
        var result = await store.UpdateAsync(Create());
        Assert.False(result.Saved);
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public void KeyIsStableAndExactly256Bits()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"), "identity.key"); try { var provider = new FileIdentityKeyProvider(path); var first = provider.GetOrCreateKey(); Assert.Equal(32, first.Length); Assert.Equal(first, provider.GetOrCreateKey()); } finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(System.IO.Path.GetDirectoryName(path))) Directory.Delete(System.IO.Path.GetDirectoryName(path)!); }
    }

    private static StorageSnapshot Create() => new(Guid.NewGuid(), Guid.NewGuid(), 1, "test", DateTimeOffset.UtcNow, ScanMode.Quick, SnapshotState.Complete, new("safe", DriveIdentityQuality.Stable, "C:\\", "NTFS", 1000, 700, 300), 500, 400, 100, 200, new(10, 2, 0, 0, 0, 0, 0, false, false, false), "builtin", "1", "digest", [new(StorageCategory.Logs, 400, 4, MeasurementPrecision.Estimated, FindingStatus.Review)], [new("logs", "1", StorageCategory.Logs, FindingConfidence.High, FindingStatus.Review, 400, 4)], ["coverage note"]);
    private sealed class TemporaryDatabase : IDisposable { public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clyr-history-" + Guid.NewGuid().ToString("N") + ".db"); public void Dispose() { if (File.Exists(Path)) File.Delete(Path); } }
}
