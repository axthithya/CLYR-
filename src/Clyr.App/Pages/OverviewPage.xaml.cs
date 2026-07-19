using Clyr.App.Controls;
using Clyr.App.ViewModels;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Persistence;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Clyr.App.Pages;

public sealed partial class OverviewPage : Page
{
    private bool recentActivityLoading;

    public OverviewPage(OverviewViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.Session.StateChanged += OnStateChanged;
        PageHost.LayoutModeChanged += (_, mode) => Reflow(mode);
        Loaded += OnLoaded;
        Refresh();
    }

    public OverviewViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (recentActivityLoading) return;
        recentActivityLoading = true;
        try
        {
            await ViewModel.LoadRecentAsync();
        }
        catch (SnapshotStoreException)
        {
            // Recent activity is optional; the Overview remains useful when local history is unavailable.
        }
        catch (IOException)
        {
            // Keep storage and the in-memory result visible without surfacing raw persistence errors.
        }
        finally
        {
            recentActivityLoading = false;
        }
        Refresh();
    }

    private void OnStateChanged(object? sender, EventArgs args) => Refresh();

    private void Refresh()
    {
        var drive = ViewModel.Session.Drives.FirstOrDefault(item => item.IsSystemVolume) ?? ViewModel.Session.SelectedDrive;
        var result = ViewModel.Session.Result;
        var scanning = ViewModel.Session.IsScanning;
        RenderDrive(drive, result);
        RenderRecentActivity();

        var hasResult = result is not null;
        FirstRunPanel.Visibility = !scanning && !hasResult ? Visibility.Visible : Visibility.Collapsed;
        RunningPanel.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        ResultActionPanel.Visibility = !scanning && hasResult ? Visibility.Visible : Visibility.Collapsed;
        LatestAnalysisSection.Visibility = !scanning && hasResult ? Visibility.Visible : Visibility.Collapsed;

        if (scanning) RenderRunning();

        if (result is null)
        {
            TopContributors.ItemsSource = null;
            ContributorsSection.Visibility = Visibility.Collapsed;
            ReviewPlanPreview.Visibility = Visibility.Collapsed;
            ResultReviewAction.Visibility = Visibility.Collapsed;
            Reflow(PageHost.LayoutMode);
            return;
        }

        RenderLatestAnalysis(result);
        RenderContributors(result);
        RenderReviewPlan(result);
        Reflow(PageHost.LayoutMode);
    }

    private void RenderRunning()
    {
        var progress = ViewModel.Session.Progress;
        var snapshot = ViewModel.Session.ProvisionalSnapshot;
        var elapsed = progress?.Elapsed.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture) ?? "00:00";
        var drive = ViewModel.Session.SelectedDrive;
        var driveText = drive is null ? "the selected drive" : drive.Root.TrimEnd('\\');
        var coverageText = snapshot?.ProvisionalCoveragePercentage is { } coverage ? $"{coverage:F1}% provisional coverage" : "gathering insights";
        RunningSummaryText.Text = $"Analyzing {driveText} - elapsed {elapsed} - {coverageText}.";
        ViewCurrentInsightsFromOverview.IsEnabled = snapshot?.EarlyInsightsReady ?? false;
    }

    private void RenderDrive(DriveSummary? drive, ScanResult? result)
    {
        var label = drive is null || string.IsNullOrWhiteSpace(drive.Label) ? "System drive" : drive.Label.Trim();
        var root = drive?.Root.TrimEnd('\\') ?? string.Empty;
        DriveIdentity.Text = drive is null ? "System drive unavailable" : $"{label} ({root})";
        DriveLatestState.Text = result is null
            ? "No analysis has been completed for this session."
            : $"Latest: Drive Analysis - {Friendly(result.Status)}";

        var percentage = drive?.CapacityBytes is > 0 && drive.UsedBytes is { } used
            ? Math.Clamp(used * 100d / drive.CapacityBytes.Value, 0, 100)
            : (double?)null;
        DriveUsedPercentage.Text = percentage is { } value ? $"{value:F1}%" : "Unavailable";
        DriveUsedValue.Text = drive?.UsedBytes is { } usedBytes ? Format(usedBytes) : "Unavailable";
        DriveFreeValue.Text = drive?.FreeBytes is { } freeBytes ? Format(freeBytes) : "Unavailable";
        DriveTotalValue.Text = drive?.CapacityBytes is { } capacityBytes ? Format(capacityBytes) : "Unavailable";

        DriveUsedColumn.Width = new GridLength(Math.Max(percentage ?? 0, 0.001), GridUnitType.Star);
        DriveFreeColumn.Width = new GridLength(Math.Max(100 - (percentage ?? 0), 0.001), GridUnitType.Star);
        UsedStorageSegment.CornerRadius = percentage >= 99.95 ? new CornerRadius(8) : new CornerRadius(8, 0, 0, 8);
        var storageDescription = percentage is { } knownPercentage
            ? $"{knownPercentage:F1}% used, {DriveUsedValue.Text}; {100 - knownPercentage:F1}% free, {DriveFreeValue.Text}; {DriveTotalValue.Text} total."
            : "Drive storage capacity is unavailable.";
        AutomationProperties.SetName(StorageVisualization, storageDescription);

        var ready = drive is { IsSupported: true, IsReady: true };
        DriveReadiness.Text = drive is null ? "Unavailable" : ready ? "Ready" : "Needs attention";
        DriveReadinessIcon.Glyph = ready ? "\uE73E" : "\uE7BA";
        var readinessBrush = (Brush)Application.Current.Resources[ready ? "Success" : "Warning"];
        DriveReadiness.Foreground = readinessBrush;
        DriveReadinessIcon.Foreground = readinessBrush;
        DriveFileSystem.Text = drive is null
            ? "Reconnect the drive and try again."
            : string.IsNullOrWhiteSpace(drive.FileSystem) ? drive.SupportReason : $"{drive.FileSystem} - {drive.SupportReason}";
    }

    private void RenderLatestAnalysis(ScanResult result)
    {
        var accounting = ScanAccounting.Summarize(result);
        var duration = result.EndedAt >= result.StartedAt ? result.EndedAt - result.StartedAt : TimeSpan.Zero;
        LatestIdentity.Text = $"Drive Analysis - completed {result.EndedAt.LocalDateTime:g} - {FormatDuration(duration)}";
        // Section 10/11 correction: AccountingBasisDiffers is a distinct, neutral state — never labelled
        // "Insufficient coverage" (which implies a genuinely low, valid percentage).
        LatestQuality.Text = accounting.Quality switch
        {
            ScanQuality.Excellent => "Excellent coverage",
            ScanQuality.Good => "Good coverage",
            ScanQuality.Partial => "Partial coverage",
            ScanQuality.AccountingBasisDiffers => "Coverage unavailable",
            _ => "Insufficient coverage"
        };
        LatestCoverageValue.Text = accounting.AccountedPercentage is { } accounted ? $"{accounted:F1}%" : "Unavailable";
        LatestCoverageDescription.Text = accounting.AccountedPercentage is not null
            ? "of used storage accounted for by this analysis"
            : accounting.Quality == ScanQuality.AccountingBasisDiffers
                ? "Logical filesystem sizes cannot be directly compared with the drive's physical used-space total."
                : "Drive coverage cannot be calculated from the available accounting basis.";
        ObservedValue.Text = Format(result.LogicalBytesObserved);
        // Section 5: never a negative "not observed" figure — PresentableUnaccountedDriveBytes is null ("Not
        // available") exactly when the raw value would be negative.
        UnobservedValue.Text = accounting.PresentableUnaccountedDriveBytes is { } notObserved ? FormatSigned(notObserved) : "Not available";
        ClassifiedValue.Text = accounting.ClassificationPercentage is { } classified ? $"{classified:F1}%" : "Unavailable";
        ExaminedValue.Text = $"{result.Coverage.FilesObserved:N0} files, {result.Coverage.DirectoriesObserved:N0} folders";

        // Phase (Quick truthfulness correction): only genuine warnings — Quick's expected budget boundaries
        // (PolicyBoundary) and informational notes (reparse skips, etc.) must never inflate this count.
        var warningCount = result.Issues.Where(item => item.Severity is
            ScanIssueSeverity.AccessWarning or ScanIssueSeverity.PermissionLimited or ScanIssueSeverity.DataChanged or ScanIssueSeverity.Fatal)
            .Sum(item => item.Count);
        WarningSummary.Visibility = warningCount > 0 || result.Status == ScanStatus.CompletedWithWarnings
            ? Visibility.Visible
            : Visibility.Collapsed;
        WarningTitle.Text = warningCount > 0
            ? $"{warningCount:N0} access {(warningCount == 1 ? "warning" : "warnings")}"
            : "Analysis completed with warnings";
    }

    private void RenderContributors(ScanResult result)
    {
        var categories = result.Classification?.Categories
            .Where(item => item.LogicalBytes > 0)
            .OrderByDescending(item => item.LogicalBytes)
            .Take(5)
            .Select((item, index) =>
            {
                var percentage = result.LogicalBytesObserved > 0
                    ? Math.Clamp(item.LogicalBytes * 100d / result.LogicalBytesObserved, 0, 100)
                    : 0;
                var name = Humanize(item.Category);
                var size = Format(item.LogicalBytes);
                return new OverviewContributorItem(index + 1, name, size, $"{percentage:F1}%", percentage,
                    $"Rank {index + 1}, {name}, {size}, {percentage:F1}% of observed storage.");
            })
            .ToArray() ?? [];
        TopContributors.ItemsSource = categories;
        ContributorsSection.Visibility = categories.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderRecentActivity()
    {
        var activity = ViewModel.RecentAnalyses.Take(3).Select(item =>
        {
            var coverage = item.UsedBytes is > 0
                ? $"{Math.Clamp(item.LogicalBytesObserved * 100d / item.UsedBytes.Value, 0, 100):F1}% coverage"
                : "Coverage unavailable";
            // Phase (progressive-analysis terminology correction): Quick-mode history remains truthfully
            // labelled (it genuinely was a bounded Quick pass, whether from the legacy dual-mode UI or the CLI);
            // every Deep-mode record — old dual-mode-UI or new "Analyze drive" — is the same underlying
            // full-drive strategy, so it is truthfully relabelled with current terminology rather than left
            // showing the retired internal name.
            var title = item.Mode == ScanMode.Quick ? "Quick Analysis" : "Drive Analysis";
            var status = Humanize(item.State);
            var detail = $"{item.CapturedAtUtc.LocalDateTime:g} - {coverage}";
            return new OverviewActivityItem(title, detail, status, $"{title}, {detail}, {status}.");
        }).ToArray();
        RecentActivity.ItemsSource = activity;
        ActivitySection.Visibility = activity.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderReviewPlan(ScanResult result)
    {
        var candidates = CleanupCandidateFactory.FromScan(result).Where(item => item.Action is not null).ToArray();
        var potentialBytes = candidates.Sum(item => item.Impact.ObservedLogicalBytes);
        var hasCandidates = candidates.Length > 0;
        ReviewPlanPreview.Visibility = hasCandidates ? Visibility.Visible : Visibility.Collapsed;
        ResultReviewAction.Visibility = hasCandidates ? Visibility.Visible : Visibility.Collapsed;
        ReviewPlanSummary.Text = hasCandidates
            ? $"CLYR found {candidates.Length:N0} {(candidates.Length == 1 ? "item" : "items")} worth reviewing, representing {Format(potentialBytes)} of observed evidence."
            : string.Empty;
    }

    private void Reflow(ResponsivePageWidth mode)
    {
        var narrow = mode == ResponsivePageWidth.Narrow;
        DriveHero.Padding = narrow ? new Thickness(20) : new Thickness(24);
        Position(DriveUsedMetric, 0, 0);
        Position(DriveFreeMetric, narrow ? 1 : 1, 0);
        Position(DriveTotalMetric, narrow ? 0 : 2, narrow ? 1 : 0);
        Position(DriveReadyMetric, narrow ? 1 : 3, narrow ? 1 : 0);

        FirstRunActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
        ResultActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;

        Position(LatestMetrics, narrow ? 0 : 1, narrow ? 1 : 0);
        var contributorsVisible = ContributorsSection.Visibility == Visibility.Visible;
        var activityVisible = ActivitySection.Visibility == Visibility.Visible;
        Position(ContributorsSection, 0, 0);
        Position(ActivitySection, narrow || !contributorsVisible ? 0 : 1, narrow && contributorsVisible ? 1 : 0);
        Grid.SetColumnSpan(ActivitySection, narrow || !contributorsVisible ? 2 : 1);
        var previewRows = (contributorsVisible ? 1 : 0) + (activityVisible ? 1 : 0);
        Grid.SetRow(ReviewPlanPreview, narrow ? previewRows : 1);
        Grid.SetColumn(ReviewPlanPreview, 0);
        Grid.SetColumnSpan(ReviewPlanPreview, 2);

        Position(ReviewPlanButton, narrow ? 1 : 2, narrow ? 1 : 0);
        Grid.SetColumnSpan(ReviewPlanButton, narrow ? 2 : 1);
    }

    private static void Position(FrameworkElement element, int column, int row)
    {
        Grid.SetColumn(element, column);
        Grid.SetRow(element, row);
    }

    private void AnalyzeDrive(object sender, RoutedEventArgs args) => ViewModel.Navigate("Scan");
    private void ViewScanProgress(object sender, RoutedEventArgs args) => ViewModel.Navigate("Scan");
    private void ViewCurrentInsights(object sender, RoutedEventArgs args) => ViewModel.Navigate("Results");

    private void ViewResults(object sender, RoutedEventArgs args) => ViewModel.Navigate("Results");
    private void ViewHistory(object sender, RoutedEventArgs args) => ViewModel.Navigate("History");
    private void ReviewActions(object sender, RoutedEventArgs args) => ViewModel.Navigate("Review Plan");
    private void RunAgain(object sender, RoutedEventArgs args) => ViewModel.Navigate("Scan");

    internal static string Format(long bytes) => bytes >= 1_073_741_824
        ? $"{bytes / 1_073_741_824d:F2} GiB"
        : bytes >= 1_048_576 ? $"{bytes / 1_048_576d:F2} MiB"
        : bytes >= 1024 ? $"{bytes / 1024d:F2} KiB" : $"{bytes} B";

    internal static string FormatSigned(long? bytes)
    {
        if (!bytes.HasValue) return "Unavailable";
        var absolute = bytes.Value == long.MinValue ? long.MaxValue : Math.Abs(bytes.Value);
        return (bytes.Value < 0 ? "-" : string.Empty) + Format(absolute);
    }

    internal static string Humanize(object value) => Clyr.Core.DisplayNames.FromPascalCase(value.ToString() ?? string.Empty);

    private static string Friendly(ScanStatus status) => status == ScanStatus.CompletedWithWarnings
        ? "Complete with warnings"
        : Humanize(status);

    internal static string FormatDuration(TimeSpan duration) => duration.TotalMinutes >= 1
        ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
        : $"{Math.Max(0, (int)Math.Ceiling(duration.TotalSeconds))}s";
}

internal sealed record OverviewContributorItem(
    int Rank, string Name, string Size, string Percentage, double PercentageValue, string AccessibleText);

internal sealed record OverviewActivityItem(string Title, string Detail, string Status, string AccessibleText);
