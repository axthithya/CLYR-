namespace Clyr.Safety.Tests;

/// <summary>
/// Phase (post-Administrator-Retry accounting and presentation correction): guards the Results/Overview/Scan/
/// History wording and structural fixes for the confirmed real-machine defects — a negative "Not observed"
/// value, a "Limited coverage" badge shown for a pure accounting-basis difference, and the obsolete "Run Deep
/// Analysis" text the normal progressive UI no longer supports.
/// </summary>
public sealed class AccountingPresentationUiTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string Pages = Path.Combine(Root, "src", "Clyr.App", "Pages");

    [Fact]
    public void ResultsPageShowsObservedLogicalSizeAndNotAvailableForIncompatibleAccounting()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        Assert.Contains("AccountedMetricLabel.Text = basisDiffers ? \"Observed logical size\" : \"Accounted by this scan\";", code, StringComparison.Ordinal);
        Assert.Contains("\"Not available\"", code, StringComparison.Ordinal);
        Assert.Contains("summary.PresentableUnaccountedDriveBytes", code, StringComparison.Ordinal);
        Assert.DoesNotContain("OverviewPage.FormatSigned(summary.UnaccountedDriveBytes)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultsPageShowsCoverageUnavailableBadgeAndHidesTheProportionalBarForIncompatibleAccounting()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        Assert.Contains("ScanQuality.AccountingBasisDiffers => (\"Coverage unavailable\"", code, StringComparison.Ordinal);
        Assert.Contains("StorageVisualization.Visibility = basisDiffers ? Visibility.Collapsed : Visibility.Visible;", code, StringComparison.Ordinal);
        Assert.Contains("AccountingBasisDiffersPanel.Visibility = basisDiffers ? Visibility.Visible : Visibility.Collapsed;", code, StringComparison.Ordinal);

        var xaml = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml"));
        Assert.Contains("x:Name=\"AccountingBasisDiffersPanel\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultsScanAndOverviewNeverActivelyDisplayRunDeepAnalysisText()
    {
        // Checks only the actual user-facing string literals (never a historical doc-comment reference to the
        // retired phrase, which some of these files legitimately carry to explain why the wording changed).
        foreach (var (file, forbiddenLiteral) in new[]
        {
            ("ResultsPage.xaml.cs", "\"Run Deep Analysis for more complete drive coverage.\""),
            ("ScanPage.xaml.cs", "\"Run Deep Analysis for more complete drive coverage.\""),
        })
            Assert.DoesNotContain(forbiddenLiteral, File.ReadAllText(Path.Combine(Pages, file)), StringComparison.Ordinal);
    }

    [Fact]
    public void ClassificationRemainsSeparateFromCoverageInResultsPage()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        Assert.Contains("ClassificationHeadlineText.Text = summary.ClassificationPercentage is { } classified", code, StringComparison.Ordinal);
        Assert.Contains("CoverageHeadlineText.Text = summary.AccountedPercentage is { } accounted", code, StringComparison.Ordinal);
    }

    [Fact]
    public void RetryComparisonShowsValidBeforeToUnavailableAfterAndExplainsTheBasis()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        // Never a blanket "Unavailable" swallowing a genuinely valid "before" percentage.
        Assert.Contains("var beforeText = beforePercentage is { } before ? $\"{before:F1}%\" : \"unavailable\";", code, StringComparison.Ordinal);
        Assert.Contains("var afterText = afterPercentage is { } after ? $\"{after:F1}%\" : \"unavailable\";", code, StringComparison.Ordinal);
        Assert.Contains("This is not a retry failure.", code, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewAndScanNeverShowANegativeNotObservedFigure()
    {
        var overview = File.ReadAllText(Path.Combine(Pages, "OverviewPage.xaml.cs"));
        Assert.Contains("accounting.PresentableUnaccountedDriveBytes is { } notObserved ? FormatSigned(notObserved) : \"Not available\"", overview, StringComparison.Ordinal);
        var scan = File.ReadAllText(Path.Combine(Pages, "ScanPage.xaml.cs"));
        Assert.Contains("accounting.PresentableUnaccountedDriveBytes is { } notObserved", scan, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryNeverShowsANegativeUnobservedFigureAndDistinguishesAccountingBasisDiffers()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "HistoryPage.xaml.cs"));
        Assert.Contains("PresentableUnaccountedBytes(item, detail)", code, StringComparison.Ordinal);
        Assert.Contains("ScanQuality.AccountingBasisDiffers => \"Coverage unavailable\"", code, StringComparison.Ordinal);
        Assert.Contains("private static AccountingConsistency ConsistencyFor(SnapshotSummary item)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DeveloperModeMayStillShowRawUnaccountedBytesForDiagnostics()
    {
        // Developer Mode is an explicit technical-diagnostic surface (distinct from the normal UI) — it may keep
        // showing the raw, unclamped value for troubleshooting; only the normal UI must never show a negative.
        var code = File.ReadAllText(Path.Combine(Pages, "DeveloperModePage.xaml.cs"));
        Assert.Contains("Bytes(snapshot.UnaccountedBytes)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ExporterNeverEmitsANegativeUnaccountedBytesValue()
    {
        var code = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Core", "ScanExport.cs"));
        Assert.Contains("UnaccountedBytes = PresentableUnaccountedBytes(result.UnaccountedBytes)", code, StringComparison.Ordinal);
        Assert.Contains("private static long? PresentableUnaccountedBytes(long? value) => value is < 0 ? null : value;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void NoCleanupMutationOrElevationBehaviorWasAddedByThisCorrection()
    {
        foreach (var file in new[] { "ResultsPage.xaml.cs", "OverviewPage.xaml.cs", "ScanPage.xaml.cs", "HistoryPage.xaml.cs" })
        {
            var text = File.ReadAllText(Path.Combine(Pages, file));
            foreach (var forbidden in new[]
            {
                "File.Delete", "Directory.Delete", "File.Move", "Directory.Move", "Process.Start",
                "powershell.exe", "cmd.exe", "requireAdministrator", "HttpClient"
            })
                Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
        }
    }
}
