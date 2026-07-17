using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clyr.Contracts;

namespace Clyr.Core;

/// <summary>
/// Builds the deterministic manifest and digest for a set of <see cref="PermissionLimitedRoot"/> values. Pure
/// and side-effect-free: no filesystem access, no process launch, no I/O of any kind. Roots are deduplicated by
/// normalized path and sorted into a stable order before hashing, so two callers presenting the same root set in
/// a different order always produce the same digest.
/// </summary>
public static class ElevatedScanManifestBuilder
{
    public static Outcome<ElevatedScanRequestManifest> Build(int protocolVersion, ElevatedScanOperation operation,
        Guid originalScanExecutionId, string driveIdentity, IReadOnlyList<PermissionLimitedRoot> roots)
    {
        if (originalScanExecutionId == Guid.Empty)
            return Outcomes.Failure<ElevatedScanRequestManifest>("manifest.execution-id", "An originating scan execution ID is required.");
        if (string.IsNullOrWhiteSpace(driveIdentity))
            return Outcomes.Failure<ElevatedScanRequestManifest>("manifest.drive-identity", "A drive identity is required.");
        if (roots.Count == 0)
            return Outcomes.Failure<ElevatedScanRequestManifest>("manifest.empty", "At least one permission-limited root is required.");
        if (roots.Count > ElevatedScanRetryProtocol.MaxRoots)
            return Outcomes.Failure<ElevatedScanRequestManifest>("manifest.too-many-roots",
                $"No more than {ElevatedScanRetryProtocol.MaxRoots} roots are supported in one manifest.");

        var ordered = roots
            .GroupBy(root => NormalizePath(root.NormalizedRootPath), StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(root => NormalizePath(root.NormalizedRootPath), StringComparer.Ordinal)
            .ToImmutableArray();

        var digest = ComputeDigest(protocolVersion, operation, originalScanExecutionId, driveIdentity, ordered);
        return Outcomes.Success(new ElevatedScanRequestManifest(protocolVersion, operation, originalScanExecutionId, driveIdentity, ordered, digest));
    }

    /// <summary>
    /// SHA-256 over a canonical JSON encoding of every security-relevant field: protocol version, operation,
    /// originating scan ID, drive identity, and — for each already-ordered root — its normalized path, its own
    /// originating scan ID and drive identity (which may legitimately differ from the request's, and must be
    /// caught if they do), its stable root identifier, and its reason code. Changing any one of these fields on
    /// any root changes the digest. JSON's own null (via <see cref="Utf8JsonWriter.WriteNull"/>) is used for an
    /// absent <see cref="PermissionLimitedRoot.StableRootIdentifier"/> so it can never collide with the empty
    /// string, and every string is written as literal UTF-8 bytes with no culture-dependent formatting anywhere
    /// in this method. Exposed separately from <see cref="Build"/> so a caller validating an externally supplied
    /// digest need not reconstruct a full manifest.
    /// </summary>
    public static string ComputeDigest(int protocolVersion, ElevatedScanOperation operation, Guid originalScanExecutionId,
        string driveIdentity, IReadOnlyList<PermissionLimitedRoot> orderedRoots)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("protocolVersion", protocolVersion);
            writer.WriteString("operation", operation.ToString());
            writer.WriteString("originalScanExecutionId", originalScanExecutionId.ToString("D"));
            writer.WriteString("driveIdentity", driveIdentity);
            writer.WriteStartArray("roots");
            foreach (var root in orderedRoots)
            {
                writer.WriteStartObject();
                writer.WriteString("normalizedRootPath", NormalizePath(root.NormalizedRootPath));
                writer.WriteString("originalScanExecutionId", root.OriginalScanExecutionId.ToString("D"));
                writer.WriteString("driveIdentity", root.DriveIdentity);
                if (root.StableRootIdentifier is null) writer.WriteNull("stableRootIdentifier");
                else writer.WriteString("stableRootIdentifier", root.StableRootIdentifier);
                writer.WriteString("reasonCode", root.ReasonCode.ToString());
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    /// <summary>Canonicalizes a Windows path for identity purposes only (duplicate detection and digest input):
    /// separators unified, redundant trailing separators trimmed (a bare drive root such as "C:\" is preserved),
    /// and the whole path upper-invariant-cased since Windows path comparison is case-insensitive. This never
    /// touches the real filesystem and never resolves reparse points — it is a pure string transform.</summary>
    internal static string NormalizePath(string path)
    {
        var normalized = path.Replace('/', '\\').TrimEnd('\\');
        if (normalized.Length == 2 && normalized[1] == ':') normalized += "\\";
        return normalized.ToUpperInvariant();
    }
}

/// <summary>
/// Pure, side-effect-free validation for <see cref="ElevatedScanRetryRequest"/> — the sole request shape for
/// <see cref="ElevatedScanOperation.RetryPermissionLimitedRoots"/>. This performs no filesystem access, no
/// process launch, no shell execution, no network access, no UAC, no named pipes, and holds no reference to
/// cleanup execution. It only inspects the already-typed request and returns a typed outcome — the elevated
/// helper itself (a later phase) is expected to re-run equivalent checks against its own live state; this layer
/// exists so a caller, and every test, can reject a malformed or hostile request before any elevated process is
/// ever considered.
/// </summary>
public static class ElevatedScanRetryValidator
{
    public static ElevatedScanRetryValidationResult Validate(ElevatedScanRetryRequest request, DateTimeOffset nowUtc)
    {
        if (request.ProtocolVersion != ElevatedScanRetryProtocol.Version)
            return Invalid(ElevatedScanRetryValidationOutcome.UnsupportedProtocol, $"Protocol version {request.ProtocolVersion} is not supported.");
        if (nowUtc >= request.ExpiresAtUtc)
            return Invalid(ElevatedScanRetryValidationOutcome.Expired, "The retry request has expired.");
        if (request.ExpiresAtUtc - request.CreatedAtUtc > ElevatedScanRetryProtocol.MaxRequestLifetime)
            return Invalid(ElevatedScanRetryValidationOutcome.ExpiryTooFarInFuture, "The retry request's expiry window exceeds the maximum allowed lifetime.");
        if (!IsValidNonce(request.Nonce))
            return Invalid(ElevatedScanRetryValidationOutcome.InvalidNonce, "The nonce is missing, too short, or not a supported hexadecimal value.");
        if (request.OriginalScanExecutionId == Guid.Empty)
            return Invalid(ElevatedScanRetryValidationOutcome.InvalidExecutionId, "An originating scan execution ID is required.");
        if (string.IsNullOrWhiteSpace(request.DriveIdentity))
            return Invalid(ElevatedScanRetryValidationOutcome.InvalidDriveIdentity, "A drive identity is required.");
        if (request.PermissionLimitedRoots.IsDefaultOrEmpty)
            return Invalid(ElevatedScanRetryValidationOutcome.EmptyRootSet, "At least one permission-limited root is required.");
        if (request.PermissionLimitedRoots.Length > ElevatedScanRetryProtocol.MaxRoots)
            return Invalid(ElevatedScanRetryValidationOutcome.TooManyRoots,
                $"No more than {ElevatedScanRetryProtocol.MaxRoots} roots are supported in one retry request.");
        if (request.MaximumDiagnosticCount < 1 || request.MaximumDiagnosticCount > ElevatedScanRetryProtocol.MaxDiagnosticCount)
            return Invalid(ElevatedScanRetryValidationOutcome.InvalidDiagnosticLimit,
                $"The diagnostic limit must be between 1 and {ElevatedScanRetryProtocol.MaxDiagnosticCount}.");

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? expectedDriveLetter = null;
        foreach (var root in request.PermissionLimitedRoots)
        {
            if (IsNetworkOrDevicePath(root.NormalizedRootPath))
                return Invalid(ElevatedScanRetryValidationOutcome.UncPath, $"'{root.NormalizedRootPath}' is a UNC or device-namespace path.");
            if (!IsDriveAbsolute(root.NormalizedRootPath))
                return Invalid(ElevatedScanRetryValidationOutcome.RelativePath, $"'{root.NormalizedRootPath}' is not an absolute local drive path.");
            if (HasTraversalSegment(root.NormalizedRootPath))
                return Invalid(ElevatedScanRetryValidationOutcome.TraversalPath, $"'{root.NormalizedRootPath}' contains a relative traversal component.");

            var normalized = ElevatedScanManifestBuilder.NormalizePath(root.NormalizedRootPath);
            if (!seenPaths.Add(normalized))
                return Invalid(ElevatedScanRetryValidationOutcome.DuplicateRoot, $"'{root.NormalizedRootPath}' appears more than once in the same retry request.");
            if (root.OriginalScanExecutionId != request.OriginalScanExecutionId)
                return Invalid(ElevatedScanRetryValidationOutcome.RootNotFromOriginalScan, $"'{root.NormalizedRootPath}' is not bound to the originating scan.");
            if (!string.Equals(root.DriveIdentity, request.DriveIdentity, StringComparison.Ordinal))
                return Invalid(ElevatedScanRetryValidationOutcome.RootDriveMismatch, $"'{root.NormalizedRootPath}' is bound to a different drive.");

            var driveLetter = normalized[..1];
            expectedDriveLetter ??= driveLetter;
            if (!string.Equals(driveLetter, expectedDriveLetter, StringComparison.OrdinalIgnoreCase))
                return Invalid(ElevatedScanRetryValidationOutcome.RootOutsideDrive, $"'{root.NormalizedRootPath}' is outside the selected drive.");
        }

        var manifest = ElevatedScanManifestBuilder.Build(request.ProtocolVersion, request.Operation, request.OriginalScanExecutionId,
            request.DriveIdentity, request.PermissionLimitedRoots);
        if (!manifest.IsSuccess || !FixedEquals(manifest.Value!.Digest, request.PermissionLimitedManifestDigest))
            return Invalid(ElevatedScanRetryValidationOutcome.InvalidManifestDigest, "The permission-limited manifest digest does not match the supplied roots.");

        return ElevatedScanRetryValidationResult.Valid;
    }

    private static ElevatedScanRetryValidationResult Invalid(ElevatedScanRetryValidationOutcome outcome, string detail) =>
        ElevatedScanRetryValidationResult.Invalid(outcome, detail);

    private static bool IsValidNonce(string nonce) =>
        !string.IsNullOrWhiteSpace(nonce) && nonce.Length >= ElevatedScanRetryProtocol.MinNonceLength && nonce.All(Uri.IsHexDigit);

    private static bool IsNetworkOrDevicePath(string value) => value.Length >= 2 && value[0] == '\\' && value[1] == '\\';
    private static bool IsDriveAbsolute(string value) => value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] == '\\';
    private static bool HasTraversalSegment(string value) =>
        value.Split('\\', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or "..");

    private static bool FixedEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
