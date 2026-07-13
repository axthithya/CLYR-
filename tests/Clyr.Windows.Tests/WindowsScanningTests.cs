using Clyr.Contracts;
using Clyr.Core;
using Clyr.Windows;

namespace Clyr.Windows.Tests;

public sealed class WindowsScanningTests
{
    private static readonly string[] SupportedFileSystems = ["NTFS"];
    [Fact]
    public void DriveDiscoveryReturnsCapabilityQualifiedMetadata()
    {
        var drives = new WindowsDriveDiscovery().Discover();
        Assert.NotEmpty(drives);
        Assert.All(drives, drive =>
        {
            Assert.Matches("^[A-Z]:\\\\$", drive.Root);
            Assert.False(string.IsNullOrWhiteSpace(drive.SupportReason));
            if (drive.IsSupported)
            {
                Assert.Equal(DriveKind.Fixed, drive.Kind);
                Assert.Contains(drive.FileSystem, SupportedFileSystems, StringComparer.OrdinalIgnoreCase);
            }
        });
    }

    [Fact]
    public void MetadataEnumeratorReportsFilesAndDirectoriesInIsolatedFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "clyr-enumeration-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "folder"));
            File.WriteAllText(Path.Combine(root, "sample.txt"), "fixture");
            var entries = new WindowsFileSystemEnumerator().Enumerate(root).ToArray();
            Assert.Contains(entries, item => item.FullPath.EndsWith("folder", StringComparison.Ordinal) && item.Traits.HasFlag(EntryTraits.Directory));
            Assert.Contains(entries, item => item.FullPath.EndsWith("sample.txt", StringComparison.Ordinal) && item.LogicalBytes == 7);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void WindowsScannerSourceContainsNoContentReadOrMutationApi()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Clyr.Windows", "WindowsScanning.cs"));
        foreach (var forbidden in new[] { "OpenRead", "ReadAllBytes", "ReadAllText", "FileStream", "File.Delete", "Directory.Delete", "SetAccessControl" })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        Assert.Contains("ReparsePoint", source, StringComparison.Ordinal);
        Assert.Contains("CloudPlaceholder", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
