using System.Security.Cryptography;
using System.Text;
using Clyr.Contracts;

namespace Clyr.Core;

public interface IIdentityKeyProvider { byte[] GetOrCreateKey(); }
public interface IRawDriveIdentitySource { string? GetStableIdentity(string root); }

public sealed class HmacDriveIdentityProvider(IRawDriveIdentitySource source, IIdentityKeyProvider keys,
    IDriveDiscovery drives) : IDriveIdentityProvider
{
    public SnapshotDrive Identify(string root, string fileSystem, long? usedBytes)
    {
        var drive = drives.Discover().FirstOrDefault(item => string.Equals(item.Root, root, StringComparison.OrdinalIgnoreCase));
        try
        {
            var raw = source.GetStableIdentity(root);
            if (string.IsNullOrWhiteSpace(raw)) return Unavailable();
            var digest = HMACSHA256.HashData(keys.GetOrCreateKey(), Encoding.UTF8.GetBytes(raw));
            return new(Convert.ToHexString(digest).ToLowerInvariant(), DriveIdentityQuality.Stable, root, fileSystem,
                drive?.CapacityBytes, usedBytes, drive?.FreeBytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        { return Unavailable(); }

        SnapshotDrive Unavailable() => new(string.Empty, DriveIdentityQuality.Unavailable, root, fileSystem,
            drive?.CapacityBytes, usedBytes, drive?.FreeBytes);
    }
}
