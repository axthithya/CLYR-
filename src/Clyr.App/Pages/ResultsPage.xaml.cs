using Clyr.App.ViewModels;
using Clyr.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Pages;

public sealed partial class ResultsPage : Page
{
    public ResultsPage(ResultsViewModel viewModel) { ViewModel = viewModel; InitializeComponent(); viewModel.Session.StateChanged += (_, _) => Refresh(); PageHost.LayoutModeChanged += (_, mode) => Reflow(mode); Refresh(); }
    public ResultsViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();
    public void Refresh() { var r = ViewModel.Session.Result; EmptyPanel.Visibility = r is null ? Visibility.Visible : Visibility.Collapsed; Dashboard.Visibility = r is null ? Visibility.Collapsed : Visibility.Visible; if (r is null) return; var c = r.Classification; DriveUsedMetric.Text = r.DriveUsedBytes is long used ? OverviewPage.Format(used) : "Unavailable"; ObservedMetric.Text = OverviewPage.Format(r.LogicalBytesObserved); var classified = c?.Coverage.ClassifiedBytes ?? 0; CoverageMetric.Text = r.LogicalBytesObserved > 0 ? $"{classified * 100d / r.LogicalBytesObserved:F0}%" : "Unavailable"; UnknownMetric.Text = OverviewPage.Format(c?.Coverage.UnknownBytes ?? r.LogicalBytesObserved); ContributorList.ItemsSource = c?.Categories.OrderByDescending(x => x.LogicalBytes).Select((x, i) => $"{i + 1}. {OverviewPage.Humanize(x.Category)} — {OverviewPage.Format(x.LogicalBytes)} — {(r.LogicalBytesObserved > 0 ? x.LogicalBytes * 100d / r.LogicalBytesObserved : 0):F1}%").ToArray() ?? []; FindingList.ItemsSource = c?.Findings.Select(x => $"{x.Title}\n{OverviewPage.Format(x.LogicalBytes)} · {OverviewPage.Humanize(x.Category)} · {x.Confidence}\n{x.Explanation.WhatItMeans}").ToArray() ?? []; DirectoryList.ItemsSource = r.LargestDirectories.Select(x => $"{x.DisplayPath} — {OverviewPage.Format(x.LogicalBytes)}").ToArray(); DirectoryHeading.Visibility = r.LargestDirectories.Count > 0 ? Visibility.Visible : Visibility.Collapsed; DirectoryList.Visibility = r.LargestDirectories.Count > 0 ? Visibility.Visible : Visibility.Collapsed; ScanSummary.Text = $"{r.Status} · {r.Mode} · {r.EndedAt.LocalDateTime:g} · {r.Coverage.FilesObserved:N0} files · {r.Coverage.DirectoriesObserved:N0} directories"; Limitations.Text = r.AccountingNote + " " + string.Join(" ", c?.Limitations ?? []); }
    private void RunQuick(object sender, RoutedEventArgs args) { ViewModel.Session.SelectedScanMode = ScanMode.Quick; ViewModel.Navigate("Scan"); }
    private void RunDeep(object sender, RoutedEventArgs args) { ViewModel.Session.SelectedScanMode = ScanMode.Deep; ViewModel.Navigate("Scan"); }
    private void RunAgain(object sender, RoutedEventArgs args) => ViewModel.Navigate("Scan");
    private void ReviewActions(object sender, RoutedEventArgs args) => ViewModel.Navigate("Review Plan");
    private void Reflow(Controls.ResponsivePageWidth mode)
    {
        var narrow = mode == Controls.ResponsivePageWidth.Narrow;
        var cards = new FrameworkElement[] { DriveUsedCard, ObservedCard, CoverageCard, UnknownCard };
        for (var i = 0; i < cards.Length; i++) { Grid.SetColumn(cards[i], narrow ? 0 : i); Grid.SetRow(cards[i], narrow ? i : 0); }
        Grid.SetColumn(ResultSidebar, narrow ? 0 : 1);
        Grid.SetRow(ResultSidebar, narrow ? 1 : 0);
        EmptyActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
    }
}
