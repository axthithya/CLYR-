namespace Clyr.Safety.Tests;

/// <summary>
/// Phase (final Results/Administrator Retry/cross-page consistency correction, driven by real-machine
/// screenshots): guards the specific structural and wording fixes for the confirmed defects — the "C L Y R"
/// branding corruption, incorrect acronym casing, the "Unavailable"/"accounted" coverage-hero pairing, generic
/// warning wording, inconsistent retry button labels, and the overlapping top-level/nested directory listing.
/// </summary>
public sealed class ResultsFinalCorrectionTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string Pages = Path.Combine(Root, "src", "Clyr.App", "Pages");

    [Fact]
    public void ResultsFindingsNeverRunSafetyStatusProseThroughTheEnumHumanizer()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        // The bug: SafetyStatus (already a complete sentence, possibly containing "CLYR") was passed through
        // OverviewPage.Humanize, which letter-spaces every embedded acronym. The fix uses SafetyStatus as-is and
        // only humanizes the enum-name fallback.
        Assert.Contains("var safety = item.Explanation.SafetyStatus.Length > 0", code, StringComparison.Ordinal);
        Assert.DoesNotContain("OverviewPage.Humanize(item.Explanation.SafetyStatus", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WarningBadgeNamesTheAccessWarningCategoryRatherThanAGenericCount()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        Assert.Contains("$\"{warningCount:N0} access {(warningCount == 1 ? \"warning\" : \"warnings\")}\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void CoverageHeroNeverRendersAsUnavailableAccounted()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        Assert.Contains("\"Coverage unavailable\"", code, StringComparison.Ordinal);
        Assert.Contains("AccountedPercentCaptionText.Visibility = summary.AccountedPercentage is not null ? Visibility.Visible : Visibility.Collapsed;", code, StringComparison.Ordinal);

        var xaml = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml"));
        Assert.Contains("x:Name=\"AccountedPercentCaptionText\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AdministratorRetryButtonWordingIsConsistentAcrossEveryState()
    {
        var core = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Core", "AdministratorRetryUx.cs"));
        Assert.Contains("public const string RetryAgainButtonText = ButtonText;", core, StringComparison.Ordinal);
    }

    [Fact]
    public void LargestDirectoriesMainListIsTopLevelOnlyWithASeparateOverlapExplainedNestedView()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        Assert.Contains("DirectoryList.ItemsSource = r.TopLevelDirectories.Select", code, StringComparison.Ordinal);
        Assert.Contains("NestedDirectoryList.ItemsSource = nested.Select", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DirectoryList.ItemsSource = r.LargestDirectories.Select", code, StringComparison.Ordinal);

        var xaml = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml"));
        Assert.Contains("Largest top-level folders", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NestedDirectoriesExpander\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Nested folders can overlap with parent folders shown above and should not be added together.", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void PercentagesAcrossContributorsDirectoriesAndFilesNameTheirDenominator()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        Assert.Contains("share of observed logical storage", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassificationDropAfterRetryIsExplainedRatherThanLeftUnexplained()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        Assert.Contains("Administrator Retry added storage-accounting evidence without enough per-file/category", code, StringComparison.Ordinal);
        Assert.Contains("Administrator-inspected logical storage remaining", code, StringComparison.Ordinal);

        var xaml = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml"));
        Assert.Contains("x:Name=\"ClassificationRetryNoteText\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ContributorFileAndFindingProvenanceAreExplainedAfterRetry()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        Assert.Contains("Rankings use classified logical storage observed by CLYR. Administrator-inspected storage without category evidence remains unclassified.", code, StringComparison.Ordinal);
        Assert.Contains("Individual-file rankings remain based on the original Drive Analysis.", code, StringComparison.Ordinal);
        Assert.Contains("Findings remain based on classification evidence from the original Drive Analysis.", code, StringComparison.Ordinal);
    }

    [Fact]
    public void NoUserFacingStringLiteralContainsSpacedOutClyrBranding()
    {
        foreach (var file in new[] { "ResultsPage.xaml.cs", "OverviewPage.xaml.cs", "ScanPage.xaml.cs", "HistoryPage.xaml.cs" })
        {
            var text = File.ReadAllText(Path.Combine(Pages, file));
            Assert.DoesNotContain("C L Y R", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ResultsViewModelPersistsAnEnrichedRetryResultOverItsOriginalHistoryRecord()
    {
        var code = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "ViewModels", "AppSessionViewModel.cs"));
        Assert.Contains("snapshotStore.UpdateAsync(snapshotFactory.Create(enriched))", code, StringComparison.Ordinal);
    }
}
