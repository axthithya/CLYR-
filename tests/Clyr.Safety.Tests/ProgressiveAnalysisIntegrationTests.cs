namespace Clyr.Safety.Tests;

/// <summary>
/// Phase (progressive full-drive analysis): "Analyze drive" replaces the normal Quick/Deep choice with one
/// progressive experience that surfaces early insights while the existing Deep engine keeps running. These
/// checks guard the App-layer wiring this correction depends on — no dedicated Clyr.App test project exists
/// (see other *DesignTests/*ArchitectureTests files for the established pattern of structural source checks).
/// </summary>
public sealed class ProgressiveAnalysisIntegrationTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void ProgressiveSnapshotIsNeverPersistedAndIsClearedAtEveryNewAttempt()
    {
        var session = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "ViewModels", "AppSessionViewModel.cs"));
        Assert.Contains("public ProgressiveScanSnapshot? ProvisionalSnapshot { get => provisionalSnapshot; private set", session, StringComparison.Ordinal);
        Assert.Contains("ProvisionalSnapshot = null;", session, StringComparison.Ordinal);
        Assert.DoesNotContain("store.SaveAsync", session, StringComparison.Ordinal);
        Assert.DoesNotContain("snapshots.SaveAsync", session, StringComparison.Ordinal);

        // History persistence (SnapshotSavingScanService) only ever inspects ScanAsync's single awaited return
        // value — never the progress callback stream — so a progressive-progress reporter cannot reach it.
        var history = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Core", "History.cs"));
        Assert.Contains("var result = await inner.ScanAsync(request, progress, cancellationToken)", history, StringComparison.Ordinal);
        Assert.DoesNotContain("ProgressiveProgress", history, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisionalResultsPageSectionNeverExposesReviewPlanOrAdministratorRetry()
    {
        var xaml = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "Pages", "ResultsPage.xaml"));
        var start = xaml.IndexOf("x:Name=\"ProvisionalPanel\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("x:Name=\"Dashboard\"", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not locate the ProvisionalPanel section.");
        var provisionalSection = xaml[start..end];
        Assert.DoesNotContain("ReviewActions", provisionalSection, StringComparison.Ordinal);
        Assert.DoesNotContain("AdministratorRetry", provisionalSection, StringComparison.Ordinal);
        Assert.DoesNotContain("Review potential actions", provisionalSection, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultsPageOnlyShowsCompletedDashboardWhenACompletedResultExists()
    {
        var code = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "Pages", "ResultsPage.xaml.cs"));
        Assert.Contains("Dashboard.Visibility = r is not null ? Visibility.Visible : Visibility.Collapsed", code, StringComparison.Ordinal);
        // Section 9 correction: provisional visibility is never decided from "Session.Result is null" alone — a
        // previous completed result can already exist when a second analysis starts. While actively scanning,
        // any non-null snapshot belongs to the current run and must take visual priority over the old result.
        Assert.Contains("var showProvisional = snapshot is not null && (session.IsScanning || r is null);", code, StringComparison.Ordinal);
        Assert.DoesNotContain("var showProvisional = r is null && snapshot is not null;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewPlanCandidatesStillReadSessionResultLiveNeverAProvisionalSnapshot()
    {
        var session = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "ViewModels", "AppSessionViewModel.cs"));
        Assert.Contains("Session.Result is null ? new List<CleanupCandidate>() : CleanupCandidateFactory.FromScan(Session.Result)", session, StringComparison.Ordinal);
        Assert.DoesNotContain("ProvisionalSnapshot", session.Substring(session.IndexOf("class ReviewPlanViewModel", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void AdministratorRetryEligibilityAlreadyExcludesAnyNonDeepOrNonCompletedResultStructurally()
    {
        // No new guard is required: ElevatedScanRetryEligibility.Evaluate already rejects anything that is not a
        // completed Deep result, and RefreshAdministratorRetry already passes null while a scan is running — a
        // provisional snapshot is never even constructed as a ScanResult, so it can never reach this path.
        var eligibility = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Core", "ElevatedScanRetryRequestFactory.cs"));
        Assert.Contains("if (result.Mode != ScanMode.Deep)", eligibility, StringComparison.Ordinal);
        Assert.Contains("if (result.Status is not (ScanStatus.Completed or ScanStatus.CompletedWithWarnings))", eligibility, StringComparison.Ordinal);
        var session = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "ViewModels", "AppSessionViewModel.cs"));
        Assert.Contains("AdministratorRetry.Evaluate(Session.IsScanning ? null : Session.Result)", session, StringComparison.Ordinal);
    }

    [Fact]
    public void ScanPageNeverShowsFakeTimeBasedProgressMilestones()
    {
        var code = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "Pages", "ScanPage.xaml.cs"));
        foreach (var forbidden in new[] { "Task.Delay", "Thread.Sleep", "FakeProgress", "SimulatedDelay" })
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        // Stage text is derived only from real ProgressiveScanSnapshot.Stage / ScanUiLifecycleState — never a
        // percentage constant.
        Assert.Contains("StageText(session.LifecycleState, snapshot?.Stage)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void CancelledProvisionalResultIsNeverLabelledAsCompleted()
    {
        var code = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "Pages", "ResultsPage.xaml.cs"));
        Assert.Contains("stillRunning ? \"Analysis in progress\" : \"Analysis stopped\"", code, StringComparison.Ordinal);
        var scanCode = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "Pages", "ScanPage.xaml.cs"));
        Assert.Contains("completed ? \"Drive analysis complete\"", scanCode, StringComparison.Ordinal);
        Assert.Contains(": cancelled ? \"Analysis stopped\"", scanCode, StringComparison.Ordinal);
    }

    [Fact]
    public void FixtureModeNeverInvokesRealUacOrPersistsRealHistoryForProgressiveStates()
    {
        var fixture = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "UiFixtureServices.cs"));
        foreach (var forbidden in new[] { "Process.Start", "runas", "requireAdministrator", "ElevatedScannerProcessStarter" })
            Assert.DoesNotContain(forbidden, fixture, StringComparison.Ordinal);
        Assert.Contains("request.ProgressiveProgress", fixture, StringComparison.Ordinal);
        Assert.Contains("UiFixtureSnapshotStore", fixture, StringComparison.Ordinal);
    }
}
