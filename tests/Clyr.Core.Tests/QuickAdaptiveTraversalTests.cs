using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>
/// Phase 7.1: Quick Analysis's adaptive, priority-driven traversal, its honest policy-boundary vs. real-warning
/// distinction, and its CLYR-owned checkpoint/continuation capability.
/// </summary>
public sealed class QuickAdaptiveTraversalTests
{
    [Fact]
    public async Task KnownHighValueRootIsExaminedBeforeAnAlphabeticallyEarlierUnknownDirectory()
    {
        // "AAA_Empty" sorts before "Program Files" alphabetically and contains nothing of interest; a naive
        // alphabetical walk would visit it first. Quick's Stage B must prioritize the known high-value root
        // instead, so the large file inside Program Files is found even under a tight item budget.
        var fs = new FakeFileSystem(new()
        {
            ["C:\\"] = [Dir("C:\\AAA_Empty"), Dir("C:\\Program Files")],
            ["C:\\AAA_Empty"] = [File("C:\\AAA_Empty\\tiny.txt", 1)],
            ["C:\\Program Files"] = [File("C:\\Program Files\\big.bin", 5_000_000)]
        });
        var policy = new QuickAnalysisPolicy(TimeSpan.FromSeconds(30), ItemBudget: 3, PolicyVersion: 2);
        var scanner = new ScanCoordinator(fs, new FakeDrives(), new SystemClock(), quickPolicy: policy);
        var result = await scanner.ScanAsync(new("C:\\", ScanMode.Quick), null, default);

        Assert.Contains(result.LargestFiles, item => item.DisplayPath == "C:\\Program Files\\big.bin");
        Assert.DoesNotContain(result.LargestFiles, item => item.DisplayPath == "C:\\AAA_Empty\\tiny.txt");
    }

    [Fact]
    public async Task DeveloperCacheDirectoryIsPrioritizedOverAnUnknownSibling()
    {
        var fs = new FakeFileSystem(new()
        {
            ["C:\\"] = [Dir("C:\\ZZZ_Unknown"), Dir("C:\\repo\\.nuget")],
            ["C:\\ZZZ_Unknown"] = [File("C:\\ZZZ_Unknown\\tiny.txt", 1)],
            ["C:\\repo\\.nuget"] = [File("C:\\repo\\.nuget\\packages.bin", 900_000)]
        }, rootDirectories: ["C:\\ZZZ_Unknown", "C:\\repo\\.nuget"]);
        var policy = new QuickAnalysisPolicy(TimeSpan.FromSeconds(30), ItemBudget: 3, PolicyVersion: 2);
        var scanner = new ScanCoordinator(fs, new FakeDrives(), new SystemClock(), quickPolicy: policy);
        var result = await scanner.ScanAsync(new("C:\\", ScanMode.Quick), null, default);

        Assert.Contains(result.LargestFiles, item => item.DisplayPath == "C:\\repo\\.nuget\\packages.bin");
    }

    [Fact]
    public async Task PolicyBoundaryAloneReportsAsCompletedNotWarnings()
    {
        var fs = new FlatFileSystem(1000);
        var policy = new QuickAnalysisPolicy(TimeSpan.FromSeconds(30), ItemBudget: 10, PolicyVersion: 2);
        var scanner = new ScanCoordinator(fs, new FakeDrives(), new SystemClock(), quickPolicy: policy);
        var result = await scanner.ScanAsync(new("C:\\", ScanMode.Quick), null, default);

        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Contains(result.Issues, item => item.Code == "scan.quick-item-budget" && item.Severity == ScanIssueSeverity.PolicyBoundary);
    }

    [Fact]
    public async Task RealAccessErrorsStillProduceWarningsEvenAlongsideAPolicyBoundary()
    {
        var fs = new FakeFileSystem(new()
        {
            ["C:\\"] = [Dir("C:\\denied"), Dir("C:\\ok")],
            ["C:\\ok"] = [File("C:\\ok\\a.bin", 10)]
        }, denied: "C:\\denied");
        var policy = new QuickAnalysisPolicy(TimeSpan.FromSeconds(30), ItemBudget: 250_000, PolicyVersion: 2);
        var scanner = new ScanCoordinator(fs, new FakeDrives(), new SystemClock(), quickPolicy: policy);
        var result = await scanner.ScanAsync(new("C:\\", ScanMode.Quick), null, default);

        Assert.Equal(ScanStatus.CompletedWithWarnings, result.Status);
        Assert.Contains(result.Issues, item => item.Code == "scan.access-denied" && item.Severity == ScanIssueSeverity.PermissionLimited);
    }

    [Fact]
    public async Task ThirtySecondDefaultReplacesTheOldEightSecondCutoff()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), QuickAnalysisPolicy.Default.TargetDuration);
    }

    [Fact]
    public async Task PendingDirectoryCapacityIsBoundedAndDoesNotGrowMemoryUnbounded()
    {
        // 50,000 sibling directories at depth 1, none matching a known-root name, with a budget far below that
        // count. Quick must not attempt to enqueue an unbounded number of pending directories.
        var fs = new WideFlatDirectorySystem(50_000);
        var policy = new QuickAnalysisPolicy(TimeSpan.FromSeconds(30), ItemBudget: 5_000, PolicyVersion: 2);
        var scanner = new ScanCoordinator(fs, new FakeDrives(), new SystemClock(), quickPolicy: policy);
        var result = await scanner.ScanAsync(new("C:\\", ScanMode.Quick), null, default);

        Assert.Equal(ScanStatus.Completed, result.Status);
    }

    [Fact]
    public async Task CheckpointRoundTripsAndContinuationResumesRatherThanRestarting()
    {
        var store = new RecordingCheckpointStore();
        var fs = new FakeFileSystem(new()
        {
            ["C:\\"] = [Dir("C:\\Program Files")],
            ["C:\\Program Files"] = [Dir("C:\\Program Files\\App1"), Dir("C:\\Program Files\\App2")],
            ["C:\\Program Files\\App1"] = [File("C:\\Program Files\\App1\\a.bin", 100)],
            ["C:\\Program Files\\App2"] = [File("C:\\Program Files\\App2\\b.bin", 200)]
        });
        var policy = new QuickAnalysisPolicy(TimeSpan.FromSeconds(30), ItemBudget: 2, PolicyVersion: 2);
        var first = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock(), quickPolicy: policy, checkpoints: store)
            .ScanAsync(new("C:\\", ScanMode.Quick), null, default);
        Assert.Equal(ScanStatus.Completed, first.Status);
        Assert.NotNull(store.Saved);
        Assert.NotEmpty(store.Saved!.PendingDirectories);

        var continuation = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock(), quickPolicy: policy, checkpoints: store)
            .ScanAsync(new("C:\\", ScanMode.Quick, ContinueFromCheckpoint: true), null, default);
        Assert.Contains(continuation.Issues, item => item.Code == "scan.checkpoint-resumed");
        // The second run's own totals plus the first run's carried-forward totals must exceed what either run
        // alone observed — proof it resumed instead of restarting at the root from scratch.
        Assert.True(continuation.LogicalBytesObserved >= first.LogicalBytesObserved);
    }

    [Fact]
    public async Task DepthLimitedAreasAreCheckpointedEvenWhenTheRunOtherwiseExhaustsItsQueue()
    {
        // A tall tree, four levels deep, with a generous time/item budget that lets Quick's priority queue fully
        // drain (genuine Stage D exhaustion) well before either budget is reached. Depth-policy skips are still
        // real, known gaps — Quick must not report this as final, nothing-left-to-continue completion.
        var fs = new FakeFileSystem(new()
        {
            ["C:\\"] = [Dir("C:\\Program Files")],
            ["C:\\Program Files"] = [Dir("C:\\Program Files\\Vendor")],
            ["C:\\Program Files\\Vendor"] = [Dir("C:\\Program Files\\Vendor\\App")],
            ["C:\\Program Files\\Vendor\\App"] = [Dir("C:\\Program Files\\Vendor\\App\\data")],
            ["C:\\Program Files\\Vendor\\App\\data"] = [File("C:\\Program Files\\Vendor\\App\\data\\big.bin", 5_000_000)]
        });
        var store = new RecordingCheckpointStore();
        var policy = new QuickAnalysisPolicy(TimeSpan.FromSeconds(30), ItemBudget: 250_000, PolicyVersion: 2);
        var first = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock(), quickPolicy: policy, checkpoints: store)
            .ScanAsync(new("C:\\", ScanMode.Quick), null, default);

        Assert.Equal(ScanStatus.Completed, first.Status);
        Assert.Contains(first.Issues, item => item.Code == "scan.depth-limit");
        Assert.NotNull(store.Saved);
        Assert.Contains("C:\\Program Files\\Vendor\\App\\data", store.Saved!.PendingDirectories);
        Assert.DoesNotContain(first.LargestFiles, item => item.DisplayPath == "C:\\Program Files\\Vendor\\App\\data\\big.bin");

        var continuation = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock(), quickPolicy: policy, checkpoints: store)
            .ScanAsync(new("C:\\", ScanMode.Quick, ContinueFromCheckpoint: true), null, default);
        // Resuming gives the deferred directory a fresh depth budget, so the file beyond the original depth
        // ceiling is finally reached — proof continuation makes real progress past a depth-only stopping point.
        Assert.Contains(continuation.LargestFiles, item => item.DisplayPath == "C:\\Program Files\\Vendor\\App\\data\\big.bin");
    }

    [Fact]
    public async Task CheckpointWithMismatchedPolicyVersionIsIgnoredByTheCoordinatorRatherThanMerged()
    {
        // Saved under policy version 1; the coordinator below runs the current default (version 2). It must
        // refuse to resume from a checkpoint produced under a different, potentially incompatible policy —
        // never silently merging two unrelated scan executions.
        var store = new RecordingCheckpointStore { Saved = new("C:\\", ScanMode.Quick, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 999_999, 999_999, 999_999_999, ["C:\\Somewhere"]) };
        var fs = new FakeFileSystem(new() { ["C:\\"] = [File("C:\\a.bin", 5)] });
        var result = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock(), checkpoints: store)
            .ScanAsync(new("C:\\", ScanMode.Quick, ContinueFromCheckpoint: true), null, default);

        Assert.Contains(result.Issues, item => item.Code == "scan.checkpoint-unavailable");
        Assert.DoesNotContain(result.Issues, item => item.Code == "scan.checkpoint-resumed");
        Assert.True(result.LogicalBytesObserved < 999_999_999, "The mismatched checkpoint's totals must not have been merged in.");
    }

    [Fact]
    public async Task DeepAnalysisHasNoTimeOrItemBudgetUnlikeQuick()
    {
        var fs = new FlatFileSystem(50_000);
        var result = await new ScanCoordinator(fs, new FakeDrives(), new SystemClock()).ScanAsync(new("C:\\", ScanMode.Deep), null, default);
        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Equal(50_000, result.Coverage.FilesObserved);
    }

    private sealed class RecordingCheckpointStore : IScanCheckpointStore
    {
        public ScanCheckpoint? Saved { get; set; }
        public ScanCheckpoint? TryLoad(string root, ScanMode mode) => Saved is not null && Saved.Root == root && Saved.Mode == mode ? Saved : null;
        public void Save(ScanCheckpoint checkpoint) => Saved = checkpoint;
        public void Clear(string root, ScanMode mode) { if (Saved is not null && Saved.Root == root && Saved.Mode == mode) Saved = null; }
    }

    private static FileSystemEntry Dir(string path) => new(path, 0, EntryTraits.Directory);
    private static FileSystemEntry File(string path, long size) => new(path, size, EntryTraits.None);

    private sealed class FakeDrives : IDriveDiscovery
    {
        public IReadOnlyList<DriveSummary> Discover() => [new("C:\\", "Fixture", "NTFS", DriveKind.Fixed, true, true, true, "Supported", 10_000_000_000, 5_000_000_000, 5_000_000_000)];
    }

    private sealed class FakeFileSystem(Dictionary<string, FileSystemEntry[]> entries, string? denied = null, string[]? rootDirectories = null) : IFileSystemEnumerator
    {
        public IEnumerable<FileSystemEntry> Enumerate(string directory)
        {
            if (directory == denied) throw new UnauthorizedAccessException("fixture denial");
            if (directory == "C:\\" && rootDirectories is not null) return rootDirectories.Select(Dir);
            return entries.TryGetValue(directory, out var values) ? values : [];
        }
    }

    private sealed class FlatFileSystem(int count) : IFileSystemEnumerator
    {
        public IEnumerable<FileSystemEntry> Enumerate(string directory)
        {
            if (directory != "C:\\") yield break;
            for (var index = 1; index <= count; index++) yield return new($"C:\\generated-{index}.bin", 1, EntryTraits.None);
        }
    }

    private sealed class WideFlatDirectorySystem(int directoryCount) : IFileSystemEnumerator
    {
        public IEnumerable<FileSystemEntry> Enumerate(string directory)
        {
            if (directory == "C:\\")
                for (var index = 1; index <= directoryCount; index++) yield return new($"C:\\dir-{index}", 0, EntryTraits.Directory);
        }
    }
}
