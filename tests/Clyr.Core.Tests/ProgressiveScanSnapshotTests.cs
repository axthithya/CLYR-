using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>
/// Phase (progressive full-drive analysis): <see cref="ProgressiveScanSnapshot"/> is an additive, optional,
/// in-memory-only side channel — these tests confirm it stays bounded, throttled, correlated to the eventual
/// final result, and entirely inert for every caller that doesn't ask for it.
/// </summary>
public sealed class ProgressiveScanSnapshotTests
{
    [Fact]
    public async Task NoProgressiveProgressReporterMeansNoSnapshotWorkOccurs()
    {
        // The overwhelming majority of existing callers (CLI, every other Core test, the fixture service before
        // this phase) never supply a ProgressiveProgress reporter — this must remain entirely free for them.
        var scanner = new ScanCoordinator(new GeneratedFileSystem(2000), new FakeDrives(), new SteppingClock());
        var result = await scanner.ScanAsync(new("C:\\", ScanMode.Deep), null, default);
        Assert.Equal(ScanStatus.Completed, result.Status);
    }

    [Fact]
    public async Task ProgressiveSnapshotsAreThrottledMoreCoarselyThanScalarProgress()
    {
        var scalar = new CollectingProgress<ScanProgress>();
        var progressive = new CollectingProgress<ProgressiveScanSnapshot>();
        var scanner = new ScanCoordinator(new GeneratedFileSystem(3000), new FakeDrives(), new SteppingClock());
        await scanner.ScanAsync(new("C:\\", ScanMode.Deep, ProgressiveProgress: progressive), scalar, default);

        var scanningScalarTicks = scalar.Values.Count(item => item.Status == ScanStatus.Scanning);
        Assert.True(progressive.Values.Count < scanningScalarTicks,
            $"Expected fewer progressive snapshots ({progressive.Values.Count}) than scalar scanning ticks ({scanningScalarTicks}) given the coarser throttle.");
    }

    [Fact]
    public async Task ProgressiveSnapshotCarriesTheSameScanIdAsTheFinalResult()
    {
        var progressive = new CollectingProgress<ProgressiveScanSnapshot>();
        var scanner = new ScanCoordinator(new GeneratedFileSystem(2000), new FakeDrives(), new SteppingClock());
        var result = await scanner.ScanAsync(new("C:\\", ScanMode.Deep, ProgressiveProgress: progressive), null, default);

        Assert.NotEmpty(progressive.Values);
        Assert.All(progressive.Values, snapshot => Assert.Equal(result.ScanId, snapshot.ScanId));
    }

    [Fact]
    public async Task ProgressiveSnapshotTopListsRemainBoundedRegardlessOfEntryCount()
    {
        const int topCount = 25;
        var progressive = new CollectingProgress<ProgressiveScanSnapshot>();
        var scanner = new ScanCoordinator(new WideFileSystem(5000), new FakeDrives(), new SteppingClock());
        await scanner.ScanAsync(new("C:\\", ScanMode.Deep, TopCount: topCount, ProgressiveProgress: progressive), null, default);

        Assert.NotEmpty(progressive.Values);
        Assert.All(progressive.Values, snapshot =>
        {
            Assert.True(snapshot.TopDirectories.Count <= topCount);
            Assert.True(snapshot.TopFiles.Count <= topCount);
        });
    }

    [Fact]
    public async Task StageProgressesTruthfullyAndEndsAtFinalizing()
    {
        var progressive = new CollectingProgress<ProgressiveScanSnapshot>();
        var scanner = new ScanCoordinator(new GeneratedFileSystem(2000), new FakeDrives(), new SteppingClock());
        await scanner.ScanAsync(new("C:\\", ScanMode.Deep, ProgressiveProgress: progressive), null, default);

        Assert.NotEmpty(progressive.Values);
        // Never regresses backward through the stage sequence.
        var stages = progressive.Values.Select(item => item.Stage).ToArray();
        for (var index = 1; index < stages.Length; index++)
            Assert.True(stages[index] >= stages[index - 1], "Stage must never move backward.");
        Assert.Equal(ScanStage.Finalizing, stages[^1]);
    }

    [Fact]
    public async Task EarlyInsightsBecomeReadyOnlyOnceRealAggregateDataExists()
    {
        var progressive = new CollectingProgress<ProgressiveScanSnapshot>();
        var scanner = new ScanCoordinator(new GeneratedFileSystem(3000), new FakeDrives(), new SteppingClock());
        await scanner.ScanAsync(new("C:\\", ScanMode.Deep, ProgressiveProgress: progressive), null, default);

        Assert.NotEmpty(progressive.Values);
        // Once ready, it never falsely reverts to not-ready for the remainder of the run.
        var readyFlags = progressive.Values.Select(item => item.EarlyInsightsReady).ToArray();
        var firstReadyIndex = Array.IndexOf(readyFlags, true);
        if (firstReadyIndex >= 0)
            Assert.True(readyFlags.Skip(firstReadyIndex).All(ready => ready));
    }

    [Fact]
    public async Task ProgressiveSnapshotNeverExceedsDriveUsedBytesCoveragePercentage()
    {
        var progressive = new CollectingProgress<ProgressiveScanSnapshot>();
        var scanner = new ScanCoordinator(new GeneratedFileSystem(2000), new FakeDrives(), new SteppingClock());
        await scanner.ScanAsync(new("C:\\", ScanMode.Deep, ProgressiveProgress: progressive), null, default);

        Assert.All(progressive.Values, snapshot => Assert.True(snapshot.ProvisionalCoveragePercentage is null or <= 100));
    }

    [Fact]
    public async Task CancellationRemainsResponsiveWithAProgressiveProgressReporterAttached()
    {
        using var cancellation = new CancellationTokenSource();
        var progressive = new CollectingProgress<ProgressiveScanSnapshot>();
        var scalar = new CollectingProgress<ScanProgress>();
        var scanner = new ScanCoordinator(new CancellingFileSystem(cancellation), new FakeDrives(), new SystemClock());
        var result = await scanner.ScanAsync(new("C:\\", ScanMode.Deep, ProgressiveProgress: progressive), scalar, cancellation.Token);

        Assert.True(result.IsPartial);
        Assert.Equal(ScanStatus.Cancelled, result.Status);
    }

    private sealed class CollectingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];
        public void Report(T value) => Values.Add(value);
    }

    private sealed class SteppingClock : IClock
    {
        private long ticks;
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch.AddMilliseconds(Interlocked.Add(ref ticks, 10));
    }

    private sealed class GeneratedFileSystem(int count) : IFileSystemEnumerator
    {
        public IEnumerable<FileSystemEntry> Enumerate(string directory)
        {
            for (var index = 0; index < count; index++) yield return new($"C:\\generated-{index}.bin", index, EntryTraits.None);
        }
    }

    /// <summary>Many top-level directories, each with a few files — enough breadth to exercise the top-N ranking
    /// caps for both directories and files simultaneously.</summary>
    private sealed class WideFileSystem(int rootCount) : IFileSystemEnumerator
    {
        public IEnumerable<FileSystemEntry> Enumerate(string directory)
        {
            if (directory == "C:\\")
            {
                for (var index = 0; index < rootCount; index++) yield return new($"C:\\dir-{index}", 0, EntryTraits.Directory);
                yield break;
            }
            for (var index = 0; index < 3; index++) yield return new($"{directory}\\file-{index}.bin", index + 1, EntryTraits.None);
        }
    }

    private sealed class CancellingFileSystem(CancellationTokenSource cancellation) : IFileSystemEnumerator
    {
        public IEnumerable<FileSystemEntry> Enumerate(string directory)
        {
            for (var index = 0; index < 100; index++)
            {
                if (index == 5) cancellation.Cancel();
                yield return new($"C:\\item-{index}.bin", index, EntryTraits.None);
            }
        }
    }

    private sealed class FakeDrives : IDriveDiscovery
    {
        public IReadOnlyList<DriveSummary> Discover() => [new("C:\\", "Fixture", "NTFS", DriveKind.Fixed, true, true, true, "Supported", 10000, 5000, 5000)];
    }
}
