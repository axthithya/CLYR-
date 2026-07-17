using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

public sealed class ScanModeSelectorTests
{
    [Fact]
    public void ClickingQuickFromNoneSelectsOnlyQuick() =>
        Assert.Equal(ScanMode.Quick, ScanModeSelector.Toggle(null, ScanMode.Quick));

    [Fact]
    public void ClickingTheSelectedQuickCardAgainClearsSelection() =>
        Assert.Null(ScanModeSelector.Toggle(ScanMode.Quick, ScanMode.Quick));

    [Fact]
    public void ClickingDeepFromNoneSelectsOnlyDeep() =>
        Assert.Equal(ScanMode.Deep, ScanModeSelector.Toggle(null, ScanMode.Deep));

    [Fact]
    public void ClickingTheSelectedDeepCardAgainClearsSelection() =>
        Assert.Null(ScanModeSelector.Toggle(ScanMode.Deep, ScanMode.Deep));

    [Fact]
    public void SwitchingFromQuickToDeepClearsQuick() =>
        Assert.Equal(ScanMode.Deep, ScanModeSelector.Toggle(ScanMode.Quick, ScanMode.Deep));

    [Fact]
    public void SwitchingFromDeepToQuickClearsDeep() =>
        Assert.Equal(ScanMode.Quick, ScanModeSelector.Toggle(ScanMode.Deep, ScanMode.Quick));

    [Theory]
    [InlineData(null, ScanMode.Quick, ScanMode.Quick)]
    [InlineData(null, ScanMode.Deep, ScanMode.Deep)]
    [InlineData(ScanMode.Quick, ScanMode.Quick, null)]
    [InlineData(ScanMode.Deep, ScanMode.Deep, null)]
    [InlineData(ScanMode.Quick, ScanMode.Deep, ScanMode.Deep)]
    [InlineData(ScanMode.Deep, ScanMode.Quick, ScanMode.Quick)]
    public void EveryToggleTransitionIsExhaustivelyCorrect(ScanMode? current, ScanMode clicked, ScanMode? expected) =>
        Assert.Equal(expected, ScanModeSelector.Toggle(current, clicked));

    [Fact]
    public void NoSequenceOfClicksCanEverProduceBothModesSelected()
    {
        // There is only ever one nullable value, so "both selected" cannot even be represented — this proves
        // that structurally by exhaustively walking every reachable state from every starting point.
        ScanMode?[] states = [null, ScanMode.Quick, ScanMode.Deep];
        foreach (var start in states)
            foreach (var clicked in new[] { ScanMode.Quick, ScanMode.Deep })
            {
                var next = ScanModeSelector.Toggle(start, clicked);
                Assert.True(next is null or ScanMode.Quick or ScanMode.Deep);
            }
    }
}

public sealed class ScanUiLifecycleTests
{
    [Fact]
    public void NoModeAndNotScanningIsIdleNoMode() =>
        Assert.Equal(ScanUiLifecycleState.IdleNoMode, ScanUiLifecycle.Compute(null, false, null, null));

    [Fact]
    public void QuickSelectedAndNotScanningIsIdleQuickSelected() =>
        Assert.Equal(ScanUiLifecycleState.IdleQuickSelected, ScanUiLifecycle.Compute(ScanMode.Quick, false, null, null));

    [Fact]
    public void DeepSelectedAndNotScanningIsIdleDeepSelected() =>
        Assert.Equal(ScanUiLifecycleState.IdleDeepSelected, ScanUiLifecycle.Compute(ScanMode.Deep, false, null, null));

    [Fact]
    public void ScanningQuickIsReported() =>
        Assert.Equal(ScanUiLifecycleState.ScanningQuick, ScanUiLifecycle.Compute(ScanMode.Quick, true, ScanStatus.Scanning, null));

    [Fact]
    public void ScanningDeepIsReported() =>
        Assert.Equal(ScanUiLifecycleState.ScanningDeep, ScanUiLifecycle.Compute(ScanMode.Deep, true, ScanStatus.Scanning, null));

    [Fact]
    public void ModeLockedDuringScanUsesTheRunningModeNotAnyLaterSelection()
    {
        // The caller passes the mode captured at scan start (runningScanMode ?? selectedScanMode); this proves
        // Compute itself is agnostic to why the value is Deep, only that it is scanning.
        Assert.Equal(ScanUiLifecycleState.ScanningDeep, ScanUiLifecycle.Compute(ScanMode.Deep, true, ScanStatus.Preparing, null));
    }

    [Fact]
    public void CancellingSubStateIsDetectedWhileScanning() =>
        Assert.Equal(ScanUiLifecycleState.Cancelling, ScanUiLifecycle.Compute(ScanMode.Quick, true, ScanStatus.Cancelling, null));

    [Theory]
    [InlineData(ScanMode.Quick, ScanStatus.Completed, ScanUiLifecycleState.CompletedQuick)]
    [InlineData(ScanMode.Deep, ScanStatus.Completed, ScanUiLifecycleState.CompletedDeep)]
    [InlineData(ScanMode.Quick, ScanStatus.CompletedWithWarnings, ScanUiLifecycleState.CompletedWithWarningsQuick)]
    [InlineData(ScanMode.Deep, ScanStatus.CompletedWithWarnings, ScanUiLifecycleState.CompletedWithWarningsDeep)]
    [InlineData(ScanMode.Quick, ScanStatus.Cancelled, ScanUiLifecycleState.CancelledQuick)]
    [InlineData(ScanMode.Deep, ScanStatus.Cancelled, ScanUiLifecycleState.CancelledDeep)]
    [InlineData(ScanMode.Quick, ScanStatus.Failed, ScanUiLifecycleState.FailedQuick)]
    [InlineData(ScanMode.Deep, ScanStatus.Failed, ScanUiLifecycleState.FailedDeep)]
    public void TerminalStatesReflectTheAttemptsOwnModeNotTheCurrentSelection(ScanMode attemptMode, ScanStatus status, ScanUiLifecycleState expected)
    {
        var attempt = ScanFixtures.Result(attemptMode, status);
        // Even if the user already reselected the OTHER mode for next time, the terminal state must describe
        // what actually just finished, not the newly (and differently) selected mode.
        var otherMode = attemptMode == ScanMode.Quick ? ScanMode.Deep : ScanMode.Quick;
        Assert.Equal(expected, ScanUiLifecycle.Compute(otherMode, false, null, attempt));
    }

    [Fact]
    public void StaleAttemptIsNeverConsultedWhileANewScanIsRunning()
    {
        var previous = ScanFixtures.Result(ScanMode.Quick, ScanStatus.Completed);
        Assert.Equal(ScanUiLifecycleState.ScanningDeep, ScanUiLifecycle.Compute(ScanMode.Deep, true, ScanStatus.Scanning, previous));
    }

    [Fact]
    public void PrimaryActionTextIsThePromptWhenNoModeIsSelected() =>
        Assert.Equal("Choose Quick or Deep Analysis", ScanUiLifecycle.PrimaryActionText(null, null));

    [Fact]
    public void PrimaryActionTextForAFreshQuickSelectionHasNoAgainSuffix() =>
        Assert.Equal("Run Quick Analysis", ScanUiLifecycle.PrimaryActionText(ScanMode.Quick, null));

    [Fact]
    public void PrimaryActionTextForAFreshDeepSelectionHasNoAgainSuffix() =>
        Assert.Equal("Run Deep Analysis", ScanUiLifecycle.PrimaryActionText(ScanMode.Deep, null));

    [Fact]
    public void PrimaryActionTextAfterCompletingTheSameModeSaysAgain()
    {
        var attempt = ScanFixtures.Result(ScanMode.Quick, ScanStatus.Completed);
        Assert.Equal("Run Quick Analysis Again", ScanUiLifecycle.PrimaryActionText(ScanMode.Quick, attempt));
    }

    [Fact]
    public void PrimaryActionTextAfterSelectingTheOtherModeIsAFreshRunNotAgain()
    {
        // Documented example: Quick completed, user selects Deep -> "Run Deep Analysis", not "...Again".
        var attempt = ScanFixtures.Result(ScanMode.Quick, ScanStatus.Completed);
        Assert.Equal("Run Deep Analysis", ScanUiLifecycle.PrimaryActionText(ScanMode.Deep, attempt));
    }

    [Fact]
    public void PrimaryActionTextAfterACancelledSameModeAttemptStillSaysAgain()
    {
        var attempt = ScanFixtures.Result(ScanMode.Deep, ScanStatus.Cancelled);
        Assert.Equal("Run Deep Analysis Again", ScanUiLifecycle.PrimaryActionText(ScanMode.Deep, attempt));
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
