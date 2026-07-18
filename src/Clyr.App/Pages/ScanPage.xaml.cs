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

    private void QuickSelected(object sender, RoutedEventArgs args)
    {
        var session = ViewModel.Session;
        if (!session.IsScanning)
            session.SelectedScanMode = ScanModeSelector.Toggle(session.SelectedScanMode, ScanMode.Quick);
        Refresh();
    }

    private void DeepSelected(object sender, RoutedEventArgs args)
    {
        var session = ViewModel.Session;
        if (!session.IsScanning)
            session.SelectedScanMode = ScanModeSelector.Toggle(session.SelectedScanMode, ScanMode.Deep);
        Refresh();
    }

    private async void StartAnalysis(object sender, RoutedEventArgs args) => await StartAsync(false);

    private void CancelAnalysis(object sender, RoutedEventArgs args)
    {
        CancelButton.IsEnabled = false;
        ViewModel.Session.Cancel();
        Refresh();
    }

    private async void ContinueQuickAnalysis(object sender, RoutedEventArgs args)
    {
        ViewModel.Session.SelectedScanMode = ScanMode.Quick;
        await StartAsync(true);
    }

    private async void RunAgain(object sender, RoutedEventArgs args)
    {
        if (ViewModel.Session.LatestAttempt is { } attempt)
            ViewModel.Session.SelectedScanMode = attempt.Mode;
        await StartAsync(false);
    }

    private void ChangeSetup(object sender, RoutedEventArgs args)
    {
        showSetupAfterAttempt = true;
        Refresh();
        PageHost.ResetScroll();
    }

    private async Task StartAsync(bool continueQuick)
    {
        showSetupAfterAttempt = false;
        await ViewModel.Session.StartAsync(continueQuick);
        Refresh();
    }

    private void ViewResults(object sender, RoutedEventArgs args) => ViewModel.Navigate("Results");
    private void StateChanged(object? sender, EventArgs args) => Refresh();

    private void Refresh()
    {
        var session = ViewModel.Session;
        RenderDrive(session.SelectedDrive);
        RenderSelection(session);

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
        DriveReadyIcon.Glyph = ready ? "\uE73E" : "\uE7BA";
        var readinessBrush = (Brush)Application.Current.Resources[ready ? "Success" : "Warning"];
        DriveReadyIcon.Foreground = readinessBrush;
        DriveReadyValue.Foreground = readinessBrush;
    }

    private void RenderSelection(AppSessionViewModel session)
    {
        QuickCard.IsChecked = session.SelectedScanMode == ScanMode.Quick;
        DeepCard.IsChecked = session.SelectedScanMode == ScanMode.Deep;
        QuickCard.IsEnabled = !session.IsScanning;
        DeepCard.IsEnabled = !session.IsScanning;
        AutomationProperties.SetHelpText(QuickCard, session.SelectedScanMode == ScanMode.Quick
            ? "Selected. Recommended bounded first look that may not account for the entire drive."
            : "Recommended bounded first look that may not account for the entire drive.");
        AutomationProperties.SetHelpText(DeepCard, session.SelectedScanMode == ScanMode.Deep
            ? "Selected. Recursively examines accessible folders; restricted areas may remain unobserved."
            : "Recursively examines accessible folders; restricted areas may remain unobserved.");

        var hasMode = session.SelectedScanMode is not null;
        ModeHelperText.Visibility = hasMode ? Visibility.Collapsed : Visibility.Visible;
        StartButton.Visibility = hasMode ? Visibility.Visible : Visibility.Collapsed;
        var buttonText = ScanUiLifecycle.PrimaryActionText(session.SelectedScanMode, session.LatestAttempt);
        StartButton.Content = buttonText;
        StartButton.IsEnabled = hasMode && session.SelectedDrive?.IsSupported == true && !session.IsScanning;
        AutomationProperties.SetName(StartButton, buttonText);
    }

    private void RenderRunning(AppSessionViewModel session)
    {
        var progress = session.Progress;
        var cancelling = session.LifecycleState == ScanUiLifecycleState.Cancelling;
        var mode = session.LifecycleState == ScanUiLifecycleState.ScanningDeep ? ScanMode.Deep
            : session.LifecycleState == ScanUiLifecycleState.ScanningQuick ? ScanMode.Quick
            : session.SelectedScanMode ?? ScanMode.Quick;
        RunningTitle.Text = cancelling ? "Cancelling analysis..." : $"{ModeName(mode)} Analysis";
        RunningStatus.Text = cancelling
            ? "Finishing the current metadata operation safely."
            : progress?.Message ?? "Examining accessible folders...";
        RunningGuidance.Text = mode == ScanMode.Quick
            ? "This is a bounded first look and may not account for the entire drive."
            : "This can take several minutes, and restricted areas may remain unobserved.";

        ElapsedText.Text = progress?.Elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture) ?? "00:00";
        FileCount.Text = progress?.FilesObserved.ToString("N0", CultureInfo.CurrentCulture) ?? "0";
        DirectoryCount.Text = progress?.DirectoriesObserved.ToString("N0", CultureInfo.CurrentCulture) ?? "0";
        ObservedSize.Text = OverviewPage.Format(progress?.LogicalBytesObserved ?? 0);
        InaccessibleCount.Text = progress?.InaccessibleEntries.ToString("N0", CultureInfo.CurrentCulture) ?? "0";
        WarningCountText.Text = progress?.WarningCount.ToString("N0", CultureInfo.CurrentCulture) ?? "0";
        CurrentLocation.Text = progress is null ? "Preparing a privacy-safe location..." : $"Current location: {progress.CurrentPath}";
        ToolTipService.SetToolTip(CurrentLocation, CurrentLocation.Text);
        AutomationProperties.SetName(ActiveProgress,
            $"{ModeName(mode)} Analysis in progress. {FileCount.Text} files and {DirectoryCount.Text} folders examined.");

        CancelButton.Content = cancelling ? "Cancelling..." : "Cancel Analysis";
        CancelButton.IsEnabled = !cancelling;
    }

    private void RenderTerminal(ScanResult attempt)
    {
        var completed = attempt.Status is ScanStatus.Completed or ScanStatus.CompletedWithWarnings;
        var cancelled = attempt.Status == ScanStatus.Cancelled;
        var failed = !completed && !cancelled;
        TerminalTitle.Text = completed ? $"{ModeName(attempt.Mode)} Analysis completed"
            : cancelled ? "Analysis cancelled" : "Analysis could not be completed";
        TerminalMessage.Text = completed
            ? "The analysis is ready to review. Coverage and access warnings are shown separately below."
            : cancelled
                ? attempt.LogicalBytesObserved > 0
                    ? "No further folders were inspected. Partial observations remain attached to this attempt."
                    : "No further folders were inspected."
                : "Your files were not changed. Try again or choose another supported drive.";
        ApplyTerminalTone(completed ? "Success" : cancelled ? "Warning" : "Error",
            completed ? "SuccessSurface" : cancelled ? "WarningSurface" : "ErrorSurface",
            completed ? "\uE73E" : cancelled ? "\uE711" : "\uEA39");

        TerminalSummary.Visibility = failed ? Visibility.Collapsed : Visibility.Visible;
        var accounting = ScanAccounting.Summarize(attempt);
        QualityText.Text = accounting.Quality switch
        {
            ScanQuality.Excellent => "Excellent coverage",
            ScanQuality.Good => "Good coverage",
            _ => "Limited coverage"
        };
        AccountedValue.Text = accounting.AccountedPercentage is { } accounted ? $"{accounted:F1}%" : "Unavailable";
        TerminalObserved.Text = OverviewPage.Format(attempt.LogicalBytesObserved);
        TerminalUnobserved.Text = OverviewPage.FormatSigned(accounting.UnaccountedDriveBytes);
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

        var boundary = ContinuationBoundary(attempt);
        var canContinue = completed && boundary is not null;
        ContinuationPanel.Visibility = canContinue ? Visibility.Visible : Visibility.Collapsed;
        ContinueQuickButton.Visibility = canContinue ? Visibility.Visible : Visibility.Collapsed;
        ContinuationReason.Text = boundary?.SafeDetail ?? string.Empty;

        ResultsButton.Visibility = completed ? Visibility.Visible : Visibility.Collapsed;
        RunAgainButton.Content = failed ? "Try Again" : "Run Again";
        ChangeSetupButton.Content = failed ? "Change drive" : "Change drive or mode";
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
        Position(DeepCard, narrow ? 0 : 1, narrow ? 1 : 0);
        SafetyNote.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
        StartButton.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;

        LayoutMetrics(RunningMetrics,
            [ElapsedMetric, FilesMetric, DirectoryMetric, ObservedMetric, InaccessibleMetric, WarningMetric], narrow ? 2 : 3);
        CancelButton.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;

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

    private static ScanIssueSummary? ContinuationBoundary(ScanResult result) => result.Mode == ScanMode.Quick
        ? result.Issues.FirstOrDefault(item => item.Code is "scan.quick-time-budget" or "scan.quick-item-budget")
        : null;

    private static long WarningCount(ScanResult result) => result.Issues
        .Where(item => item.Severity is ScanIssueSeverity.AccessWarning or ScanIssueSeverity.PermissionLimited
            or ScanIssueSeverity.DataChanged or ScanIssueSeverity.Fatal)
        .Sum(item => item.Count);

    private static string ModeName(ScanMode mode) => mode == ScanMode.Quick ? "Quick" : "Deep";
    private static string DrivePickerLabel(DriveSummary drive)
    {
        var label = string.IsNullOrWhiteSpace(drive.Label) ? "Local drive" : drive.Label.Trim();
        return $"{drive.Root.TrimEnd('\\')}  {label}";
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalMinutes >= 1
        ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
        : $"{Math.Max(0, (int)Math.Ceiling(duration.TotalSeconds))}s";
}
