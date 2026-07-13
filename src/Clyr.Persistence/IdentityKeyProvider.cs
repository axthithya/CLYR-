using System.Security.Cryptography;
using Clyr.Core;

namespace Clyr.Persistence;

public sealed class FileIdentityKeyProvider(string keyPath) : IIdentityKeyProvider
{
    private readonly string keyPath = Path.GetFullPath(keyPath);

    public byte[] GetOrCreateKey()
    {
        if (File.Exists(keyPath)) return Validate(File.ReadAllBytes(keyPath));
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            using var stream = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(key); stream.Flush(true); return key;
        }
        catch (IOException) when (File.Exists(keyPath)) { return Validate(File.ReadAllBytes(keyPath)); }
    }

    private static byte[] Validate(byte[] key) => key.Length == 32 ? key : throw new CryptographicException("The local history identity key is invalid.");
}
