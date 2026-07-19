using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>
/// Phase (progressive full-drive analysis): the normal-app scan lifecycle is mode-agnostic — there is no more
/// normal-user Quick/Deep selection to toggle (see the removed <c>ScanModeSelector</c>/mode-suffixed
/// <c>ScanUiLifecycleState</c> values), only whether a scan is running, has early insights ready, or has reached
/// a terminal state.
/// </summary>
public sealed class ScanUiLifecycleTests
{
    [Fact]
    public void NotScanningWithNoAttemptIsIdle() =>
        Assert.Equal(ScanUiLifecycleState.Idle, ScanUiLifecycle.Compute(false, null, false, null));

    [Fact]
    public void ScanningWithNoLiveStatusYetIsPreparing() =>
        Assert.Equal(ScanUiLifecycleState.Preparing, ScanUiLifecycle.Compute(true, null, false, null));

    [Fact]
    public void ScanningWithPreparingStatusIsPreparing() =>
        Assert.Equal(ScanUiLifecycleState.Preparing, ScanUiLifecycle.Compute(true, ScanStatus.Preparing, false, null));

    [Fact]
    public void ScanningWithoutEarlyInsightsIsRunningBeforeInsights() =>
        Assert.Equal(ScanUiLifecycleState.RunningBeforeInsights, ScanUiLifecycle.Compute(true, ScanStatus.Scanning, false, null));

    [Fact]
    public void ScanningWithEarlyInsightsReadyIsRunningWithInsights() =>
        Assert.Equal(ScanUiLifecycleState.RunningWithInsights, ScanUiLifecycle.Compute(true, ScanStatus.Scanning, true, null));

    [Fact]
    public void CancellingSubStateIsDetectedWhileScanningRegardlessOfEarlyInsights() =>
        Assert.Equal(ScanUiLifecycleState.Cancelling, ScanUiLifecycle.Compute(true, ScanStatus.Cancelling, true, null));

    [Theory]
    [InlineData(ScanStatus.Completed, ScanUiLifecycleState.Completed)]
    [InlineData(ScanStatus.CompletedWithWarnings, ScanUiLifecycleState.CompletedWithWarnings)]
    [InlineData(ScanStatus.Cancelled, ScanUiLifecycleState.Cancelled)]
    [InlineData(ScanStatus.Failed, ScanUiLifecycleState.Failed)]
    public void TerminalStatesReflectTheAttemptsOwnStatus(ScanStatus status, ScanUiLifecycleState expected)
    {
        var attempt = ScanFixtures.Result(ScanMode.Deep, status);
        Assert.Equal(expected, ScanUiLifecycle.Compute(false, null, false, attempt));
    }

    [Fact]
    public void StaleAttemptIsNeverConsultedWhileANewScanIsRunning()
    {
        var previous = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Completed);
        Assert.Equal(ScanUiLifecycleState.RunningBeforeInsights, ScanUiLifecycle.Compute(true, ScanStatus.Scanning, false, previous));
    }
}

internal static class ScanFixtures
{
    public static ScanResult Result(ScanMode mode, ScanStatus status, long observed = 1000, long? driveUsed = 2000,
        long classified = 600, long unknown = 400, long inaccessible = 0, ClassificationResult? classification = null,
        AllocationAccounting? allocation = null)
    {
        var started = DateTimeOffset.UtcNow.AddSeconds(-5);
        var ended = DateTimeOffset.UtcNow;
        var coverage = new ScanCoverage(10, 2, inaccessible, 0, 0, 0, 0, false, false, false);
        // Not clamped: observed can legitimately exceed driveUsed (hard links, sparse files, basis
        // differences) — see AccountingConsistency.LogicalExceedsDriveUsed and Phase 7.2.5.
        return new(Guid.NewGuid(), status, mode, "C:\\", "NTFS", started, ended, observed, driveUsed,
            driveUsed.HasValue ? driveUsed.Value - observed : null, MeasurementPrecision.Estimated,
            "Logical metadata bytes only.", coverage, [], [], [], [], [],
            status == ScanStatus.Failed ? "scan.failed" : null, status == ScanStatus.Failed ? "Simulated failure." : null,
            classification, allocation);
    }
}
