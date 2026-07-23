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
        // OverviewPage.Humanize, which letter-spaces every embedded acronym. Findings were since restructured to
        // a natural per-status phrase (section 12) that never touches SafetyStatus at all.
        Assert.DoesNotContain("OverviewPage.Humanize(item.Explanation.SafetyStatus", code, StringComparison.Ordinal);
        Assert.Contains("NaturalFindingStatus(item.Category, item.Status)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WarningBadgeNamesTheAccessIssueCategoryRatherThanAGenericCount()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        // Section 15: "access issues" reads more naturally in normal UI than the more technical "access warnings".
        Assert.Contains("$\"{warningCount:N0} access {(warningCount == 1 ? \"issue\" : \"issues\")}\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void CoverageHeroNeverRendersAsUnavailableAccounted()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        // Section 4 correction: one canonical phrase, and "Coverage" is now a permanent caption (never hidden) —
        // never alternating between "Unavailable"/"Cannot be compared directly"/"Coverage unavailable".
        Assert.Contains("\"Cannot be calculated\"", code, StringComparison.Ordinal);

        var xaml = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml"));
        Assert.Contains("x:Name=\"AccountedPercentCaptionText\" Text=\"Coverage\"", xaml, StringComparison.Ordinal);
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
        Assert.Contains("topLevelDirectoriesAll = topLevelSorted.Select", code, StringComparison.Ordinal);
        Assert.Contains("NestedDirectoryList.ItemsSource = nested.Select", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DirectoryList.ItemsSource = r.LargestDirectories.Select", code, StringComparison.Ordinal);
        // Section 10: zero-byte and very small entries are hidden by default (top 10 + >=0.5% share, bounded),
        // revealed only via "Show all folders".
        Assert.Contains("topLevelDirectoriesDefault = topLevelDirectoriesAll", code, StringComparison.Ordinal);
        Assert.Contains("DefaultTopLevelFolderMinPercentage", code, StringComparison.Ordinal);
        Assert.Contains("\"Show all folders\"", code, StringComparison.Ordinal);

        var xaml = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml"));
        Assert.Contains("Largest top-level folders", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NestedDirectoriesExpander\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Nested folders can overlap with parent folders shown above and should not be added together.", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShowAllFoldersButton\"", xaml, StringComparison.Ordinal);
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
        // Section 13: plain-language explanation — no "classification evidence"/"enriched root contribution"/
        // "unknown reconciliation bytes" jargon in the normal-UI note.
        Assert.Contains("Why did the categorized percentage change? Administrator Retry found more storage", code, StringComparison.Ordinal);
        Assert.Contains("did not return enough file-level details to categorize all of it", code, StringComparison.Ordinal);

        var xaml = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml"));
        Assert.Contains("x:Name=\"ClassificationRetryNoteText\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ContributorFileAndFindingProvenanceAreExplainedAfterRetry()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        Assert.Contains("Some administrator-inspected storage could not be categorized and is not shown as a named category below.", code, StringComparison.Ordinal);
        Assert.Contains("Individual-file rankings remain based on the ", code, StringComparison.Ordinal);
        Assert.Contains("Large files are not automatically safe to remove.", code, StringComparison.Ordinal);
        Assert.Contains("Findings remain based on the original Drive Analysis.", code, StringComparison.Ordinal);
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
