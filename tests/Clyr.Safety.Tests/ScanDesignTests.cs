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
    public void QuickModeProducesRunQuickAnalysis()
    {
        Assert.Contains("modeName = selectedMode == ScanMode.Quick ? " + Q + "Quick" + Q, Lifecycle, StringComparison.Ordinal);
        Assert.Contains("Run {modeName} Analysis", Lifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void DeepModeProducesRunDeepAnalysis()
    {
        Assert.Contains("selectedMode == ScanMode.Quick ? " + Q + "Quick" + Q + " : " + Q + "Deep" + Q, Lifecycle, StringComparison.Ordinal);
        Assert.Contains("StartButton.Content = buttonText", Code, StringComparison.Ordinal);
    }

    [Fact]
    public void IdleStateDoesNotRenderCancel()
    {
        AssertInOrder(Page, Named("SetupPanel"), Named("RunningPanel"), Named("CancelButton"), Named("TerminalPanel"));
        Assert.Contains("RunningPanel.Visibility = active", Code, StringComparison.Ordinal);
        Assert.DoesNotContain("CancelButton", Section(Page, "SetupPanel", "RunningPanel"), StringComparison.Ordinal);
    }

    [Fact]
    public void RunningStateShowsSecondaryCancel()
    {
        Assert.Contains("Content=" + Q + "Cancel Analysis" + Q, Page, StringComparison.Ordinal);
        Assert.Contains("SecondaryActionStyle", Section(Page, "CancelButton", "TerminalPanel"), StringComparison.Ordinal);
        Assert.Contains("RunningPanel.Visibility = active ? Visibility.Visible", Code, StringComparison.Ordinal);
    }

    [Fact]
    public void CancellingBlocksDuplicateCancellation()
    {
        Assert.Contains("CancelButton.IsEnabled = false", Code, StringComparison.Ordinal);
        Assert.Contains("CancelButton.IsEnabled = !cancelling", Code, StringComparison.Ordinal);
        Assert.Contains("Cancelling analysis...", Code, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletedStateContainsNoCancelControl()
    {
        var terminal = Section(Page, "TerminalPanel", "/Page");
        Assert.DoesNotContain("CancelButton", terminal, StringComparison.Ordinal);
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
        Assert.Contains("Content=" + Q + "Run Again" + Q, Page, StringComparison.Ordinal);
        Assert.Contains("Click=" + Q + "RunAgain" + Q, Page, StringComparison.Ordinal);
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
    public void QuickCopyDoesNotPromiseFullCoverage()
    {
        Assert.Contains("May not account for the entire drive", Page, StringComparison.Ordinal);
        Assert.Contains("bounded", Page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Quick Analysis scans the entire drive", Page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeepCopyDoesNotPromiseRestrictedAccess()
    {
        Assert.Contains("every folder CLYR can safely access", Page, StringComparison.Ordinal);
        Assert.Contains("Restricted areas may remain unobserved", Page, StringComparison.Ordinal);
        Assert.DoesNotContain("accesses every folder", Page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuickContinuationUsesOnlySupportedCheckpointReasons()
    {
        Assert.Contains("Continue Quick Analysis", Page, StringComparison.Ordinal);
        Assert.Contains("scan.quick-time-budget", Code, StringComparison.Ordinal);
        Assert.Contains("scan.quick-item-budget", Code, StringComparison.Ordinal);
        Assert.Contains("ContinueFromCheckpoint: continueQuick && mode == ScanMode.Quick", Session, StringComparison.Ordinal);
        Assert.Contains("StartAsync(true)", Code, StringComparison.Ordinal);
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
    public void NarrowLayoutStacksModeSelectionAndActions()
    {
        Assert.Contains("Position(DeepCard, narrow ? 0 : 1, narrow ? 1 : 0)", Code, StringComparison.Ordinal);
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
