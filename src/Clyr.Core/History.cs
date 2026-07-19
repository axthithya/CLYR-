using Clyr.Contracts;

namespace Clyr.Core;

public interface IDriveIdentityProvider
{
    SnapshotDrive Identify(string root, string fileSystem, long? usedBytes);
}

public interface ISnapshotStore
{
    Task<SnapshotSaveResult> SaveAsync(StorageSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SnapshotSummary>> ListAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<StorageSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> ClearAsync(CancellationToken cancellationToken = default);
    Task<HistorySettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task SetSettingsAsync(HistorySettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the existing saved record for <paramref name="snapshot"/>'s <see cref="StorageSnapshot.ScanId"/> in
    /// place (same stored Id, same original capture time) rather than inserting a second row — this is how a
    /// successful Administrator Retry's enriched figures reach History and survive an application restart without
    /// ever creating a second, misleading analysis record for the same Drive Analysis. A no-op, safely-failing
    /// default is provided so every pre-existing <see cref="ISnapshotStore"/> implementation (fixtures, test
    /// doubles) keeps compiling and behaving exactly as before without needing to implement this; only
    /// <see cref="Clyr.Persistence.SqliteSnapshotStore"/> currently overrides it with a real implementation. The
    /// original evidence is never destructively discarded by this: <paramref name="snapshot"/> is expected to be
    /// built from the enriched <see cref="Clyr.Contracts.ScanResult"/>, which itself is the original result plus
    /// safely-reconciled retry deltas — never a fabricated substitute.
    /// </summary>
    Task<SnapshotSaveResult> UpdateAsync(StorageSnapshot snapshot, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SnapshotSaveResult(false, null, "snapshot.update-unsupported",
            "Updating a saved analysis is not supported by this snapshot store."));
}

public interface IUsnChangeSource
{
    bool IsSupported { get; }
    Task<IReadOnlyList<string>> ReadOpaqueChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class UnsupportedUsnChangeSource : IUsnChangeSource
{
    public bool IsSupported => false;
    public Task<IReadOnlyList<string>> ReadOpaqueChangesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}

public sealed class SnapshotFactory(IDriveIdentityProvider identity, IApplicationVersion applicationVersion)
{
    public const int SchemaVersion = 1;

    public StorageSnapshot Create(ScanResult result)
    {
        var state = result.Status switch
        {
            ScanStatus.Completed => SnapshotState.Complete,
            ScanStatus.CompletedWithWarnings => SnapshotState.Partial,
            ScanStatus.Cancelled => SnapshotState.Cancelled,
            _ => SnapshotState.Failed
        };
        var classification = result.Classification;
        var categories = classification?.Categories.Select(item => new SnapshotCategory(item.Category,
            item.LogicalBytes, item.FileCount, item.Precision, item.Status)).ToArray() ?? [];
        var findings = classification?.Findings.Select(item => new SnapshotFinding(item.RuleId, item.RuleVersion,
            item.Category, item.Confidence, item.Status, item.LogicalBytes, item.FileCount)).ToArray() ?? [];
        var warnings = result.Issues.Select(item => $"{item.Code}: {item.Count}").Concat(
            classification?.Limitations ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new(Guid.NewGuid(), result.ScanId, SchemaVersion, applicationVersion.Value, result.EndedAt.ToUniversalTime(),
            result.Mode, state, identity.Identify(result.Root, result.FileSystem, result.DriveUsedBytes),
            result.LogicalBytesObserved, classification?.Coverage.ClassifiedBytes ?? 0,
            classification?.Coverage.UnknownBytes ?? result.LogicalBytesObserved, result.UnaccountedBytes,
            result.Coverage, classification?.RulePack.Id ?? string.Empty, classification?.RulePack.Version ?? string.Empty,
            classification?.RulePack.Digest ?? string.Empty, categories, findings, warnings);
    }
}

public sealed class SnapshotSavingScanService(IScanService inner, SnapshotFactory factory, ISnapshotStore store) : IScanService
{
    public async Task<ScanResult> ScanAsync(ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var result = await inner.ScanAsync(request, progress, cancellationToken).ConfigureAwait(false);
        var settings = await store.GetSettingsAsync(CancellationToken.None).ConfigureAwait(false);
        var eligible = result.Status == ScanStatus.Completed ||
            result.Status == ScanStatus.CompletedWithWarnings && settings.SavePartial ||
            result.Status == ScanStatus.Cancelled && settings.SaveCancelled;
        if (settings.IsEnabled && eligible)
            await store.SaveAsync(factory.Create(result), CancellationToken.None).ConfigureAwait(false);
        return result;
    }
}

public sealed class SnapshotComparer
{
    private const long AbsoluteThreshold = 250L * 1024 * 1024;
    private const long RelativeFloor = 50L * 1024 * 1024;

    public static SnapshotComparison Compare(StorageSnapshot before, StorageSnapshot after)
    {
        var compatibility = Compatibility(before, after);
        if (compatibility.Kind == SnapshotCompatibility.NotComparable)
            return new(before.Id, after.Id, before.CapturedAtUtc, after.CapturedAtUtc, compatibility, [], [], [],
                ["These snapshots are not comparable; no growth conclusion is available."]);

        var metrics = new[]
        {
            Delta("drive.used", before.Drive.UsedBytes, after.Drive.UsedBytes),
            Delta("drive.free", before.Drive.FreeBytes, after.Drive.FreeBytes),
            Delta("observed", before.LogicalBytesObserved, after.LogicalBytesObserved),
            Delta("classified", before.ClassifiedBytes, after.ClassifiedBytes),
            Delta("unknown", before.UnknownBytes, after.UnknownBytes),
            Delta("unaccounted", before.UnaccountedBytes, after.UnaccountedBytes),
            Delta("coverage.files", before.Coverage.FilesObserved, after.Coverage.FilesObserved),
            Delta("coverage.skipped", Skipped(before), Skipped(after))
        };
        var categories = CompareGroups(before.Categories.ToDictionary(x => x.Category.ToString(), x => x.LogicalBytes),
            after.Categories.ToDictionary(x => x.Category.ToString(), x => x.LogicalBytes), "category.");
        var findings = CompareGroups(before.Findings.GroupBy(x => x.RuleId).ToDictionary(x => x.Key, x => x.Sum(y => y.LogicalBytes)),
            after.Findings.GroupBy(x => x.RuleId).ToDictionary(x => x.Key, x => x.Sum(y => y.LogicalBytes)), "finding.");
        var insights = metrics.Concat(categories).Where(x => x.IsSignificant && x.Kind is DeltaKind.Increased or DeltaKind.Decreased)
            .OrderByDescending(x => Abs(x.Change ?? 0)).ThenBy(x => x.Metric, StringComparer.Ordinal)
            .Take(5).Select(x => $"{x.Metric} {x.Kind.ToString().ToLowerInvariant()} by {Abs(x.Change ?? 0)} bytes between snapshots; this is an observation, not a cause.").ToArray();
        return new(before.Id, after.Id, before.CapturedAtUtc, after.CapturedAtUtc, compatibility, metrics, categories, findings,
            insights.Length == 0 ? ["No significant aggregate change crossed the documented threshold."] : insights);
    }

    private static SnapshotCompatibilityResult Compatibility(StorageSnapshot before, StorageSnapshot after)
    {
        var warnings = new List<string>();
        if (before.Drive.IdentityQuality == DriveIdentityQuality.Unavailable || after.Drive.IdentityQuality == DriveIdentityQuality.Unavailable ||
            !string.Equals(before.Drive.Fingerprint, after.Drive.Fingerprint, StringComparison.Ordinal))
            return new(SnapshotCompatibility.NotComparable, ComparisonConfidence.Unavailable, ["Drive identity is unavailable or differs."]);
        if (before.SchemaVersion != after.SchemaVersion)
            return new(SnapshotCompatibility.NotComparable, ComparisonConfidence.Unavailable, ["Snapshot schema versions differ."]);
        if (before.State is not (SnapshotState.Complete or SnapshotState.Partial or SnapshotState.Cancelled) ||
            after.State is not (SnapshotState.Complete or SnapshotState.Partial or SnapshotState.Cancelled))
            return new(SnapshotCompatibility.NotComparable, ComparisonConfidence.Unavailable, ["One snapshot is not displayable."]);
        var kind = SnapshotCompatibility.FullyComparable;
        var confidence = ComparisonConfidence.High;
        if (!string.Equals(before.RulePackDigest, after.RulePackDigest, StringComparison.Ordinal))
        { kind = SnapshotCompatibility.ClassificationAdjusted; confidence = ComparisonConfidence.Medium; warnings.Add("Classification rules changed; category and finding deltas may reflect classification drift."); }
        if (before.Mode != after.Mode || !string.Equals(before.Drive.FileSystem, after.Drive.FileSystem, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(before.ApplicationVersion, after.ApplicationVersion, StringComparison.Ordinal) || CapacityDrift(before, after) || CoverageDrift(before, after))
        { if (kind == SnapshotCompatibility.FullyComparable) kind = SnapshotCompatibility.ComparableWithWarnings; confidence = ComparisonConfidence.Low; warnings.Add("Scan mode, filesystem, or coverage differs; changes are uncertain."); }
        return new(kind, confidence, warnings);
    }

    private static bool CapacityDrift(StorageSnapshot before, StorageSnapshot after) =>
        before.Drive.CapacityBytes.HasValue != after.Drive.CapacityBytes.HasValue ||
        before.Drive.CapacityBytes.HasValue && before.Drive.CapacityBytes != after.Drive.CapacityBytes;
    private static bool CoverageDrift(StorageSnapshot a, StorageSnapshot b)
    {
        var left = a.Coverage.FilesObserved + Skipped(a); var right = b.Coverage.FilesObserved + Skipped(b);
        if (left == 0 || right == 0) return left != right;
        return Math.Abs((double)a.Coverage.FilesObserved / left - (double)b.Coverage.FilesObserved / right) >= 0.1;
    }

    private static long Skipped(StorageSnapshot item) => item.Coverage.InaccessibleEntries + item.Coverage.ReparsePointsSkipped +
        item.Coverage.ChangedEntries + item.Coverage.OtherSkippedEntries;
    private static SnapshotMetricDelta[] CompareGroups(IReadOnlyDictionary<string, long> before,
        IReadOnlyDictionary<string, long> after, string prefix) => before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal).Select(key => Delta(prefix + key, before.GetValueOrDefault(key), after.GetValueOrDefault(key),
            !before.ContainsKey(key), !after.ContainsKey(key))).ToArray();
    private static SnapshotMetricDelta Delta(string metric, long? before, long? after, bool isNew = false, bool removed = false)
    {
        if (!before.HasValue || !after.HasValue) return new(metric, before, after, null, DeltaKind.Uncertain, false);
        var change = SaturatingSubtract(after.Value, before.Value);
        var kind = isNew ? DeltaKind.New : removed ? DeltaKind.NoLongerPresent : change > 0 ? DeltaKind.Increased : change < 0 ? DeltaKind.Decreased : DeltaKind.Unchanged;
        var absolute = Abs(change); var baseline = Abs(before.Value);
        var significant = absolute >= AbsoluteThreshold || absolute >= RelativeFloor && (baseline == 0 || (double)absolute / baseline >= 0.10);
        return new(metric, before, after, change, kind, significant);
    }
    private static long SaturatingSubtract(long after, long before)
    { try { return checked(after - before); } catch (OverflowException) { return after >= before ? long.MaxValue : long.MinValue; } }
    private static long Abs(long value) => value == long.MinValue ? long.MaxValue : Math.Abs(value);
}
