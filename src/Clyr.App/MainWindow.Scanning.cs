using System.Diagnostics.CodeAnalysis;
using Clyr.Contracts;
using Clyr.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Clyr.App;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The scan token is disposed at the operation boundary; WinUI owns the Window lifecycle.")]
public sealed partial class MainWindow
{
    private IScanService? scanService;
    private IDriveDiscovery? driveDiscovery;
    private CancellationTokenSource? scanCancellation;
    private IReadOnlyList<DriveSummary> availableDrives = [];

    public MainWindow(IDemoDataService demo, ApplicationConfiguration configuration, IScanService scanService,
        IDriveDiscovery driveDiscovery) : this(demo, configuration)
    {
        this.scanService = scanService;
        this.driveDiscovery = driveDiscovery;
        LoadDrives();
    }

    private void LoadDrives()
    {
        availableDrives = driveDiscovery!.Discover();
        DriveSelector.ItemsSource = availableDrives.Select(DriveLabel).ToArray();
        var defaultIndex = availableDrives.Select((drive, index) => (drive, index))
            .Where(item => item.drive.IsSupported).OrderByDescending(item => item.drive.IsSystemVolume).Select(item => item.index).DefaultIfEmpty(-1).First();
        DriveSelector.SelectedIndex = defaultIndex;
        var eligible = availableDrives.Count(item => item.IsSupported);
        DriveSummaryText.Text = eligible == 0
            ? "No eligible ready fixed NTFS volume was found. No scan can start."
            : $"{eligible} eligible local volume(s). Analysis reads metadata only and never follows reparse points or hydrates cloud files.";
        StartScanButton.IsEnabled = defaultIndex >= 0;
    }

    private async void OnStartScan(object sender, RoutedEventArgs args)
    {
        if (scanService is null || DriveSelector.SelectedIndex < 0 || DriveSelector.SelectedIndex >= availableDrives.Count) return;
        var drive = availableDrives[DriveSelector.SelectedIndex];
        if (!drive.IsSupported) { ScanStatusText.Text = drive.SupportReason; return; }
        var mode = DeepMode.IsChecked == true ? ScanMode.Deep : ScanMode.Quick;
        scanCancellation = new CancellationTokenSource();
        StartScanButton.IsEnabled = false;
        CancelScanButton.IsEnabled = true;
        ResultList.ItemsSource = null;
        var dispatcher = DispatcherQueue;
        var progress = new UiProgress(dispatcher, value =>
        {
            ScanStatusText.Text = $"{value.Status}: {value.FilesObserved:N0} files, {FormatBytes(value.LogicalBytesObserved)} observed; {value.SkippedEntries:N0} skipped.";
            CurrentPathText.Text = "Current location: " + value.CurrentPath;
        });
        try
        {
            var result = await scanService.ScanAsync(new(drive.Root, mode), progress, scanCancellation.Token);
            ScanStatusText.Text = $"{result.Status}: {FormatBytes(result.LogicalBytesObserved)} logical metadata observed. Precision: {result.Precision}.";
            CurrentPathText.Text = result.AccountingNote;
            ResultList.ItemsSource = result.TopLevelDirectories.Select(item => $"{item.DisplayPath} — {FormatBytes(item.LogicalBytes)} ({item.Precision})").ToArray();
            CoverageText.Text = $"Coverage: {result.Coverage.FilesObserved:N0} files; {result.Coverage.DirectoriesObserved:N0} directories; {result.Issues.Sum(item => item.Count):N0} warnings; no content read.";
        }
        finally
        {
            StartScanButton.IsEnabled = true;
            CancelScanButton.IsEnabled = false;
            scanCancellation.Dispose();
            scanCancellation = null;
        }
    }

    private void OnCancelScan(object sender, RoutedEventArgs args)
    {
        scanCancellation?.Cancel();
        ScanStatusText.Text = "Cancelling — partial observations will be retained.";
        CancelScanButton.IsEnabled = false;
    }

    private static string DriveLabel(DriveSummary drive)
    {
        var used = drive.UsedBytes.HasValue ? FormatBytes(drive.UsedBytes.Value) : "used unavailable";
        var capacity = drive.CapacityBytes.HasValue ? FormatBytes(drive.CapacityBytes.Value) : "capacity unavailable";
        return $"{drive.Root} {drive.Label} — {drive.FileSystem} — {used} of {capacity} — {(drive.IsSupported ? "supported" : drive.SupportReason)}";
    }

    private static string FormatBytes(long bytes) => bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824d:F2} GiB" : bytes >= 1_048_576 ? $"{bytes / 1_048_576d:F2} MiB" : bytes >= 1024 ? $"{bytes / 1024d:F2} KiB" : bytes + " B";

    private sealed class UiProgress(DispatcherQueue dispatcher, Action<ScanProgress> update) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => dispatcher.TryEnqueue(() => update(value));
    }
}
