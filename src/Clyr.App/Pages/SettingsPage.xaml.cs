using Clyr.App.Controls;
using Clyr.App.ViewModels;
using Clyr.Persistence;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PageHost.LayoutModeChanged += (_, mode) => Reflow(mode);
        Reflow(ResponsivePageWidth.Wide);
    }

    public SettingsViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();

    public async Task ActivateAsync()
    {
        try
        {
            await ViewModel.LoadAsync();
            HistoryEnabled.IsOn = ViewModel.History.IsEnabled;
            Retention.Value = ViewModel.History.RetentionPerDrive;
            UpdateHistorySummary();
            SetHistoryControlsAvailable(true);
        }
        catch (SnapshotStoreException)
        {
            SetHistoryControlsAvailable(false);
            HistorySummary.Text = "Local history settings are unavailable right now.";
            SettingsStatus.Text = "CLYR could not load local history settings. No preferences were changed.";
        }
    }

    private void ThemeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (XamlRoot?.Content is not FrameworkElement root) return;
        root.RequestedTheme = ThemeSelector.SelectedIndex switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        SettingsStatus.Text = "Theme applied to this window. No restart is required.";
    }

    private async void SaveHistory(object sender, RoutedEventArgs args)
    {
        if (double.IsNaN(Retention.Value))
        {
            SettingsStatus.Text = "Enter a history limit from 2 through 1,000.";
            return;
        }

        try
        {
            await ViewModel.SaveHistoryAsync(HistoryEnabled.IsOn, (int)Retention.Value);
            UpdateHistorySummary();
            SettingsStatus.Text = "History settings saved locally. No restart is required.";
        }
        catch (ArgumentOutOfRangeException)
        {
            SettingsStatus.Text = "Enter a history limit from 2 through 1,000.";
        }
        catch (SnapshotStoreException)
        {
            SettingsStatus.Text = "History settings could not be saved. Your previous saved values remain in effect.";
        }
    }

    private async void ClearHistory(object sender, RoutedEventArgs args)
    {
        var dialog = new ContentDialog
        {
            Title = "Clear local history?",
            Content = "This removes saved aggregate analysis history. It does not delete files, settings, Review Plans or execution receipts.",
            PrimaryButtonText = "Clear history",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var count = await ViewModel.ClearHistoryAsync();
            SettingsStatus.Text = count == 1
                ? "Deleted 1 local history entry. Other local data was unchanged."
                : $"Deleted {count} local history entries. Other local data was unchanged.";
        }
        catch (SnapshotStoreException)
        {
            SettingsStatus.Text = "Local history could not be cleared. No settings or files were changed.";
        }
    }

    private void UpdateHistorySummary()
    {
        HistorySummary.Text = ViewModel.History.IsEnabled
            ? $"History is enabled and keeps up to {ViewModel.History.RetentionPerDrive} aggregate analyses per drive."
            : $"History is disabled. The saved retention limit remains {ViewModel.History.RetentionPerDrive} analyses per drive.";
    }

    private void SetHistoryControlsAvailable(bool available)
    {
        HistoryEnabled.IsEnabled = available;
        Retention.IsEnabled = available;
        SaveHistoryButton.IsEnabled = available;
        ClearHistoryButton.IsEnabled = available;
        if (available) return;
        const string help = "Local history settings could not be loaded, so this control is unavailable.";
        AutomationProperties.SetHelpText(HistoryEnabled, help);
        AutomationProperties.SetHelpText(Retention, help);
        AutomationProperties.SetHelpText(SaveHistoryButton, help);
        AutomationProperties.SetHelpText(ClearHistoryButton, help);
    }

    private void Reflow(ResponsivePageWidth mode)
    {
        var narrow = mode == ResponsivePageWidth.Narrow;
        ReflowSettingRow(ThemeSettingRow, ThemeSettingCopy, ThemeSelector, narrow);
        ReflowSettingRow(HistoryEnabledRow, HistoryEnabledCopy, HistoryEnabled, narrow);
        ReflowSettingRow(RetentionSettingRow, RetentionSettingCopy, Retention, narrow);
        SettingsActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
    }

    private static void ReflowSettingRow(Grid grid, FrameworkElement copy, FrameworkElement control, bool stack)
    {
        Grid.SetColumn(copy, 0);
        Grid.SetRow(copy, 0);
        Grid.SetColumnSpan(copy, stack ? grid.ColumnDefinitions.Count : 1);
        Grid.SetColumn(control, stack ? 0 : 1);
        Grid.SetRow(control, stack ? 1 : 0);
        Grid.SetColumnSpan(control, stack ? grid.ColumnDefinitions.Count : 1);
        control.HorizontalAlignment = stack ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
    }
}
