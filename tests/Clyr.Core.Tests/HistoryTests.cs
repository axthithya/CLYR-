using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

public sealed class HistoryTests
{
    [Fact]
    public void EqualSnapshotsAreFullyComparable()
    {
        var before = Snapshot(used: 1000); var after = Snapshot(used: 1000) with { Id = Guid.NewGuid(), CapturedAtUtc = before.CapturedAtUtc.AddHours(1) };
        var result = SnapshotComparer.Compare(before, after);
        Assert.Equal(SnapshotCompatibility.FullyComparable, result.Compatibility.Kind);
        Assert.All(result.Metrics, item => Assert.Equal(DeltaKind.Unchanged, item.Kind));
    }

    [Fact]
    public void DifferentIdentityIsNotComparable()
    {
        var before = Snapshot(); var after = Snapshot() with { Id = Guid.NewGuid(), Drive = Snapshot().Drive with { Fingerprint = "other" } };
        Assert.Equal(SnapshotCompatibility.NotComparable, SnapshotComparer.Compare(before, after).Compatibility.Kind);
    }

    [Fact]
    public void RuleDriftAdjustsClassificationCompatibility()
    {
        var before = Snapshot(); var after = Snapshot() with { Id = Guid.NewGuid(), RulePackDigest = "changed" };
        Assert.Equal(SnapshotCompatibility.ClassificationAdjusted, SnapshotComparer.Compare(before, after).Compatibility.Kind);
    }

    [Fact]
    public void CoverageAndModeDriftLowerConfidence()
    {
        var before = Snapshot(); var after = Snapshot() with { Id = Guid.NewGuid(), Mode = ScanMode.Deep, Coverage = new(1, 0, 100, 0, 0, 0, 0, false, false, false) };
        var result = SnapshotComparer.Compare(before, after);
        Assert.Equal(SnapshotCompatibility.ComparableWithWarnings, result.Compatibility.Kind);
        Assert.Equal(ComparisonConfidence.Low, result.Compatibility.Confidence);
    }

    [Theory]
    [InlineData(1_000_000_000, 1_300_000_000, true)]
    [InlineData(1_000_000_000, 1_060_000_000, false)]
    [InlineData(100_000_000, 160_000_000, true)]
    public void SignificanceUsesAbsoluteOrCombinedRelativeThreshold(long beforeUsed, long afterUsed, bool significant)
    {
        var before = Snapshot(beforeUsed); var after = Snapshot(afterUsed) with { Id = Guid.NewGuid() };
        Assert.Equal(significant, SnapshotComparer.Compare(before, after).Metrics.Single(x => x.Metric == "drive.used").IsSignificant);
    }

    [Fact]
    public void SignedDeltaSaturatesInsteadOfOverflowing()
    {
        var before = Snapshot(long.MinValue); var after = Snapshot(long.MaxValue) with { Id = Guid.NewGuid() };
        Assert.Equal(long.MaxValue, SnapshotComparer.Compare(before, after).Metrics.Single(x => x.Metric == "drive.used").Change);
    }

    [Fact]
    public void NewAndRemovedCategoriesAreExplicit()
    {
        var before = Snapshot() with { Categories = [new(StorageCategory.Logs, 500, 1, MeasurementPrecision.Estimated, FindingStatus.Review)] };
        var after = Snapshot() with { Id = Guid.NewGuid(), Categories = [new(StorageCategory.BrowserCache, 900, 1, MeasurementPrecision.Estimated, FindingStatus.Review)] };
        var deltas = SnapshotComparer.Compare(before, after).Categories;
        Assert.Contains(deltas, item => item.Metric.EndsWith("BrowserCache", StringComparison.Ordinal) && item.Kind == DeltaKind.New);
        Assert.Contains(deltas, item => item.Metric.EndsWith("Logs", StringComparison.Ordinal) && item.Kind == DeltaKind.NoLongerPresent);
    }

    [Fact]
    public async Task SavingDecoratorNeverPersistsFailedScan()
    {
        var store = new FakeStore(); var service = new SnapshotSavingScanService(new FailedScanner(), new SnapshotFactory(new FakeIdentity(), new ApplicationVersion("test")), store);
        await service.ScanAsync(new("C:\\", ScanMode.Quick), null, default);
        Assert.Empty(store.Items);
    }

    private static StorageSnapshot Snapshot(long used = 1_000_000_000) => new(Guid.NewGuid(), Guid.NewGuid(), 1, "test", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), ScanMode.Quick, SnapshotState.Complete,
        new("fingerprint", DriveIdentityQuality.Stable, "C:\\", "NTFS", 2_000_000_000, used, 2_000_000_000 - used), 500, 400, 100, used - 500,
        new(100, 10, 0, 0, 0, 0, 0, false, false, false), "builtin", "1", "digest", [], [], []);

    private sealed class FakeIdentity : IDriveIdentityProvider { public SnapshotDrive Identify(string root, string fs, long? used) => new("fingerprint", DriveIdentityQuality.Stable, root, fs, 1000, used, 0); }
    private sealed class FailedScanner : IScanService { public Task<ScanResult> ScanAsync(ScanRequest r, IProgress<ScanProgress>? p, CancellationToken c) { var now = DateTimeOffset.UtcNow; return Task.FromResult(new ScanResult(Guid.NewGuid(), ScanStatus.Failed, r.Mode, r.Root, "", now, now, 0, null, null, MeasurementPrecision.Unavailable, "", new(0, 0, 0, 0, 0, 0, 0, false, false, false), [], [], [], [], [], "failed", "failed")); } }
    private sealed class FakeStore : ISnapshotStore
    { public List<StorageSnapshot> Items { get; } = []; public Task<SnapshotSaveResult> SaveAsync(StorageSnapshot s, CancellationToken c = default) { Items.Add(s); return Task.FromResult(new SnapshotSaveResult(true, s.Id, "saved", "saved")); } public Task<IReadOnlyList<SnapshotSummary>> ListAsync(int l = 100, CancellationToken c = default) => Task.FromResult<IReadOnlyList<SnapshotSummary>>([]); public Task<StorageSnapshot?> GetAsync(Guid i, CancellationToken c = default) => Task.FromResult<StorageSnapshot?>(null); public Task<bool> DeleteAsync(Guid i, CancellationToken c = default) => Task.FromResult(false); public Task<int> ClearAsync(CancellationToken c = default) => Task.FromResult(0); public Task<HistorySettings> GetSettingsAsync(CancellationToken c = default) => Task.FromResult(HistorySettings.Default); public Task SetSettingsAsync(HistorySettings s, CancellationToken c = default) => Task.CompletedTask; }
}
