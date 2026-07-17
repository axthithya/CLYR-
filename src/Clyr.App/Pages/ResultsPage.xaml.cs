using Clyr.App.ViewModels;
using Clyr.Contracts;
using Clyr.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Pages;

public sealed partial class ResultsPage : Page
{
    public ResultsPage(ResultsViewModel viewModel) { ViewModel = viewModel; InitializeComponent(); viewModel.Session.StateChanged += (_, _) => Refresh(); PageHost.LayoutModeChanged += (_, mode) => Reflow(mode); Refresh(); }
    public ResultsViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();

    public void Refresh()
    {
        var r = ViewModel.Session.Result;
        EmptyPanel.Visibility = r is null ? Visibility.Visible : Visibility.Collapsed;
        Dashboard.Visibility = r is null ? Visibility.Collapsed : Visibility.Visible;
        if (r is null) return;
        var c = r.Classification;

        // The metric cards below use the exact same Clyr.Core.ScanAccounting model as the Scan page's own "Last
        // attempt" summary — one accounting model, never two competing sets of numbers on different pages.
        var summary = ScanAccounting.Summarize(r);
        DriveUsedMetric.Text = r.DriveUsedBytes is long used ? OverviewPage.Format(used) : "Unavailable";
        AccountedMetric.Text = OverviewPage.Format(r.LogicalBytesObserved);
        AccountedPercentText.Text = summary.AccountedPercentage is { } accounted
            ? $"{accounted:F1}% of drive used"
            : "Drive-used basis unavailable";
        NotObservedMetric.Text = summary.UnaccountedDriveBytes is { } unaccounted
            ? OverviewPage.Format(unaccounted)
            : "Unavailable";
        ClassifiedMetric.Text = summary.ClassificationPercentage is { } classification ? $"{classification:F1}%" : "Unavailable";
        UnclassifiedText.Text = $"Observed but unclassified: {OverviewPage.Format(summary.UnclassifiedObservedBytes)}";

        QualityBannerText.Text = summary.Quality switch
        {
            ScanQuality.Insufficient => "Scan quality: Insufficient coverage",
            ScanQuality.Partial => "Scan quality: Partial coverage",
            ScanQuality.Good => "Scan quality: Good coverage",
            ScanQuality.Excellent => "Scan quality: Excellent coverage",
            _ => "Scan quality: Unavailable"
        };
        QualityRecommendationText.Text = summary.Quality switch
        {
            ScanQuality.Insufficient or ScanQuality.Partial =>
                "Recommended next step: Run Deep Analysis to account for more of this drive.",
            _ => "This scan accounted for most or all of this drive's used space."
        };

        ContributorList.ItemsSource = c?.Categories.OrderByDescending(x => x.LogicalBytes).Select((x, i) => $"{i + 1}. {OverviewPage.Humanize(x.Category)} — {OverviewPage.Format(x.LogicalBytes)} — {(r.LogicalBytesObserved > 0 ? x.LogicalBytes * 100d / r.LogicalBytesObserved : 0):F1}%").ToArray() ?? [];
        FindingList.ItemsSource = c?.Findings.Select(x => $"{x.Title}\n{OverviewPage.Format(x.LogicalBytes)} · {OverviewPage.Humanize(x.Category)} · {x.Confidence}\n{x.Explanation.WhatItMeans}").ToArray() ?? [];
        DirectoryList.ItemsSource = r.LargestDirectories.Select(x => $"{x.DisplayPath} — {OverviewPage.Format(x.LogicalBytes)}").ToArray();
        DirectoryHeading.Visibility = r.LargestDirectories.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DirectoryList.Visibility = r.LargestDirectories.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ScanSummary.Text = $"{r.Status} · {r.Mode} · {r.EndedAt.LocalDateTime:g} · {r.Coverage.FilesObserved:N0} files · {r.Coverage.DirectoriesObserved:N0} directories";
        Limitations.Text = r.AccountingNote + " " + string.Join(" ", c?.Limitations ?? []);
    }

    private void RunQuick(object sender, RoutedEventArgs args) { ViewModel.Session.SelectedScanMode = ScanMode.Quick; ViewModel.Navigate("Scan"); }
    private void RunDeep(object sender, RoutedEventArgs args) { ViewModel.Session.SelectedScanMode = ScanMode.Deep; ViewModel.Navigate("Scan"); }
    private void RunAgain(object sender, RoutedEventArgs args) => ViewModel.Navigate("Scan");
    private void ReviewActions(object sender, RoutedEventArgs args) => ViewModel.Navigate("Review Plan");
    private void Reflow(Controls.ResponsivePageWidth mode)
    {
        var narrow = mode == Controls.ResponsivePageWidth.Narrow;
        var cards = new FrameworkElement[] { DriveUsedCard, AccountedCard, NotObservedCard, ClassifiedCard };
        for (var i = 0; i < cards.Length; i++) { Grid.SetColumn(cards[i], narrow ? 0 : i); Grid.SetRow(cards[i], narrow ? i : 0); }
        Grid.SetColumn(ResultSidebar, narrow ? 0 : 1);
        Grid.SetRow(ResultSidebar, narrow ? 1 : 0);
        EmptyActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
    }
}
