using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Clyr.Windows;

/// <summary>
/// Read-only Windows metadata queries backing Phase 7.2's allocated-size and hard-link-aware accounting.
/// Every P/Invoke here queries metadata only — no handle is ever opened for read/write data access, no file
/// content is ever read, and nothing is ever written. Every call is wrapped so a failure (permission denial,
/// a file that vanished between enumeration and query, an unsupported filesystem) degrades to "unavailable"
/// rather than throwing — matching the scan engine's existing bounded-diagnostic behavior for access failures.
/// </summary>
internal static class WindowsFileIdentity
{
    private const uint FileReadAttributes = 0x80;
    private const uint FileShareAll = 0x1 | 0x2 | 0x4; // FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint InvalidFileSize = 0xFFFFFFFF;
    private const int ErrorSuccess = 0;

    /// <summary>
    /// The real on-disk allocation for a file, accounting for compression and sparse regions — never the same
    /// thing as its logical (namespace) size. Returns null, never a guess, when the query fails for any reason.
    /// </summary>
    public static long? TryGetAllocatedBytes(string path)
    {
        var low = GetCompressedFileSizeW(path, out var high);
        if (low == InvalidFileSize && Marshal.GetLastWin32Error() != ErrorSuccess) return null;
        return ((long)high << 32) | (uint)low;
    }

    /// <summary>
    /// A stable, volume-scoped NTFS file identity plus the visible hard-link count, read through a metadata-
    /// only handle (<c>FILE_READ_ATTRIBUTES</c> — no data access rights requested). The handle never follows a
    /// reparse point (<c>FILE_FLAG_OPEN_REPARSE_POINT</c>) — consistent with the scan engine never traversing
    /// reparse targets — though reparse-point entries are never passed here in the first place. Returns
    /// (null, null) rather than throwing on any failure; an identity failure is a bounded diagnostic to the
    /// caller, never a fatal scan error.
    /// </summary>
    public static (ulong? Identity, int? LinkCount) TryGetIdentity(string path)
    {
        using var handle = CreateFileW(path, FileReadAttributes, FileShareAll, IntPtr.Zero, OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid) return (null, null);
        if (!GetFileInformationByHandle(handle, out var info)) return (null, null);
        var identity = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
        return (identity, checked((int)info.NumberOfLinks));
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetCompressedFileSizeW")]
    private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation lpFileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
