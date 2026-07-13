using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

public sealed class ScanLifecycleTests
{
    [Fact]
    public async Task ProgressIsThrottledAndTerminalStateIsLast()
    {
        var progress = new CollectingProgress();
        var scanner = new ScanCoordinator(new GeneratedFileSystem(1000), new FakeDrives(), new SteppingClock());
        var result = await scanner.ScanAsync(new("C:\\", ScanMode.Deep), progress, default);
        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Equal(ScanStatus.Preparing, progress.Values[0].Status);
        Assert.Equal(ScanStatus.Completed, progress.Values[^1].Status);
        Assert.InRange(progress.Values.Count(item => item.Status == ScanStatus.Scanning), 35, 45);
        Assert.DoesNotContain(progress.Values, item => item.CurrentPath.Contains("generated-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancellationTransitionsThroughCancellingAndEndsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new CollectingProgress();
        var scanner = new ScanCoordinator(new CancellingFileSystem(cancellation), new FakeDrives(), new SystemClock());
        var result = await scanner.ScanAsync(new("C:\\", ScanMode.Deep), progress, cancellation.Token);
        Assert.Equal([ScanStatus.Preparing, ScanStatus.Scanning, ScanStatus.Cancelling, ScanStatus.Cancelled],
            progress.Values.Select(item => item.Status).Distinct().ToArray());
        Assert.True(result.IsPartial);
    }

    private sealed class CollectingProgress : IProgress<ScanProgress>
    {
        public List<ScanProgress> Values { get; } = [];
        public void Report(ScanProgress value) => Values.Add(value);
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
