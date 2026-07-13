using System.Diagnostics;
using Clyr.Contracts;
using Clyr.Core;
using Xunit.Abstractions;

namespace Clyr.Core.Tests;

public sealed class ScannerPerformanceTests(ITestOutputHelper output)
{
    [Theory]
    [Trait("Category", "Performance")]
    [InlineData(10_000)]
    [InlineData(100_000)]
    [InlineData(1_000_000)]
    public async Task SyntheticStreamRetainsBoundedState(int count)
    {
        GC.Collect();
        var before = GC.GetTotalMemory(true);
        var workingSetBefore = Process.GetCurrentProcess().WorkingSet64;
        var watch = Stopwatch.StartNew();
        var scanner = new ScanCoordinator(new GeneratedFileSystem(count), new FakeDrives(), new SystemClock());
        var result = await scanner.ScanAsync(new("C:\\", ScanMode.Deep, 25), null, default);
        watch.Stop();
        var retained = GC.GetTotalMemory(true) - before;
        var workingSetGrowth = Math.Max(0, Process.GetCurrentProcess().WorkingSet64 - workingSetBefore);
        output.WriteLine($"entries={count}; elapsedMs={watch.ElapsedMilliseconds}; retainedBytes={retained}; workingSetGrowthBytes={workingSetGrowth}; topFiles={result.LargestFiles.Count}");
        Assert.Equal(count, result.Coverage.FilesObserved);
        Assert.InRange(result.LargestFiles.Count, 0, 25);
        Assert.True(retained < 256L * 1024 * 1024, $"Retained managed memory {retained} exceeded the 256 MiB budget.");
        Assert.True(workingSetGrowth < 256L * 1024 * 1024, $"Working-set growth {workingSetGrowth} exceeded the 256 MiB budget.");
    }

    private sealed class GeneratedFileSystem(int count) : IFileSystemEnumerator
    {
        public IEnumerable<FileSystemEntry> Enumerate(string directory)
        {
            for (var index = 1; index <= count; index++) yield return new($"C:\\generated-{index}.bin", index, EntryTraits.None);
        }
    }

    private sealed class FakeDrives : IDriveDiscovery
    {
        public IReadOnlyList<DriveSummary> Discover() => [new("C:\\", "Fixture", "NTFS", DriveKind.Fixed, true, true, true, "Supported", long.MaxValue, long.MaxValue / 2, long.MaxValue / 2)];
    }
}
