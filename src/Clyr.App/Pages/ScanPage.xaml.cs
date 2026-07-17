using System.Globalization;
using Clyr.App.ViewModels;
using Clyr.Contracts;
using Clyr.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Pages;

public sealed partial class ScanPage : Page
{
    public ScanPage(ScanViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DriveSelector.ItemsSource = viewModel.Session.Drives.Select(Label).ToArray();
        DriveSelector.SelectedIndex = viewModel.Session.SelectedDriveIndex;
        viewModel.Session.StateChanged += StateChanged;
        PageHost.LayoutModeChanged += (_, mode) => Reflow(mode);
        Reflow(Controls.ResponsivePageWidth.Wide);
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
        if (!session.IsScanning) session.SelectedScanMode = ScanModeSelector.Toggle(session.SelectedScanMode, ScanMode.Quick);
        Refresh();
    }

    private void DeepSelected(object sender, RoutedEventArgs args)
    {
        var session = ViewModel.Session;
        if (!session.IsScanning) session.SelectedScanMode = ScanModeSelector.Toggle(session.SelectedScanMode, ScanMode.Deep);
        Refresh();
    }

    private async void StartAnalysis(object sender, RoutedEventArgs args) { await ViewModel.Session.StartAsync(); Refresh(); }
    private void CancelAnalysis(object sender, RoutedEventArgs args) => ViewModel.Session.Cancel();
    private void ViewResults(object sender, RoutedEventArgs args) => ViewModel.Navigate("Results");
    private void StateChanged(object? sender, EventArgs args) => Refresh();

    private void Refresh()
    {
        var s = ViewModel.Session;
        DriveDetails.Text = s.SelectedDrive is null ? "No supported local drive is available." : $"{s.SelectedDrive.FileSystem} · {OverviewPage.Format(s.SelectedDrive.CapacityBytes ?? 0)} total · {s.SelectedDrive.SupportReason}";
        DriveSelector.IsEnabled = !s.IsScanning;

        // Selection: the only place either card's IsChecked is ever set — always re-synced here from the one
        // authoritative ViewModel value (Session.SelectedScanMode), regardless of what ToggleButton's own
        // click-driven auto-toggle just did to its IsChecked. This makes "both selected" or "checkmark doesn't
        // match the ViewModel" structurally impossible: there is exactly one place selection is rendered.
        QuickCard.IsChecked = s.SelectedScanMode == ScanMode.Quick;
        DeepCard.IsChecked = s.SelectedScanMode == ScanMode.Deep;
        QuickCard.IsEnabled = !s.IsScanning;
        DeepCard.IsEnabled = !s.IsScanning;
        AutomationProperties.SetHelpText(QuickCard, s.SelectedScanMode == ScanMode.Quick ? "Selected. Recommended, bounded first look." : "Recommended, bounded first look.");
        AutomationProperties.SetHelpText(DeepCard, s.SelectedScanMode == ScanMode.Deep ? "Selected. Unbounded recursive analysis." : "Unbounded recursive analysis.");
        ModeHelperText.Visibility = s.SelectedScanMode is null && !s.IsScanning ? Visibility.Visible : Visibility.Collapsed;

        var p = s.Progress;
        ScanState.Text = StateText(s.LifecycleState, s.LatestAttempt);
        ActiveProgress.Visibility = s.IsScanning ? Visibility.Visible : Visibility.Collapsed;
        MetricsGrid.Visibility = s.IsScanning ? Visibility.Visible : Visibility.Collapsed;
        FileCount.Text = p?.FilesObserved.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
        DirectoryCount.Text = p?.DirectoriesObserved.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
        ObservedSize.Text = p is null ? "—" : OverviewPage.Format(p.LogicalBytesObserved);
        InaccessibleCount.Text = p?.InaccessibleEntries.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
        ReparseSkippedCount.Text = p?.ReparsePointsSkipped.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
        WarningCountText.Text = p?.WarningCount.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
        ElapsedText.Text = p is null ? "—" : p.Elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        CurrentLocation.Text = p is null ? "Ready when you are." : $"Current location: {p.CurrentPath}";

        var buttonText = ScanUiLifecycle.PrimaryActionText(s.SelectedScanMode, s.LatestAttempt);
        StartButton.Content = buttonText;
        AutomationProperties.SetName(StartButton, buttonText);
        StartButton.IsEnabled = !s.IsScanning && s.SelectedScanMode is not null && s.SelectedDrive?.IsSupported == true;
        CancelButton.IsEnabled = s.IsScanning;
        ResultsButton.Visibility = !s.IsScanning && s.Result is not null ? Visibility.Visible : Visibility.Collapsed;

        RefreshAccounting(s.LatestAttempt);
    }

    private void RefreshAccounting(ScanResult? attempt)
    {
        if (attempt is null) { AccountingCard.Visibility = Visibility.Collapsed; return; }
        AccountingCard.Visibility = Visibility.Visible;

        var duration = attempt.EndedAt - attempt.StartedAt;
        AttemptSummary.Text = $"{Humanize(attempt.Mode)} Analysis · {Humanize(attempt.Status)} · {attempt.EndedAt.LocalDateTime:g} · duration {duration:mm\\:ss}\n" +
            $"{attempt.Coverage.FilesObserved:N0} files · {attempt.Coverage.DirectoriesObserved:N0} directories examined · observed {OverviewPage.Format(attempt.LogicalBytesObserved)}" +
            (attempt.Coverage.InaccessibleEntries > 0 ? $" · {attempt.Coverage.InaccessibleEntries:N0} inaccessible" : string.Empty) +
            $" · {attempt.Issues.Sum(x => x.Count):N0} warnings";

        var summary = ScanAccounting.Summarize(attempt);
        QualityBanner.Text = summary.Quality switch
        {
            ScanQuality.Insufficient => "Insufficient coverage — recommend Deep Analysis for a fuller picture of this drive.",
            ScanQuality.Partial => "Partial coverage — Deep Analysis can account for more of this drive.",
            ScanQuality.Good => "Good coverage of this drive's used space.",
            ScanQuality.Excellent => "Excellent coverage of this drive's used space.",
            _ => string.Empty
        };

        var accountedText = summary.AccountedPercentage is { } accounted ? $"{accounted:F1}%" : "unavailable";
        var classificationText = summary.ClassificationPercentage is { } classification ? $"{classification:F1}%" : "unavailable";
        AccountingDetail.Text =
            $"Accounted (of drive used space): {accountedText} · Classified (of what this scan observed): {classificationText}\n" +
            $"Observed and classified: {OverviewPage.Format(summary.ClassifiedObservedBytes)} · Observed but unclassified: {OverviewPage.Format(summary.UnclassifiedObservedBytes)}\n" +
            (summary.UnaccountedDriveBytes is { } unaccounted
                ? $"Not observed by this scan — permission-limited, volume-managed or filesystem-managed storage, or an accounting difference: {OverviewPage.Format(unaccounted)}"
                : "Remaining drive usage: unavailable (no comparable drive-used basis).");
    }

    private static string StateText(ScanUiLifecycleState state, ScanResult? latestAttempt) => state switch
    {
        ScanUiLifecycleState.ScanningQuick => "Running Quick Analysis",
        ScanUiLifecycleState.ScanningDeep => "Running Deep Analysis",
        ScanUiLifecycleState.Cancelling => "Cancelling…",
        ScanUiLifecycleState.CompletedQuick => "Quick Analysis completed",
        ScanUiLifecycleState.CompletedDeep => "Deep Analysis completed",
        ScanUiLifecycleState.CompletedWithWarningsQuick => "Quick Analysis completed with warnings",
        ScanUiLifecycleState.CompletedWithWarningsDeep => "Deep Analysis completed with warnings",
        ScanUiLifecycleState.CancelledQuick => "Quick Analysis cancelled; partial observations retained",
        ScanUiLifecycleState.CancelledDeep => "Deep Analysis cancelled; partial observations retained",
        ScanUiLifecycleState.FailedQuick => "Quick Analysis failed" + (latestAttempt?.FailureMessage is { Length: > 0 } message ? ": " + message : string.Empty),
        ScanUiLifecycleState.FailedDeep => "Deep Analysis failed" + (latestAttempt?.FailureMessage is { Length: > 0 } message ? ": " + message : string.Empty),
        _ => "Ready to analyze"
    };

    private static string Humanize(ScanMode mode) => mode == ScanMode.Quick ? "Quick" : "Deep";
    private static string Humanize(ScanStatus status) => status switch
    {
        ScanStatus.Completed => "Completed",
        ScanStatus.CompletedWithWarnings => "Completed with warnings",
        ScanStatus.Cancelled => "Cancelled",
        ScanStatus.Failed => "Failed",
        _ => status.ToString()
    };

    private static string Label(DriveSummary drive) => $"{drive.Root} {drive.Label} — {OverviewPage.Format(drive.UsedBytes ?? 0)} used of {OverviewPage.Format(drive.CapacityBytes ?? 0)}";

    private void Reflow(Controls.ResponsivePageWidth mode)
    {
        var narrow = mode == Controls.ResponsivePageWidth.Narrow;
        Grid.SetColumn(DeepCard, narrow ? 0 : 1);
        Grid.SetRow(DeepCard, narrow ? 1 : 0);
        ScanActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;

        FrameworkElement[] metrics = [FilesMetric, DirectoryMetric, ObservedMetric, InaccessibleMetric, ReparseMetric, WarningMetric, ElapsedMetric];
        var columns = narrow ? 1 : 3;
        MetricsGrid.ColumnDefinitions.Clear();
        MetricsGrid.RowDefinitions.Clear();
        for (var column = 0; column < columns; column++) MetricsGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < (int)Math.Ceiling(metrics.Length / (double)columns); row++) MetricsGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var index = 0; index < metrics.Length; index++)
        {
            Grid.SetColumn(metrics[index], index % columns);
            Grid.SetRow(metrics[index], index / columns);
        }
    }
}
