using Clyr.Contracts;
using Clyr.Core;
using Clyr.Persistence;

namespace Clyr.App;

/// <summary>
/// Optional override of the Phase 6 built-in action's trusted root. Real launches carry a null Path (the
/// executor resolves the real %LocalAppData%\Clyr\Temp folder); CLYR_UI_FIXTURE=1 launches carry a private
/// temporary directory seeded with synthetic stale files so UI Automation can exercise execution deterministically
/// without ever touching a real user path.
/// </summary>
/// <summary>DI-friendly reference-type wrapper: <see cref="ExecutionSessionId"/> is a value type and cannot be registered directly.</summary>
public sealed record ExecutionSessionContext(ExecutionSessionId Value);

public sealed record ExecutionFixtureRoot(string? Path)
{
    public static ExecutionFixtureRoot CreateSeeded()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clyr-ui-fixture-execution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        var old = DateTime.UtcNow.AddDays(-30);
        for (var index = 1; index <= 4; index++)
        {
            var file = System.IO.Path.Combine(path, "fixture-scratch-" + index + ".tmp");
            File.WriteAllText(file, "synthetic fixture scratch data");
            File.SetLastWriteTimeUtc(file, old);
            File.SetCreationTimeUtc(file, old);
        }
        return new(path);
    }
}

/// <summary>
/// In-memory-only execution receipt store for CLYR_UI_FIXTURE=1 launches. It exists so UI Automation can
/// exercise the receipt history/view/export/delete flow deterministically without ever opening the real
/// CLYR-owned SQLite history database — nothing here is written to disk.
/// </summary>
internal sealed class UiFixtureExecutionReceiptStore : IExecutionReceiptStore
{
    private readonly Dictionary<ExecutionId, ExecutionReceipt> receipts = [];

    public Task SaveAsync(ExecutionReceipt receipt, CancellationToken cancellationToken = default)
    {
        receipts[receipt.ExecutionId] = receipt;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ExecutionReceiptSummary>> ListAsync(int limit = 50, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ExecutionReceiptSummary>>(receipts.Values
            .OrderByDescending(receipt => receipt.StartedAtUtc)
            .Take(limit)
            .Select(receipt => new ExecutionReceiptSummary(receipt.ExecutionId, receipt.SourcePlanId, receipt.StartedAtUtc,
                receipt.CompletedAtUtc, receipt.FinalState, receipt.Summary.RemovedCount, receipt.Summary.SkippedCount,
                receipt.Summary.FailedCount, receipt.Summary.RemovedLogicalBytes))
            .ToArray());

    public Task<ExecutionReceipt?> GetAsync(ExecutionId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(receipts.TryGetValue(id, out var receipt) ? receipt : null);

    public Task<bool> DiscardAsync(ExecutionId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(receipts.Remove(id));

    public Task<int> ReconcileInterruptedAsync(TimeSpan staleAfter, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) => Task.FromResult(0);
}

internal sealed class UiFixtureDriveDiscovery : IDriveDiscovery
{
    public IReadOnlyList<DriveSummary> Discover() => [new("C:\\", "Fixture system drive", "NTFS", DriveKind.Fixed, true, true, true, "Ready for private analysis.", 512L * 1024 * 1024 * 1024, 320L * 1024 * 1024 * 1024, 192L * 1024 * 1024 * 1024)];
}

/// <summary>Truthful UI-fixture stand-in for <see cref="IElevatedScanRetryService"/>: <see cref="UiFixtureScanService"/>'s
/// synthetic results never populate <see cref="ScanResult.RootContributions"/>, so reporting
/// <see cref="ElevatedScanRetryEligibilityOutcome.NoRootContributions"/> here is accurate, not merely a stub —
/// the administrator-retry action is correctly never shown for a CLYR_UI_FIXTURE=1 run. <see cref="RetryAsync"/>
/// exists only to satisfy the interface; the action being hidden means it is never actually invoked.</summary>
internal sealed class UiFixtureElevatedScanRetryService : IElevatedScanRetryService
{
    public ElevatedScanRetryAvailability Evaluate(ScanResult originalResult) =>
        new(false, ElevatedScanRetryEligibilityOutcome.NoRootContributions, 0, 0, "elevated-retry-availability.no-root-contributions");

    public Task<ElevatedScanRetryWorkflowResult> RetryAsync(ScanResult originalResult, CancellationToken cancellationToken) =>
        Task.FromResult(new ElevatedScanRetryWorkflowResult(ElevatedScanRetryWorkflowOutcome.NotEligible, originalResult,
            ElevatedScanRetryEligibilityOutcome.NoRootContributions, null, null, null, null, null, 0, 0, 0, "elevated-retry.not-eligible"));
}

internal sealed class UiFixtureScanService : IScanService
{
    public async Task<ScanResult> ScanAsync(ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var scanId = Guid.NewGuid();
        for (var index = 1; index <= 8; index++)
        {
            await Task.Delay(120, CancellationToken.None);
            if (cancellationToken.IsCancellationRequested) return Result(ScanStatus.Cancelled, index * 400, started, scanId);
            progress?.Report(new(ScanStatus.Scanning, DateTimeOffset.UtcNow - started, index * 400, index * 75,
                index * 450_000_000L, index / 3, "C:\\<redacted>", "Observing file metadata locally."));
            // Deterministic UI-fixture coverage for the progressive-analysis states (section 25): "running before
            // insights" for the first two ticks, "running with early insights" from tick 3 onward, once enough
            // synthetic aggregate data exists to be worth showing — never real UAC, never a real scan.
            if (request.ProgressiveProgress is { } reporter)
            {
                var stage = index <= 2 ? ScanStage.DiscoveringMajorStorageAreas
                    : index >= 7 ? ScanStage.Finalizing : ScanStage.InspectingFilesAndFolders;
                var earlyInsightsReady = index >= 3;
                var contributors = earlyInsightsReady
                    ? new CategorySummary[]
                    {
                        new(StorageCategory.WindowsSystemManaged, index * 160_000_000L, index * 70, MeasurementPrecision.Estimated, FindingStatus.Protected),
                        new(StorageCategory.UserMedia, index * 90_000_000L, index * 50, MeasurementPrecision.Estimated, FindingStatus.Review),
                    }
                    : [];
                var topDirectories = earlyInsightsReady
                    ? new RankedPath[] { new("C:\\<redacted>\\Users", index * 140_000_000L, index * 120, MeasurementPrecision.Estimated) }
                    : [];
                var topFiles = earlyInsightsReady
                    ? new RankedPath[] { new("C:\\<redacted>\\Media\\large-file.bin", index * 90_000_000L, 1, MeasurementPrecision.Estimated) }
                    : [];
                reporter.Report(new(scanId, "C:\\", DateTimeOffset.UtcNow - started, index * 400, index * 75, index * 450_000_000L,
                    320L * 1024 * 1024 * 1024, Math.Min(99.9, index * 12.5), index / 4, index / 5,
                    contributors, topDirectories, topFiles, stage, earlyInsightsReady));
            }
        }
        return Result(ScanStatus.CompletedWithWarnings, 3_200, started, scanId);

        ScanResult Result(ScanStatus status, long files, DateTimeOffset began, Guid id)
        {
            var categories = new CategorySummary[]
            {
                new(StorageCategory.WindowsSystemManaged, 1_600_000_000, 700, MeasurementPrecision.Estimated, FindingStatus.Protected),
                new(StorageCategory.UserMedia, 900_000_000, 500, MeasurementPrecision.Estimated, FindingStatus.Review),
                new(StorageCategory.DeveloperCache, 600_000_000, 900, MeasurementPrecision.Estimated, FindingStatus.Informational),
                new(StorageCategory.Unknown, 500_000_000, 1_100, MeasurementPrecision.Estimated, FindingStatus.Unknown)
            };
            var pack = new RulePackSummary("clyr.builtin", "1.0.0", "fixture-digest", RulePackTrust.BuiltIn, true, true, 36, "verified", "Verified", "fixture", "MIT");
            var classification = new ClassificationResult(categories, [new("fixture", "developer.npm.cache", "1.0.0", "clyr.builtin", "1.0.0", "fixture-digest", "Developer package cache", StorageCategory.DeveloperCache, ["developer"], FindingConfidence.High, FindingStatus.Informational, 600_000_000, 900, MeasurementPrecision.Estimated, new("Caches downloaded packages.", "This aggregate can grow as tools download packages.", "Report only. No files are changed.", "Fixture metadata evidence.", ["Fixture-backed UI verification data."]))], new(files - 1_100, 3_100_000_000, 1_100, 500_000_000, 2, 4, 20_000_000_000), pack, "Most observed storage is Windows-managed or user media.", ["Logical sizes are estimates."]);
            var coverage = new ScanCoverage(files, 600, 2, 1, 0, 0, 1, false, false, false);
            return new(id, status, request.Mode, "C:\\", "NTFS", began, DateTimeOffset.UtcNow, 3_600_000_000,
                320L * 1024 * 1024 * 1024, 20_000_000_000, MeasurementPrecision.Estimated, "Logical metadata only; no file contents were read.", coverage,
                [new("C:\\<redacted>\\Users", 1_400_000_000, 1_200, MeasurementPrecision.Estimated), new("C:\\<redacted>\\Windows", 1_300_000_000, 900, MeasurementPrecision.Estimated)],
                [new("C:\\<redacted>\\Media", 900_000_000, 500, MeasurementPrecision.Estimated)], [], [],
                [new(ScanIssueKind.AccessDenied, "scan.access-denied", 2, "Two entries were inaccessible.")], null, null, classification);
        }
    }
}

/// <summary>Trivial fixture identity so <see cref="SnapshotFactory"/> can be constructed identically in both
/// fixture and real modes — fixture mode never needs a genuinely stable per-drive identity, only a non-throwing
/// one.</summary>
internal sealed class UiFixtureDriveIdentityProvider : IDriveIdentityProvider
{
    public SnapshotDrive Identify(string root, string fileSystem, long? usedBytes) =>
        new("fixture-drive", DriveIdentityQuality.Stable, root, fileSystem, null, usedBytes, null);
}

internal sealed class UiFixtureSnapshotStore : ISnapshotStore
{
    private readonly List<StorageSnapshot> snapshots;
    private HistorySettings settings = HistorySettings.Default;
    public UiFixtureSnapshotStore() => snapshots = [Create(DateTimeOffset.UtcNow.AddDays(-7), 300L * 1024 * 1024 * 1024, 2_800_000_000), Create(DateTimeOffset.UtcNow.AddHours(-1), 320L * 1024 * 1024 * 1024, 3_600_000_000)];
    public Task<SnapshotSaveResult> SaveAsync(StorageSnapshot snapshot, CancellationToken cancellationToken = default) { snapshots.Add(snapshot); return Task.FromResult(new SnapshotSaveResult(true, snapshot.Id, "saved", "Saved")); }
    public Task<IReadOnlyList<SnapshotSummary>> ListAsync(int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SnapshotSummary>>(snapshots.OrderByDescending(x => x.CapturedAtUtc).Take(limit).Select(x => new SnapshotSummary(x.Id, x.CapturedAtUtc, x.State, x.Drive.Fingerprint, x.Drive.IdentityQuality, x.Drive.Root, x.Drive.FileSystem, x.Mode, x.LogicalBytesObserved, x.Drive.UsedBytes, x.UnknownBytes)).ToArray());
    public Task<StorageSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(snapshots.FirstOrDefault(x => x.Id == id));
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(snapshots.RemoveAll(x => x.Id == id) > 0);
    public Task<int> ClearAsync(CancellationToken cancellationToken = default) { var count = snapshots.Count; snapshots.Clear(); return Task.FromResult(count); }
    public Task<HistorySettings> GetSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);
    public Task SetSettingsAsync(HistorySettings value, CancellationToken cancellationToken = default) { settings = value; return Task.CompletedTask; }
    private static StorageSnapshot Create(DateTimeOffset time, long used, long observed) => new(Guid.NewGuid(), Guid.NewGuid(), 1, "fixture", time, ScanMode.Quick, SnapshotState.Complete, new("fixture-drive", DriveIdentityQuality.Stable, "C:\\", "NTFS", 512L * 1024 * 1024 * 1024, used, 512L * 1024 * 1024 * 1024 - used), observed, observed - 500_000_000, 500_000_000, 20_000_000_000, new(3_200, 600, 2, 1, 0, 0, 1, false, false, false), "clyr.builtin", "1.0.0", "fixture-digest", [new(StorageCategory.DeveloperCache, observed - 500_000_000, 900, MeasurementPrecision.Estimated, FindingStatus.Informational)], [new("developer.npm.cache", "1.1.0", StorageCategory.DeveloperCache, FindingConfidence.High, FindingStatus.Informational, observed - 500_000_000, 900)], []);
}
