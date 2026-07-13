using Clyr.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Pages;

public sealed partial class OverviewPage : Page
{
    public OverviewPage(OverviewViewModel viewModel)
    {
        ViewModel = viewModel; InitializeComponent(); ViewModel.Session.StateChanged += OnStateChanged; PageHost.LayoutModeChanged += (_, mode) => Reflow(mode); Refresh();
    }
    public OverviewViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();
    private void OnStateChanged(object? sender, EventArgs args) => Refresh();
    private void Refresh()
    {
        var drive = ViewModel.Session.Drives.FirstOrDefault(item => item.IsSystemVolume) ?? ViewModel.Session.SelectedDrive;
        DriveTitle.Text = drive is null ? "System drive unavailable" : $"{drive.Root} drive";
        DriveNumbers.Text = drive?.CapacityBytes is long capacity && drive.UsedBytes is long used ? $"{Format(used)} used · {Format(drive.FreeBytes ?? 0)} free · {Format(capacity)} total" : "Capacity information is unavailable.";
        DriveUsage.Value = drive?.CapacityBytes is > 0 && drive.UsedBytes is long usedBytes ? Math.Clamp(usedBytes * 100d / drive.CapacityBytes.Value, 0, 100) : 0;
        DriveSupport.Text = drive is null ? "Reconnect the drive and try again." : $"{drive.FileSystem} · {(drive.IsSupported ? "Ready for private analysis" : drive.SupportReason)}";
        var result = ViewModel.Session.Result; NoAnalysis.Visibility = result is null ? Visibility.Visible : Visibility.Collapsed; LatestAnalysis.Visibility = result is null ? Visibility.Collapsed : Visibility.Visible;
        if (result is null) return;
        LatestStatus.Text = $"{Friendly(result.Status)} · {result.Mode} · {result.EndedAt.LocalDateTime:g}";
        LatestSummary.Text = $"Observed {Format(result.LogicalBytesObserved)} across {result.Coverage.FilesObserved:N0} files. Unknown: {Format(result.Classification?.Coverage.UnknownBytes ?? result.LogicalBytesObserved)}.";
        TopContributors.ItemsSource = result.Classification?.Categories.OrderByDescending(item => item.LogicalBytes).Take(5).Select(item => $"{Humanize(item.Category)} — {Format(item.LogicalBytes)}").ToArray() ?? [];
    }
    private void Reflow(Controls.ResponsivePageWidth mode)
    {
        var narrow = mode == Controls.ResponsivePageWidth.Narrow;
        Grid.SetColumn(OverviewDeepCard, narrow ? 0 : 1);
        Grid.SetRow(OverviewDeepCard, narrow ? 1 : 0);
        LatestActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
    }
    private void RunQuick(object sender, RoutedEventArgs args) { ViewModel.Session.SelectedMode = Clyr.Contracts.ScanMode.Quick; ViewModel.Navigate("Scan"); }
    private void RunDeep(object sender, RoutedEventArgs args) { ViewModel.Session.SelectedMode = Clyr.Contracts.ScanMode.Deep; ViewModel.Navigate("Scan"); }
    private void ViewResults(object sender, RoutedEventArgs args) => ViewModel.Navigate("Results");
    private void ViewHistory(object sender, RoutedEventArgs args) => ViewModel.Navigate("History");
    private void RunAgain(object sender, RoutedEventArgs args) => ViewModel.Navigate("Scan");
    internal static string Format(long bytes) => bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824d:F2} GiB" : bytes >= 1_048_576 ? $"{bytes / 1_048_576d:F2} MiB" : bytes >= 1024 ? $"{bytes / 1024d:F2} KiB" : $"{bytes} B";
    internal static string FormatSigned(long? bytes)
    {
        if (!bytes.HasValue) return "unavailable";
        var absolute = bytes.Value == long.MinValue ? long.MaxValue : Math.Abs(bytes.Value);
        return (bytes.Value < 0 ? "−" : string.Empty) + Format(absolute);
    }
    internal static string Humanize(object value) => System.Text.RegularExpressions.Regex.Replace(value.ToString() ?? string.Empty, "(?<!^)([A-Z])", " $1");
    private static string Friendly(Clyr.Contracts.ScanStatus status) => status == Clyr.Contracts.ScanStatus.CompletedWithWarnings ? "Complete with warnings" : status.ToString();
}
