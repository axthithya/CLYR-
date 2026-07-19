using System.Collections.Immutable;
using Clyr.Contracts;

namespace Clyr.Core;

/// <summary>
/// Builds the single, active, enriched <see cref="ScanResult"/> a successful administrator retry produces —
/// pure, in-memory, no I/O, no mutation of any input. This is the missing half of Phase 7.2.6G's reconciliation
/// work: <see cref="ElevatedScanResultReconciler.Reconcile"/> already computes a safely deduplicated combined
/// byte total, but nothing previously turned that into a new, fully consistent <see cref="ScanResult"/> the rest
/// of the application could actually display — the original scan's own <see cref="ScanResult"/> object kept
/// being shown, unchanged, even after a successful retry. <see cref="Build"/> closes that gap.
/// <para/>
/// The enriched result keeps the original scan's identity (<see cref="ScanResult.ScanId"/>, root, file system,
/// start time) and completion metadata (end time, failure fields) exactly as they were — this is the same
/// completed Deep Analysis, now with more of it successfully observed, not a new scan. Every count derived from
/// the retry is the reconciler's own already-deduplicated per-root delta (see
/// <see cref="AppliedRootAccountingDelta"/>) — this never re-adds the elevated engine's raw totals a second time.
/// <para/>
/// The elevated retry protocol is deliberately aggregate-only per root (see <see cref="ElevatedRootRetryResult"/>)
/// — it carries no per-file paths, no per-file classification, and no per-file ranking data. Recomputing
/// contributor/finding/largest-file rankings with the same fidelity a real per-file scan would require is
/// therefore not honestly possible from this data, and this method does not pretend otherwise:
/// <see cref="ScanResult.LargestFiles"/> and <see cref="ClassificationResult.Categories"/>/
/// <see cref="ClassificationResult.Findings"/> are left exactly as the original scan produced them (nothing new
/// was ever individually classified), while newly retried bytes are truthfully folded into
/// <see cref="ClassificationCoverage.UnknownBytes"/>/<see cref="ClassificationCoverage.UnknownFiles"/> — so
/// classification percentages correctly reflect "more is now observed, but this specific batch has not been
/// categorized" rather than silently inflating a category that never actually classified it.
/// <see cref="ScanResult.TopLevelDirectories"/> and <see cref="ScanResult.LargestDirectories"/> — both keyed by
/// directory path, matching exactly the aggregate root-level data the retry protocol does carry — are updated
/// precisely where a retried root corresponds to an existing entry.
/// </summary>
public static class ElevatedScanResultEnricher
{
    public static ScanResult Build(ElevatedReconciliationResult reconciliation)
    {
        if (!reconciliation.IsApplied) return reconciliation.OriginalResult;

        var original = reconciliation.OriginalResult;
        var attempt = reconciliation.Attempt;
        var deltas = reconciliation.AppliedRootDeltas;
        if (deltas.IsDefaultOrEmpty) return original;

        var deltaFiles = deltas.Sum(delta => delta.DeltaFilesExamined);
        var deltaDirectories = deltas.Sum(delta => delta.DeltaDirectoriesExamined);
        var resolvedInaccessible = deltas.Sum(delta => delta.OriginalInaccessibleEntryCount);

        var newLogicalBytes = original.LogicalBytesObserved + attempt.AdditionalLogicalBytes;
        var newFiles = original.Coverage.FilesObserved + deltaFiles;
        var newDirectories = original.Coverage.DirectoriesObserved + deltaDirectories;
        var newInaccessible = Math.Max(0, original.Coverage.InaccessibleEntries - resolvedInaccessible);
        var newCoverage = original.Coverage with
        {
            FilesObserved = newFiles,
            DirectoriesObserved = newDirectories,
            InaccessibleEntries = newInaccessible,
        };

        var newIssues = ReduceAccessDeniedIssues(original.Issues, resolvedInaccessible);
        var newAllocation = MergeAllocation(original.Allocation, attempt.AdditionalAllocatedBytes, deltas);
        var newClassification = MergeClassification(original.Classification, attempt.AdditionalLogicalBytes,
            deltaFiles, resolvedInaccessible, original.DriveUsedBytes, newLogicalBytes);
        var newRootContributions = MergeRootContributions(original.RootContributions, deltas);
        var newTopLevelDirectories = MergeRankedPaths(original.TopLevelDirectories, deltas, growBeyondOriginalCount: false);
        var newLargestDirectories = MergeRankedPaths(original.LargestDirectories, deltas, growBeyondOriginalCount: true);

        long? newUnaccounted = original.DriveUsedBytes.HasValue ? original.DriveUsedBytes.Value - newLogicalBytes : null;
        var newStatus = GenuineWarningCount(newIssues) > 0 ? ScanStatus.CompletedWithWarnings : ScanStatus.Completed;

        return original with
        {
            Status = newStatus,
            LogicalBytesObserved = newLogicalBytes,
            UnaccountedBytes = newUnaccounted,
            Coverage = newCoverage,
            Issues = newIssues,
            Classification = newClassification,
            Allocation = newAllocation,
            RootContributions = newRootContributions,
            TopLevelDirectories = newTopLevelDirectories,
            LargestDirectories = newLargestDirectories,
        };
    }

    private static long GenuineWarningCount(IReadOnlyList<ScanIssueSummary> issues) => issues
        .Where(issue => issue.Severity is ScanIssueSeverity.AccessWarning or ScanIssueSeverity.PermissionLimited
            or ScanIssueSeverity.DataChanged or ScanIssueSeverity.Fatal)
        .Sum(issue => issue.Count);

    /// <summary>Reduces the aggregate access-denied issue by exactly the number of previously-inaccessible entries
    /// the applied retry resolved — removing the issue entirely once its count reaches zero — rather than leaving
    /// a stale warning count for entries that are, truthfully, no longer inaccessible.</summary>
    private static IReadOnlyList<ScanIssueSummary> ReduceAccessDeniedIssues(IReadOnlyList<ScanIssueSummary> original, long resolvedInaccessible)
    {
        if (resolvedInaccessible <= 0) return original;
        return ReduceAccessDeniedIssuesCore(original, resolvedInaccessible);
    }

    private static ScanIssueSummary[] ReduceAccessDeniedIssuesCore(IReadOnlyList<ScanIssueSummary> original, long resolvedInaccessible)
    {
        var result = new List<ScanIssueSummary>(original.Count);
        var remaining = resolvedInaccessible;
        foreach (var issue in original)
        {
            if (issue.Code != "scan.access-denied" || remaining <= 0) { result.Add(issue); continue; }
            var reduceBy = Math.Min(remaining, issue.Count);
            remaining -= reduceBy;
            var newCount = issue.Count - reduceBy;
            if (newCount > 0) result.Add(issue with { Count = newCount });
        }
        return result.ToArray();
    }

    private static AllocationAccounting? MergeAllocation(AllocationAccounting? original, long additionalAllocated,
        ImmutableArray<AppliedRootAccountingDelta> deltas)
    {
        if (original is null) return null;
        return original with
        {
            AllocatedBytesObserved = original.AllocatedBytesObserved + additionalAllocated,
            // Best available estimate, not a proven exact figure: these retried roots were previously totally or
            // partially inaccessible to the original scan, so their content could not already be part of the
            // original's unique-allocation set — see AccountingConsistency.CrossScanIdentityReconciliationUnavailable
            // (already set on the reconciliation result) for the one known residual risk this does not cover: a
            // hard link between a retried root and content the original scan observed elsewhere.
            UniqueAllocatedBytesObserved = original.UniqueAllocatedBytesObserved + additionalAllocated,
            FilesWithUnavailableAllocatedSize = original.FilesWithUnavailableAllocatedSize + deltas.Sum(delta => delta.DeltaAllocationUnavailableCount),
            SparseFileCount = original.SparseFileCount + deltas.Sum(delta => delta.DeltaSparseFileCount),
            CompressedFileCount = original.CompressedFileCount + deltas.Sum(delta => delta.DeltaCompressedFileCount),
            VisibleHardLinkEntries = original.VisibleHardLinkEntries + deltas.Sum(delta => delta.DeltaHardLinkEntriesDetected),
            Consistency = original.Consistency | AccountingConsistency.CrossScanIdentityReconciliationUnavailable,
        };
    }

    private static ClassificationResult? MergeClassification(ClassificationResult? original, long additionalLogicalBytes,
        long additionalFiles, long resolvedInaccessible, long? driveUsedBytes, long newLogicalBytesObserved)
    {
        if (original is null) return null;
        var coverage = original.Coverage;
        var newUnknownBytes = coverage.UnknownBytes + additionalLogicalBytes;
        var newUnknownFiles = coverage.UnknownFiles + additionalFiles;
        var newCoverageInaccessible = Math.Max(0, coverage.InaccessibleEntries - resolvedInaccessible);
        var newCoverage = coverage with
        {
            UnknownBytes = newUnknownBytes,
            UnknownFiles = newUnknownFiles,
            InaccessibleEntries = newCoverageInaccessible,
            UnaccountedDriveBytes = driveUsedBytes.HasValue ? driveUsedBytes.Value - newLogicalBytesObserved : null,
        };
        var limitations = additionalLogicalBytes > 0
            ? original.Limitations.Append(
                "Storage found by administrator retry is included in observed totals but has not been individually classified.").ToArray()
            : original.Limitations;
        return original with { Coverage = newCoverage, Limitations = limitations };
    }

    private static ScanRootContribution[] MergeRootContributions(IReadOnlyList<ScanRootContribution> original,
        ImmutableArray<AppliedRootAccountingDelta> deltas)
    {
        var byPath = deltas.ToDictionary(delta => delta.CanonicalRootIdentity, StringComparer.Ordinal);
        var result = new List<ScanRootContribution>(original.Count);
        foreach (var contribution in original)
        {
            if (!byPath.TryGetValue(contribution.CanonicalRootIdentity, out var delta)) { result.Add(contribution); continue; }
            result.Add(contribution with
            {
                EnumerationState = ScanRootEnumerationState.Completed,
                FilesExamined = contribution.FilesExamined + delta.DeltaFilesExamined,
                DirectoriesExamined = contribution.DirectoriesExamined + delta.DeltaDirectoriesExamined,
                LogicalBytesObserved = contribution.LogicalBytesObserved + delta.DeltaLogicalBytes,
                AllocatedBytesObserved = contribution.AllocatedBytesObserved + delta.DeltaAllocatedBytes,
                UniqueAllocatedBytesObservedWithinRoot = contribution.UniqueAllocatedBytesObservedWithinRoot + delta.DeltaAllocatedBytes,
                HardLinkEntriesDetected = contribution.HardLinkEntriesDetected + delta.DeltaHardLinkEntriesDetected,
                AllocationUnavailableCount = contribution.AllocationUnavailableCount + delta.DeltaAllocationUnavailableCount,
                SparseFileCount = contribution.SparseFileCount + delta.DeltaSparseFileCount,
                CompressedFileCount = contribution.CompressedFileCount + delta.DeltaCompressedFileCount,
                InaccessibleEntryCount = 0,
            });
        }
        return result.ToArray();
    }

    /// <summary>Updates an existing ranked-path entry whose path matches a retried root with its delta, or —
    /// only when <paramref name="growBeyondOriginalCount"/> is true (used for <see cref="ScanResult.LargestDirectories"/>,
    /// never <see cref="ScanResult.TopLevelDirectories"/>) — inserts the retried root itself as a brand-new entry,
    /// since a previously totally-inaccessible root could not have appeared in a byte-ranked list before. The
    /// merged list is always re-sorted and capped at the original count plus the (also bounded) number of applied
    /// roots, so this can never grow the list unboundedly.</summary>
    private static IReadOnlyList<RankedPath> MergeRankedPaths(IReadOnlyList<RankedPath> original,
        ImmutableArray<AppliedRootAccountingDelta> deltas, bool growBeyondOriginalCount)
    {
        if (deltas.IsDefaultOrEmpty || original.Count == 0 && !growBeyondOriginalCount) return original;
        // Keyed by the same normalized identity the retry protocol correlates roots by — never the raw display
        // path, which can vary in case/trailing separator and would otherwise let the same directory appear twice.
        var byNormalizedPath = new Dictionary<string, RankedPath>(StringComparer.Ordinal);
        foreach (var path in original) byNormalizedPath[ElevatedScanManifestBuilder.NormalizePath(path.DisplayPath)] = path;

        foreach (var delta in deltas)
        {
            if (delta.DeltaLogicalBytes == 0 && delta.DeltaFilesExamined == 0) continue;
            if (byNormalizedPath.TryGetValue(delta.CanonicalRootIdentity, out var existing))
            {
                byNormalizedPath[delta.CanonicalRootIdentity] = existing with
                {
                    LogicalBytes = existing.LogicalBytes + delta.DeltaLogicalBytes,
                    FileCount = existing.FileCount + delta.DeltaFilesExamined,
                };
            }
            else if (growBeyondOriginalCount)
            {
                byNormalizedPath[delta.CanonicalRootIdentity] = new(delta.DisplayRootPath, delta.RootResult.LogicalBytesObserved,
                    delta.RootResult.FilesExamined, MeasurementPrecision.Estimated);
            }
        }

        // Bounded, never unbounded: room for the original entries plus every applied root (each of which is
        // itself bounded by ElevatedScanRetryProtocol.MaxRoots) — never the raw new dictionary size across
        // repeated retries, and never so tight a cap that a newly applied root evicts an original entry.
        var cap = original.Count + deltas.Length;
        return byNormalizedPath.Values.OrderByDescending(path => path.LogicalBytes).Take(cap).ToArray();
    }
}
