namespace Clyr.Safety.Tests;

/// <summary>
/// Phase (usability/persistence pass driven by real-machine screenshots): guards the removal of the separate
/// "Early Insights" experience from the running-analysis UI, the natural-language running-state redesign, and
/// the Overview startup restoration that surfaces a previously saved analysis instead of a false "No analysis
/// yet" state.
/// </summary>
public sealed class RunningAnalysisAndStartupRestorationTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string Pages = Path.Combine(Root, "src", "Clyr.App", "Pages");

    [Fact]
    public void ScanPageRunningStateHasNoEarlyInsightsPresentationOrRedactedLocationRow()
    {
        var xaml = File.ReadAllText(Path.Combine(Pages, "ScanPage.xaml"));
        var code = File.ReadAllText(Path.Combine(Pages, "ScanPage.xaml.cs"));
        Assert.DoesNotContain("EarlyInsightsPanel", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewCurrentInsightsButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentLocation", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("EarlyInsightsReady", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewCurrentInsights", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ScanPageRunningStateUsesNaturalMetricLabels()
    {
        var xaml = File.ReadAllText(Path.Combine(Pages, "ScanPage.xaml"));
        Assert.Contains("Storage found so far", xaml, StringComparison.Ordinal);
        Assert.Contains("Files checked", xaml, StringComparison.Ordinal);
        Assert.Contains("Folders checked", xaml, StringComparison.Ordinal);
        Assert.Contains("Access issues", xaml, StringComparison.Ordinal);
        Assert.Contains("Time elapsed", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Bytes observed", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ScanPageProgressBarIsAlwaysIndeterminateSinceMappedStorageIsNotScanCompletion()
    {
        // Section 7 correction (confirmed defect): storage-mapped coverage is not equivalent to scan-completion
        // percentage — a drive can reach high mapped coverage quickly while most files/folders remain unexamined.
        // The progressive backend exposes no genuine work-completion estimate, so the bar must never be driven by
        // ProvisionalCoveragePercentage (or any other coverage figure).
        var code = File.ReadAllText(Path.Combine(Pages, "ScanPage.xaml.cs"));
        Assert.Contains("ActiveProgress.IsIndeterminate = true;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ActiveProgress.IsIndeterminate = coveragePercentage", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ActiveProgress.Value = ", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultsPageNoLongerShowsProvisionalContributorDirectoryOrFileRankings()
    {
        var xaml = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml"));
        Assert.DoesNotContain("ProvisionalContributorList", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ProvisionalDirectoryList", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ProvisionalFileList", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewShowsLatestSavedAnalysisInsteadOfFalseNoAnalysisYetWhenHistoryHasARecord()
    {
        var xaml = File.ReadAllText(Path.Combine(Pages, "OverviewPage.xaml"));
        var code = File.ReadAllText(Path.Combine(Pages, "OverviewPage.xaml.cs"));
        Assert.Contains("x:Name=\"SavedOnlyPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Latest saved analysis", xaml, StringComparison.Ordinal);
        Assert.Contains("var savedOnly = !hasResult && !scanning && ViewModel.LatestSaved is not null;", code, StringComparison.Ordinal);
        Assert.Contains("FirstRunPanel.Visibility = !scanning && !hasResult && !savedOnly", code, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewViewModelExposesLatestSavedFromHistoryOrderedNewestFirst()
    {
        var code = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "ViewModels", "AppSessionViewModel.cs"));
        Assert.Contains("public SnapshotSummary? LatestSaved => RecentAnalyses.Count > 0 ? RecentAnalyses[0] : null;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewRunningStateNoLongerOffersViewCurrentInsights()
    {
        var xaml = File.ReadAllText(Path.Combine(Pages, "OverviewPage.xaml"));
        var code = File.ReadAllText(Path.Combine(Pages, "OverviewPage.xaml.cs"));
        Assert.DoesNotContain("ViewCurrentInsightsFromOverview", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewCurrentInsights", code, StringComparison.Ordinal);
    }
}
