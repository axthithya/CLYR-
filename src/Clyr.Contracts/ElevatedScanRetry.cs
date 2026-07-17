using System.Collections.Immutable;

namespace Clyr.Contracts;

/// <summary>
/// The only elevated-scan operation CLYR defines. There is deliberately no case for arbitrary path scanning,
/// generic full-drive elevated scanning, cleanup, movement, command execution, or scripts — those capabilities
/// simply do not exist on this closed request surface, not merely "disabled" ones a caller could ask for.
/// </summary>
public enum ElevatedScanOperation
{
    RetryPermissionLimitedRoots
}

/// <summary>Why a root was inaccessible to the originating non-elevated scan, carried through unchanged from
/// that scan's own diagnostics — never a caller-supplied justification.</summary>
public enum PermissionLimitedReasonCode
{
    Unspecified, AccessDenied, InsufficientPrivilege, ProtectedByOwner
}

public static class ElevatedScanRetryProtocol
{
    public const int Version = 1;
    /// <summary>Bounds both a raw retry request's root list and a built <see cref="ElevatedScanRequestManifest"/>.</summary>
    public const int MaxRoots = 64;
    public const int MaxDiagnosticCount = 256;
    public const int MinNonceLength = 32;
    /// <summary>The longest a retry request may remain valid from the moment it is created — deliberately short,
    /// since this authorizes a one-time, narrow, read-only retry of a specific prior scan's inaccessible roots,
    /// not a standing grant.</summary>
    public static readonly TimeSpan MaxRequestLifetime = TimeSpan.FromMinutes(5);
}

/// <summary>
/// A single root a preceding non-elevated scan could not fully read. Every field traces back to something that
/// scan itself observed — there is no free-form, user-typed path anywhere on this type. Binding a root to
/// <see cref="OriginalScanExecutionId"/> and <see cref="DriveIdentity"/> is what lets the validator reject a
/// root that was quietly swapped in from a different scan or a different drive.
/// </summary>
public sealed record PermissionLimitedRoot(
    string NormalizedRootPath, Guid OriginalScanExecutionId, string DriveIdentity,
    string? StableRootIdentifier, PermissionLimitedReasonCode ReasonCode);

/// <summary>
/// A deterministic, order-independent manifest of the permission-limited roots produced by one scan, together
/// with a digest binding it to the protocol version, the operation, the originating scan, the drive, and every
/// security-relevant field of every root (not just its path) — changing any one of them invalidates
/// <see cref="Digest"/>. Not persisted by this task — see <c>ElevatedScanManifestBuilder</c> in Clyr.Core for
/// how the digest is derived.
/// </summary>
public sealed record ElevatedScanRequestManifest(
    int ProtocolVersion, ElevatedScanOperation Operation, Guid OriginalScanExecutionId, string DriveIdentity,
    ImmutableArray<PermissionLimitedRoot> Roots, string Digest);

/// <summary>
/// The one closed request shape for <see cref="ElevatedScanOperation.RetryPermissionLimitedRoots"/>. Deliberately
/// excludes anything resembling a command, script, executable path, argument list, cleanup action ID, execution
/// token, or destination path — see <c>ElevatedScanRetrySafetyBoundaryTests</c> for the enforced absence of
/// those concepts from this file.
/// </summary>
public sealed record ElevatedScanRetryRequest(
    int ProtocolVersion, ElevatedScanOperation Operation, string Nonce, DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc, Guid OriginalScanExecutionId, string DriveIdentity,
    string PermissionLimitedManifestDigest, ImmutableArray<PermissionLimitedRoot> PermissionLimitedRoots,
    int MaximumDiagnosticCount);

/// <summary>Every way a <see cref="ElevatedScanRetryRequest"/> can be rejected, named specifically enough that a
/// caller (or a test) never has to parse a message string to know which check failed.</summary>
public enum ElevatedScanRetryValidationOutcome
{
    Valid,
    UnsupportedProtocol,
    Expired,
    ExpiryTooFarInFuture,
    InvalidNonce,
    InvalidExecutionId,
    InvalidDriveIdentity,
    InvalidManifestDigest,
    EmptyRootSet,
    TooManyRoots,
    DuplicateRoot,
    RelativePath,
    TraversalPath,
    UncPath,
    RootOutsideDrive,
    RootNotFromOriginalScan,
    RootDriveMismatch,
    InvalidDiagnosticLimit
}

/// <summary>A typed validation outcome rather than a thrown exception — an invalid retry request is an expected,
/// routine input to reject, not an exceptional program state.</summary>
public sealed record ElevatedScanRetryValidationResult(ElevatedScanRetryValidationOutcome Outcome, string? Detail)
{
    public bool IsValid => Outcome == ElevatedScanRetryValidationOutcome.Valid;
    public static ElevatedScanRetryValidationResult Valid { get; } = new(ElevatedScanRetryValidationOutcome.Valid, null);
    public static ElevatedScanRetryValidationResult Invalid(ElevatedScanRetryValidationOutcome outcome, string detail) => new(outcome, detail);
}
