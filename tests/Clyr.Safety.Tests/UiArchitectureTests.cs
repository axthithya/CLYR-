namespace Clyr.Safety.Tests;

public sealed class UiArchitectureTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string Pages = Path.Combine(Root, "src", "Clyr.App", "Pages");
    private static readonly string[] PageNames = ["Overview", "Scan", "Results", "History", "DeveloperMode", "Privacy", "Licenses", "About", "Settings"];

    [Fact]
    public void EveryDestinationIsARealPageWithItsOwnCodeBehind()
    {
        foreach (var name in PageNames)
        {
            Assert.True(File.Exists(Path.Combine(Pages, name + "Page.xaml")), name + " XAML is missing.");
            Assert.True(File.Exists(Path.Combine(Pages, name + "Page.xaml.cs")), name + " code-behind is missing.");
        }
        var shell = Read(Path.Combine(Root, "src", "Clyr.App", "MainWindow.xaml"));
        Assert.Contains("ContentControl", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("DriveSelector", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Start Analysis", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPageUsesSharedResponsiveHostWithScrollContract()
    {
        var host = Read(Path.Combine(Root, "src", "Clyr.App", "Controls", "ResponsivePageHost.xaml"));
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", host, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollMode=\"Auto\"", host, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", host, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollMode=\"Disabled\"", host, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"1120\"", host, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\"", host, StringComparison.Ordinal);
        foreach (var name in PageNames)
        {
            var xaml = Read(Path.Combine(Pages, name + "Page.xaml"));
            Assert.Contains("ResponsivePageHost", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ScanControlsBelongOnlyToScanExperience()
    {
        var scan = Read(Path.Combine(Pages, "ScanPage.xaml"));
        Assert.Contains("Local volume selector", scan, StringComparison.Ordinal);
        Assert.Contains("Quick Analysis mode card", scan, StringComparison.Ordinal);
        Assert.Contains("Deep Analysis mode card", scan, StringComparison.Ordinal);
        Assert.Contains("Start Analysis", scan, StringComparison.Ordinal);
        Assert.Contains("Cancel Analysis", scan, StringComparison.Ordinal);
        foreach (var name in new[] { "Overview", "Results", "History", "DeveloperMode", "Privacy", "Licenses", "About", "Settings" })
        {
            var xaml = Read(Path.Combine(Pages, name + "Page.xaml"));
            Assert.DoesNotContain("Local volume selector", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Cancel Analysis", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Start Analysis", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NormalPagesUsePolishedCopyAndNoPhaseLanguage()
    {
        foreach (var name in PageNames.Where(name => name != "About"))
        {
            var xaml = Read(Path.Combine(Pages, name + "Page.xaml"));
            foreach (var forbidden in new[] { "Phase 1", "Phase 2", "Phase 3", "Phase 4", "engineering foundation", "outside Phase", "detection-only rules active" })
                Assert.DoesNotContain(forbidden, xaml, StringComparison.OrdinalIgnoreCase);
        }
        var combined = string.Join(Environment.NewLine, PageNames.Select(name => Read(Path.Combine(Pages, name + "Page.xaml"))));
        Assert.Contains("Private", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("read-only", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PageSpecificViewModelsAndResponsiveReflowExist()
    {
        var models = Read(Path.Combine(Root, "src", "Clyr.App", "ViewModels", "AppSessionViewModel.cs")) + Read(Path.Combine(Root, "src", "Clyr.App", "ViewModels", "HistoryViewModel.cs"));
        foreach (var name in PageNames) Assert.Contains("class " + name + "ViewModel", models, StringComparison.Ordinal);
        var responsive = Read(Path.Combine(Pages, "OverviewPage.xaml.cs")) + Read(Path.Combine(Pages, "ScanPage.xaml.cs")) + Read(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        Assert.Contains("LayoutModeChanged", responsive, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow", responsive, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedHostHasConsistentGuttersAndBreakpoints()
    {
        var codeBehind = Read(Path.Combine(Root, "src", "Clyr.App", "Controls", "ResponsivePageHost.xaml.cs"));
        Assert.Contains("< 760", codeBehind, StringComparison.Ordinal);
        Assert.Contains("< 1200", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ResponsivePageWidth.Narrow", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ResponsivePageWidth.Medium", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ResponsivePageWidth.Wide", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void NoPagesHaveUnsafeFixedRootWidthOrLargeLeftMargin()
    {
        foreach (var name in PageNames)
        {
            var xaml = Read(Path.Combine(Pages, name + "Page.xaml"));
            Assert.DoesNotContain("Width=\"1", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Margin=\"200", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Margin=\"300", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("TranslateTransform", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AllPagesHavePageRootAutomationNames()
    {
        foreach (var name in PageNames)
        {
            var xaml = Read(Path.Combine(Pages, name + "Page.xaml"));
            Assert.Contains("AutomationProperties.Name", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NoPagesContainCleanupOrElevationLanguage()
    {
        foreach (var name in PageNames)
        {
            var xaml = Read(Path.Combine(Pages, name + "Page.xaml"));
            foreach (var forbidden in new[] { "DeleteFile", "MoveFile", "Process.Start", "runas", "requireAdministrator", "powershell", "cmd.exe" })
                Assert.DoesNotContain(forbidden, xaml, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AccessibilityNamesAndTextAlternativesCoverCriticalFlow()
    {
        var all = string.Join(Environment.NewLine, Directory.EnumerateFiles(Pages, "*.xaml").Select(Read));
        foreach (var required in new[] { "AutomationProperties.Name=\"Overview page\"", "AutomationProperties.Name=\"Scan page\"", "AutomationProperties.Name=\"Results page\"", "Contributor text alternatives", "AutomationProperties.LiveSetting=\"Polite\"", "Local snapshot history" })
            Assert.Contains(required, all, StringComparison.Ordinal);
    }

    private static string Read(string path) => File.ReadAllText(path);
}
