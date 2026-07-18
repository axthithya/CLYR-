using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>Phase 7.2.6G3: the pure, in-memory eligibility service and request factory that converts one
/// completed non-elevated Full Analysis result into one safe <see cref="ElevatedScanRetryRequest"/>. No
/// filesystem, IPC, process, or UAC involved anywhere — every fixture below is an in-memory typed value.</summary>
public sealed class ElevatedScanRetryRequestFactoryTests
{
    private static readonly Guid ScanId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-18T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private const string RootDrive = "C:\\";

    [Fact]
    public void EligibleDeepResultBuildsAValidRequest()
    {
        var result = DeepResult([Contribution("C:\\Alpha", ScanRootEnumerationState.InaccessibleAtRoot, 0)]);

        var built = Factory().Build(result);

        Assert.True(built.IsSuccess);
        Assert.Equal(ElevatedScanRetryProtocol.Version, built.Request!.ProtocolVersion);
        Assert.Equal(ElevatedScanOperation.RetryPermissionLimitedRoots, built.Request.Operation);
        Assert.Equal(ScanId, built.Request.OriginalScanExecutionId);
        Assert.Single(built.Request.PermissionLimitedRoots);
        Assert.True(ElevatedScanRetryValidator.Validate(built.Request, Now).IsValid);
    }

    [Fact]
    public void QuickResultIsRejected()
    {
        var result = DeepResult([Contribution("C:\\Alpha", ScanRootEnumerationState.InaccessibleAtRoot, 0)]) with { Mode = ScanMode.Quick };

        var built = Factory().Build(result);

        Assert.Equal(ElevatedScanRetryEligibilityOutcome.QuickAnalysisNotEligible, built.Outcome);
        Assert.False(built.IsSuccess);
    }

    [Fact]
    public void IncompleteDeepResultIsRejected()
    {
        var result = DeepResult([Contribution("C:\\Alpha", ScanRootEnumerationState.InaccessibleAtRoot, 0)]) with { Status = ScanStatus.Cancelled };

        var built = Factory().Build(result);

        Assert.Equal(ElevatedScanRetryEligibilityOutcome.ScanNotCompleted, built.Outcome);
    }

    [Fact]
    public void ScanWithNoRootContributionsIsRejected()
    {
        var built = Factory().Build(DeepResult([]));
        Assert.Equal(ElevatedScanRetryEligibilityOutcome.NoRootContributions, built.Outcome);
    }

    [Fact]
    public void ScanWithNoReplaceablePermissionLimitedRootsIsRejected()
    {
        var result = DeepResult([Contribution("C:\\Alpha", ScanRootEnumerationState.Completed, 0)]);

        var built = Factory().Build(result);

        Assert.Equal(ElevatedScanRetryEligibilityOutcome.NoReplaceablePermissionLimitedRoots, built.Outcome);
    }

    [Fact]
    public void InaccessibleAtRootIsIncluded()
    {
        var result = DeepResult([Contribution("C:\\Alpha", ScanRootEnumerationState.InaccessibleAtRoot, 0)]);

        var built = Factory().Build(result);

        Assert.True(built.IsSuccess);
        Assert.Equal("C:\\Alpha", built.Request!.PermissionLimitedRoots.Single().NormalizedRootPath);
    }

    [Fact]
    public void PartiallyObservedWithInaccessibleEntriesIsIncluded()
    {
        var result = DeepResult([Contribution("C:\\Beta", ScanRootEnumerationState.PartiallyObserved, 2)]);

        var built = Factory().Build(result);

        Assert.True(built.IsSuccess);
        Assert.Equal("C:\\Beta", built.Request!.PermissionLimitedRoots.Single().NormalizedRootPath);
    }

    [Fact]
    public void CompletedRootIsExcluded()
    {
        var result = DeepResult(
        [
            Contribution("C:\\Alpha", ScanRootEnumerationState.InaccessibleAtRoot, 0),
            Contribution("C:\\Gamma", ScanRootEnumerationState.Completed, 0)
        ]);

        var built = Factory().Build(result);

        Assert.True(built.IsSuccess);
        Assert.DoesNotContain(built.Request!.PermissionLimitedRoots, root => root.NormalizedRootPath == "C:\\Gamma");
    }

    [Fact]
    public void NestedDiagnosticPathWithoutAContributionIsNotIncluded()
    {
        // Only the top-level contribution "C:\Delta" exists — a nested warning path such as "C:\Delta\Sub" has
        // no matching ScanRootContribution and can never be selected, because this service only ever iterates
        // ScanResult.RootContributions, never any nested diagnostic/issue list.
        var result = DeepResult([Contribution("C:\\Delta", ScanRootEnumerationState.PartiallyObserved, 1)]);

        var built = Factory().Build(result);

        Assert.True(built.IsSuccess);
        Assert.All(built.Request!.PermissionLimitedRoots, root => Assert.DoesNotContain("Sub", root.NormalizedRootPath, StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateRootsAreRejected()
    {
        var result = DeepResult(
        [
            Contribution("C:\\Alpha", ScanRootEnumerationState.InaccessibleAtRoot, 0),
            Contribution("C:\\Alpha", ScanRootEnumerationState.InaccessibleAtRoot, 0)
        ]);

        var built = Factory().Build(result);

        Assert.Equal(ElevatedScanRetryEligibilityOutcome.DuplicateRoot, built.Outcome);
    }

    [Fact]
    public void CaseOnlyDuplicatesAreRejected()
    {
        var result = DeepResult(
        [
            Contribution("C:\\Alpha", ScanRootEnumerationState.InaccessibleAtRoot, 0),
            Contribution("C:\\ALPHA\\", ScanRootEnumerationState.InaccessibleAtRoot, 0)
        ]);

        var built = Factory().Build(result);

        Assert.Equal(ElevatedScanRetryEligibilityOutcome.DuplicateRoot, built.Outcome);
    }

    [Fact]
    public void ParentChildOverlapIsRejected()
    {
        var result = DeepResult(
        [
            Contribution("C:\\Users", ScanRootEnumerationState.InaccessibleAtRoot, 0),
            Contribution("C:\\Users\\Example", ScanRootEnumerationState.InaccessibleAtRoot, 0)
        ]);

        var built = Factory().Build(result);

        Assert.Equal(ElevatedScanRetryEligibilityOutcome.OverlappingRoots, built.Outcome);
    }

    [Fact]
    public void RootOutsideTheSelectedDriveIsRejected()
    {
        var result = DeepResult([Contribution("D:\\Other", ScanRootEnumerationState.InaccessibleAtRoot, 0)]);

        var built = Factory().Build(result);

        Assert.Equal(ElevatedScanRetryEligibilityOutcome.RootOutsideDrive, built.Outcome);
    }

    [Fact]
    public void GeneratedManifestDigestValidatesSuccessfully()
    {
        var result = DeepResult([Contribution("C:\\Alpha", ScanRootEnumerationState.InaccessibleAtRoot, 0)]);

        var built = Factory().Build(result);

        var recomputed = ElevatedScanManifestBuilder.Build(built.Request!.ProtocolVersion, built.Request.Operation,
            built.Request.OriginalScanExecutionId, built.Request.DriveIdentity, built.Request.PermissionLimitedRoots);
        Assert.True(recomputed.IsSuccess);
        Assert.Equal(recomputed.Value!.Digest, built.Request.PermissionLimitedManifestDigest);
    }

    [Fact]
    public void NonceAndExpiryAreGeneratedInternally()
    {
        var result = DeepResult([Contribution("C:\\Alpha", ScanRootEnumerationState.InaccessibleAtRoot, 0)]);

        var built = Factory().Build(result);

        Assert.Equal(64, built.Request!.Nonce.Length);
        Assert.Equal(Now, built.Request.CreatedAtUtc);
        Assert.Equal(Now + ElevatedScanRetryProtocol.MaxRequestLifetime, built.Request.ExpiresAtUtc);
    }

    [Fact]
    public void RequestRootOrderingIsDeterministic()
    {
        var resultA = DeepResult(
        [
            Contribution("C:\\Zulu", ScanRootEnumerationState.InaccessibleAtRoot, 0),
            Contribution("C:\\Alpha", ScanRootEnumerationState.InaccessibleAtRoot, 0)
        ]);
        var resultB = DeepResult(
        [
            Contribution("C:\\Alpha", ScanRootEnumerationState.InaccessibleAtRoot, 0),
            Contribution("C:\\Zulu", ScanRootEnumerationState.InaccessibleAtRoot, 0)
        ]);

        var builtA = Factory().Build(resultA);
        var builtB = Factory().Build(resultB);

        Assert.Equal(
            builtA.Request!.PermissionLimitedRoots.Select(root => root.NormalizedRootPath),
            builtB.Request!.PermissionLimitedRoots.Select(root => root.NormalizedRootPath));
    }

    private static ScanResult DeepResult(IReadOnlyList<ScanRootContribution> contributions) =>
        ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed, observed: 1000) with { ScanId = ScanId, Root = RootDrive, RootContributions = contributions };

    private static ScanRootContribution Contribution(string path, ScanRootEnumerationState state, long inaccessibleEntries) =>
        new(ElevatedScanManifestBuilder.NormalizePath(path), null, path, state, 1, 0, 100, 80, 80, 0, 0, 0, 0, inaccessibleEntries, 0, 0);

    private static ElevatedScanRetryRequestFactory Factory() =>
        new(new FakeDrives(), new FakeDriveIdentityProvider(), new FixedClock(Now), new FixedNonceGenerator());

    private sealed class FakeDrives : IDriveDiscovery
    {
        public IReadOnlyList<DriveSummary> Discover() =>
            [new(RootDrive, "Fixture", "NTFS", DriveKind.Fixed, true, true, true, "Supported", 1_000_000, 500_000, 500_000)];
    }

    private sealed class FakeDriveIdentityProvider : IDriveIdentityProvider
    {
        public SnapshotDrive Identify(string root, string fileSystem, long? usedBytes) =>
            new("drive-fingerprint-request-factory", DriveIdentityQuality.Stable, root, fileSystem, 1_000_000, usedBytes, 500_000);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FixedNonceGenerator : INonceGenerator
    {
        public string CreateNonce() => new('a', 64);
    }
}
