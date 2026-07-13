using Clyr.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace Clyr.App.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel) { ViewModel = viewModel; InitializeComponent(); PageHost.LayoutModeChanged += (_, mode) => SettingsActions.Orientation = mode == Controls.ResponsivePageWidth.Narrow ? Orientation.Vertical : Orientation.Horizontal; }
    public SettingsViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll(); public async Task ActivateAsync() { await ViewModel.LoadAsync(); HistoryEnabled.IsOn = ViewModel.History.IsEnabled; Retention.Value = ViewModel.History.RetentionPerDrive; HistorySummary.Text = $"Keeps up to {ViewModel.History.RetentionPerDrive} aggregate analyses per drive."; }
    private void ThemeChanged(object sender, SelectionChangedEventArgs args) { if (XamlRoot?.Content is FrameworkElement root) root.RequestedTheme = ThemeSelector.SelectedIndex switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default }; }
    private async void SaveHistory(object sender, RoutedEventArgs args) { if (double.IsNaN(Retention.Value)) return; await ViewModel.SaveHistoryAsync(HistoryEnabled.IsOn, (int)Retention.Value); SettingsStatus.Text = "History settings saved."; }
    private async void ClearHistory(object sender, RoutedEventArgs args) { var dialog = new ContentDialog { Title = "Clear local history?", Content = "This deletes only CLYR’s local history. It does not delete files from your drive.", PrimaryButtonText = "Clear history", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot }; if (await dialog.ShowAsync() != ContentDialogResult.Primary) return; var count = await ViewModel.ClearHistoryAsync(); SettingsStatus.Text = $"Deleted {count} local history entries."; }
}
