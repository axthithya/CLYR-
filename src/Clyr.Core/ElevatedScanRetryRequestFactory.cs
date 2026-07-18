using System.Collections.Immutable;
using System.Security.Cryptography;
using Clyr.Contracts;

namespace Clyr.Core;

/// <summary>Every way a completed <see cref="ScanResult"/> can fail to qualify for an elevated permission-limited
/// root retry, plus <see cref="Eligible"/> for the one case that does. Expected invalid inputs are always
/// reported this way — this type never throws for a routine ineligible scan.</summary>
public enum ElevatedScanRetryEligibilityOutcome
{
    Eligible,
    /// <summary>Only a completed Deep ("Full") Analysis carries the per-root <see cref="ScanResult.RootContributions"/>
    /// accounting this service depends on; Quick Analysis never populates that field.</summary>
    QuickAnalysisNotEligible,
    ScanNotCompleted,
    /// <summary>Reserved for a future scan-result shape that can represent "this scan already ran elevated".
    /// Every <see cref="ScanResult"/> producible today comes from the non-elevated <c>ScanCoordinator</c> — the
    /// elevated retry path produces an <see cref="ElevatedScanRetryResponse"/>, never a <see cref="ScanResult"/>
    /// — so this outcome cannot currently be reached; it exists so the eligibility surface stays stable if that
    /// ever changes.</summary>
    AlreadyElevated,
    DriveNotEligible,
    NoRootContributions,
    NoReplaceablePermissionLimitedRoots,
    TooManyRoots,
    DuplicateRoot,
    OverlappingRoots,
    InvalidRootIdentity,
    RootOutsideDrive
}

/// <summary>The typed result of one eligibility evaluation. <see cref="EligibleRoots"/> and <see cref="DriveIdentity"/>
/// are populated only when <see cref="Outcome"/> is <see cref="ElevatedScanRetryEligibilityOutcome.Eligible"/>;
/// <see cref="EligibleRoots"/> is already in the same deterministic order a request/manifest would use.</summary>
public sealed record ElevatedScanRetryEligibilityResult(
    ElevatedScanRetryEligibilityOutcome Outcome, ImmutableArray<ScanRootContribution> EligibleRoots, string? DriveIdentity)
{
    public bool IsEligible => Outcome == ElevatedScanRetryEligibilityOutcome.Eligible;
    public static ElevatedScanRetryEligibilityResult Ineligible(ElevatedScanRetryEligibilityOutcome outcome) => new(outcome, [], null);
}

/// <summary>
/// Pure, in-memory decision of whether a completed non-elevated Deep Analysis result qualifies for an elevated
/// permission-limited-root retry, and — if so — exactly which of its <see cref="ScanResult.RootContributions"/>
/// may safely be retried. No filesystem access, no process launch, no IPC, no UAC. Only the exact top-level root
/// contributions <see cref="ElevatedScanResultReconciler"/> already knows how to safely replace are ever
/// selected — never a nested warning-only path that has no matching contribution record, and never an
/// arbitrary, caller-supplied path.
/// </summary>
public static class ElevatedScanRetryEligibility
{
    public static ElevatedScanRetryEligibilityResult Evaluate(ScanResult result, DriveSummary? drive, string? trustedDriveIdentity)
    {
        if (result.Mode != ScanMode.Deep)
            return ElevatedScanRetryEligibilityResult.Ineligible(ElevatedScanRetryEligibilityOutcome.QuickAnalysisNotEligible);
        if (result.Status is not (ScanStatus.Completed or ScanStatus.CompletedWithWarnings))
            return ElevatedScanRetryEligibilityResult.Ineligible(ElevatedScanRetryEligibilityOutcome.ScanNotCompleted);
        if (drive is null || drive.Kind != DriveKind.Fixed || string.IsNullOrWhiteSpace(trustedDriveIdentity))
            return ElevatedScanRetryEligibilityResult.Ineligible(ElevatedScanRetryEligibilityOutcome.DriveNotEligible);
        if (result.RootContributions.Count == 0)
            return ElevatedScanRetryEligibilityResult.Ineligible(ElevatedScanRetryEligibilityOutcome.NoRootContributions);

        var replaceable = result.RootContributions.Where(IsExactReplaceable).ToImmutableArray();
        if (replaceable.Length == 0)
            return ElevatedScanRetryEligibilityResult.Ineligible(ElevatedScanRetryEligibilityOutcome.NoReplaceablePermissionLimitedRoots);
        if (replaceable.Length > ElevatedScanRetryProtocol.MaxRoots)
            return ElevatedScanRetryEligibilityResult.Ineligible(ElevatedScanRetryEligibilityOutcome.TooManyRoots);

        var driveLetter = ElevatedScanManifestBuilder.NormalizePath(drive.Root)[..1];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalizedOrdered = new List<(string Normalized, ScanRootContribution Contribution)>();
        foreach (var contribution in replaceable)
        {
            if (!IsStructurallyValidRoot(contribution.RootPath))
                return ElevatedScanRetryEligibilityResult.Ineligible(ElevatedScanRetryEligibilityOutcome.InvalidRootIdentity);

            var normalized = ElevatedScanManifestBuilder.NormalizePath(contribution.RootPath);
            if (normalized.Length < 1 || !string.Equals(normalized[..1], driveLetter, StringComparison.OrdinalIgnoreCase))
                return ElevatedScanRetryEligibilityResult.Ineligible(ElevatedScanRetryEligibilityOutcome.RootOutsideDrive);
            if (!seen.Add(normalized))
                return ElevatedScanRetryEligibilityResult.Ineligible(ElevatedScanRetryEligibilityOutcome.DuplicateRoot);

            normalizedOrdered.Add((normalized, contribution));
        }

        foreach (var (normalized, _) in normalizedOrdered)
            foreach (var (otherNormalized, _) in normalizedOrdered)
                if (normalized != otherNormalized && IsAncestor(normalized, otherNormalized))
                    return ElevatedScanRetryEligibilityResult.Ineligible(ElevatedScanRetryEligibilityOutcome.OverlappingRoots);

        var ordered = normalizedOrdered.OrderBy(item => item.Normalized, StringComparer.Ordinal)
            .Select(item => item.Contribution).ToImmutableArray();
        return new(ElevatedScanRetryEligibilityOutcome.Eligible, ordered, trustedDriveIdentity);
    }

    /// <summary>Only these two states leave a top-level root in a state a retry can safely and exactly replace —
    /// see <see cref="ScanRootEnumerationState"/> and <c>ElevatedScanResultReconciler</c> for why
    /// <see cref="ScanRootEnumerationState.Completed"/>, <see cref="ScanRootEnumerationState.Cancelled"/> and
    /// <see cref="ScanRootEnumerationState.Failed"/> are never eligible.</summary>
    private static bool IsExactReplaceable(ScanRootContribution contribution) => contribution.EnumerationState switch
    {
        ScanRootEnumerationState.InaccessibleAtRoot => true,
        ScanRootEnumerationState.PartiallyObserved => contribution.InaccessibleEntryCount > 0,
        _ => false
    };

    private static bool IsStructurallyValidRoot(string path) =>
        !IsNetworkOrDevicePath(path) && IsDriveAbsolute(path) && !HasTraversalSegment(path);

    private static bool IsNetworkOrDevicePath(string value) => value.Length >= 2 && value[0] == '\\' && value[1] == '\\';
    private static bool IsDriveAbsolute(string value) => value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] == '\\';
    private static bool HasTraversalSegment(string value) =>
        value.Split('\\', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or "..");

    /// <summary>Segment-aware ancestor test (never raw <see cref="string.StartsWith(string)"/>) so "C:\Users"
    /// is correctly flagged as an ancestor of "C:\Users\Example" while "C:\Users" is correctly NOT an ancestor
    /// of "C:\UsersOther". Both inputs must already be normalized (see <see cref="ElevatedScanManifestBuilder.NormalizePath"/>).</summary>
    private static bool IsAncestor(string normalizedPossibleAncestor, string normalizedOther)
    {
        var ancestorSegments = normalizedPossibleAncestor.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var otherSegments = normalizedOther.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (ancestorSegments.Length >= otherSegments.Length) return false;
        for (var i = 0; i < ancestorSegments.Length; i++)
            if (!string.Equals(ancestorSegments[i], otherSegments[i], StringComparison.Ordinal)) return false;
        return true;
    }
}

/// <summary>Generates a cryptographically strong, request-scoped nonce — never anything derived from a path,
/// username, or other identifying/predictable value.</summary>
public interface INonceGenerator
{
    string CreateNonce();
}

/// <summary>64 lowercase hex characters (256 random bits) — comfortably above <see cref="ElevatedScanRetryProtocol.MinNonceLength"/>.</summary>
public sealed class CryptographicNonceGenerator : INonceGenerator
{
    public string CreateNonce() => RandomNumberGenerator.GetHexString(64, lowercase: true);
}

/// <summary>The typed result of one request-build attempt. <see cref="Request"/> is non-null only when
/// <see cref="Outcome"/> is <see cref="ElevatedScanRetryEligibilityOutcome.Eligible"/>.</summary>
public sealed record ElevatedScanRetryRequestBuildResult(ElevatedScanRetryEligibilityOutcome Outcome, ElevatedScanRetryRequest? Request)
{
    public bool IsSuccess => Outcome == ElevatedScanRetryEligibilityOutcome.Eligible && Request is not null;
}

/// <summary>
/// Converts one completed, eligible, non-elevated Deep Analysis <see cref="ScanResult"/> into one valid, safely
/// bounded <see cref="ElevatedScanRetryRequest"/>. This is the only production entry point that ever constructs
/// such a request. The public <see cref="Build"/> method accepts only the original typed <see cref="ScanResult"/>
/// — never a root path, a manifest, a nonce, a pipe name, an executable path, a command, or any other
/// caller-supplied value; every field of the resulting request is either read from the scan result itself,
/// resolved through the already-trusted drive-identity infrastructure, or generated internally
/// (<see cref="INonceGenerator"/>, <see cref="IClock"/>). No filesystem access, no process launch, no IPC, no UAC.
/// </summary>
public sealed class ElevatedScanRetryRequestFactory(
    IDriveDiscovery drives, IDriveIdentityProvider driveIdentity, IClock clock, INonceGenerator nonceGenerator)
    : IElevatedScanRetryRequestFactory
{
    /// <summary>Bounded, fixed diagnostic cap for every request this factory builds — a small, safe value, never
    /// caller-configurable and never the protocol's own upper bound.</summary>
    private const int MaximumDiagnosticCount = 32;

    public ElevatedScanRetryRequestBuildResult Build(ScanResult result)
    {
        var drive = drives.Discover().FirstOrDefault(candidate =>
            string.Equals(ElevatedScanManifestBuilder.NormalizePath(candidate.Root), ElevatedScanManifestBuilder.NormalizePath(result.Root), StringComparison.Ordinal));
        var identified = drive is null ? null : driveIdentity.Identify(drive.Root, result.FileSystem, drive.UsedBytes);
        var trustedDriveIdentity = identified is { IdentityQuality: DriveIdentityQuality.Stable, Fingerprint.Length: > 0 } ? identified.Fingerprint : null;

        var eligibility = ElevatedScanRetryEligibility.Evaluate(result, drive, trustedDriveIdentity);
        if (!eligibility.IsEligible)
            return new ElevatedScanRetryRequestBuildResult(eligibility.Outcome, null);

        var roots = eligibility.EligibleRoots
            .OrderBy(contribution => ElevatedScanManifestBuilder.NormalizePath(contribution.RootPath), StringComparer.Ordinal)
            .Select(contribution => ToPermissionLimitedRoot(contribution, result.ScanId, eligibility.DriveIdentity!))
            .ToImmutableArray();

        var manifest = ElevatedScanManifestBuilder.Build(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots,
            result.ScanId, eligibility.DriveIdentity!, roots);
        if (!manifest.IsSuccess)
            return new ElevatedScanRetryRequestBuildResult(ElevatedScanRetryEligibilityOutcome.InvalidRootIdentity, null);

        var createdAtUtc = clock.UtcNow;
        var request = new ElevatedScanRetryRequest(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots,
            nonceGenerator.CreateNonce(), createdAtUtc, createdAtUtc + ElevatedScanRetryProtocol.MaxRequestLifetime,
            result.ScanId, eligibility.DriveIdentity!, manifest.Value!.Digest, roots, MaximumDiagnosticCount);
        return new ElevatedScanRetryRequestBuildResult(ElevatedScanRetryEligibilityOutcome.Eligible, request);
    }

    /// <summary>Deterministic reason mapping: a root the original scan could never even open at all is
    /// <see cref="PermissionLimitedReasonCode.AccessDenied"/>; a root that opened but hit at least one denied
    /// entry somewhere within it is <see cref="PermissionLimitedReasonCode.InsufficientPrivilege"/> — distinct
    /// codes so the elevated helper (and any future UI) can tell "the root itself was locked" apart from "part of
    /// the tree beneath an open root was locked" without parsing free-form text.</summary>
    private static PermissionLimitedRoot ToPermissionLimitedRoot(ScanRootContribution contribution, Guid scanId, string driveIdentity)
    {
        var reason = contribution.EnumerationState == ScanRootEnumerationState.InaccessibleAtRoot
            ? PermissionLimitedReasonCode.AccessDenied
            : PermissionLimitedReasonCode.InsufficientPrivilege;
        return new PermissionLimitedRoot(contribution.RootPath, scanId, driveIdentity, contribution.StableRootIdentifier, reason);
    }
}
