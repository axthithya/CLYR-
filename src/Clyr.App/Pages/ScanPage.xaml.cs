using Clyr.App.ViewModels;
using Clyr.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Pages;

public sealed partial class ScanPage : Page
{
    public ScanPage(ScanViewModel viewModel)
    { ViewModel = viewModel; InitializeComponent(); DriveSelector.ItemsSource = viewModel.Session.Drives.Select(Label).ToArray(); DriveSelector.SelectedIndex = viewModel.Session.SelectedDriveIndex; viewModel.Session.StateChanged += StateChanged; PageHost.LayoutModeChanged += (_, mode) => Reflow(mode); Refresh(); }
    public ScanViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();
    private void DriveChanged(object sender, SelectionChangedEventArgs args) { ViewModel.Session.SelectedDriveIndex = DriveSelector.SelectedIndex; Refresh(); }
    private void QuickSelected(object sender, RoutedEventArgs args) { ViewModel.Session.SelectedMode = ScanMode.Quick; QuickCard.IsChecked = true; DeepCard.IsChecked = false; }
    private void DeepSelected(object sender, RoutedEventArgs args) { ViewModel.Session.SelectedMode = ScanMode.Deep; DeepCard.IsChecked = true; QuickCard.IsChecked = false; }
    private async void StartAnalysis(object sender, RoutedEventArgs args) { await ViewModel.Session.StartAsync(); Refresh(); }
    private void CancelAnalysis(object sender, RoutedEventArgs args) => ViewModel.Session.Cancel();
    private void ViewResults(object sender, RoutedEventArgs args) => ViewModel.Navigate("Results");
    private void StateChanged(object? sender, EventArgs args) => Refresh();
    private void Refresh() { var s = ViewModel.Session; DriveDetails.Text = s.SelectedDrive is null ? "No supported local drive is available." : $"{s.SelectedDrive.FileSystem} · {OverviewPage.Format(s.SelectedDrive.CapacityBytes ?? 0)} total · {s.SelectedDrive.SupportReason}"; QuickCard.IsChecked = s.SelectedMode == ScanMode.Quick; DeepCard.IsChecked = s.SelectedMode == ScanMode.Deep; AutomationProperties.SetHelpText(QuickCard, s.SelectedMode == ScanMode.Quick ? "Selected. Recommended faster depth-limited analysis." : "Recommended faster depth-limited analysis."); AutomationProperties.SetHelpText(DeepCard, s.SelectedMode == ScanMode.Deep ? "Selected. Slower analysis of accessible directories." : "Slower analysis of accessible directories."); var p = s.Progress; ScanState.Text = p is null ? "Ready to analyze" : p.Status == ScanStatus.Cancelling ? "Stopping safely…" : p.Message; FileCount.Text = p?.FilesObserved.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) ?? "—"; DirectoryCount.Text = p?.DirectoriesObserved.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) ?? "—"; ObservedSize.Text = p is null ? "—" : OverviewPage.Format(p.LogicalBytesObserved); CurrentLocation.Text = p is null ? "Ready when you are." : $"Current location: {p.CurrentPath}"; ActiveProgress.Visibility = s.IsScanning ? Visibility.Visible : Visibility.Collapsed; StartButton.IsEnabled = !s.IsScanning && s.SelectedDrive?.IsSupported == true; CancelButton.IsEnabled = s.IsScanning; ResultsButton.Visibility = !s.IsScanning && s.Result is not null ? Visibility.Visible : Visibility.Collapsed; }
    private static string Label(DriveSummary drive) => $"{drive.Root} {drive.Label} — {OverviewPage.Format(drive.UsedBytes ?? 0)} used of {OverviewPage.Format(drive.CapacityBytes ?? 0)}";
    private void Reflow(Controls.ResponsivePageWidth mode)
    {
        var narrow = mode == Controls.ResponsivePageWidth.Narrow;
        Grid.SetColumn(DeepCard, narrow ? 0 : 1);
        Grid.SetRow(DeepCard, narrow ? 1 : 0);
        Grid.SetColumn(DirectoryMetric, narrow ? 0 : 1);
        Grid.SetRow(DirectoryMetric, narrow ? 1 : 0);
        Grid.SetColumn(ObservedMetric, narrow ? 0 : 2);
        Grid.SetRow(ObservedMetric, narrow ? 2 : 0);
        ScanActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
    }
}
