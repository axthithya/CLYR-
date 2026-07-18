using System.Linq;
using Clyr.Contracts;

namespace Clyr.Core;

/// <summary>Narrow, testable abstraction exposing only the pure eligibility decision (never a launch, never a
/// nonce, never a manifest). The real production implementation is <see cref="ElevatedScanRetryRequestFactory"/>,
/// which already owns this exact decision — see <see cref="ElevatedScanRetryRequestFactory.EvaluateEligibility"/>.</summary>
public interface IElevatedScanRetryEligibilityEvaluator
{
    ElevatedScanRetryEligibilityResult EvaluateEligibility(ScanResult result);
}

/// <summary>
/// A small, immutable, bounded snapshot of whether a completed scan qualifies for an elevated permission-limited
/// root retry, and roughly how much work that retry would cover. Carries no raw path, no unrestricted diagnostic,
/// no manifest, no request nonce, and no executable or process information — only plain counts and a fixed,
/// non-identifying status key a caller can map to user-facing text.
/// </summary>
public sealed record ElevatedScanRetryAvailability(
    bool IsEligible, ElevatedScanRetryEligibilityOutcome EligibilityOutcome,
    int ReplaceableRootCount, int PermissionLimitedRootCount, string SafeStatusMessageKey);

/// <summary>
/// The one app-facing surface for the elevated permission-limited-root retry feature. Accepts only the original
/// typed <see cref="ScanResult"/> (and, for <see cref="RetryAsync"/>, a <see cref="CancellationToken"/>) — no
/// path, root, executable name, pipe name, launch plan, manifest, nonce, command argument, or other user-entered
/// value can ever reach this class from a caller. <see cref="Evaluate"/> performs no launch, no IPC, generates no
/// UAC prompt, enumerates no filesystem, modifies no result, and creates no scan history — it only asks the
/// already-existing, already-reviewed eligibility logic what it would decide. <see cref="RetryAsync"/> delegates
/// exactly once to the already-completed <see cref="ElevatedScanRetryCoordinator"/>, duplicating none of its
/// workflow logic.
/// </summary>
public interface IElevatedScanRetryService
{
    ElevatedScanRetryAvailability Evaluate(ScanResult originalResult);

    Task<ElevatedScanRetryWorkflowResult> RetryAsync(ScanResult originalResult, CancellationToken cancellationToken);
}

public sealed class ElevatedScanRetryService(IElevatedScanRetryEligibilityEvaluator eligibilityEvaluator, ElevatedScanRetryCoordinator coordinator)
    : IElevatedScanRetryService
{
    public ElevatedScanRetryAvailability Evaluate(ScanResult originalResult)
    {
        var eligibility = eligibilityEvaluator.EvaluateEligibility(originalResult);
        // A truthful count of every top-level root that looks permission-limited at all, independent of whether
        // the final, validated request set could actually include it (duplicates, overlaps, or an outside-drive
        // root are still counted here, then excluded from EligibleRoots by the eligibility check itself).
        var permissionLimitedRootCount = originalResult.RootContributions.Count(ElevatedScanRetryEligibility.IsExactReplaceable);
        return new ElevatedScanRetryAvailability(eligibility.IsEligible, eligibility.Outcome,
            eligibility.EligibleRoots.Length, permissionLimitedRootCount, StatusMessageKeyFor(eligibility.Outcome));
    }

    public Task<ElevatedScanRetryWorkflowResult> RetryAsync(ScanResult originalResult, CancellationToken cancellationToken) =>
        coordinator.RetryAsync(originalResult, cancellationToken);

    private static string StatusMessageKeyFor(ElevatedScanRetryEligibilityOutcome outcome) => outcome switch
    {
        ElevatedScanRetryEligibilityOutcome.Eligible => "elevated-retry-availability.eligible",
        ElevatedScanRetryEligibilityOutcome.QuickAnalysisNotEligible => "elevated-retry-availability.quick-analysis-not-eligible",
        ElevatedScanRetryEligibilityOutcome.ScanNotCompleted => "elevated-retry-availability.scan-not-completed",
        ElevatedScanRetryEligibilityOutcome.AlreadyElevated => "elevated-retry-availability.already-elevated",
        ElevatedScanRetryEligibilityOutcome.DriveNotEligible => "elevated-retry-availability.drive-not-eligible",
        ElevatedScanRetryEligibilityOutcome.NoRootContributions => "elevated-retry-availability.no-root-contributions",
        ElevatedScanRetryEligibilityOutcome.NoReplaceablePermissionLimitedRoots => "elevated-retry-availability.no-replaceable-roots",
        ElevatedScanRetryEligibilityOutcome.TooManyRoots => "elevated-retry-availability.too-many-roots",
        ElevatedScanRetryEligibilityOutcome.DuplicateRoot => "elevated-retry-availability.duplicate-root",
        ElevatedScanRetryEligibilityOutcome.OverlappingRoots => "elevated-retry-availability.overlapping-roots",
        ElevatedScanRetryEligibilityOutcome.InvalidRootIdentity => "elevated-retry-availability.invalid-root-identity",
        ElevatedScanRetryEligibilityOutcome.RootOutsideDrive => "elevated-retry-availability.root-outside-drive",
        _ => "elevated-retry-availability.not-eligible"
    };
}
