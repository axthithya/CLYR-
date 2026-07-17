using Clyr.App.Controls;
using Clyr.App.ViewModels;
using Clyr.Contracts;
using Clyr.Core.DeveloperMode;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Pages;

public sealed partial class DeveloperModePage : Page
{
    private readonly List<Guid> snapshotOrder = [];
    private DeveloperToolId? selectedToolId;

    public DeveloperModePage(DeveloperModeViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PageHost.LayoutModeChanged += (_, mode) => Reflow(mode);
        Reflow(ResponsivePageWidth.Wide);
    }

    public DeveloperModeViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();

    public async Task ActivateAsync()
    {
        try
        {
            await ViewModel.LoadSnapshotsAsync();
            RenderSnapshotPicker();
            if (ViewModel.Reports.Count == 0)
                DeveloperStatus.Text = "Select an analysis and choose Detect developer tools to see results.";
        }
        catch (Exception exception) when (exception is IOException or Clyr.Persistence.SnapshotStoreException)
        {
            DeveloperStatus.Text = "Local analysis history is unavailable. " + exception.Message;
        }
    }

    private void RenderSnapshotPicker()
    {
        snapshotOrder.Clear();
        SnapshotPicker.Items.Clear();
        foreach (var summary in ViewModel.Snapshots.OrderByDescending(item => item.CapturedAtUtc))
        {
            snapshotOrder.Add(summary.Id);
            SnapshotPicker.Items.Add($"{summary.CapturedAtUtc.LocalDateTime:g}  ·  {summary.Root}  ·  {summary.Mode}");
        }
        var selectedIndex = ViewModel.SelectedSnapshotId is { } id ? snapshotOrder.IndexOf(id) : -1;
        SnapshotPicker.SelectedIndex = selectedIndex >= 0 ? selectedIndex : (snapshotOrder.Count > 0 ? 0 : -1);
    }

    private void SnapshotSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        var index = SnapshotPicker.SelectedIndex;
        ViewModel.SelectedSnapshotId = index >= 0 && index < snapshotOrder.Count ? snapshotOrder[index] : null;
    }

    private async void DetectClick(object sender, RoutedEventArgs args)
    {
        DetectButton.IsEnabled = false;
        DeveloperStatus.Text = "Detecting developer tools using local analysis data and a narrow set of read-only status checks…";
        try
        {
            await ViewModel.DetectAsync();
            DeveloperStatus.Text = ViewModel.StatusMessage ?? $"Detection complete. {ViewModel.Reports.Count} tool(s) evaluated.";
            RenderTools();
        }
        finally { DetectButton.IsEnabled = true; }
    }

    private void RenderTools()
    {
        var reports = ViewModel.Reports;
        DeveloperEmpty.Visibility = reports.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DeveloperContent.Visibility = reports.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var columns = Math.Max(1, ToolGrid.ColumnDefinitions.Count);
        ToolGrid.Children.Clear();
        ToolGrid.RowDefinitions.Clear();
        for (var row = 0; row < (int)Math.Ceiling(reports.Count / (double)columns); row++)
            ToolGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });

        for (var index = 0; index < reports.Count; index++)
        {
            var report = reports[index];
            var descriptor = DeveloperToolRegistry.Descriptor(report.ToolId);
            var content = new StackPanel { Spacing = 6 };
            content.Children.Add(new TextBlock { Text = descriptor.DisplayName, Style = (Style)Application.Current.Resources["CardTitleStyle"] });
            content.Children.Add(new TextBlock
            {
                Text = Humanize(report.Status) + (report.DetectedVersion is null ? string.Empty : $" · version {report.DetectedVersion}"),
                Style = (Style)Application.Current.Resources["BodyMutedStyle"],
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(new TextBlock
            {
                Text = $"{OverviewPage.Format(report.TotalObservedLogicalBytes)} observed · {report.Candidates.Length} finding(s)",
                Style = (Style)Application.Current.Resources["BodyMutedStyle"]
            });
            var detailsButton = new Button { Content = "View details", IsEnabled = report.Candidates.Length > 0 || report.Diagnostics.Length > 0 };
            AutomationProperties.SetName(detailsButton, descriptor.DisplayName + " view details");
            var toolId = report.ToolId;
            detailsButton.Click += (_, _) => { selectedToolId = toolId; RenderDetail(); };
            content.Children.Add(detailsButton);
            var tile = new Border { Style = (Style)Application.Current.Resources["CardStyle"], Child = content };
            AutomationProperties.SetName(tile, descriptor.DisplayName + " developer tool card");
            Grid.SetColumn(tile, index % columns);
            Grid.SetRow(tile, index / columns);
            ToolGrid.Children.Add(tile);
        }
    }

    private void RenderDetail()
    {
        var report = selectedToolId is { } id ? ViewModel.Reports.FirstOrDefault(item => item.ToolId == id) : null;
        if (report is null) { DetailPanel.Visibility = Visibility.Collapsed; return; }
        var descriptor = DeveloperToolRegistry.Descriptor(report.ToolId);
        DetailPanel.Visibility = Visibility.Visible;
        DetailTitle.Text = $"{descriptor.DisplayName} — {Humanize(report.Status)}";
        DetailExplanation.Text = descriptor.Explanation;

        DetailDiagnosticsStack.Children.Clear();
        foreach (var diagnostic in report.Diagnostics)
            DetailDiagnosticsStack.Children.Add(new TextBlock { Text = diagnostic.Message, Style = (Style)Application.Current.Resources["BodyMutedStyle"], TextWrapping = TextWrapping.Wrap });

        DetailStack.Children.Clear();
        foreach (var candidate in report.Candidates)
        {
            var body = new StackPanel { Spacing = 6 };
            body.Children.Add(new TextBlock { Text = $"{candidate.Title} — {OverviewPage.Format(candidate.Impact.ObservedLogicalBytes)} observed", Style = (Style)Application.Current.Resources["CardTitleStyle"], TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock
            {
                Text = $"{candidate.Eligibility} · {candidate.Risk} risk · {candidate.Confidence} confidence\n{candidate.EligibilityReason}\nPossible consequence: {candidate.Consequence.PossibleOutcome}",
                Style = (Style)Application.Current.Resources["BodyMutedStyle"],
                TextWrapping = TextWrapping.Wrap
            });
            if (candidate.Eligibility == CleanupEligibility.DryRunEligible)
            {
                var reviewButton = new Button { Content = "Review in plan" };
                AutomationProperties.SetName(reviewButton, candidate.Title + " review in plan");
                var findingId = candidate.FindingId;
                reviewButton.Click += async (_, _) =>
                {
                    var plan = await ViewModel.CreatePlanAsync(findingId);
                    if (plan is not null) ViewModel.Navigate("Review Plan");
                    else DeveloperStatus.Text = "This finding could not be added to a plan. Try detecting developer tools again.";
                };
                body.Children.Add(reviewButton);
            }
            else
            {
                body.Children.Add(new TextBlock { Text = "No automatic action is available for this finding. Review manually if needed.", Style = (Style)Application.Current.Resources["BodyMutedStyle"], TextWrapping = TextWrapping.Wrap });
            }
            DetailStack.Children.Add(new Border { Style = (Style)Application.Current.Resources["CardStyle"], Child = body });
        }
    }

    private void CloseDetails(object sender, RoutedEventArgs args)
    {
        selectedToolId = null;
        DetailPanel.Visibility = Visibility.Collapsed;
    }

    private void Reflow(ResponsivePageWidth mode)
    {
        var columns = mode switch { ResponsivePageWidth.Wide => 3, ResponsivePageWidth.Medium => 2, _ => 1 };
        ToolGrid.ColumnDefinitions.Clear();
        for (var column = 0; column < columns; column++) ToolGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        SnapshotGrid.ColumnDefinitions[1].Width = mode == ResponsivePageWidth.Narrow ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        if (ViewModel.Reports.Count > 0) RenderTools();
    }

    private static string Humanize(DeveloperToolStatus status) => status switch
    {
        DeveloperToolStatus.FullyDetected => "Detected",
        DeveloperToolStatus.PartiallyDetected => "Partially detected",
        DeveloperToolStatus.InstalledNoData => "Installed · no storage data yet",
        DeveloperToolStatus.NotInstalled => "Not found",
        DeveloperToolStatus.Unavailable => "No evidence yet",
        DeveloperToolStatus.PermissionLimited => "Permission limited",
        DeveloperToolStatus.UnsupportedVersion => "Unsupported version",
        DeveloperToolStatus.ProbeFailed => "Status check failed",
        _ => status.ToString()
    };
}
