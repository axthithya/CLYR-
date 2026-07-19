namespace Clyr.Safety.Tests;

public sealed class ScanDesignTests
{
    private const char Q = '"';
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string App = Path.Combine(Root, "src", "Clyr.App");
    private static readonly string Page = Read(Path.Combine("Pages", "ScanPage.xaml"));
    private static readonly string Code = Read(Path.Combine("Pages", "ScanPage.xaml.cs"));
    private static readonly string Session = Read(Path.Combine("ViewModels", "AppSessionViewModel.cs"));
    private static readonly string Lifecycle = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Core", "ScanUx.cs"));

    [Fact]
    public void NormalFlowShowsOneAnalyzeDriveActionNeverModeWording()
    {
        Assert.Contains("Content=" + Q + "Analyze drive" + Q, Page, StringComparison.Ordinal);
        Assert.Contains("Click=" + Q + "StartAnalysis" + Q, Page, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "Quick Analysis", "Deep Analysis", "Quick estimate", "Fast mode", "Thorough mode", "Choose Quick or Deep" })
            Assert.DoesNotContain(forbidden, Page, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyzeDriveAlwaysRunsTheExistingDeepStrategyInternally()
    {
        Assert.Contains("SelectedScanMode = ScanMode.Deep", Session, StringComparison.Ordinal);
        Assert.Contains("public Task<ScanResult?> AnalyzeDriveAsync()", Session, StringComparison.Ordinal);
        Assert.Contains("ViewModel.Session.AnalyzeDriveAsync()", Code, StringComparison.Ordinal);
    }

    [Fact]
    public void IdleStateDoesNotRenderStop()
    {
        AssertInOrder(Page, Named("SetupPanel"), Named("RunningPanel"), Named("StopButton"), Named("TerminalPanel"));
        Assert.Contains("RunningPanel.Visibility = active", Code, StringComparison.Ordinal);
        Assert.DoesNotContain("StopButton", Section(Page, "SetupPanel", "RunningPanel"), StringComparison.Ordinal);
    }

    [Fact]
    public void RunningStateShowsSecondaryStopWithConfirmation()
    {
        Assert.Contains("Content=" + Q + "Stop analysis" + Q, Page, StringComparison.Ordinal);
        Assert.Contains("QuietButtonStyle", Section(Page, "StopButton", "TerminalPanel"), StringComparison.Ordinal);
        Assert.Contains("RunningPanel.Visibility = active ? Visibility.Visible", Code, StringComparison.Ordinal);
        // Section 13: a confirmation dialog, defaulting to the safe "Cancel" choice, gates the actual stop.
        Assert.Contains("Title = " + Q + "Stop analysis?" + Q, Code, StringComparison.Ordinal);
        Assert.Contains("DefaultButton = ContentDialogButton.Close", Code, StringComparison.Ordinal);
        Assert.Contains("CloseButtonText = " + Q + "Cancel" + Q, Code, StringComparison.Ordinal);
    }

    [Fact]
    public void CancellingBlocksDuplicateCancellation()
    {
        Assert.Contains("StopButton.IsEnabled = false", Code, StringComparison.Ordinal);
        Assert.Contains("StopButton.IsEnabled = !cancelling", Code, StringComparison.Ordinal);
        Assert.Contains("Stopping analysis", Code, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletedStateContainsNoStopControl()
    {
        var terminal = Section(Page, "TerminalPanel", "/Page");
        Assert.DoesNotContain("StopButton", terminal, StringComparison.Ordinal);
        Assert.Contains("TerminalPanel.Visibility = !active && hasAttempt", Code, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletedStateShowsViewResults()
    {
        Assert.Contains("Content=" + Q + "View Results" + Q, Page, StringComparison.Ordinal);
        Assert.Contains("ResultsButton.Visibility = completed", Code, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletedStateShowsRunAgain()
    {
        Assert.Contains("Content=" + Q + "Run again" + Q, Page, StringComparison.Ordinal);
        Assert.Contains("Click=" + Q + "RunAgain" + Q, Page, StringComparison.Ordinal);
        Assert.Contains("Drive analysis complete", Code, StringComparison.Ordinal);
    }

    [Fact]
    public void WarningCountIsSeparateFromCoverageQuality()
    {
        Assert.Contains(Named("QualityText"), Page, StringComparison.Ordinal);
        Assert.Contains(Named("TerminalWarnings"), Page, StringComparison.Ordinal);
        Assert.Contains("WarningCount(attempt)", Code, StringComparison.Ordinal);
        Assert.DoesNotContain("completed with warnings" + Q + " Style=" + Q + "{StaticResource SectionTitleStyle}", Page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalCopyNeverPromisesGuaranteedFullCoverage()
    {
        // No Quick/Deep contrast to draw anymore, but the same truthfulness requirement holds: the normal flow
        // must never claim guaranteed, unconditional full coverage of the drive.
        Assert.DoesNotContain("scans the entire drive", Page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accesses every folder", Page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CLYR finished inspecting the safely accessible areas of this drive", Code, StringComparison.Ordinal);
    }

    [Fact]
    public void ScanCliRetainsQuickAndDeepFlagsUnaffectedByNormalUiModeRemoval()
    {
        // Confirms the CLI's --quick/--deep remain intact and independent of the WinUI mode-card removal — see
        // Clyr.Cli.Tests for CLI-side coverage; this just guards the App-side assumption that nothing here
        // depends on (or removes) the Quick engine itself.
        var cliScanCommands = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Cli", "ScanCliCommands.cs"));
        Assert.Contains("--quick", cliScanCommands, StringComparison.Ordinal);
        Assert.Contains("--deep", cliScanCommands, StringComparison.Ordinal);
        var scanning = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Core", "Scanning.cs"));
        Assert.Contains("void RunQuick(", scanning, StringComparison.Ordinal);
        Assert.Contains("void RunDeep(", scanning, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedStateDoesNotExposeRawFailureDetails()
    {
        Assert.Contains("Analysis could not be completed", Code, StringComparison.Ordinal);
        Assert.Contains("Your files were not changed", Code, StringComparison.Ordinal);
        Assert.DoesNotContain("FailureMessage", Code, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", Code, StringComparison.Ordinal);
    }

    [Fact]
    public void NarrowLayoutStacksRunningAndTerminalActions()
    {
        Assert.Contains("RunningActions.Orientation = narrow ? Orientation.Vertical", Code, StringComparison.Ordinal);
        Assert.Contains("TerminalActions.Orientation = narrow ? Orientation.Vertical", Code, StringComparison.Ordinal);
        Assert.Contains("StartButton.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch", Code, StringComparison.Ordinal);
    }

    [Fact]
    public void ScanPageUsesOnlyTheSharedPageScrollbar()
    {
        Assert.DoesNotContain("ScrollViewer", Page, StringComparison.Ordinal);
        Assert.Contains("ResponsivePageHost", Page, StringComparison.Ordinal);
    }

    [Fact]
    public void ScanUiContainsNoPrivilegeMutationNetworkOrTelemetryBehavior()
    {
        var combined = Page + Code;
        foreach (var forbidden in new[]
        {
            "Process.Start", "NamedPipe", "runas", "ShellExecute", "File.Delete", "Directory.Delete",
            "File.Move", "Directory.Move", "FileSystem.Enumerate", "HttpClient", "Telemetry", "powershell", "cmd.exe"
        })
            Assert.DoesNotContain(forbidden, combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StateSurfacesUseAccessibleNativeProgressAndMotion()
    {
        Assert.Contains("IsIndeterminate=" + Q + "True" + Q, Page, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=" + Q + "Polite" + Q, Page, StringComparison.Ordinal);
        Assert.Contains("EntranceThemeTransition", Page, StringComparison.Ordinal);
        Assert.Contains("FromVerticalOffset=" + Q + "8" + Q, Page, StringComparison.Ordinal);
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

    private static string Section(string text, string startName, string endName)
    {
        var start = text.IndexOf(Named(startName), StringComparison.Ordinal);
        var end = text.IndexOf(Named(endName), start + 1, StringComparison.Ordinal);
        if (end < 0 && endName == "/Page") end = text.Length;
        Assert.True(start >= 0 && end > start, "Could not locate section " + startName + ".");
        return text[start..end];
    }

    private static string Named(string name) => "x:Name=" + Q + name + Q;
    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(App, relativePath));
}
