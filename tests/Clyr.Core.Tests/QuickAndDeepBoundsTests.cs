using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

public sealed class QuickAndDeepBoundsTests
{
    [Fact]
    public async Task QuickStopsAtItsDocumentedItemBudgetRatherThanExhaustingAHugeFlatDirectory()
    {
        // 300,000 items in one flat directory exceeds Quick's documented 250,000-item budget; Quick must stop
        // itself honestly (CompletedWithWarnings, with a diagnostic identifying the budget) rather than either
        // silently truncating or taking as long as Deep would.
        var scanner = new ScanCoordinator(new FlatFileSystem(300_000), new FakeDrives(), new SystemClock());
        var result = await scanner.ScanAsync(new("C:\\", ScanMode.Quick), null, default);
        // Reaching Quick's own, documented item budget is expected, by-design behavior — a PolicyBoundary
        // diagnostic, not a scan failure — so the terminal status must read as a normal Completed, never
        // CompletedWithWarnings, unless a real coverage problem also occurred.
        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Contains(result.Issues, item => item.Code == "scan.quick-item-budget" && item.Severity == ScanIssueSeverity.PolicyBoundary);
        Assert.True(result.Coverage.FilesObserved <= 250_000, $"Quick observed {result.Coverage.FilesObserved} files, exceeding its documented item budget.");
        Assert.True(result.Coverage.FilesObserved > 0);
    }

    [Fact]
    public async Task QuickStopsAtItsDocumentedTimeBudgetEvenWithFewItemsRemaining()
    {
        // A clock that reports far beyond the time budget on the very first live check proves the time bound is
        // enforced independently of the item/depth bounds, deterministically and without a real 8-second wait.
        var scanner = new ScanCoordinator(new FlatFileSystem(10), new FakeDrives(), new JumpAfterFirstReadClock());
        var result = await scanner.ScanAsync(new("C:\\", ScanMode.Quick), null, default);
        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Contains(result.Issues, item => item.Code == "scan.quick-time-budget" && item.Severity == ScanIssueSeverity.PolicyBoundary);
    }

    [Fact]
    public async Task DeepHasNoItemOrTimeBudgetAndProcessesEverythingInAFlatDirectory()
    {
        var scanner = new ScanCoordinator(new FlatFileSystem(300_000), new FakeDrives(), new SystemClock());
        var result = await scanner.ScanAsync(new("C:\\", ScanMode.Deep), null, default);
        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Equal(300_000, result.Coverage.FilesObserved);
        Assert.DoesNotContain(result.Issues, item => item.Code is "scan.quick-item-budget" or "scan.quick-time-budget" or "scan.depth-limit");
    }

    [Fact]
    public async Task DeepHasNoConfiguredDepthCeilingAndReachesWellPastTheFormerFiniteLimit()
    {
        // Phase 7.2.1: Deep's depth bound is int.MaxValue, not a large-but-finite constant. 600 levels is well
        // past the old 512-level ceiling that existed before this change — proof there is no configured ceiling
        // left to hit, not just a generous one.
        var fs = new VeryDeepFileSystem(600);
        var result = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);
        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Equal(VeryDeepFileSystem.FileBytes, result.LogicalBytesObserved);
        Assert.DoesNotContain(result.Issues, item => item.Code == "scan.depth-limit");
    }

    [Fact]
    public async Task DeepReachesNestedDataThatQuicksDepthLimitIntentionallySkips()
    {
        // Six levels deep — one level past Quick's documented depth-3 limit — a developer-cache-style file that
        // Quick, by design, never reaches.
        var fs = new NestedFileSystem();
        var quick = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Quick), null, default);
        var deep = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);

        Assert.Equal(ScanStatus.Completed, quick.Status);
        Assert.Contains(quick.Issues, item => item.Code == "scan.depth-limit" && item.Severity == ScanIssueSeverity.PolicyBoundary);
        Assert.True(quick.LogicalBytesObserved < deep.LogicalBytesObserved,
            "Deep must observe strictly more than Quick when data exists beyond Quick's depth limit.");
        Assert.Equal(NestedFileSystem.DeepFileBytes, deep.LogicalBytesObserved);
        Assert.Equal(ScanStatus.Completed, deep.Status);
    }

    private sealed class FlatFileSystem(int count) : IFileSystemEnumerator
    {
        public IEnumerable<FileSystemEntry> Enumerate(string directory)
        {
            for (var index = 1; index <= count; index++) yield return new($"C:\\generated-{index}.bin", 1, EntryTraits.None);
        }
    }

    /// <summary>C:\ -> a -> b -> c -> d -> e (depth 5) contains one file; Quick's depth-3 bound never opens
    /// past depth 3, so it can never see this file, while Deep (bound 512) reaches it easily.</summary>
    private sealed class VeryDeepFileSystem(int levels) : IFileSystemEnumerator
    {
        public const long FileBytes = 777;
        public IEnumerable<FileSystemEntry> Enumerate(string directory)
        {
            var depth = directory.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length - 1;
            var child = directory.EndsWith('\\') ? directory : directory + "\\";
            if (depth < levels) return [new(child + "d" + depth, 0, EntryTraits.Directory)];
            if (depth == levels) return [new(child + "deep.bin", FileBytes, EntryTraits.None)];
            return [];
        }
    }

    private sealed class NestedFileSystem : IFileSystemEnumerator
    {
        public const long DeepFileBytes = 4096;
        private static readonly string[] Chain = ["C:\\a", "C:\\a\\b", "C:\\a\\b\\c", "C:\\a\\b\\c\\d", "C:\\a\\b\\c\\d\\e"];

        public IEnumerable<FileSystemEntry> Enumerate(string directory)
        {
            var index = Array.IndexOf(Chain, directory);
            if (directory == "C:\\") return [new(Chain[0], 0, EntryTraits.Directory)];
            if (index >= 0 && index < Chain.Length - 1) return [new(Chain[index + 1], 0, EntryTraits.Directory)];
            if (directory == Chain[^1]) return [new(Chain[^1] + "\\deep.bin", DeepFileBytes, EntryTraits.None)];
            return [];
        }
    }

    private sealed class JumpAfterFirstReadClock : IClock
    {
        private int calls;
        private static readonly DateTimeOffset Base = DateTimeOffset.UnixEpoch;
        public DateTimeOffset UtcNow => Interlocked.Increment(ref calls) == 1 ? Base : Base.AddSeconds(100);
    }

    private sealed class FakeDrives : IDriveDiscovery
    {
        public IReadOnlyList<DriveSummary> Discover() => [new("C:\\", "Fixture", "NTFS", DriveKind.Fixed, true, true, true, "Supported", long.MaxValue, long.MaxValue / 2, long.MaxValue / 2)];
    }
}
