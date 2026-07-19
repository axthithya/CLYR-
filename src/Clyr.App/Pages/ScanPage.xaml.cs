using System.Globalization;
using Clyr.App.Controls;
using Clyr.App.ViewModels;
using Clyr.Contracts;
using Clyr.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Clyr.App.Pages;

public sealed partial class ScanPage : Page
{
    private bool showSetupAfterAttempt;

    public ScanPage(ScanViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DriveSelector.ItemsSource = viewModel.Session.Drives.Select(DrivePickerLabel).ToArray();
        DriveSelector.SelectedIndex = viewModel.Session.SelectedDriveIndex;
        viewModel.Session.StateChanged += StateChanged;
        PageHost.LayoutModeChanged += (_, mode) => Reflow(mode);
        Reflow(ResponsivePageWidth.Wide);
        Refresh();
    }

    public ScanViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();

    private void DriveChanged(object sender, SelectionChangedEventArgs args)
    {
        if (ViewModel.Session.IsScanning) return;
        ViewModel.Session.SelectedDriveIndex = DriveSelector.SelectedIndex;
        Refresh();
    }

    private async void StartAnalysis(object sender, RoutedEventArgs args) => await StartAsync();

    /// <summary>Truthfully labelled "Stop analysis," not "Cancel Analysis" — see section 13. Confirms first
    /// (Cancel is the safe default focus) rather than stopping immediately, and never claims more than
    /// <see cref="AppSessionViewModel.ProvisionalSnapshot"/> actually preserves.</summary>
    private async void StopAnalysis(object sender, RoutedEventArgs args)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Stop analysis?",
            Content = new TextBlock
            {
                Text = "CLYR will stop inspecting the drive. The insights gathered so far will remain available, " +
                    "marked as incomplete. Cleanup planning will remain unavailable until a new full analysis completes.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Stop analysis",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        StopButton.IsEnabled = false;
        ViewModel.Session.Cancel();
        Refresh();
    }

    private async void ViewCurrentInsights(object sender, RoutedEventArgs args)
    {
        await Task.CompletedTask;
        ViewModel.Navigate("Results");
    }

    private async void RunAgain(object sender, RoutedEventArgs args) => await StartAsync();

    private void ChangeSetup(object sender, RoutedEventArgs args)
    {
        showSetupAfterAttempt = true;
        Refresh();
        PageHost.ResetScroll();
    }

    private async Task StartAsync()
    {
        showSetupAfterAttempt = false;
        await ViewModel.Session.AnalyzeDriveAsync();
        Refresh();
    }

    private void ViewResults(object sender, RoutedEventArgs args) => ViewModel.Navigate("Results");
    private void StateChanged(object? sender, EventArgs args) => Refresh();

    private void Refresh()
    {
        var session = ViewModel.Session;
        RenderDrive(session.SelectedDrive);
        RenderIdle(session);

        var active = session.IsScanning;
        var hasAttempt = session.LatestAttempt is not null;
        SetupPanel.Visibility = !active && (!hasAttempt || showSetupAfterAttempt) ? Visibility.Visible : Visibility.Collapsed;
        RunningPanel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        TerminalPanel.Visibility = !active && hasAttempt && !showSetupAfterAttempt ? Visibility.Visible : Visibility.Collapsed;

        if (active) RenderRunning(session);
        if (!active && hasAttempt && !showSetupAfterAttempt) RenderTerminal(session.LatestAttempt!);
        Reflow(PageHost.LayoutMode);
    }

    private void RenderDrive(DriveSummary? drive)
    {
        if (DriveSelector.SelectedIndex != ViewModel.Session.SelectedDriveIndex)
            DriveSelector.SelectedIndex = ViewModel.Session.SelectedDriveIndex;
        DriveSelector.IsEnabled = !ViewModel.Session.IsScanning;

        var volumeLabel = drive is null || string.IsNullOrWhiteSpace(drive.Label) ? "Local drive" : drive.Label.Trim();
        var root = drive?.Root.TrimEnd('\\') ?? string.Empty;
        var fullIdentity = drive is null ? "No supported local drive is available" : $"{volumeLabel} ({root})";
        DriveIdentity.Text = fullIdentity;
        ToolTipService.SetToolTip(DriveIdentity, fullIdentity);
        AutomationProperties.SetName(DriveIdentity, fullIdentity);
        ToolTipService.SetToolTip(DriveSelector, fullIdentity);
        DriveTechnicalSummary.Text = drive is null
            ? "Reconnect a supported drive and try again."
            : $"{drive.FileSystem} - {drive.SupportReason}";

        var percentage = drive?.CapacityBytes is > 0 && drive.UsedBytes is { } used
            ? Math.Clamp(used * 100d / drive.CapacityBytes.Value, 0, 100)
            : (double?)null;
        DriveUsedPercentage.Text = percentage is { } value ? $"{value:F1}%" : "Unavailable";
        DriveUsedValue.Text = drive?.UsedBytes is { } usedBytes ? OverviewPage.Format(usedBytes) : "Unavailable";
        DriveFreeValue.Text = drive?.FreeBytes is { } freeBytes ? OverviewPage.Format(freeBytes) : "Unavailable";
        DriveTotalValue.Text = drive?.CapacityBytes is { } capacityBytes ? OverviewPage.Format(capacityBytes) : "Unavailable";
        DriveUsedColumn.Width = new GridLength(Math.Max(percentage ?? 0, 0.001), GridUnitType.Star);
        DriveFreeColumn.Width = new GridLength(Math.Max(100 - (percentage ?? 0), 0.001), GridUnitType.Star);
        DriveUsedSegment.CornerRadius = percentage >= 99.95 ? new CornerRadius(8) : new CornerRadius(8, 0, 0, 8);
        var storageText = percentage is { } known
            ? $"{known:F1}% used, {DriveUsedValue.Text}; {100 - known:F1}% free, {DriveFreeValue.Text}; {DriveTotalValue.Text} total."
            : "Drive storage capacity is unavailable.";
        AutomationProperties.SetName(DriveStorageBar, storageText);

        var ready = drive is { IsReady: true, IsSupported: true };
        DriveReadyValue.Text = ready ? "Ready" : "Needs attention";
        DriveReadyIcon.Glyph = ready ? "" : "";
        var readinessBrush = (Brush)Application.Current.Resources[ready ? "Success" : "Warning"];
        DriveReadyIcon.Foreground = readinessBrush;
        DriveReadyValue.Foreground = readinessBrush;
    }

    private void RenderIdle(AppSessionViewModel session)
    {
        StartButton.IsEnabled = session.SelectedDrive?.IsSupported == true && !session.IsScanning;
    }

    private void RenderRunning(AppSessionViewModel session)
    {
        var progress = session.Progress;
        var snapshot = session.ProvisionalSnapshot;
        var cancelling = session.LifecycleState == ScanUiLifecycleState.Cancelling;
        var stageText = StageText(session.LifecycleState, snapshot?.Stage);
        RunningTitle.Text = cancelling ? "Stopping analysis…" : stageText;
        RunningStatus.Text = cancelling
            ? "Finishing the current metadata operation safely."
            : progress?.Message ?? "Examining accessible folders…";
        RunningGuidance.Text = "CLYR begins showing useful storage insights early and continues toward a complete drive analysis.";

        ElapsedText.Text = progress?.Elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture) ?? "00:00";
        FileCount.Text = progress?.FilesObserved.ToString("N0", CultureInfo.CurrentCulture) ?? "0";
        DirectoryCount.Text = progress?.DirectoriesObserved.ToString("N0", CultureInfo.CurrentCulture) ?? "0";
        ObservedSize.Text = OverviewPage.Format(progress?.LogicalBytesObserved ?? 0);
        InaccessibleCount.Text = progress?.InaccessibleEntries.ToString("N0", CultureInfo.CurrentCulture) ?? "0";
        WarningCountText.Text = progress?.WarningCount.ToString("N0", CultureInfo.CurrentCulture) ?? "0";
        CurrentLocation.Text = progress is null ? "Preparing a privacy-safe location…" : $"Current location: {progress.CurrentPath}";
        ToolTipService.SetToolTip(CurrentLocation, CurrentLocation.Text);
        AutomationProperties.SetName(ActiveProgress,
            $"{stageText}. {FileCount.Text} files and {DirectoryCount.Text} folders examined.");

        var insightsReady = !cancelling && (snapshot?.EarlyInsightsReady ?? false);
        EarlyInsightsPanel.Visibility = insightsReady ? Visibility.Visible : Visibility.Collapsed;
        if (insightsReady && snapshot is not null)
        {
            EarlyInsightsSummary.Text = $"{OverviewPage.Format(snapshot.LogicalBytesObserved)} observed so far across " +
                $"{snapshot.TopContributors.Count} storage area{(snapshot.TopContributors.Count == 1 ? "" : "s")}.";
            ViewCurrentInsightsButton.IsEnabled = true;
        }

        StopButton.Content = cancelling ? "Stopping…" : "Stop analysis";
        StopButton.IsEnabled = !cancelling;
    }

    private static string StageText(ScanUiLifecycleState lifecycle, ScanStage? stage) => lifecycle switch
    {
        ScanUiLifecycleState.Preparing => "Preparing drive",
        _ => stage switch
        {
            ScanStage.DiscoveringMajorStorageAreas => "Discovering major storage areas",
            ScanStage.InspectingFilesAndFolders => "Inspecting files and folders",
            ScanStage.Finalizing => "Finalizing results",
            _ => "Preparing drive"
        }
    };

    private void RenderTerminal(ScanResult attempt)
    {
        var completed = attempt.Status is ScanStatus.Completed or ScanStatus.CompletedWithWarnings;
        var cancelled = attempt.Status == ScanStatus.Cancelled;
        var failed = !completed && !cancelled;
        TerminalTitle.Text = completed ? "Drive analysis complete"
            : cancelled ? "Analysis stopped" : "Analysis could not be completed";
        TerminalMessage.Text = completed
            ? "CLYR finished inspecting the safely accessible areas of this drive."
            : cancelled
                ? attempt.LogicalBytesObserved > 0
                    ? "No further folders were inspected. The insights gathered so far remain available, marked incomplete."
                    : "No further folders were inspected."
                : "Your files were not changed. Try again or choose another supported drive.";
        ApplyTerminalTone(completed ? "Success" : cancelled ? "Warning" : "Error",
            completed ? "SuccessSurface" : cancelled ? "WarningSurface" : "ErrorSurface",
            completed ? "" : cancelled ? "" : "");

        TerminalSummary.Visibility = failed ? Visibility.Collapsed : Visibility.Visible;
        var accounting = ScanAccounting.Summarize(attempt);
        // Section 10/11 correction: AccountingBasisDiffers is a distinct, neutral state — never "Limited
        // coverage", which is reserved for a genuinely low but valid, comparable percentage.
        QualityText.Text = accounting.Quality switch
        {
            ScanQuality.Excellent => "Excellent coverage",
            ScanQuality.Good => "Good coverage",
            ScanQuality.AccountingBasisDiffers => "Coverage unavailable",
            _ => "Limited coverage"
        };
        AccountedValue.Text = accounting.AccountedPercentage is { } accounted ? $"{accounted:F1}%" : "Unavailable";
        TerminalObserved.Text = OverviewPage.Format(attempt.LogicalBytesObserved);
        // Section 5: never a negative "not observed" figure.
        TerminalUnobserved.Text = accounting.PresentableUnaccountedDriveBytes is { } notObserved
            ? OverviewPage.FormatSigned(notObserved)
            : "Not available";
        TerminalClassified.Text = accounting.ClassificationPercentage is { } classified ? $"{classified:F1}%" : "Unavailable";
        var duration = attempt.EndedAt >= attempt.StartedAt ? attempt.EndedAt - attempt.StartedAt : TimeSpan.Zero;
        TerminalDuration.Text = FormatDuration(duration);
        TerminalFiles.Text = attempt.Coverage.FilesObserved.ToString("N0", CultureInfo.CurrentCulture);
        TerminalFolders.Text = attempt.Coverage.DirectoriesObserved.ToString("N0", CultureInfo.CurrentCulture);
        TerminalCompleted.Text = attempt.EndedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);

        var warningCount = WarningCount(attempt);
        TerminalWarnings.Visibility = !failed && (warningCount > 0 || attempt.Status == ScanStatus.CompletedWithWarnings)
            ? Visibility.Visible : Visibility.Collapsed;
        TerminalWarningTitle.Text = warningCount > 0
            ? $"{warningCount:N0} access {(warningCount == 1 ? "warning" : "warnings")}"
            : "Analysis completed with warnings";

        ResultsButton.Visibility = completed || (cancelled && attempt.LogicalBytesObserved > 0) ? Visibility.Visible : Visibility.Collapsed;
        ResultsButton.Content = completed ? "View Results" : "View partial results";
        RunAgainButton.Content = failed ? "Try Again" : "Run again";
        ChangeSetupButton.Content = "Change drive";
    }

    private void ApplyTerminalTone(string foregroundKey, string surfaceKey, string glyph)
    {
        TerminalIcon.Foreground = (Brush)Application.Current.Resources[foregroundKey];
        TerminalIconSurface.Background = (Brush)Application.Current.Resources[surfaceKey];
        TerminalIcon.Glyph = glyph;
    }

    private void Reflow(ResponsivePageWidth mode)
    {
        var narrow = mode == ResponsivePageWidth.Narrow;
        DrivePanel.Padding = narrow ? new Thickness(16) : new Thickness(20);
        RunningPanel.Padding = narrow ? new Thickness(20) : new Thickness(24);
        Position(DriveUsedMetric, 0, 0);
        Position(DriveFreeMetric, 1, 0);
        Position(DriveTotalMetric, narrow ? 0 : 2, narrow ? 1 : 0);
        Position(DriveReadyMetric, narrow ? 1 : 3, narrow ? 1 : 0);
        StartButton.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;

        LayoutMetrics(RunningMetrics,
            [ElapsedMetric, FilesMetric, DirectoryMetric, ObservedMetric, InaccessibleMetric, WarningMetric], narrow ? 2 : 3);
        RunningActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;

        Position(TerminalStorageMetrics, narrow ? 0 : 1, narrow ? 1 : 0);
        LayoutMetrics(TerminalDetails,
            [TerminalDurationMetric, TerminalFilesMetric, TerminalFoldersMetric, TerminalCompletedMetric], narrow ? 2 : 4);
        TerminalActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
    }

    private static void LayoutMetrics(Grid grid, IReadOnlyList<FrameworkElement> metrics, int columns)
    {
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        for (var column = 0; column < columns; column++)
            grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var rows = (int)Math.Ceiling(metrics.Count / (double)columns);
        for (var row = 0; row < rows; row++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var index = 0; index < metrics.Count; index++) Position(metrics[index], index % columns, index / columns);
    }

    private static void Position(FrameworkElement element, int column, int row)
    {
        Grid.SetColumn(element, column);
        Grid.SetRow(element, row);
    }

    private static long WarningCount(ScanResult result) => result.Issues
        .Where(item => item.Severity is ScanIssueSeverity.AccessWarning or ScanIssueSeverity.PermissionLimited
            or ScanIssueSeverity.DataChanged or ScanIssueSeverity.Fatal)
        .Sum(item => item.Count);

    private static string DrivePickerLabel(DriveSummary drive)
    {
        var label = string.IsNullOrWhiteSpace(drive.Label) ? "Local drive" : drive.Label.Trim();
        return $"{drive.Root.TrimEnd('\\')}  {label}";
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalMinutes >= 1
        ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
        : $"{Math.Max(0, (int)Math.Ceiling(duration.TotalSeconds))}s";
}
