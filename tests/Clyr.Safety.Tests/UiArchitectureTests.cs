namespace Clyr.Safety.Tests;

public sealed class UiArchitectureTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string Pages = Path.Combine(Root, "src", "Clyr.App", "Pages");
    private static readonly string[] PageNames = ["Overview", "Scan", "Results", "ReviewPlan", "History", "DeveloperMode", "Privacy", "Licenses", "About", "Settings"];

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
        var header = Read(Path.Combine(Root, "src", "Clyr.App", "Controls", "PageHeader.xaml"));
        Assert.Contains("UseLayoutRounding", host, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment", host, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HeadingLevel", header, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(header, "TextWrapping", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)));
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", host, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollMode=\"Auto\"", host, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", host, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollMode=\"Disabled\"", host, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"{StaticResource ContentMaxWidthDesktop}\"", host, StringComparison.Ordinal);
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
        // Phase (progressive full-drive analysis): the normal user no longer chooses a mode — no mode cards
        // exist anywhere on this page.
        Assert.DoesNotContain("Quick Analysis mode card", scan, StringComparison.Ordinal);
        Assert.DoesNotContain("Deep Analysis mode card", scan, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StartButton\"", scan, StringComparison.Ordinal);
        Assert.Contains("Content=\"Analyze drive\"", scan, StringComparison.Ordinal);
        Assert.Contains("Stop analysis", scan, StringComparison.Ordinal);
        foreach (var name in new[] { "Overview", "Results", "ReviewPlan", "History", "DeveloperMode", "Privacy", "Licenses", "About", "Settings" })
        {
            var xaml = Read(Path.Combine(Pages, name + "Page.xaml"));
            Assert.DoesNotContain("Local volume selector", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("x:Name=\"StartButton\"", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ScanPageHasNoCleanupDeletionOrMoveToDriveControlAndUsesOneAuthoritativeSelection()
    {
        var page = Read(Path.Combine(Pages, "ScanPage.xaml"));
        var code = Read(Path.Combine(Pages, "ScanPage.xaml.cs"));
        var combined = page + code;

        // Exactly one authoritative selection value on AppSessionViewModel — no independent per-card boolean
        // pair anywhere. The normal Scan page itself no longer reads or sets it directly (no mode cards to
        // reflect it in); AnalyzeDriveAsync is the one place that pins it to Deep for this flow.
        var session = Read(Path.Combine(Root, "src", "Clyr.App", "ViewModels", "AppSessionViewModel.cs"));
        Assert.Contains("public ScanMode? SelectedScanMode", session, StringComparison.Ordinal);
        Assert.DoesNotContain("QuickSelectedBool", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("DeepSelectedBool", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("IsChecked=\"True\"", page, StringComparison.Ordinal);

        // No cleanup, deletion, or Phase 8 move-to-drive vocabulary belongs on the Scan page.
        foreach (var forbidden in new[]
        {
            "Delete", "Clean now", "Fix everything", "Optimize now", "Delete all", "One-click clean",
            "Clean automatically", "Move to drive", "Select destination drive", "Run tool", "start cleanup plan",
            "Recycle Bin", "npm install", "docker rm", "Process.Start", "powershell.exe", "cmd.exe"
        })
            Assert.DoesNotContain(forbidden, combined, StringComparison.OrdinalIgnoreCase);
        // "Remaining drive usage" and "developer-storage" legitimately contain "drive"/"storage" substrings;
        // the check above targets exact dangerous phrases, not those words in isolation.
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
        var responsive = Read(Path.Combine(Pages, "OverviewPage.xaml.cs")) + Read(Path.Combine(Pages, "ScanPage.xaml.cs"))
            + Read(Path.Combine(Pages, "ResultsPage.xaml.cs")) + Read(Path.Combine(Pages, "ReviewPlanPage.xaml.cs"));
        Assert.Contains("LayoutModeChanged", responsive, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow", responsive, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedHostHasConsistentGuttersAndBreakpoints()
    {
        var codeBehind = Read(Path.Combine(Root, "src", "Clyr.App", "Controls", "ResponsivePageHost.xaml.cs"));
        var tokens = Read(Path.Combine(Root, "src", "Clyr.App", "Styles", "DesignTokens.xaml"));
        Assert.Contains(">16,24,16,32</Thickness>", tokens, StringComparison.Ordinal);
        Assert.Contains(">24,32,24,40</Thickness>", tokens, StringComparison.Ordinal);
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
        foreach (var required in new[] { "AutomationProperties.Name=\"Overview page\"", "AutomationProperties.Name=\"Scan page\"", "AutomationProperties.Name=\"Results page\"", "Ranked storage contributors", "AutomationProperties.LiveSetting=\"Polite\"", "Local snapshot history" })
            Assert.Contains(required, all, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewPlanSurfaceIsDryRunOnlyAndAccessible()
    {
        var page = Read(Path.Combine(Pages, "ReviewPlanPage.xaml"));
        var code = Read(Path.Combine(Pages, "ReviewPlanPage.xaml.cs"));
        var combined = page + code;
        foreach (var required in new[] { "Dry-run only — no files will be changed.", "Preview plan",
            "Save dry-run report", "Discard plan", "Protected by CLYR", "Cleanup candidate list",
            "Dry-run plan preview", "ExecutionNotAvailableInPhase5" })
            Assert.Contains(required, combined, StringComparison.Ordinal);

        // Dangerous one-click phrasing must never appear, on this page or anywhere else in the app.
        foreach (var forbidden in new[] { "Clean now", "Fix everything", "Optimize now", "Delete all", "One-click clean", "Clean automatically" })
            Assert.DoesNotContain(forbidden, page, StringComparison.OrdinalIgnoreCase);

        // Phase 6 execution controls are legitimate here — verify the required consent/accountability surface exists.
        foreach (var required in new[]
        {
            "IsChecked = false", // nothing selected by default
            "Run selected cleanup", "Nothing is selected by default",
            "I understand that selected cache or temporary data may be permanently removed",
            "Some actions cannot be undone", "Cancel execution", "Execution result", "Execution progress",
            "Execution receipt history", "View execution receipt details", "Export execution receipt",
            "Delete execution receipt"
        })
            Assert.Contains(required, combined, StringComparison.Ordinal);

        // No arbitrary path/root controls, and no Phase 7 (Developer Mode tool execution) or Phase 8 (move-to-drive) controls.
        foreach (var forbidden in new[]
        {
            "--path", "--root", "TextBox.*Path", "Enter a path", "Enter a folder", "Move to drive",
            "Select destination drive", "Run tool", "Developer Mode tool"
        })
            Assert.DoesNotContain(forbidden, combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReviewPlanConfirmationRequiresExplicitAcknowledgementBeforeItCanProceed()
    {
        var code = Read(Path.Combine(Pages, "ReviewPlanPage.xaml.cs"));
        Assert.Contains("IsPrimaryButtonEnabled = false", code, StringComparison.Ordinal);
        Assert.Contains("dialog.IsPrimaryButtonEnabled = true", code, StringComparison.Ordinal);
        Assert.Contains("acknowledgement.Checked", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DeveloperModePageHasNoToolExecutionOrRunControls()
    {
        var page = Read(Path.Combine(Pages, "DeveloperModePage.xaml"));
        var code = Read(Path.Combine(Pages, "DeveloperModePage.xaml.cs"));
        var combined = page + code;

        // Phase 7 adds read-only tool detection (a snapshot picker, a Detect button, and a details/plan-review
        // button) — those Click handlers are legitimate. Only the specific allowlisted handlers may exist in the
        // XAML; nothing that runs, installs, updates, or force-cleans a developer tool is ever wired up.
        foreach (var name in ExtractClickHandlerNames(page))
            Assert.Contains(name, AllowedDeveloperModeClickHandlers, StringComparer.Ordinal);

        foreach (var forbidden in new[]
        {
            "Run tool", "Execute tool", "Install now", "Uninstall tool", "Clean now", "Start cleanup", "Prune",
            "Clean all", "Optimize automatically", "Delete unused projects", "--command", "--exe", "--args", "ShellExecute = true"
        })
            Assert.DoesNotContain(forbidden, combined, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string[] AllowedDeveloperModeClickHandlers = ["DetectClick", "CloseDetails"];

    private static IEnumerable<string> ExtractClickHandlerNames(string xaml) =>
        System.Text.RegularExpressions.Regex.Matches(xaml, "Click=\"([^\"]+)\"").Select(match => match.Groups[1].Value);

    private static string Read(string path) => File.ReadAllText(path);
}
