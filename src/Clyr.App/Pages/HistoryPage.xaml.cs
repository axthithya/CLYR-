using Clyr.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Pages;

public sealed partial class HistoryPage : Page
{
    public HistoryPage(HistoryViewModel viewModel) { ViewModel = viewModel; InitializeComponent(); PageHost.LayoutModeChanged += (_, mode) => Reflow(mode); }
    public HistoryViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();
    private void Reflow(Controls.ResponsivePageWidth mode)
    {
        var narrow = mode == Controls.ResponsivePageWidth.Narrow;
        HistoryActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
        DeleteActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
        EmptyActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
        Grid.SetColumn(HistoryActions, narrow ? 0 : 1);
        Grid.SetRow(HistoryActions, narrow ? 1 : 0);
        Grid.SetColumn(HistorySettingsButton, narrow ? 0 : 1);
        Grid.SetRow(HistorySettingsButton, narrow ? 1 : 0);
        HistorySettingsButton.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
    }
    public async Task ActivateAsync() { try { await ViewModel.LoadAsync(); Render(); } catch (Exception e) when (e is IOException or Clyr.Persistence.SnapshotStoreException) { HistoryStatus.Text = "History is unavailable. " + e.Message; } }
    private void Render() { HistoryEmpty.Visibility = ViewModel.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed; HistoryContent.Visibility = ViewModel.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible; HistoryList.ItemsSource = ViewModel.Items.Select(item => $"{item.CapturedAtUtc.LocalDateTime:g}  ·  {item.Root}  ·  {item.Mode}  ·  {item.State}\nObserved {OverviewPage.Format(item.LogicalBytesObserved)}  ·  Drive used {(item.UsedBytes is long used ? OverviewPage.Format(used) : "unavailable")}  ·  Unknown {OverviewPage.Format(item.UnknownBytes)}").ToArray(); RetentionSummary.Text = $"{(ViewModel.Settings.IsEnabled ? "Enabled" : "Disabled")} · keeps {ViewModel.Settings.RetentionPerDrive} analyses per drive"; }
    private void HistorySelectionChanged(object sender, SelectionChangedEventArgs args) => CompareButton.IsEnabled = HistoryList.SelectedItems.Count == 2;
    private async void RefreshHistory(object sender, RoutedEventArgs args) => await ActivateAsync();
    private async void CompareSelected(object sender, RoutedEventArgs args) { var indexes = HistoryList.SelectedItems.Select(item => HistoryList.Items.IndexOf(item)).Where(i => i >= 0).ToArray(); if (indexes.Length != 2) return; var report = await ViewModel.CompareAsync(ViewModel.Items[indexes[0]].Id, ViewModel.Items[indexes[1]].Id); if (report is null) { HistoryStatus.Text = "Comparison is unavailable."; return; } ComparisonPanel.Visibility = Visibility.Visible; ComparisonConfidence.Text = $"{OverviewPage.Humanize(report.Compatibility.Kind)} · {report.Compatibility.Confidence} confidence"; DeltaList.ItemsSource = report.Metrics.Concat(report.Categories).Where(x => x.Kind != Clyr.Contracts.DeltaKind.Unchanged).OrderByDescending(x => Math.Abs(x.Change ?? 0)).Take(20).Select(x => $"{OverviewPage.Humanize(x.Metric)}: {(x.Change >= 0 ? "+" : "")}{OverviewPage.FormatSigned(x.Change)} · {OverviewPage.Humanize(x.Kind)}").ToArray(); ComparisonWarnings.Text = string.Join(" ", report.Compatibility.Warnings.Concat(report.Insights)); }
    private async void DeleteSelected(object sender, RoutedEventArgs args) { if (HistoryList.SelectedItems.Count != 1) { HistoryStatus.Text = "Select one snapshot to delete."; return; } if (!await Confirm("Delete this history entry?", "This deletes only CLYR’s local history. It does not delete files from your drive.")) return; var index = HistoryList.Items.IndexOf(HistoryList.SelectedItems[0]); var deleted = await ViewModel.DeleteAsync(ViewModel.Items[index].Id); HistoryStatus.Text = deleted ? "History entry deleted." : "The history entry was not found."; Render(); }
    private async void ClearHistory(object sender, RoutedEventArgs args) { if (!await Confirm("Clear all history?", "This deletes only CLYR’s local history. It does not delete files from your drive.")) return; var count = await ViewModel.ClearAsync(); HistoryStatus.Text = $"Deleted {count} local history entries."; Render(); }
    private void RunAnalysis(object sender, RoutedEventArgs args) => ViewModel.Navigate("Scan"); private void OpenSettings(object sender, RoutedEventArgs args) => ViewModel.Navigate("Settings");
    private async Task<bool> Confirm(string title, string message) { var dialog = new ContentDialog { Title = title, Content = message, PrimaryButtonText = "Confirm", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot }; return await dialog.ShowAsync() == ContentDialogResult.Primary; }
}
