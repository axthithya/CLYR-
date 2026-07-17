using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Windows;

public sealed class WindowsDriveDiscovery : IDriveDiscovery
{
    public IReadOnlyList<DriveSummary> Discover()
    {
        var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
        var results = new List<DriveSummary>();
        foreach (var drive in DriveInfo.GetDrives().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            try { results.Add(Map(drive, systemRoot)); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                results.Add(new(drive.Name, string.Empty, string.Empty, DriveKind.Unknown, false,
                    string.Equals(drive.Name, systemRoot, StringComparison.OrdinalIgnoreCase), false,
                    "Drive metadata became unavailable during discovery.", null, null, null));
            }
        }
        return results;
    }

    private static DriveSummary Map(DriveInfo drive, string systemRoot)
    {
        var kind = drive.DriveType switch
        {
            DriveType.Fixed => DriveKind.Fixed,
            DriveType.Removable => DriveKind.Removable,
            DriveType.Network => DriveKind.Network,
            DriveType.CDRom => DriveKind.Optical,
            DriveType.Ram => DriveKind.Ram,
            _ => DriveKind.Unknown
        };
        if (!drive.IsReady)
            return new(drive.Name, string.Empty, string.Empty, kind, false,
                string.Equals(drive.Name, systemRoot, StringComparison.OrdinalIgnoreCase), false,
                "Drive is not ready.", null, null, null);
        var supported = kind == DriveKind.Fixed && drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase);
        var capacity = drive.TotalSize;
        var free = drive.AvailableFreeSpace;
        return new(drive.Name, drive.VolumeLabel, drive.DriveFormat, kind, true,
            string.Equals(drive.Name, systemRoot, StringComparison.OrdinalIgnoreCase), supported,
            supported ? "Metadata-only scanning supported." : "Phase 2 supports ready fixed NTFS volumes only.",
            capacity, capacity - free, free);
    }
}

public sealed class WindowsFileSystemEnumerator : IFileSystemEnumerator
{
    private static readonly FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private static readonly FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    public IEnumerable<FileSystemEntry> Enumerate(string directory)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, ReturnSpecialDirectories = false, AttributesToSkip = 0 };
        foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", options))
        {
            var attributes = File.GetAttributes(path);
            var traits = EntryTraits.None;
            if ((attributes & FileAttributes.Directory) != 0) traits |= EntryTraits.Directory;
            if ((attributes & FileAttributes.ReparsePoint) != 0) traits |= EntryTraits.ReparsePoint;
            if ((attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0) traits |= EntryTraits.CloudPlaceholder;
            if ((attributes & FileAttributes.SparseFile) != 0) traits |= EntryTraits.Sparse;
            if ((attributes & FileAttributes.Compressed) != 0) traits |= EntryTraits.Compressed;

            // Allocated size and hard-link identity are read only for plain files that are not reparse points
            // and not cloud placeholders — a cloud placeholder's "allocated size" would misleadingly report
            // near-zero local disk usage for content that mostly isn't materialized locally, and a reparse
            // point is never traversed or queried for real content metadata in the first place.
            var isPlainFile = (traits & (EntryTraits.Directory | EntryTraits.ReparsePoint | EntryTraits.CloudPlaceholder)) == EntryTraits.None;
            var logicalBytes = (traits & EntryTraits.Directory) != 0 ? 0 : new FileInfo(path).Length;
            long? allocatedBytes = null;
            ulong? identity = null;
            int? linkCount = null;
            if (isPlainFile)
            {
                allocatedBytes = WindowsFileIdentity.TryGetAllocatedBytes(path);
                (identity, linkCount) = WindowsFileIdentity.TryGetIdentity(path);
            }
            yield return new(path, logicalBytes, traits, allocatedBytes, identity, linkCount);
        }
    }
}
