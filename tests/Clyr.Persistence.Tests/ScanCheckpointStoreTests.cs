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
