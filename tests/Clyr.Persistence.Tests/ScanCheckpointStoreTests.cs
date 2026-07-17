using Clyr.Contracts;
using Clyr.Core;
using Clyr.Persistence;

namespace Clyr.Persistence.Tests;

public sealed class ScanCheckpointStoreTests
{
    [Fact]
    public void SaveThenLoadRoundTripsAllFields()
    {
        WithStore((store, clock) =>
        {
            var saved = new ScanCheckpoint("C:\\", ScanMode.Quick, 2, clock.UtcNow, clock.UtcNow, 10, 5, 12345, ["C:\\Windows", "C:\\Users"]);
            store.Save(saved);
            var loaded = store.TryLoad("C:\\", ScanMode.Quick);
            Assert.NotNull(loaded);
            Assert.Equal(saved.Root, loaded.Root);
            Assert.Equal(saved.Mode, loaded.Mode);
            Assert.Equal(saved.PolicyVersion, loaded.PolicyVersion);
            Assert.Equal(saved.FilesObserved, loaded.FilesObserved);
            Assert.Equal(saved.DirectoriesObserved, loaded.DirectoriesObserved);
            Assert.Equal(saved.LogicalBytesObserved, loaded.LogicalBytesObserved);
            Assert.Equal(saved.PendingDirectories, loaded.PendingDirectories);
        });
    }

    [Fact]
    public void DifferentDriveRootDoesNotLoadAnUnrelatedCheckpoint()
    {
        WithStore((store, clock) =>
        {
            store.Save(new("C:\\", ScanMode.Quick, 2, clock.UtcNow, clock.UtcNow, 0, 0, 0, []));
            Assert.Null(store.TryLoad("D:\\", ScanMode.Quick));
        });
    }

    [Fact]
    public void DeepModeHasNoCheckpointEvenIfQuickHasOne()
    {
        WithStore((store, clock) =>
        {
            store.Save(new("C:\\", ScanMode.Quick, 2, clock.UtcNow, clock.UtcNow, 0, 0, 0, []));
            Assert.Null(store.TryLoad("C:\\", ScanMode.Deep));
        });
    }

    [Fact]
    public void ClearRemovesTheCheckpoint()
    {
        WithStore((store, clock) =>
        {
            store.Save(new("C:\\", ScanMode.Quick, 2, clock.UtcNow, clock.UtcNow, 0, 0, 0, []));
            store.Clear("C:\\", ScanMode.Quick);
            Assert.Null(store.TryLoad("C:\\", ScanMode.Quick));
        });
    }

    [Fact]
    public void StaleCheckpointIsRejectedRatherThanReused()
    {
        WithStore((store, clock) =>
        {
            store.Save(new("C:\\", ScanMode.Quick, 2, clock.UtcNow, clock.UtcNow, 10, 5, 1000, ["C:\\Somewhere"]));
            Assert.NotNull(store.TryLoad("C:\\", ScanMode.Quick));
            clock.Advance(FileScanCheckpointStore.MaximumAge + TimeSpan.FromMinutes(1));
            Assert.Null(store.TryLoad("C:\\", ScanMode.Quick));
        });
    }

    [Fact]
    public void CorruptCheckpointFileIsTreatedAsNoCheckpointRatherThanThrowing()
    {
        var directory = TemporaryDirectory();
        try
        {
            var clock = new SteppableClock();
            var store = new FileScanCheckpointStore(directory, clock);
            store.Save(new("C:\\", ScanMode.Quick, 2, clock.UtcNow, clock.UtcNow, 0, 0, 0, []));
            var file = Directory.GetFiles(directory, "*.json").Single();
            File.WriteAllText(file, "{ not valid json");
            Assert.Null(store.TryLoad("C:\\", ScanMode.Quick));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("C:\\")]
    [InlineData("C:\\..\\..\\Windows\\System32\\")]
    [InlineData("..\\..\\..\\..\\..\\..\\Windows\\System32\\evil.json")]
    [InlineData("\\\\server\\share\\")]
    [InlineData("C:\\Users\\Alice\\.. \\.. \\.. \\Windows\\")]
    [InlineData("CON\\NUL\\PRN\\")]
    [InlineData("C:\\<drive-label-injection>/../../etc\x00passwd")]
    [InlineData("C:\\üñíçødé\\目录\\🚀\\")]
    [InlineData("")]
    // Reparse-point-style and malformed-identity content is just more untrusted text to the checkpoint store —
    // it is never resolved against the real filesystem here, only hashed.
    [InlineData("C:\\reparse-target-outside-volume\\..\\..\\..\\")]
    public void CheckpointFilePathsCanNeverEscapeTheConfinedCheckpointsDirectory(string adversarialRoot)
    {
        WithStore((store, clock) =>
        {
            // A malicious/malformed root must never throw, never write outside the confined directory, and
            // never produce a file name containing anything other than the fixed hash-and-mode shape.
            store.Save(new(adversarialRoot, ScanMode.Quick, 2, clock.UtcNow, clock.UtcNow, 1, 1, 1, ["C:\\x"]));
            var files = Directory.GetFiles(store.ConfinedDirectory, "*", SearchOption.AllDirectories);
            Assert.Single(files);
            var created = files[0];
            Assert.StartsWith(store.ConfinedDirectory, Path.GetFullPath(created), StringComparison.OrdinalIgnoreCase);
            Assert.Matches("^[0-9a-f]{16}-Quick\\.json$", Path.GetFileName(created));
            // No subdirectory was created — the file sits directly in the confined directory, one level deep.
            Assert.Equal(store.ConfinedDirectory, Path.GetDirectoryName(created) + Path.DirectorySeparatorChar);

            Assert.NotNull(store.TryLoad(adversarialRoot, ScanMode.Quick));
            store.Clear(adversarialRoot, ScanMode.Quick);
            Assert.Empty(Directory.GetFiles(store.ConfinedDirectory, "*", SearchOption.AllDirectories));
        });
    }

    [Fact]
    public void DifferentAdversarialRootsNeverCollideOrEscapeEvenWhenSavedTogether()
    {
        WithStore((store, clock) =>
        {
            string[] roots =
            [
                "C:\\", "C:\\..\\..\\Windows\\", "\\\\server\\share\\", "C:\\üñíçødé\\", "",
                "C:\\Users\\Alice\\..\\..\\Windows\\System32\\"
            ];
            foreach (var root in roots) store.Save(new(root, ScanMode.Quick, 2, clock.UtcNow, clock.UtcNow, 1, 1, 1, []));
            var files = Directory.GetFiles(store.ConfinedDirectory, "*", SearchOption.AllDirectories);
            Assert.Equal(roots.Distinct(StringComparer.OrdinalIgnoreCase).Count(), files.Length);
            Assert.All(files, file => Assert.StartsWith(store.ConfinedDirectory, Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase));
        });
    }

    private static void WithStore(Action<FileScanCheckpointStore, SteppableClock> body)
    {
        var directory = TemporaryDirectory();
        try
        {
            var clock = new SteppableClock();
            body(new FileScanCheckpointStore(directory, clock), clock);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static string TemporaryDirectory() => Path.Combine(Path.GetTempPath(), "clyr-checkpoint-tests-" + Guid.NewGuid().ToString("N"));

    private sealed class SteppableClock : IClock
    {
        private DateTimeOffset now = DateTimeOffset.UnixEpoch;
        public DateTimeOffset UtcNow => now;
        public void Advance(TimeSpan by) => now += by;
    }
}
