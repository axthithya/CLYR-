using System.Text.Json;
using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

public sealed class ScanningTests
{
    [Theory]
    [InlineData("C:\\", true)]
    [InlineData("c:\\", true)]
    [InlineData("C:", false)]
    [InlineData("C:\\folder", false)]
    [InlineData("\\\\server\\share", false)]
    [InlineData("relative", false)]
    [InlineData("C:\\file:stream", false)]
    public void RootValidationRejectsAmbiguousOrBroaderPaths(string path, bool expected)
    {
        Assert.Equal(expected, ScanPathValidator.TryNormalizeRoot(path, out _, out _));
    }

    [Fact]
    public async Task StreamingScanAggregatesNestedTreeAndRanksTopItems()
    {
        var fs = new FakeFileSystem(new Dictionary<string, FileSystemEntry[]>
        {
            ["C:\\"] = [Dir("C:\\Alpha"), Dir("C:\\Beta")],
            ["C:\\Alpha"] = [File("C:\\Alpha\\a.zip", 700), File("C:\\Alpha\\b.txt", 300)],
            ["C:\\Beta"] = [File("C:\\Beta\\movie.mp4", 2000)]
        });
        var result = await Scanner(fs).ScanAsync(new("C:\\", ScanMode.Deep, 2), null, default);
        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Equal(3000, result.LogicalBytesObserved);
        Assert.Equal(3, result.Coverage.FilesObserved);
        Assert.Equal("C:\\Beta", result.TopLevelDirectories[0].DisplayPath);
        Assert.Equal(2, result.LargestFiles.Count);
        Assert.Equal(ExtensionFamily.Video, result.ExtensionFamilies[0].Family);
    }

    [Fact]
    public async Task ReparseDirectoriesAreNeverTraversed()
    {
        var fs = new FakeFileSystem(new Dictionary<string, FileSystemEntry[]>
        {
            ["C:\\"] = [new("C:\\loop", 0, EntryTraits.Directory | EntryTraits.ReparsePoint)]
        });
        var result = await Scanner(fs).ScanAsync(new("C:\\", ScanMode.Deep), null, default);
        Assert.Equal(1, result.Coverage.ReparsePointsSkipped);
        Assert.False(result.Coverage.ReparsePointsFollowed);
        Assert.DoesNotContain("C:\\loop", fs.EnumeratedDirectories);
    }

    [Fact]
    public async Task CloudPlaceholderUsesMetadataWithoutHydration()
    {
        var fs = new FakeFileSystem(new() { ["C:\\"] = [new("C:\\online.bin", 4096, EntryTraits.CloudPlaceholder)] });
        var result = await Scanner(fs).ScanAsync(new("C:\\", ScanMode.Deep), null, default);
        Assert.Equal(4096, result.LogicalBytesObserved);
        Assert.Equal(1, result.Coverage.CloudPlaceholdersObserved);
        Assert.False(result.Coverage.CloudFilesHydrated);
        Assert.False(result.Coverage.ContentBytesRead);
    }

    [Fact]
    public async Task AccessDeniedBecomesWarningAndPartialCoverage()
    {
        var fs = new FakeFileSystem(new() { ["C:\\"] = [Dir("C:\\denied"), File("C:\\ok.txt", 10)] }, "C:\\denied");
        var result = await Scanner(fs).ScanAsync(new("C:\\", ScanMode.Deep), null, default);
        Assert.Equal(ScanStatus.CompletedWithWarnings, result.Status);
        Assert.Equal(1, result.Coverage.InaccessibleEntries);
        Assert.Equal(10, result.LogicalBytesObserved);
    }

    [Fact]
    public async Task CancellationRetainsObservedPartialResult()
    {
        using var cancellation = new CancellationTokenSource();
        var fs = new GeneratedFileSystem(100_000, index => { if (index == 20) cancellation.Cancel(); });
        var result = await Scanner(fs).ScanAsync(new("C:\\", ScanMode.Deep), null, cancellation.Token);
        Assert.Equal(ScanStatus.Cancelled, result.Status);
        Assert.True(result.Coverage.FilesObserved >= 20);
        Assert.True(result.IsPartial);
    }

    [Fact]
    public async Task QuickModeStopsAtDocumentedDepthWithoutFailing()
    {
        var fs = new FakeFileSystem(new()
        {
            ["C:\\"] = [Dir("C:\\a")],
            ["C:\\a"] = [Dir("C:\\a\\b")],
            ["C:\\a\\b"] = [Dir("C:\\a\\b\\c")],
            ["C:\\a\\b\\c"] = [Dir("C:\\a\\b\\c\\d")]
        });
        var result = await Scanner(fs).ScanAsync(new("C:\\", ScanMode.Quick), null, default);
        Assert.Equal(ScanStatus.CompletedWithWarnings, result.Status);
        Assert.Contains(result.Issues, item => item.Code == "scan.depth-limit");
        Assert.DoesNotContain("C:\\a\\b\\c\\d", fs.EnumeratedDirectories);
    }

    [Fact]
    public async Task TopRetentionIsBoundedForLargeStream()
    {
        var result = await Scanner(new GeneratedFileSystem(100_000)).ScanAsync(new("C:\\", ScanMode.Deep, 17), null, default);
        Assert.Equal(100_000, result.Coverage.FilesObserved);
        Assert.Equal(17, result.LargestFiles.Count);
        Assert.Equal(100_000, result.LargestFiles[0].LogicalBytes);
    }

    [Fact]
    public async Task UnsupportedDriveFailsBeforeEnumeration()
    {
        var fs = new FakeFileSystem(new());
        var scanner = new ScanCoordinator(fs, new FakeDrives(false), new SystemClock());
        var result = await scanner.ScanAsync(new("C:\\", ScanMode.Quick), null, default);
        Assert.Equal(ScanStatus.Failed, result.Status);
        Assert.Equal("scan.drive-unsupported", result.FailureCode);
        Assert.Empty(fs.EnumeratedDirectories);
    }

    [Fact]
    public async Task OverlappingScanIsRejected()
    {
        var blocker = new BlockingFileSystem();
        var scanner = Scanner(blocker);
        var first = scanner.ScanAsync(new("C:\\", ScanMode.Deep), null, default);
        Assert.True(blocker.Started.Wait(TimeSpan.FromSeconds(2)));
        var second = await scanner.ScanAsync(new("C:\\", ScanMode.Quick), null, default);
        blocker.Release.Set();
        await first;
        Assert.Equal("scan.overlap", second.FailureCode);
    }

    [Fact]
    public async Task ExportIsVersionedValidJsonAndRemovesPersonalNames()
    {
        var fs = new FakeFileSystem(new() { ["C:\\"] = [Dir("C:\\Users\\Alice"), File("C:\\secret-client.txt", 10)], ["C:\\Users\\Alice"] = [File("C:\\Users\\Alice\\token.txt", 20)] });
        var result = await Scanner(fs).ScanAsync(new("C:\\", ScanMode.Deep), null, default);
        var json = new ScanReportExporter().Serialize(result);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.DoesNotContain("Alice", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-client", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("support-safe", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".pdf", ExtensionFamily.Documents)]
    [InlineData(".MP4", ExtensionFamily.Video)]
    [InlineData(".cs", ExtensionFamily.SourceCode)]
    [InlineData("", ExtensionFamily.NoExtension)]
    [InlineData(".odd", ExtensionFamily.Other)]
    public void ExtensionFamiliesAreStructuralOnly(string extension, ExtensionFamily expected) =>
        Assert.Equal(expected, ExtensionClassifier.Classify(extension));

    private static ScanCoordinator Scanner(IFileSystemEnumerator fs) => new(fs, new FakeDrives(true), new SystemClock());
    private static FileSystemEntry Dir(string path) => new(path, 0, EntryTraits.Directory);
    private static FileSystemEntry File(string path, long size) => new(path, size, EntryTraits.None);

    private sealed class FakeDrives(bool supported) : IDriveDiscovery
    {
        public IReadOnlyList<DriveSummary> Discover() => [new("C:\\", "Fixture", "NTFS", DriveKind.Fixed, true, true, supported, supported ? "Supported" : "Unsupported fixture", 10_000_000, 5_000_000, 5_000_000)];
    }

    private sealed class FakeFileSystem(Dictionary<string, FileSystemEntry[]> entries, string? denied = null) : IFileSystemEnumerator
    {
        public List<string> EnumeratedDirectories { get; } = [];
        public IEnumerable<FileSystemEntry> Enumerate(string directory)
        {
            EnumeratedDirectories.Add(directory);
            if (directory == denied) throw new UnauthorizedAccessException("fixture denial");
            return entries.TryGetValue(directory, out var values) ? values : [];
        }
    }

    private sealed class GeneratedFileSystem(int count, Action<int>? observed = null) : IFileSystemEnumerator
    {
        public IEnumerable<FileSystemEntry> Enumerate(string directory)
        {
            for (var index = 1; index <= count; index++)
            {
                observed?.Invoke(index);
                yield return new($"C:\\fixture-{index}.bin", index, EntryTraits.None);
            }
        }
    }

    private sealed class BlockingFileSystem : IFileSystemEnumerator
    {
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public IEnumerable<FileSystemEntry> Enumerate(string directory)
        {
            Started.Set(); Release.Wait(TimeSpan.FromSeconds(5));
            return [];
        }
    }
}
