namespace Clyr.Safety.Tests;

public sealed class OverviewDesignTests
{
    private const char Q = '"';
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string App = Path.Combine(Root, "src", "Clyr.App");
    private static readonly string Page = Read(Path.Combine("Pages", "OverviewPage.xaml"));
    private static readonly string Code = Read(Path.Combine("Pages", "OverviewPage.xaml.cs"));

    [Fact]
    public void HeaderAndInformationOrderMatchTheOverviewPurpose()
    {
        Assert.Contains("Title=" + Q + "Overview" + Q, Page, StringComparison.Ordinal);
        Assert.Contains("Understand your drive and decide what to do next.", Page, StringComparison.Ordinal);
        AssertInOrder(Page, Named("DriveHero"), Named("PrimaryActionArea"),
            Named("LatestAnalysisSection"), Named("PreviewGrid"));
        Assert.DoesNotContain("Private · Local · Read-only", Page, StringComparison.Ordinal);
    }

    [Fact]
    public void DriveHeroUsesDirectSegmentedAccountingInsteadOfDashboardWidgets()
    {
        foreach (var required in new[]
        {
            "System drive health", "DriveUsedPercentage", "Used storage", "Free storage", "Total capacity",
            "Scan readiness", "DriveFileSystem", "StorageVisualization", "UsedStorageSegment"
        })
            Assert.Contains(required, Page, StringComparison.Ordinal);
        Assert.Contains("DriveUsedColumn.Width", Code, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(StorageVisualization", Code, StringComparison.Ordinal);
        Assert.DoesNotContain(Named("DriveUsage"), Page, StringComparison.Ordinal);
        Assert.DoesNotContain("health score", Page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FirstRunAndExistingResultActionsHaveOneClearPrimaryAction()
    {
        foreach (var required in new[]
        {
            "No analysis yet", "Analyze drive",
            "View latest results", "Run another analysis", "Review potential actions"
        })
            Assert.Contains(required, Page, StringComparison.Ordinal);
        Assert.Contains("Analyze drive" + Q + " Click=" + Q + "AnalyzeDrive" + Q + " Style=" + Q + "{StaticResource PrimaryActionStyle}", Page, StringComparison.Ordinal);
        Assert.DoesNotContain("controls:EmptyState", Page, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "Run Quick Analysis", "Choose Deep Analysis", "Quick Analysis", "Deep Analysis", "Overview Quick Analysis card" })
            Assert.DoesNotContain(forbidden, Page, StringComparison.Ordinal);
    }

    [Fact]
    public void RunningStateShowsProgressAndCurrentInsightsWhenAvailable()
    {
        foreach (var required in new[] { "Analysis in progress", "RunningPanel", "ViewScanProgress", "ViewCurrentInsights", "RunningSummaryText" })
            Assert.Contains(required, Page + Code, StringComparison.Ordinal);
        Assert.Contains("RunningPanel.Visibility = scanning", Code, StringComparison.Ordinal);
    }

    [Fact]
    public void LatestAnalysisKeepsCoverageQualityAndWarningsSeparate()
    {
        foreach (var required in new[]
        {
            "LatestCoverageValue", "of used storage accounted for", "Observed", "Not observed",
            "Classified of observed", "Examined", "WarningSummary", "Analysis warnings"
        })
            Assert.Contains(required, Page + Code, StringComparison.Ordinal);
        Assert.Contains("ScanAccounting.Summarize(result)", Code, StringComparison.Ordinal);
        Assert.Contains("result.EndedAt.LocalDateTime", Code, StringComparison.Ordinal);
        Assert.Contains("FormatDuration(duration)", Code, StringComparison.Ordinal);
        Assert.DoesNotContain("LatestSummary", Page, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewSectionsAreBoundedConditionalAndTruthfullyWorded()
    {
        foreach (var required in new[]
        {
            "Top storage contributors", "Take(5)", "View all results", "Recent activity", "Take(3)",
            "View history", "Potential actions ready for review", "not guaranteed reclaimable space"
        })
            Assert.Contains(required, Page + Code, StringComparison.Ordinal);
        var models = Read(Path.Combine("ViewModels", "AppSessionViewModel.cs"));
        Assert.Contains("history.ListAsync(3)", models, StringComparison.Ordinal);
        Assert.Contains("item.Action is not null", Code, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollViewer", Page, StringComparison.Ordinal);
        Assert.DoesNotContain("DisplayPath", Code, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponsiveMotionAndAccessibilityContractsAreExplicit()
    {
        foreach (var required in new[]
        {
            "EntranceThemeTransition", "FromVerticalOffset=" + Q + "8" + Q,
            "AutomationProperties.HeadingLevel=" + Q + "Level2" + Q,
            "AutomationProperties.LiveSetting=" + Q + "Polite" + Q,
            "AccessibleText", "TextTrimming=" + Q + "CharacterEllipsis" + Q
        })
            Assert.Contains(required, Page, StringComparison.Ordinal);
        foreach (var required in new[]
        {
            "ResponsivePageWidth.Narrow", "Grid.SetColumnSpan", "FirstRunActions.Orientation", "ResultActions.Orientation"
        })
            Assert.Contains(required, Code, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewRemainsInsideItsSafetyBoundary()
    {
        var combined = Page + Code;
        foreach (var forbidden in new[]
        {
            "Process.Start", "NamedPipe", "runas", "ShellExecute", "File.Delete", "Directory.Delete",
            "File.Move", "Directory.Move", "powershell", "cmd.exe", "HttpClient", "Telemetry"
        })
            Assert.DoesNotContain(forbidden, combined, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertInOrder(string text, params string[] values)
    {
        var cursor = -1;
        foreach (var value in values)
        {
            var next = text.IndexOf(value, cursor + 1, StringComparison.Ordinal);
            Assert.True(next > cursor, value + " is missing or out of order.");
            cursor = next;
        }
    }

    private static string Named(string name) => "x:Name=" + Q + name + Q;
    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(App, relativePath));
}
