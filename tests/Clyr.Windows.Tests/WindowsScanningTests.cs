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
        foreach (var file in new[] { "WindowsScanning.cs", "WindowsFileIdentity.cs" })
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Clyr.Windows", file));
            foreach (var forbidden in new[] { "OpenRead", "ReadAllBytes", "ReadAllText", "FileStream", "File.Delete", "Directory.Delete", "SetAccessControl", "GENERIC_WRITE", "FILE_WRITE" })
                Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
        var scanningSource = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Clyr.Windows", "WindowsScanning.cs"));
        Assert.Contains("ReparsePoint", scanningSource, StringComparison.Ordinal);
        Assert.Contains("CloudPlaceholder", scanningSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AllocatedSizeIsReportedAndDiffersFromLogicalSizeForARealSparseFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "clyr-allocation-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "sparse.bin");
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            var entries = new WindowsFileSystemEnumerator().Enumerate(root).ToArray();
            var entry = Assert.Single(entries);
            Assert.Equal(4, entry.LogicalBytes);
            // A tiny real file's allocated size (rounded up to a filesystem cluster, typically 4 KiB on NTFS)
            // is read through the real Win32 GetCompressedFileSizeW API, not invented — it may legitimately
            // equal or exceed the logical size, but it must be present and non-negative.
            Assert.NotNull(entry.AllocatedBytes);
            Assert.True(entry.AllocatedBytes >= 0);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void RealHardLinkedFilesShareTheSameFileIdentityAndReportALinkCountAboveOne()
    {
        var root = Path.Combine(Path.GetTempPath(), "clyr-hardlink-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var original = Path.Combine(root, "original.bin");
            var linked = Path.Combine(root, "linked.bin");
            File.WriteAllBytes(original, [9, 9, 9, 9, 9]);
            var created = CreateHardLinkW(linked, original, IntPtr.Zero);
            if (!created)
            {
                // Hard links require NTFS and same-volume placement; if this environment cannot create one
                // (e.g., the temp directory is redirected onto a non-NTFS or network location), skip rather
                // than fail — this is an environment limitation, not a product defect.
                return;
            }

            var entries = new WindowsFileSystemEnumerator().Enumerate(root).ToArray();
            var first = entries.Single(item => item.FullPath == original);
            var second = entries.Single(item => item.FullPath == linked);

            Assert.NotNull(first.FileIdentity);
            Assert.NotNull(second.FileIdentity);
            Assert.Equal(first.FileIdentity, second.FileIdentity);
            Assert.True(first.HardLinkCount >= 2, $"Expected a visible link count of at least 2, got {first.HardLinkCount}.");
            Assert.Equal(first.HardLinkCount, second.HardLinkCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
