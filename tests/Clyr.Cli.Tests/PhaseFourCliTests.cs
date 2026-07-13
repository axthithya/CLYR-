using Clyr.Cli;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Rules;

namespace Clyr.Cli.Tests;

public sealed class PhaseFourCliTests
{
    [Fact]
    public void ListIsPrivacySafeAndStable()
    {
        var output = new StringWriter(); Assert.Equal(0, Create().Run(["snapshots", "list"], output, TextWriter.Null));
        Assert.Contains("C:\\", output.ToString(), StringComparison.Ordinal); Assert.DoesNotContain("Users", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShowAndCompareEmitVersionedTypedJson()
    {
        var store = new FakeStore(); var app = Create(store); var output = new StringWriter();
        Assert.Equal(0, app.Run(["snapshots", "show", store.Before.Id.ToString()], output, TextWriter.Null)); Assert.Contains("schemaVersion", output.ToString(), StringComparison.Ordinal);
        output.GetStringBuilder().Clear(); Assert.Equal(0, app.Run(["snapshots", "compare", store.Before.Id.ToString(), store.After.Id.ToString()], output, TextWriter.Null)); Assert.Contains("fully-comparable", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DestructiveCommandsRequireExplicitConfirmation()
    {
        var store = new FakeStore(); var error = new StringWriter(); var app = Create(store);
        Assert.Equal(2, app.Run(["snapshots", "delete", store.Before.Id.ToString()], TextWriter.Null, error)); Assert.Equal(2, app.Run(["snapshots", "clear"], TextWriter.Null, error)); Assert.Equal(2, store.Items.Count);
    }

    [Fact]
    public void SettingsValidateRetention()
    {
        var store = new FakeStore(); var app = Create(store);
        Assert.Equal(2, app.Run(["snapshots", "settings", "set", "true", "1", "true", "true"], TextWriter.Null, TextWriter.Null));
        Assert.Equal(0, app.Run(["snapshots", "settings", "set", "false", "20", "true", "false"], TextWriter.Null, TextWriter.Null)); Assert.False(store.Settings.IsEnabled);
    }

    private static CliApplication Create(FakeStore? store = null)
    { store ??= new(); var env = new EnvironmentFixture(); var schema = File.ReadAllText(Path.Combine(Root(), "rules", "schemas", "rule.schema.json")); return new(env, new DemoDataService(), new RuleValidator(schema), new PrivacyRedactor(env), "test", new Drives(), new Scanner(), new ScanReportExporter(), null, store); }
    private static string Root() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private sealed class EnvironmentFixture : IEnvironmentInfo { public string UserName => "Private"; public string UserProfilePath => "C:\\Users\\Private"; public string OperatingSystem => "Windows"; public string Architecture => "X64"; }
    private sealed class Drives : IDriveDiscovery { public IReadOnlyList<DriveSummary> Discover() => []; }
    private sealed class Scanner : IScanService { public Task<ScanResult> ScanAsync(ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken token) => throw new NotSupportedException(); }
    private sealed class FakeStore : ISnapshotStore
    { public StorageSnapshot Before { get; } = Snapshot(DateTimeOffset.UtcNow.AddHours(-1), 500); public StorageSnapshot After { get; } = Snapshot(DateTimeOffset.UtcNow, 800); public List<StorageSnapshot> Items { get; } public HistorySettings Settings { get; private set; } = HistorySettings.Default; public FakeStore() { Items = [Before, After]; } public Task<SnapshotSaveResult> SaveAsync(StorageSnapshot s, CancellationToken c = default) => Task.FromResult(new SnapshotSaveResult(false, null, "", "")); public Task<IReadOnlyList<SnapshotSummary>> ListAsync(int l = 100, CancellationToken c = default) => Task.FromResult<IReadOnlyList<SnapshotSummary>>(Items.Select(s => new SnapshotSummary(s.Id, s.CapturedAtUtc, s.State, s.Drive.Fingerprint, s.Drive.IdentityQuality, s.Drive.Root, s.Drive.FileSystem, s.Mode, s.LogicalBytesObserved, s.Drive.UsedBytes, s.UnknownBytes)).ToArray()); public Task<StorageSnapshot?> GetAsync(Guid id, CancellationToken c = default) => Task.FromResult(Items.FirstOrDefault(x => x.Id == id)); public Task<bool> DeleteAsync(Guid id, CancellationToken c = default) => Task.FromResult(Items.RemoveAll(x => x.Id == id) > 0); public Task<int> ClearAsync(CancellationToken c = default) { var count = Items.Count; Items.Clear(); return Task.FromResult(count); } public Task<HistorySettings> GetSettingsAsync(CancellationToken c = default) => Task.FromResult(Settings); public Task SetSettingsAsync(HistorySettings s, CancellationToken c = default) { if (s.RetentionPerDrive < 2) throw new ArgumentOutOfRangeException(nameof(s)); Settings = s; return Task.CompletedTask; } }
    private static StorageSnapshot Snapshot(DateTimeOffset time, long value) => new(Guid.NewGuid(), Guid.NewGuid(), 1, "test", time, ScanMode.Quick, SnapshotState.Complete, new("safe", DriveIdentityQuality.Stable, "C:\\", "NTFS", 1000, value, 1000 - value), value, value, 0, 0, new(1, 1, 0, 0, 0, 0, 0, false, false, false), "pack", "1", "digest", [], [], []);
}
