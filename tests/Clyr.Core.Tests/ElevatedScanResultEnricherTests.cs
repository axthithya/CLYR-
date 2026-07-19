using System.Collections.Immutable;
using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>
/// Phase (Administrator Retry result-integration correction): the enriched <see cref="ScanResult"/> a successful
/// retry produces must actually replace what the application displays, must never double-count anything the
/// original scan already observed, and must never fabricate per-file detail the elevated retry protocol does not
/// carry. No filesystem, IPC, process, or UAC involved — every fixture is an in-memory typed value.
/// </summary>
public sealed class ElevatedScanResultEnricherTests
{
    private static readonly Guid ScanId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private const string DriveIdentity = "drive-fingerprint-enricher";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-19T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private const string RootPath = "C:\\Data\\Alpha";

    [Fact]
    public void NotAppliedReconciliationReturnsTheOriginalResultUnchanged()
    {
        var original = OriginalResult(1000, [], driveUsed: 5000);
        var notApplied = new ElevatedReconciliationResult(ElevatedReconciliationOutcome.Denied, original,
            Attempt(applied: false), [], [], null, null, AccountingConsistency.Consistent);

        var enriched = ElevatedScanResultEnricher.Build(notApplied);

        Assert.Same(original, enriched);
    }

    [Fact]
    public void SuccessfulRetryIncreasesObservedBytesByTheDeduplicatedDeltaOnly()
    {
        var request = BuildRequest(RootFixture(RootPath));
        var original = OriginalResult(1000, [Contribution(RootPath, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0, inaccessibleEntries: 12)], driveUsed: 5000);
        var response = ResponseFor(request, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300, files: 20, directories: 4)]);
        var reconciliation = Reconcile(original, request, response);

        var enriched = ElevatedScanResultEnricher.Build(reconciliation);

        Assert.Equal(1500, enriched.LogicalBytesObserved);
        Assert.Equal(1000, original.LogicalBytesObserved); // original untouched
        Assert.Equal(ScanId, enriched.ScanId); // same scan identity retained
        Assert.Equal(original.Root, enriched.Root);
        Assert.Equal(original.StartedAt, enriched.StartedAt);
        Assert.Equal(original.EndedAt, enriched.EndedAt);
    }

    [Fact]
    public void OverlappingRetryBytesAlreadyCountedByTheOriginalScanDoNotIncreaseObservedBytesTwice()
    {
        // Alpha was PartiallyObserved with 200 bytes already counted; the elevated engine reports 200 for the
        // same root (i.e., it found nothing new) — the applied delta must be zero.
        var request = BuildRequest(RootFixture(RootPath));
        var original = OriginalResult(1000, [Contribution(RootPath, ScanRootEnumerationState.PartiallyObserved, 200, 100, files: 3)], driveUsed: 5000);
        var response = ResponseFor(request, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 200, 100, files: 3, directories: 0)]);
        var reconciliation = Reconcile(original, request, response);

        var enriched = ElevatedScanResultEnricher.Build(reconciliation);

        Assert.Equal(1000, enriched.LogicalBytesObserved);
        Assert.Equal(0L, reconciliation.Attempt.AdditionalLogicalBytes);
    }

    [Fact]
    public void DirectoryEntryForARetriedRootIsNotAddedTwiceAcrossTopLevelAndLargestDirectories()
    {
        var request = BuildRequest(RootFixture(RootPath));
        var original = OriginalResult(1000, [Contribution(RootPath, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0)], driveUsed: 50_000) with
        {
            TopLevelDirectories = [new(RootPath, 0, 0, MeasurementPrecision.Estimated)],
            LargestDirectories = [new("C:\\Data\\Other", 900, 5, MeasurementPrecision.Estimated)],
        };
        var response = ResponseFor(request, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300, files: 10, directories: 2)]);
        var reconciliation = Reconcile(original, request, response);

        var enriched = ElevatedScanResultEnricher.Build(reconciliation);

        Assert.Single(enriched.TopLevelDirectories, item => item.DisplayPath == RootPath);
        Assert.Equal(500, enriched.TopLevelDirectories.Single(item => item.DisplayPath == RootPath).LogicalBytes);
        Assert.Single(enriched.LargestDirectories, item => item.DisplayPath == RootPath);
        Assert.Equal(500, enriched.LargestDirectories.Single(item => item.DisplayPath == RootPath).LogicalBytes);
    }

    [Fact]
    public void DuplicateFindingsAreNotCreatedByEnrichment()
    {
        var classification = Classification(classifiedBytes: 600, unknownBytes: 400, findingsCount: 3);
        var request = BuildRequest(RootFixture(RootPath));
        var original = OriginalResult(1000, [Contribution(RootPath, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0)], driveUsed: 5000, classification: classification);
        var response = ResponseFor(request, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300)]);
        var reconciliation = Reconcile(original, request, response);

        var enriched = ElevatedScanResultEnricher.Build(reconciliation);

        Assert.Equal(3, enriched.Classification!.Findings.Count);
        Assert.Equal(classification.Findings, enriched.Classification.Findings);
    }

    [Fact]
    public void DuplicateCategoryTotalsAreNotCreatedByEnrichmentAndNewBytesLandInUnknown()
    {
        var classification = Classification(classifiedBytes: 600, unknownBytes: 400, findingsCount: 1);
        var request = BuildRequest(RootFixture(RootPath));
        var original = OriginalResult(1000, [Contribution(RootPath, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0)], driveUsed: 5000, classification: classification);
        var response = ResponseFor(request, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300)]);
        var reconciliation = Reconcile(original, request, response);

        var enriched = ElevatedScanResultEnricher.Build(reconciliation);

        // Category list itself is untouched (nothing new was individually classified)...
        Assert.Equal(classification.Categories, enriched.Classification!.Categories);
        // ...but the newly observed bytes are truthfully folded into Unknown, not silently dropped or double-added
        // to an existing classified category.
        Assert.Equal(600, enriched.Classification.Coverage.ClassifiedBytes);
        Assert.Equal(900, enriched.Classification.Coverage.UnknownBytes);
        Assert.Contains(enriched.Classification.Limitations, item => item.Contains("administrator retry", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LogicalBytesExceedingDriveUsedIsALegitimateBasisDifferenceNotARejection()
    {
        // Original: 4900 observed of 5000 used (consistent). Retry root reports 500 new bytes, which pushes
        // combined observed to 5400 — over the drive's own used-space basis. This is a legitimate logical-vs-
        // physical basis difference (hard links, sparse files, compression) — exactly the same condition the
        // original scan itself never rejects (see AccountingConsistency.LogicalExceedsDriveUsed) — so the retry
        // must still apply, flagging the condition rather than discarding real, already-deduplicated coverage.
        var request = BuildRequest(RootFixture(RootPath));
        var original = OriginalResult(4900, [Contribution(RootPath, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0)], driveUsed: 5000);
        var response = ResponseFor(request, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300)]);
        var reconciliation = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.Equal(ElevatedReconciliationOutcome.Applied, reconciliation.Outcome);
        Assert.True(reconciliation.IsApplied);
        Assert.Equal(5400, reconciliation.CombinedLogicalBytesObserved);
        Assert.True(reconciliation.Consistency.HasFlag(AccountingConsistency.LogicalExceedsDriveUsed));
        Assert.Same(original, reconciliation.OriginalResult);
        Assert.Equal(4900, original.LogicalBytesObserved); // original genuinely untouched
    }

    [Fact]
    public void ACompleteRootReportingFewerBytesThanAlreadyObservedIsALegitimateReplacementNotARejection()
    {
        // Phase (root-reconciliation correction): Alpha was PartiallyObserved with 400 bytes already counted;
        // the elevated engine's Completed (fully, authoritatively re-enumerated) result now reports only 100 for
        // the same root — fewer than before. This is a legitimate Replacement (files deleted, a cache cleared,
        // logs rotated between the original scan and this retry), never a rejection: Completed already proves
        // the elevated figure is the current, authoritative truth for this root.
        var request = BuildRequest(RootFixture(RootPath));
        var original = OriginalResult(1000, [Contribution(RootPath, ScanRootEnumerationState.PartiallyObserved, 400, 300)], driveUsed: 5000);
        var response = ResponseFor(request, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 100, 50)]);
        var reconciliation = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.Equal(ElevatedReconciliationOutcome.Applied, reconciliation.Outcome);
        Assert.True(reconciliation.IsApplied);
        // 1000 (whole original, including Alpha's partial 400) - 400 (Alpha's own partial contribution) + 100
        // (the elevated engine's complete, authoritative figure for Alpha) = 700.
        Assert.Equal(700, reconciliation.CombinedLogicalBytesObserved);
        Assert.Equal(RootReconciliationMode.Replacement, Assert.Single(reconciliation.AppliedRootDeltas).Mode);
        Assert.Equal(-300, reconciliation.Attempt.ReplacementNetLogicalBytes);
        Assert.Equal(0, reconciliation.Attempt.AdditiveLogicalBytes);
        Assert.Equal(1000, original.LogicalBytesObserved); // original genuinely untouched
    }

    [Fact]
    public void AnAdditiveRootReportingNegativeBytesRemainsRejectedAsGenuinelyImpossible()
    {
        // Unlike Replacement, an Additive root (InaccessibleAtRoot originally — contributed zero by construction)
        // can never legitimately report negative bytes; the elevated engine's own counters are never negative.
        // This defensive guard only ever fires against a malformed/hostile response, but must still hold.
        var request = BuildRequest(RootFixture(RootPath));
        var original = OriginalResult(1000, [Contribution(RootPath, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0)], driveUsed: 5000);
        var response = ResponseFor(request, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, -100, -50)]);
        var reconciliation = ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

        Assert.Equal(ElevatedReconciliationOutcome.AccountingBasisMismatch, reconciliation.Outcome);
        Assert.False(reconciliation.IsApplied);
        Assert.Null(reconciliation.CombinedLogicalBytesObserved);
        Assert.Equal(1000, original.LogicalBytesObserved); // original genuinely untouched
    }

    [Fact]
    public void NotObservedBytesNeverBecomeNegative()
    {
        var request = BuildRequest(RootFixture(RootPath));
        var original = OriginalResult(4000, [Contribution(RootPath, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0)], driveUsed: 5000);
        var response = ResponseFor(request, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300)]);
        var reconciliation = Reconcile(original, request, response);

        var enriched = ElevatedScanResultEnricher.Build(reconciliation);

        Assert.True(enriched.UnaccountedBytes is null or >= 0);
        Assert.Equal(5000 - 4500, enriched.UnaccountedBytes);
    }

    [Fact]
    public void CoverageRemainsValidAfterEnrichment()
    {
        var request = BuildRequest(RootFixture(RootPath));
        var original = OriginalResult(1000, [Contribution(RootPath, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0, inaccessibleEntries: 7)], driveUsed: 5000);
        var response = ResponseFor(request, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300, files: 9, directories: 2)]);
        var reconciliation = Reconcile(original, request, response);

        var enriched = ElevatedScanResultEnricher.Build(reconciliation);

        Assert.True(enriched.Coverage.FilesObserved >= original.Coverage.FilesObserved);
        Assert.True(enriched.Coverage.DirectoriesObserved >= original.Coverage.DirectoriesObserved);
        Assert.True(enriched.Coverage.InaccessibleEntries >= 0);
        Assert.Equal(Math.Max(0, original.Coverage.InaccessibleEntries - 7), enriched.Coverage.InaccessibleEntries);
    }

    [Fact]
    public void RemainingInaccessibleRootsAreReflectedInRootContributionsAfterEnrichment()
    {
        const string secondRoot = "C:\\Data\\Beta";
        var request = BuildRequest(RootFixture(RootPath));
        var original = OriginalResult(1000, [
            Contribution(RootPath, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0),
            Contribution(secondRoot, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0),
        ], driveUsed: 50_000);
        var response = ResponseFor(request, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300)]);
        var reconciliation = Reconcile(original, request, response);

        var enriched = ElevatedScanResultEnricher.Build(reconciliation);

        Assert.Equal(ScanRootEnumerationState.Completed, enriched.RootContributions.Single(item => item.CanonicalRootIdentity == ElevatedScanManifestBuilder.NormalizePath(RootPath)).EnumerationState);
        // Beta was never part of this retry request at all — its own contribution record is untouched.
        Assert.Equal(ScanRootEnumerationState.InaccessibleAtRoot, enriched.RootContributions.Single(item => item.CanonicalRootIdentity == ElevatedScanManifestBuilder.NormalizePath(secondRoot)).EnumerationState);
    }

    [Fact]
    public void AccessDeniedWarningCountDecreasesByResolvedEntriesAndScanCompletesWithoutWarningsWhenNoneRemain()
    {
        var request = BuildRequest(RootFixture(RootPath));
        var original = OriginalResult(1000, [Contribution(RootPath, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0, inaccessibleEntries: 5)], driveUsed: 5000) with
        {
            Status = ScanStatus.CompletedWithWarnings,
            Issues = [new(ScanIssueKind.AccessDenied, "scan.access-denied", 5, "An entry was inaccessible.", ScanIssueSeverity.PermissionLimited)],
        };
        var response = ResponseFor(request, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300)]);
        var reconciliation = Reconcile(original, request, response);

        var enriched = ElevatedScanResultEnricher.Build(reconciliation);

        Assert.DoesNotContain(enriched.Issues, item => item.Code == "scan.access-denied");
        Assert.Equal(ScanStatus.Completed, enriched.Status);
    }

    [Fact]
    public void AccessDeniedWarningCountIsReducedNotEliminatedWhenOnlySomeEntriesAreResolved()
    {
        const string secondRoot = "C:\\Data\\Beta";
        var request = BuildRequest(RootFixture(RootPath));
        var original = OriginalResult(1000, [
            Contribution(RootPath, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0, inaccessibleEntries: 5),
            Contribution(secondRoot, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0, inaccessibleEntries: 9),
        ], driveUsed: 50_000) with
        {
            Status = ScanStatus.CompletedWithWarnings,
            Issues = [new(ScanIssueKind.AccessDenied, "scan.access-denied", 14, "An entry was inaccessible.", ScanIssueSeverity.PermissionLimited)],
        };
        var response = ResponseFor(request, [RootResult(RootPath, ElevatedRootRetryOutcome.Completed, 500, 300)]);
        var reconciliation = Reconcile(original, request, response);

        var enriched = ElevatedScanResultEnricher.Build(reconciliation);

        var issue = Assert.Single(enriched.Issues, item => item.Code == "scan.access-denied");
        Assert.Equal(9, issue.Count);
        Assert.Equal(ScanStatus.CompletedWithWarnings, enriched.Status);
    }

    private static ScanResult OriginalResult(long observed, IReadOnlyList<ScanRootContribution> contributions,
        long? driveUsed = null, ClassificationResult? classification = null) =>
        ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: observed, driveUsed: driveUsed, classification: classification)
            with
        { ScanId = ScanId, RootContributions = contributions };

    private static ClassificationResult Classification(long classifiedBytes, long unknownBytes, int findingsCount)
    {
        var categories = ImmutableArray.Create(new CategorySummary(StorageCategory.UserDocuments, classifiedBytes, 4, MeasurementPrecision.Estimated, FindingStatus.Informational));
        var findings = Enumerable.Range(0, findingsCount)
            .Select(index => new StorageFinding($"finding-{index}", "rule-1", "1.0", "pack", "1.0", "digest", "Title", StorageCategory.UserDocuments,
                [], FindingConfidence.High, FindingStatus.Informational, 100, 1, MeasurementPrecision.Estimated,
                new("why", "what", "safe", "evidence", [])))
            .ToImmutableArray();
        var coverage = new ClassificationCoverage(4, classifiedBytes, 2, unknownBytes, 0, 0, null);
        return new(categories, findings, coverage, new("pack", "1.0", "digest", RulePackTrust.BuiltIn, true, true, 10, "ok", "ok", "builtin", "MIT"),
            "Summary", []);
    }

    private static ScanRootContribution Contribution(string path, ScanRootEnumerationState state, long logicalBytes, long allocatedBytes,
        long inaccessibleEntries = 0, long files = 1) =>
        new(ElevatedScanManifestBuilder.NormalizePath(path), null, path, state, files, 0, logicalBytes, allocatedBytes, allocatedBytes, 0, 0, 0, 0, inaccessibleEntries, 0, 0);

    private static PermissionLimitedRoot RootFixture(string path) =>
        new(path, ScanId, DriveIdentity, null, PermissionLimitedReasonCode.AccessDenied);

    private static ElevatedRootRetryResult RootResult(string path, ElevatedRootRetryOutcome outcome, long logicalBytes = 0,
        long allocatedBytes = 0, long files = 1, long directories = 0) =>
        new(path, null, outcome, files, directories, logicalBytes, allocatedBytes, allocatedBytes, 0, 0, 0, 0);

    private static ElevatedScanRetryRequest BuildRequest(PermissionLimitedRoot root)
    {
        var roots = ImmutableArray.Create(root);
        var manifest = ElevatedScanManifestBuilder.Build(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots, ScanId, DriveIdentity, roots);
        return new ElevatedScanRetryRequest(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots,
            new string('a', ElevatedScanRetryProtocol.MinNonceLength), Now, Now.AddMinutes(1), ScanId, DriveIdentity,
            manifest.Value!.Digest, roots, 16);
    }

    private static ElevatedScanRetryResponse ResponseFor(ElevatedScanRetryRequest request, ImmutableArray<ElevatedRootRetryResult> rootResults) =>
        new(request.ProtocolVersion, request.Nonce, ElevatedScanRetryOutcome.Completed, Now, Now.AddSeconds(1), request.PermissionLimitedRoots.Length,
            rootResults.Count(item => item.Outcome == ElevatedRootRetryOutcome.Completed),
            rootResults.Count(item => item.Outcome != ElevatedRootRetryOutcome.Completed),
            10, 2, 1000, 800, 800, 0, 0, 0, [], rootResults);

    private static ElevatedScannerLauncherResult Completed(ElevatedScanRetryResponse response) => new(ElevatedScannerLauncherOutcome.Completed, response);

    private static ElevatedReconciliationResult Reconcile(ScanResult original, ElevatedScanRetryRequest request, ElevatedScanRetryResponse response) =>
        ElevatedScanResultReconciler.Reconcile(original, request, Completed(response), new FixedClock(Now));

    private static ElevatedRetryAttempt Attempt(bool applied) =>
        new(Guid.NewGuid(), ScanId, DriveIdentity, "nonce", Now, Now, ElevatedScannerLauncherOutcome.Completed,
            ElevatedScanRetryOutcome.Completed, 0, 0, 0, 0, 0, [], applied);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
