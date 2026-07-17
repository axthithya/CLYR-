using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Persistence;

/// <summary>
/// A CLYR-owned, file-based store for <see cref="ScanCheckpoint"/>s, one JSON file per drive root under
/// CLYR's own application-data checkpoints directory. Deliberately not SQLite: a checkpoint is small, transient,
/// single-writer, bounded, and unrelated to permanent scan history, so a plain per-root file keeps it simple and
/// makes a corrupt or unreadable checkpoint trivially safe to ignore (never crash, never block a fresh scan).
/// </summary>
public sealed class FileScanCheckpointStore : IScanCheckpointStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>A checkpoint older than this is treated as stale and rejected, even if the drive root and
    /// policy version still match — the underlying filesystem has almost certainly changed enough that resuming
    /// from it would no longer represent a coherent single scan execution.</summary>
    public static readonly TimeSpan MaximumAge = TimeSpan.FromHours(24);

    private readonly string directory;
    private readonly IClock clock;

    public FileScanCheckpointStore(string directory, IClock clock)
    {
        this.directory = directory;
        this.clock = clock;
        Directory.CreateDirectory(directory);
    }

    public ScanCheckpoint? TryLoad(string root, ScanMode mode)
    {
        var path = PathFor(root, mode);
        if (!File.Exists(path)) return null;
        try
        {
            var checkpoint = JsonSerializer.Deserialize<ScanCheckpoint>(File.ReadAllText(path), JsonOptions);
            if (checkpoint is null) return null;
            if (!string.Equals(checkpoint.Root, root, StringComparison.OrdinalIgnoreCase)) return null;
            if (checkpoint.Mode != mode) return null;
            if (clock.UtcNow - checkpoint.SavedAtUtc > MaximumAge) return null;
            return checkpoint;
        }
        // A checkpoint file is CLYR-owned convenience state, never authoritative data: any read/parse failure
        // (corrupt JSON, unexpected shape, concurrent deletion) is treated exactly like "no checkpoint" rather
        // than surfaced as a scan error.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { return null; }
    }

    public void Save(ScanCheckpoint checkpoint)
    {
        var path = PathFor(checkpoint.Root, checkpoint.Mode);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(checkpoint, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    public void Clear(string root, ScanMode mode)
    {
        var path = PathFor(root, mode);
        if (File.Exists(path)) File.Delete(path);
    }

    private string PathFor(string root, ScanMode mode) => Path.Combine(directory, $"{Fingerprint(root)}-{mode}.json");

    private static string Fingerprint(string root)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant()));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
