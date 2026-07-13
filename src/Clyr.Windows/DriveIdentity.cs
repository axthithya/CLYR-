using System.Runtime.InteropServices;
using Clyr.Core;

namespace Clyr.Windows;

public sealed class WindowsVolumeIdentitySource : IRawDriveIdentitySource
{
    public string? GetStableIdentity(string root)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var buffer = new char[1024];
        return GetVolumeNameForVolumeMountPoint(root, buffer, buffer.Length)
            ? new string(buffer, 0, Array.IndexOf(buffer, '\0') is var end and >= 0 ? end : buffer.Length)
            : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string mountPoint, [Out] char[] volumeName, int bufferLength);
}
