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
    private readonly string confinedDirectory;
    private readonly IClock clock;

    /// <summary>The real, fully-resolved checkpoints directory every checkpoint file is confined to. Exposed so
    /// tests can independently prove containment rather than trusting <see cref="PathFor"/>'s own arithmetic.</summary>
    public string ConfinedDirectory => confinedDirectory;

    public FileScanCheckpointStore(string directory, IClock clock)
    {
        this.directory = directory;
        this.clock = clock;
        Directory.CreateDirectory(directory);
        // Resolved once, after creation, so it reflects the real filesystem path (fully qualified, no "..",
        // trailing separator normalized) that every derived checkpoint path is checked against below.
        confinedDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
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

    /// <summary>
    /// Builds a checkpoint file path for an arbitrary, untrusted <paramref name="root"/> string. The path is
    /// never built from <paramref name="root"/>'s own characters — only from a fixed-length SHA-256 hex digest
    /// of it — so no drive label, traversal sequence ("..\"), reparse-point target, malformed drive identity,
    /// or unicode/control-character content in <paramref name="root"/> can ever influence the resulting file
    /// name's shape, let alone escape <see cref="confinedDirectory"/>. The containment check below is a
    /// defense-in-depth belt-and-braces assertion, not the actual safety mechanism (the hash is) — it exists so
    /// a future change to this method that broke containment would fail loudly instead of silently.
    /// </summary>
    private string PathFor(string root, ScanMode mode)
    {
        var path = Path.GetFullPath(Path.Combine(directory, $"{Fingerprint(root)}-{mode}.json"));
        if (!path.StartsWith(confinedDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A checkpoint path resolved outside CLYR's checkpoints directory; refusing to touch it.");
        return path;
    }

    private static string Fingerprint(string root)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant()));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
