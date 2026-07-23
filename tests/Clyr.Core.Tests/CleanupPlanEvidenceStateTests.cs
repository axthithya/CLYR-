using System.Collections.Immutable;
using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>
/// Phase 6 safety correction: the audit found that a Review Plan built before Administrator Retry could remain
/// valid after Administrator Retry enriched the active result, because retry deliberately preserves ScanId while
/// changing root contributions, coverage, allocation, and findings — exactly the evidence cleanup candidates are
/// built from. These tests guard <see cref="EvidenceState"/> and its binding through
/// <see cref="CleanupPlanBuilder"/>/<see cref="CleanupPlanValidator"/>/<see cref="Clyr.Core.Execution.ExecutionTokenService"/>
/// end to end with real, in-memory objects — no filesystem, IPC, process, or UAC involved anywhere.
/// </summary>
public sealed class CleanupPlanEvidenceStateTests
{
    private static readonly Guid ScanId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private const string DriveIdentity = "drive-fingerprint-evidence";
    private const string RootPath = "C:\\Data\\Alpha";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-23T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void PlanValidatesAgainstTheExactResultItWasCreatedFrom()
    {
        var original = OriginalResult();
        var plan = PlanFor(original, ["a"]);

        var validation = CleanupPlanValidator.Validate(plan, ContextFor(original));

        Assert.True(validation.IsValid);
        Assert.Equal(CleanupPlanStatus.Valid, validation.Status);
    }

    [Fact]
    public void AdministratorRetryEnrichmentChangesTheEvidenceStateIdentity()
    {
        var original = OriginalResult();
        var enriched = EnrichedResult(original);

        Assert.Equal(original.ScanId, enriched.ScanId); // retry deliberately preserves ScanId
        Assert.NotEqual(EvidenceState.ForResult(original), EvidenceState.ForResult(enriched));
    }

    [Fact]
    public void EnrichedResultKeepsTheSameScanIdButInvalidatesTheOldPlan()
    {
        var original = OriginalResult();
        var enriched = EnrichedResult(original);
        var plan = PlanFor(original, ["a"]);

        var validation = CleanupPlanValidator.Validate(plan, ContextFor(enriched));

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, item => item.Code == "plan.evidence-stale");
        // The correction this test guards: ScanId alone must not be why this fails — it still matches.
        Assert.DoesNotContain(validation.Diagnostics, item => item.Code == "plan.scan-stale");
    }

    [Fact]
    public void TheOldPlanReportsStaleNotInvalidOrValid()
    {
        var original = OriginalResult();
        var enriched = EnrichedResult(original);
        var plan = PlanFor(original, ["a"]);

        var status = CleanupPlanValidator.Validate(plan, ContextFor(enriched)).Status;

        Assert.Equal(CleanupPlanStatus.Stale, status);
    }

    [Fact]
    public void ARebuiltPlanValidatesAgainstTheEnrichedResult()
    {
        var original = OriginalResult();
        var enriched = EnrichedResult(original);
        var rebuilt = PlanFor(enriched, ["a"]);

        var validation = CleanupPlanValidator.Validate(rebuilt, ContextFor(enriched));

        Assert.True(validation.IsValid);
    }

    [Fact]
    public void PlanDigestIncludesTheEvidenceStateIdentity()
    {
        var original = OriginalResult();
        var enriched = EnrichedResult(original);
        var beforeRetry = PlanFor(original, ["a"]);
        var afterRetry = PlanFor(enriched, ["a"]);

        Assert.NotEqual(beforeRetry.Digest, afterRetry.Digest);
        Assert.NotEqual(beforeRetry.Binding.EvidenceStateId, afterRetry.Binding.EvidenceStateId);
    }

    [Fact]
    public void LegacyPlanSchemaVersionFailsClosedRatherThanBeingSilentlyAcceptedAsCurrent()
    {
        var original = OriginalResult();
        var plan = PlanFor(original, ["a"]);
        // Simulates a plan built under the pre-correction schema (no evidence-state binding concept at all) —
        // the schema-version bump this correction made is what a validator must reject on, never a silently
        // accepted "current" result just because every other field happens to still line up.
        var legacy = new CleanupPlan(1, plan.Id, plan.ApplicationVersion, plan.Binding, plan.Expiry, plan.Items,
            plan.TotalImpact, plan.Risk, plan.Confidence, plan.Warnings, plan.ExecutionAvailability, plan.Digest);

        var validation = CleanupPlanValidator.Validate(legacy, ContextFor(original));

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, item => item.Code == "plan.schema");
    }

    [Fact]
    public void ReopeningTheSameImmutableResultDoesNotInvalidateThePlan()
    {
        var original = OriginalResult();
        var reloaded = original with { }; // a fresh reference to structurally identical evidence

        Assert.Equal(EvidenceState.ForResult(original), EvidenceState.ForResult(reloaded));
    }

    [Fact]
    public void UiOnlyAndNavigationChangesDoNotAlterEvidenceIdentity()
    {
        var original = OriginalResult();
        // Nothing about page navigation or cosmetic UI state ever reaches ScanResult's evidence fields, so the
        // only faithful way to represent "the user just looked at the same result again" is calling this twice.
        var first = EvidenceState.ForResult(original);
        var second = EvidenceState.ForResult(original);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ExistingExecutionTokenCannotAuthorizeARebuiltPlan()
    {
        var original = OriginalResult();
        var enriched = EnrichedResult(original);
        var oldPlan = PlanFor(original, ["a"]);
        var newPlan = PlanFor(enriched, ["a"]);
        var tokenService = new Clyr.Core.Execution.ExecutionTokenService();
        var sessionId = new Clyr.Contracts.ExecutionSessionId(Guid.NewGuid());
        var token = tokenService.Issue(oldPlan, sessionId, "S-1-5-21-fixture", ["developer.npm.cache"], Now);

        var result = tokenService.Validate(token, newPlan, sessionId, "S-1-5-21-fixture", Now);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void NoAdditionalCleanupActionBecomesExecutable()
    {
        // This correction only tightens staleness detection — it must never widen the execution allowlist.
        Assert.Single(Clyr.Core.Execution.BuiltInExecutionActions.Policy.EnabledActions);
        Assert.NotNull(Clyr.Core.Execution.BuiltInExecutionActions.Find("builtin.clyr-owned-temp-artifacts"));
    }

    private static CleanupPlan PlanFor(ScanResult result, IReadOnlyList<string> selected)
    {
        var candidate = PlanningFixtures.Candidate("a", 100);
        return CleanupPlanBuilder.Create(new(result.ScanId, null, DriveIdentity, "clyr.builtin", "1.1.0",
            "pack-digest", "test", "support-safe", EvidenceState.ForResult(result), Now, [candidate], selected));
    }

    private static PlanValidationContext ContextFor(ScanResult result) => new(Now, result.ScanId, null,
        DriveIdentity, "clyr.builtin", "1.1.0", "pack-digest", CleanupPlanningConstants.CategoryRegistryVersion,
        CleanupPlanningConstants.ApplicationCompatibilityVersion, "support-safe", EvidenceState.ForResult(result),
        ImmutableDictionary<string, CleanupTarget>.Empty);

    private static ScanResult OriginalResult() =>
        ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 1000, driveUsed: 5000)
            with
        { ScanId = ScanId, RootContributions = [Contribution(RootPath, ScanRootEnumerationState.InaccessibleAtRoot, 0, 0, inaccessibleEntries: 12)] };

    /// <summary>Runs the real, production Administrator Retry reconciliation and enrichment pipeline — the exact
    /// same code path <c>AppSessionViewModel.ApplyEnrichedResult</c> receives — against a synthetic elevated
    /// response, so "Administrator Retry enrichment" in these tests means the real thing, not a hand-rolled
    /// stand-in.</summary>
    private static ScanResult EnrichedResult(ScanResult original)
    {
        var root = new PermissionLimitedRoot(RootPath, ScanId, DriveIdentity, null, PermissionLimitedReasonCode.AccessDenied);
        var roots = ImmutableArray.Create(root);
        var manifest = ElevatedScanManifestBuilder.Build(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots, ScanId, DriveIdentity, roots);
        var request = new ElevatedScanRetryRequest(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots,
            new string('a', ElevatedScanRetryProtocol.MinNonceLength), Now, Now.AddMinutes(1), ScanId, DriveIdentity,
            manifest.Value!.Digest, roots, 16);
        var rootResult = new ElevatedRootRetryResult(RootPath, null, ElevatedRootRetryOutcome.Completed, 20, 4, 500, 300, 300, 0, 0, 0, 0);
        var response = new ElevatedScanRetryResponse(request.ProtocolVersion, request.Nonce, ElevatedScanRetryOutcome.Completed,
            Now, Now.AddSeconds(1), 1, 1, 0, 10, 2, 1000, 800, 800, 0, 0, 0, [], [rootResult]);
        var launcherResult = new ElevatedScannerLauncherResult(ElevatedScannerLauncherOutcome.Completed, response);
        var reconciliation = ElevatedScanResultReconciler.Reconcile(original, request, launcherResult, new FixedClock(Now));
        Assert.True(reconciliation.IsApplied);
        return ElevatedScanResultEnricher.Build(reconciliation);
    }

    private static ScanRootContribution Contribution(string path, ScanRootEnumerationState state, long logicalBytes,
        long allocatedBytes, long inaccessibleEntries = 0, long files = 1) =>
        new(ElevatedScanManifestBuilder.NormalizePath(path), null, path, state, files, 0, logicalBytes, allocatedBytes,
            allocatedBytes, 0, 0, 0, 0, inaccessibleEntries, 0, 0);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
