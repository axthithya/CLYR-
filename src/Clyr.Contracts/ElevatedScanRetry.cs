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

    /// <summary>The declared length prefix of a serialized <see cref="ElevatedScanRetryRequest"/> frame is
    /// rejected — before any buffer sized to it is allocated — once it exceeds this many bytes.</summary>
    public const int MaxRequestFrameBytes = 256 * 1024;
    /// <summary>Same bound as <see cref="MaxRequestFrameBytes"/>, applied to a serialized
    /// <see cref="ElevatedScanRetryResponse"/> frame. Larger than the request bound because
    /// <see cref="ElevatedScanRetryResponse.BoundedDiagnostics"/> can legitimately carry up to
    /// <see cref="MaxDiagnosticCount"/> short diagnostic strings.</summary>
    public const int MaxResponseFrameBytes = 1024 * 1024;
}

/// <summary>
/// Naming and validation rules for the random, per-request named pipe used by <c>ElevatedScanIpcTransport</c>.
/// The name itself carries no meaning beyond "this one request" — no drive path, username, scan path, execution
/// ID, or other identifying information is ever encoded into it.
/// </summary>
public static class ElevatedScanPipeNameFormat
{
    public const string Prefix = "clyr-elevated-scan-";
    /// <summary>16 random bytes (128 bits) hex-encoded lowercase — 32 characters.</summary>
    public const int RandomHexLength = 32;
    public static readonly int ExpectedTotalLength = Prefix.Length + RandomHexLength;
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

/// <summary>How an <see cref="ElevatedScanRetryRequest"/> was resolved. <see cref="ValidationRejected"/> and
/// <see cref="ProtocolRejected"/> are distinct: the former means the request parsed but
/// <c>ElevatedScanRetryValidator</c> rejected its contents; the latter means the frame itself could not be
/// interpreted as a well-formed request at all (malformed JSON, wrong protocol version at the transport level).
/// <see cref="PartiallyCompleted"/> (added for <c>ElevatedMetadataRetryEngine</c>, Phase 7.2.6C) means every
/// validated root was attempted and enumeration otherwise ran to completion, but one or more roots remained
/// inaccessible even under retry — an ordinary, expected outcome, never <see cref="Failed"/>.</summary>
public enum ElevatedScanRetryOutcome
{
    Completed, ValidationRejected, Cancelled, TimedOut, ProtocolRejected, Failed, PartiallyCompleted
}

/// <summary>How one retried permission-limited root ended, within one <see cref="ElevatedScanRetryResponse"/>.</summary>
public enum ElevatedRootRetryOutcome { Completed, StillInaccessible, Cancelled, Failed }

/// <summary>
/// Phase 7.2.6G1: bounded, per-root retry detail. The response's own aggregate counters
/// (<see cref="ElevatedScanRetryResponse.RootsCompleted"/> and friends) cannot tell a caller — or a result
/// reconciler — <em>which</em> specific root succeeded, so this exists to carry that binding explicitly.
/// <see cref="CanonicalRootIdentity"/> is the normalized root path used to correlate this result back to the
/// originating request's <see cref="PermissionLimitedRoot.NormalizedRootPath"/>; it is never a free-form,
/// user-typed path. This is not an unrestricted per-file inventory — one bounded record per requested root,
/// nothing more.
/// </summary>
public sealed record ElevatedRootRetryResult(
    string CanonicalRootIdentity, string? StableRootIdentifier, ElevatedRootRetryOutcome Outcome,
    long FilesExamined, long DirectoriesExamined, long LogicalBytesObserved, long AllocatedBytesObserved,
    long UniqueAllocatedBytesObservedWithinRoot, long HardLinkEntriesDetected, long AllocationUnavailableCount,
    long SparseFileCount, long CompressedFileCount);

/// <summary>
/// The one closed response shape for <see cref="ElevatedScanOperation.RetryPermissionLimitedRoots"/>. Every
/// figure is a plain count or byte total already computed by the (future) elevated scan — never a command,
/// script, executable path, argument, destination path, cleanup action, or Phase 6 execution token.
/// <see cref="BoundedDiagnostics"/> holds up to <see cref="ElevatedScanRetryProtocol.MaxDiagnosticCount"/> short,
/// safe diagnostic codes/messages — never a raw, unrestricted exception dump. <see cref="RootResults"/> is
/// optional and defaults to empty for backward compatibility with every response already produced by
/// <c>ElevatedMetadataRetryEngine</c> (Phase 7.2.6C) — that engine does not yet populate per-root detail;
/// populating it is later, out-of-scope work. A caller (such as the Phase 7.2.6G1 result reconciler) that needs
/// per-root detail and finds this empty or incomplete must treat that as insufficient data, never guess.
/// </summary>
public sealed record ElevatedScanRetryResponse(
    int ProtocolVersion, string Nonce, ElevatedScanRetryOutcome Outcome,
    DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc,
    int RootsAttempted, int RootsCompleted, int RootsStillInaccessible,
    long FilesExamined, long DirectoriesExamined, long LogicalBytesObserved,
    long AllocatedBytesObserved, long UniqueAllocatedBytesObserved,
    long HardLinkEntriesDetected, long SparseFileCount, long CompressedFileCount,
    ImmutableArray<string> BoundedDiagnostics, ImmutableArray<ElevatedRootRetryResult> RootResults = default)
{
    public ImmutableArray<ElevatedRootRetryResult> RootResults { get; init; } = RootResults.IsDefault ? [] : RootResults;
}
