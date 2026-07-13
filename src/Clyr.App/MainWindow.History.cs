using Clyr.Contracts;
using Clyr.Core;
using Clyr.Persistence;
using Clyr.Rules;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App;

public sealed partial class MainWindow
{
    private ISnapshotStore? historyStore;
    private IReadOnlyList<SnapshotSummary> historyItems = [];
    private bool loadingHistorySettings;

    public MainWindow(IDemoDataService demo, ApplicationConfiguration configuration, IScanService scanService,
        IDriveDiscovery driveDiscovery, RulePackLoadResult rulePack, ISnapshotStore historyStore)
        : this(demo, configuration, scanService, driveDiscovery, rulePack) => this.historyStore = historyStore;

    private async Task LoadHistoryAsync()
    {
        if (historyStore is null) return;
        try
        {
            historyItems = await historyStore.ListAsync();
            HistoryList.ItemsSource = historyItems.Select(item => $"{item.CapturedAtUtc.LocalDateTime:g} — {item.Root} — {item.State} — {item.Mode} — {FormatBytes(item.LogicalBytesObserved)}").ToArray();
            var settings = await historyStore.GetSettingsAsync();
            loadingHistorySettings = true; HistoryEnabled.IsOn = settings.IsEnabled; HistoryRetention.Value = settings.RetentionPerDrive; loadingHistorySettings = false;
        }
        catch (Exception exception) when (exception is IOException or SnapshotStoreException)
        { ComparisonText.Text = "History is unavailable: " + exception.Message; }
    }

    private async void OnRefreshHistory(object sender, RoutedEventArgs args) => await LoadHistoryAsync();

    private async void OnCompareHistory(object sender, RoutedEventArgs args)
    {
        if (historyStore is null || HistoryList.SelectedRanges.Count == 0) return;
        var indexes = HistoryList.SelectedItems.Select(item => HistoryList.Items.IndexOf(item)).Where(index => index >= 0).Take(2).ToArray();
        if (indexes.Length != 2) { ComparisonText.Text = "Select exactly two snapshots."; return; }
        var snapshots = await Task.WhenAll(historyStore.GetAsync(historyItems[indexes[0]].Id), historyStore.GetAsync(historyItems[indexes[1]].Id));
        if (snapshots[0] is null || snapshots[1] is null) { ComparisonText.Text = "A selected snapshot is no longer available."; return; }
        var before = snapshots.OrderBy(item => item!.CapturedAtUtc).First()!; var after = snapshots.OrderBy(item => item!.CapturedAtUtc).Last()!;
        var report = SnapshotComparer.Compare(before, after);
        ComparisonText.Text = $"{report.Compatibility.Kind} ({report.Compatibility.Confidence}). " + string.Join(" ", report.Compatibility.Warnings.Concat(report.Insights));
    }

    private async void OnDeleteHistory(object sender, RoutedEventArgs args)
    {
        if (historyStore is null || HistoryList.SelectedItems.Count != 1) { ComparisonText.Text = "Select one snapshot to delete."; return; }
        if (!await ConfirmAsync("Delete snapshot?", "This removes only the selected aggregate database record.")) return;
        var index = HistoryList.Items.IndexOf(HistoryList.SelectedItems[0]); if (index >= 0) await historyStore.DeleteAsync(historyItems[index].Id); await LoadHistoryAsync();
    }

    private async void OnClearHistory(object sender, RoutedEventArgs args)
    {
        if (historyStore is null || !await ConfirmAsync("Clear history?", "This removes all local aggregate snapshot records. It does not touch scanned files.")) return;
        var count = await historyStore.ClearAsync(); ComparisonText.Text = $"Removed {count} aggregate snapshots."; await LoadHistoryAsync();
    }

    private async void OnHistorySettingsChanged(object sender, RoutedEventArgs args) => await SaveSettingsAsync();
    private async void OnHistoryRetentionChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => await SaveSettingsAsync();
    private async Task SaveSettingsAsync()
    {
        if (loadingHistorySettings || historyStore is null || double.IsNaN(HistoryRetention.Value)) return;
        await historyStore.SetSettingsAsync(new(HistoryEnabled.IsOn, (int)HistoryRetention.Value, true, true));
    }
    private async Task<bool> ConfirmAsync(string title, string content)
    {
        var dialog = new ContentDialog { Title = title, Content = content, PrimaryButtonText = "Confirm", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, XamlRoot = Content.XamlRoot };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
