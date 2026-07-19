using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>Scopes non-parallelization to only the deep-traversal fixtures below (which allocate real,
/// if modestly sized, per-level frames) so they never run concurrently with each other — not an assembly-wide
/// parallelization disable, which would slow down every other unrelated test in the project.</summary>
[CollectionDefinition("QuickAndDeepBoundsSequential", DisableParallelization = true)]
public sealed class QuickAndDeepBoundsSequentialMarker;

[Collection("QuickAndDeepBoundsSequential")]
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
        // Old-limit regression: exactly 600 synthetic levels — one level past the former 512-deep constant —
        // is sufficient to prove that constant is gone. Deep's depth bound is genuinely absent (ScanPolicy.
        // MaximumDepth is null for Deep, and RunDeep never reads that field at all — see Scanning.cs), not a
        // large sentinel standing in for "unlimited".
        var fs = new CompactDeepFileSystem(600);
        var result = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);
        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Equal(CompactDeepFileSystem.FileBytes, result.LogicalBytesObserved);
        Assert.DoesNotContain(result.Issues, item => item.Code == "scan.depth-limit");
        Assert.Equal(600, result.Coverage.DirectoriesObserved);
    }

    [Fact]
    public async Task DeepTraversalIsStackSafeAcrossTwoThousandFortyEightLevelsBecauseItIsHeapAllocatedNotRecursive()
    {
        // A recursive implementation would risk overflowing the real call stack well before 2,048 levels;
        // Deep's traversal uses a heap-allocated Stack<T> (see RunDeep in Scanning.cs), so this must complete
        // normally with no StackOverflowException — proof of genuine iterative stack safety at a scale that
        // still keeps the fixture's own memory footprint small (each synthetic path is a short, fixed-shape
        // string like "C:\lvl2048\deep.bin", never a duplicated per-level path chain).
        var fs = new CompactDeepFileSystem(2048);
        var result = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);
        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Equal(2048, result.Coverage.DirectoriesObserved);
    }

    [Fact]
    public async Task CancellationAfterAHundredDirectoryEnumerationsStopsTraversalWithPartialResults()
    {
        using var cancellation = new CancellationTokenSource();
        var fs = new CompactDeepFileSystem(1024, observedDirectory: count => { if (count == 100) cancellation.Cancel(); });
        var result = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, cancellation.Token);
        Assert.Equal(ScanStatus.Cancelled, result.Status);
        // Cancellation is checked once per loop iteration, before opening the next frame, so the traversal must
        // stop close to the 100th enumeration — not create the remaining ~900 directory frames first.
        Assert.True(result.Coverage.DirectoriesObserved is > 0 and <= 105,
            $"Expected cancellation to stop within a few directories of the 100th enumeration; observed {result.Coverage.DirectoriesObserved} directories.");
    }

    [Fact]
    public async Task DeepNeverFollowsAReparsePointEvenWhenItWouldCreateAnInfiniteLoop()
    {
        // C:\loop is a reparse point whose target (if ever followed) points back to C:\ itself — an immediate
        // infinite loop. Deep must count it as a skipped reparse point and move on, never opening it, so the
        // scan terminates normally instead of hanging.
        var fs = new SelfReferencingReparseFileSystem();
        var result = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);
        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Equal(1, result.Coverage.ReparsePointsSkipped);
        Assert.DoesNotContain("C:\\loop", fs.EnumeratedDirectories);
    }

    [Fact]
    public async Task QuickAndDeepBothReachDeeplyNestedDataNowThatQuicksFixedDepthCeilingIsRemoved()
    {
        // Phase (Quick truthfulness correction): six levels deep — one level past Quick's OLD documented depth-3
        // limit. That fixed ceiling was the confirmed root cause of Quick's low real-world coverage, so it was
        // removed; Quick is now bounded only by its time/item/pending-frontier budget. With a small, six-directory
        // tree well within the default budget, Quick must reach exactly as much data as Deep, and neither may
        // report a "scan.depth-limit" policy boundary (that issue code, and the fixed ceiling it described, no
        // longer exist).
        var fs = new NestedFileSystem();
        var quick = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Quick), null, default);
        var deep = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);

        Assert.Equal(ScanStatus.Completed, quick.Status);
        Assert.DoesNotContain(quick.Issues, item => item.Code == "scan.depth-limit");
        Assert.DoesNotContain(deep.Issues, item => item.Code == "scan.depth-limit");
        Assert.Equal(NestedFileSystem.DeepFileBytes, quick.LogicalBytesObserved);
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

    /// <summary>
    /// A synthetic, arbitrarily-deep single-chain directory tree (depth 0 = root, depth <paramref name="levels"/>
    /// = the directory holding one file) that deliberately never accumulates a growing path string per level.
    /// Each directory identity is just "C:\lvl{depth}" — a short, fixed-shape string derived directly from the
    /// numeric depth, not built by repeatedly appending onto the parent's already-long path — so the fixture's
    /// own memory footprint stays flat regardless of how many levels are requested, and no duplicated path
    /// chain is retained anywhere. <paramref name="observedDirectory"/>, if given, is invoked with a running
    /// 1-based count immediately as each directory is opened (i.e., once per <see cref="Enumerate"/> call).
    /// </summary>
    private sealed class CompactDeepFileSystem(int levels, Action<int>? observedDirectory = null) : IFileSystemEnumerator
    {
        public const long FileBytes = 777;
        private int enumerateCount;

        public IEnumerable<FileSystemEntry> Enumerate(string directory)
        {
            observedDirectory?.Invoke(++enumerateCount);
            var depth = DepthOf(directory);
            if (depth < levels) return [new(PathFor(depth + 1), 0, EntryTraits.Directory)];
            if (depth == levels) return [new(PathFor(depth) + "\\deep.bin", FileBytes, EntryTraits.None)];
            return [];
        }

        private static string PathFor(int depth) => "C:\\lvl" + depth;
        private static int DepthOf(string directory) => directory == "C:\\" ? 0 : int.Parse(directory["C:\\lvl".Length..], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>C:\loop is a reparse point. If the scan engine ever followed it, Enumerate("C:\loop") would
    /// yield C:\ itself again, looping forever. The engine must never call Enumerate for a reparse-point path
    /// at all — proven here by tracking every directory actually opened.</summary>
    private sealed class SelfReferencingReparseFileSystem : IFileSystemEnumerator
    {
        public List<string> EnumeratedDirectories { get; } = [];
        public IEnumerable<FileSystemEntry> Enumerate(string directory)
        {
            EnumeratedDirectories.Add(directory);
            if (directory == "C:\\loop") throw new InvalidOperationException("A reparse-point target must never be enumerated.");
            if (directory == "C:\\") return [new("C:\\loop", 0, EntryTraits.Directory | EntryTraits.ReparsePoint)];
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
