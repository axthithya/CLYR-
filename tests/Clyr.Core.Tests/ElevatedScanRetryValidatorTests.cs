using System.Collections.Immutable;
using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>Phase 7.2.6A: pure, deterministic validation for the closed elevated-scan retry request. No test
/// here touches the filesystem, launches a process, or triggers UAC — every fixture is an in-memory typed value.</summary>
public sealed class ElevatedScanRetryValidatorTests
{
    private static readonly Guid ScanId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherScanId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string DriveIdentity = "drive-fingerprint-abc123";
    private const string OtherDriveIdentity = "drive-fingerprint-xyz789";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-17T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void ValidRequestPassesEveryCheck()
    {
        var request = BuildValidRequest(RootPaths(3));
        var result = ElevatedScanRetryValidator.Validate(request, Now);
        Assert.True(result.IsValid);
        Assert.Equal(ElevatedScanRetryValidationOutcome.Valid, result.Outcome);
    }

    [Fact]
    public void UnsupportedProtocolVersionIsRejected()
    {
        var request = BuildValidRequest(RootPaths(1)) with { ProtocolVersion = ElevatedScanRetryProtocol.Version + 1 };
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.UnsupportedProtocol);
    }

    [Fact]
    public void ExpiredRequestIsRejected()
    {
        var request = BuildValidRequest(RootPaths(1), createdAtUtc: Now.AddMinutes(-10), expiresAtUtc: Now.AddMinutes(-1));
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.Expired);
    }

    [Fact]
    public void ExpiryFarBeyondTheAllowedWindowIsRejected()
    {
        var request = BuildValidRequest(RootPaths(1), createdAtUtc: Now, expiresAtUtc: Now + ElevatedScanRetryProtocol.MaxRequestLifetime + TimeSpan.FromMinutes(1));
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.ExpiryTooFarInFuture);
    }

    [Fact]
    public void MalformedNonceIsRejected()
    {
        var request = BuildValidRequest(RootPaths(1)) with { Nonce = "not-hex-and-way-too-short" };
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.InvalidNonce);
    }

    [Fact]
    public void EmptyScanExecutionIdIsRejected()
    {
        var request = BuildValidRequest(RootPaths(1)) with { OriginalScanExecutionId = Guid.Empty };
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.InvalidExecutionId);
    }

    [Fact]
    public void EmptyDriveIdentityIsRejected()
    {
        var request = BuildValidRequest(RootPaths(1)) with { DriveIdentity = "  " };
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.InvalidDriveIdentity);
    }

    [Fact]
    public void EmptyRootSetIsRejected()
    {
        var request = BuildValidRequest([]) with
        {
            PermissionLimitedManifestDigest = "irrelevant-because-empty-root-set-is-rejected-first"
        };
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.EmptyRootSet);
    }

    [Fact]
    public void ExcessiveRootCountIsRejected()
    {
        var request = BuildValidRequest(RootPaths(ElevatedScanRetryProtocol.MaxRoots + 1));
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.TooManyRoots);
    }

    [Fact]
    public void DuplicateRootIsRejected()
    {
        var roots = BuildRoots(["C:\\Data\\Alpha", "C:\\Data\\Alpha"]);
        var request = BuildValidRequestFromRoots(roots);
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.DuplicateRoot);
    }

    [Fact]
    public void RelativeRootIsRejected()
    {
        var roots = BuildRoots(["Data\\Alpha"]);
        var request = BuildValidRequestFromRoots(roots);
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.RelativePath);
    }

    [Fact]
    public void TraversalRootIsRejected()
    {
        var roots = BuildRoots(["C:\\Data\\..\\Windows"]);
        var request = BuildValidRequestFromRoots(roots);
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.TraversalPath);
    }

    [Fact]
    public void UncRootIsRejected()
    {
        var roots = BuildRoots(["\\\\server\\share\\Data"]);
        var request = BuildValidRequestFromRoots(roots);
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.UncPath);
    }

    [Fact]
    public void RootOutsideTheSelectedDriveIsRejected()
    {
        // Both roots correctly carry the same DriveIdentity, but the second literal path is on a different
        // drive letter than the first — a defense-in-depth check independent of the opaque identity string.
        var roots = BuildRoots(["C:\\Data\\Alpha", "D:\\Data\\Beta"]);
        var request = BuildValidRequestFromRoots(roots);
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.RootOutsideDrive);
    }

    [Fact]
    public void RootFromAnotherScanIsRejected()
    {
        var roots = ImmutableArray.Create(
            new PermissionLimitedRoot("C:\\Data\\Alpha", ScanId, DriveIdentity, "root-1", PermissionLimitedReasonCode.AccessDenied),
            new PermissionLimitedRoot("C:\\Data\\Beta", OtherScanId, DriveIdentity, "root-2", PermissionLimitedReasonCode.AccessDenied));
        var request = BuildValidRequestFromRoots(roots);
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.RootNotFromOriginalScan);
    }

    [Fact]
    public void RootFromAnotherDriveIsRejected()
    {
        var roots = ImmutableArray.Create(
            new PermissionLimitedRoot("C:\\Data\\Alpha", ScanId, DriveIdentity, "root-1", PermissionLimitedReasonCode.AccessDenied),
            new PermissionLimitedRoot("C:\\Data\\Beta", ScanId, OtherDriveIdentity, "root-2", PermissionLimitedReasonCode.AccessDenied));
        var request = BuildValidRequestFromRoots(roots);
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.RootDriveMismatch);
    }

    [Fact]
    public void ManifestDigestMismatchIsRejected()
    {
        var request = BuildValidRequest(RootPaths(2)) with { PermissionLimitedManifestDigest = new string('0', 64) };
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.InvalidManifestDigest);
    }

    [Fact]
    public void ManifestDigestIsDeterministicRegardlessOfRootOrder()
    {
        var forward = BuildRoots(["C:\\Data\\Alpha", "C:\\Data\\Beta", "C:\\Data\\Gamma"]);
        var reversed = ImmutableArray.CreateRange(forward.Reverse());

        var forwardManifest = Manifest(forward);
        var reversedManifest = Manifest(reversed);

        Assert.True(forwardManifest.IsSuccess);
        Assert.True(reversedManifest.IsSuccess);
        Assert.Equal(forwardManifest.Value!.Digest, reversedManifest.Value!.Digest);
    }

    [Fact]
    public void ManifestDigestIsUnaffectedByPathCasing()
    {
        var lower = BuildRoots(["C:\\Users\\Test"]);
        var upper = BuildRoots(["c:\\users\\test"]);
        Assert.Equal(Manifest(lower).Value!.Digest, Manifest(upper).Value!.Digest);
    }

    [Fact]
    public void ManifestDigestIsUnaffectedByATrailingSeparator()
    {
        var withoutTrailing = BuildRoots(["C:\\Data\\Alpha"]);
        var withTrailing = BuildRoots(["C:\\Data\\Alpha\\"]);
        Assert.Equal(Manifest(withoutTrailing).Value!.Digest, Manifest(withTrailing).Value!.Digest);
    }

    [Fact]
    public void ChangingStableRootIdentifierChangesTheDigest()
    {
        var original = ImmutableArray.Create(new PermissionLimitedRoot("C:\\Data\\Alpha", ScanId, DriveIdentity, "root-1", PermissionLimitedReasonCode.AccessDenied));
        var changed = ImmutableArray.Create(new PermissionLimitedRoot("C:\\Data\\Alpha", ScanId, DriveIdentity, "root-2", PermissionLimitedReasonCode.AccessDenied));
        Assert.NotEqual(Manifest(original).Value!.Digest, Manifest(changed).Value!.Digest);
    }

    [Fact]
    public void ChangingReasonCodeChangesTheDigest()
    {
        var original = ImmutableArray.Create(new PermissionLimitedRoot("C:\\Data\\Alpha", ScanId, DriveIdentity, "root-1", PermissionLimitedReasonCode.AccessDenied));
        var changed = ImmutableArray.Create(new PermissionLimitedRoot("C:\\Data\\Alpha", ScanId, DriveIdentity, "root-1", PermissionLimitedReasonCode.ProtectedByOwner));
        Assert.NotEqual(Manifest(original).Value!.Digest, Manifest(changed).Value!.Digest);
    }

    [Fact]
    public void ChangingARootsOriginalScanExecutionIdChangesTheDigest()
    {
        var original = ImmutableArray.Create(new PermissionLimitedRoot("C:\\Data\\Alpha", ScanId, DriveIdentity, "root-1", PermissionLimitedReasonCode.AccessDenied));
        var changed = ImmutableArray.Create(new PermissionLimitedRoot("C:\\Data\\Alpha", OtherScanId, DriveIdentity, "root-1", PermissionLimitedReasonCode.AccessDenied));
        Assert.NotEqual(Manifest(original).Value!.Digest, Manifest(changed).Value!.Digest);
    }

    [Fact]
    public void ChangingARootsDriveIdentityChangesTheDigest()
    {
        var original = ImmutableArray.Create(new PermissionLimitedRoot("C:\\Data\\Alpha", ScanId, DriveIdentity, "root-1", PermissionLimitedReasonCode.AccessDenied));
        var changed = ImmutableArray.Create(new PermissionLimitedRoot("C:\\Data\\Alpha", ScanId, OtherDriveIdentity, "root-1", PermissionLimitedReasonCode.AccessDenied));
        Assert.NotEqual(Manifest(original).Value!.Digest, Manifest(changed).Value!.Digest);
    }

    [Fact]
    public void ChangingOperationChangesTheDigest()
    {
        var roots = BuildRoots(["C:\\Data\\Alpha"]);
        var asRetry = ElevatedScanManifestBuilder.Build(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots, ScanId, DriveIdentity, roots);
        var asOther = ElevatedScanManifestBuilder.Build(ElevatedScanRetryProtocol.Version, (ElevatedScanOperation)999, ScanId, DriveIdentity, roots);
        Assert.NotEqual(asRetry.Value!.Digest, asOther.Value!.Digest);
    }

    [Fact]
    public void CaseOnlyDuplicateRootsAreRejected()
    {
        var roots = BuildRoots(["C:\\Data\\Alpha", "c:\\data\\alpha"]);
        var request = BuildValidRequestFromRoots(roots);
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.DuplicateRoot);
    }

    [Fact]
    public void TrailingSeparatorDuplicateRootsAreRejected()
    {
        var roots = BuildRoots(["C:\\Data\\Alpha", "C:\\Data\\Alpha\\"]);
        var request = BuildValidRequestFromRoots(roots);
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.DuplicateRoot);
    }

    [Fact]
    public void ValidationFailsWhenABoundRootFieldIsModifiedAfterTheDigestWasCreated()
    {
        var request = BuildValidRequest(RootPaths(1));
        var tamperedRoot = request.PermissionLimitedRoots[0] with { StableRootIdentifier = "swapped-in-after-signing" };
        var tampered = request with { PermissionLimitedRoots = [tamperedRoot] };
        AssertOutcome(tampered, ElevatedScanRetryValidationOutcome.InvalidManifestDigest);
    }

    [Fact]
    public void ValidDiagnosticLimitPasses()
    {
        var request = BuildValidRequest(RootPaths(1), maximumDiagnosticCount: ElevatedScanRetryProtocol.MaxDiagnosticCount);
        var result = ElevatedScanRetryValidator.Validate(request, Now);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ExcessiveDiagnosticLimitIsRejected()
    {
        var request = BuildValidRequest(RootPaths(1), maximumDiagnosticCount: ElevatedScanRetryProtocol.MaxDiagnosticCount + 1);
        AssertOutcome(request, ElevatedScanRetryValidationOutcome.InvalidDiagnosticLimit);
    }

    private static void AssertOutcome(ElevatedScanRetryRequest request, ElevatedScanRetryValidationOutcome expected)
    {
        var result = ElevatedScanRetryValidator.Validate(request, Now);
        Assert.False(result.IsValid);
        Assert.Equal(expected, result.Outcome);
        Assert.NotNull(result.Detail);
    }

    private static string[] RootPaths(int count) => [.. Enumerable.Range(0, count).Select(index => $"C:\\Data\\Root{index}")];

    private static ImmutableArray<PermissionLimitedRoot> BuildRoots(IEnumerable<string> paths) =>
        [.. paths.Select((path, index) => new PermissionLimitedRoot(path, ScanId, DriveIdentity, $"root-{index}", PermissionLimitedReasonCode.AccessDenied))];

    private static Outcome<ElevatedScanRequestManifest> Manifest(IReadOnlyList<PermissionLimitedRoot> roots) =>
        ElevatedScanManifestBuilder.Build(ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots, ScanId, DriveIdentity, roots);

    private static ElevatedScanRetryRequest BuildValidRequest(IEnumerable<string> paths, int maximumDiagnosticCount = 16,
        DateTimeOffset? createdAtUtc = null, DateTimeOffset? expiresAtUtc = null) =>
        BuildValidRequestFromRoots(BuildRoots(paths), maximumDiagnosticCount, createdAtUtc, expiresAtUtc);

    private static ElevatedScanRetryRequest BuildValidRequestFromRoots(ImmutableArray<PermissionLimitedRoot> roots,
        int maximumDiagnosticCount = 16, DateTimeOffset? createdAtUtc = null, DateTimeOffset? expiresAtUtc = null)
    {
        IReadOnlyList<PermissionLimitedRoot> rootsForManifest = roots.IsDefaultOrEmpty ? Array.Empty<PermissionLimitedRoot>() : roots;
        var manifest = Manifest(rootsForManifest);
        var digest = manifest.IsSuccess ? manifest.Value!.Digest : new string('0', 64);
        return new ElevatedScanRetryRequest(
            ElevatedScanRetryProtocol.Version, ElevatedScanOperation.RetryPermissionLimitedRoots,
            new string('a', ElevatedScanRetryProtocol.MinNonceLength), createdAtUtc ?? Now, expiresAtUtc ?? Now.AddMinutes(1),
            ScanId, DriveIdentity, digest, roots, maximumDiagnosticCount);
    }
}
