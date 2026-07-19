using System.Globalization;
using System.Runtime.InteropServices;
using Clyr.App.Controls;
using Clyr.App.ViewModels;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Core.DeveloperMode;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace Clyr.App.Pages;

public sealed partial class DeveloperModePage : Page
{
    private readonly List<DispatcherQueueTimer> copyFeedbackTimers = [];
    private readonly List<Grid> technicalRows = [];
    private readonly List<Guid> snapshotOrder = [];
    private bool renderingSnapshotPicker;
    private DeveloperToolId? selectedToolId;

    public DeveloperModePage(DeveloperModeViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        RunAnalysisButton.Click += (_, _) => ViewModel.Navigate("Scan");
        PageHost.LayoutModeChanged += (_, mode) => Reflow(mode);
        Reflow(ResponsivePageWidth.Wide);
    }

    public DeveloperModeViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();

    public async Task ActivateAsync()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        NoDataState.Visibility = Visibility.Collapsed;
        UnavailableState.Visibility = Visibility.Collapsed;
        DeveloperContent.Visibility = Visibility.Collapsed;
        try
        {
            await ViewModel.LoadSnapshotsAsync();
            RenderSnapshotPicker();
            RenderSelectedSnapshot();
        }
        catch (Exception exception) when (exception is IOException or Clyr.Persistence.SnapshotStoreException)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            UnavailableState.Visibility = Visibility.Visible;
        }
    }

    private void RenderSnapshotPicker()
    {
        renderingSnapshotPicker = true;
        snapshotOrder.Clear();
        SnapshotPicker.Items.Clear();
        foreach (var summary in ViewModel.Snapshots.OrderByDescending(item => item.CapturedAtUtc))
        {
            snapshotOrder.Add(summary.Id);
            SnapshotPicker.Items.Add($"{summary.CapturedAtUtc.LocalDateTime:g}  ·  {summary.Root}  ·  {ModeLabel(summary.Mode)}");
        }
        var selectedIndex = ViewModel.SelectedSnapshotId is { } id ? snapshotOrder.IndexOf(id) : -1;
        SnapshotPicker.SelectedIndex = selectedIndex >= 0 ? selectedIndex : (snapshotOrder.Count > 0 ? 0 : -1);
        renderingSnapshotPicker = false;
    }

    private async void SnapshotSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (renderingSnapshotPicker) return;
        var index = SnapshotPicker.SelectedIndex;
        var id = index >= 0 && index < snapshotOrder.Count ? snapshotOrder[index] : (Guid?)null;
        try
        {
            await ViewModel.SelectSnapshotAsync(id);
            selectedToolId = null;
            DetailPanel.Visibility = Visibility.Collapsed;
            RenderSelectedSnapshot();
        }
        catch (Exception exception) when (exception is IOException or Clyr.Persistence.SnapshotStoreException)
        {
            DeveloperContent.Visibility = Visibility.Collapsed;
            UnavailableState.Visibility = Visibility.Visible;
        }
    }

    private void RenderSelectedSnapshot()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        technicalRows.Clear();
        var snapshot = ViewModel.SelectedSnapshot;
        if (snapshot is null)
        {
            NoDataState.Visibility = Visibility.Visible;
            DeveloperContent.Visibility = Visibility.Collapsed;
            return;
        }

        NoDataState.Visibility = Visibility.Collapsed;
        UnavailableState.Visibility = Visibility.Collapsed;
        DeveloperContent.Visibility = Visibility.Visible;
        RenderSummary(snapshot);
        RenderTechnicalSections(snapshot);
        RenderTools();
        DeveloperStatus.Text = ViewModel.Reports.Count == 0
            ? "Selected analysis loaded. Developer tool detection has not run."
            : ViewModel.StatusMessage ?? $"Detection complete. {ViewModel.Reports.Count:N0} tools evaluated.";
        Reflow(PageHost.LayoutMode);
    }

    private void RenderSummary(StorageSnapshot snapshot)
    {
        SummaryActions.Children.Clear();
        SummaryActions.Children.Add(CreateCopyButton("diagnostic summary", DiagnosticSummary(snapshot)));
        SummaryGrid.Children.Clear();
        var metrics = new[]
        {
            ("Scan availability", "Available"),
            ("Analysis type", ModeLabel(snapshot.Mode)),
            ("Completion status", StateLabel(snapshot.State)),
            ("Accounted storage", Percentage(AccountedPercentage(snapshot))),
            ("Files examined", Number(snapshot.Coverage.FilesObserved)),
            ("Directories examined", Number(snapshot.Coverage.DirectoriesObserved)),
            ("Warning count", Number(snapshot.Warnings.Count)),
            ("Snapshot schema", $"Version {snapshot.SchemaVersion:N0}")
        };
        foreach (var (label, value) in metrics)
        {
            var metric = new StackPanel { Spacing = 3, MinWidth = 0 };
            metric.Children.Add(new TextBlock
            {
                Text = label,
                Style = (Style)Application.Current.Resources["BodyMutedStyle"]
            });
            metric.Children.Add(new TextBlock
            {
                Text = value,
                Style = (Style)Application.Current.Resources["CardTitleStyle"],
                TextWrapping = TextWrapping.Wrap
            });
            SummaryGrid.Children.Add(metric);
        }
    }

    private void RenderTechnicalSections(StorageSnapshot snapshot)
    {
        ClearRows(ScanExecutionRows);
        AddTechnicalRow(ScanExecutionRows, "Scan ID", snapshot.ScanId.ToString("D", CultureInfo.InvariantCulture),
            "Full identifier for this recorded scan.", monospace: true, copy: true);
        AddTechnicalRow(ScanExecutionRows, "Captured", snapshot.CapturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            "UTC timestamp from the saved analysis.", monospace: true, copy: true);
        AddTechnicalRow(ScanExecutionRows, "Analysis type", ModeLabel(snapshot.Mode));
        AddTechnicalRow(ScanExecutionRows, "Completion status", StateLabel(snapshot.State));
        AddTechnicalRow(ScanExecutionRows, "Drive", snapshot.Drive.Root,
            "Drive root only; personal subpaths are not shown.", monospace: true);
        AddTechnicalRow(ScanExecutionRows, "File system", Recorded(snapshot.Drive.FileSystem), monospace: true);

        ClearRows(StorageAccountingRows);
        AddTechnicalRow(StorageAccountingRows, "Observed storage", OverviewPage.Format(snapshot.LogicalBytesObserved));
        AddTechnicalRow(StorageAccountingRows, "Drive-used basis", Bytes(snapshot.Drive.UsedBytes));
        AddTechnicalRow(StorageAccountingRows, "Unobserved storage", Bytes(snapshot.UnaccountedBytes));
        AddTechnicalRow(StorageAccountingRows, "Accounted percentage", Percentage(AccountedPercentage(snapshot)),
            "Observed storage divided by the recorded drive-used basis.");
        AddTechnicalRow(StorageAccountingRows, "Classified observed storage", OverviewPage.Format(snapshot.ClassifiedBytes));
        AddTechnicalRow(StorageAccountingRows, "Unclassified observed storage", OverviewPage.Format(snapshot.UnknownBytes));
        AddTechnicalRow(StorageAccountingRows, "Classification percentage", Percentage(ClassificationPercentage(snapshot)),
            "Classification coverage is separate from whole-drive accounted coverage.");
        AddTechnicalRow(StorageAccountingRows, "Files examined", Number(snapshot.Coverage.FilesObserved));
        AddTechnicalRow(StorageAccountingRows, "Directories examined", Number(snapshot.Coverage.DirectoriesObserved));
        AddTechnicalRow(StorageAccountingRows, "Inaccessible entries", Number(snapshot.Coverage.InaccessibleEntries));

        RenderClassification(snapshot);
        RenderWarnings(snapshot);

        ClearRows(MetadataRows);
        AddTechnicalRow(MetadataRows, "Snapshot schema",
            snapshot.SchemaVersion.ToString("N0", CultureInfo.CurrentCulture), monospace: true);
        AddTechnicalRow(MetadataRows, "Application version", Recorded(snapshot.ApplicationVersion), monospace: true, copy: true);
        AddTechnicalRow(MetadataRows, "Rule pack", Recorded(snapshot.RulePackId), monospace: true, copy: true);
        AddTechnicalRow(MetadataRows, "Rule pack version", Recorded(snapshot.RulePackVersion), monospace: true);
        AddTechnicalRow(MetadataRows, "Rule pack digest", Recorded(snapshot.RulePackDigest),
            "Integrity digest recorded with the analysis.", monospace: true, copy: true);
        AddTechnicalRow(MetadataRows, "Drive identity quality",
            Friendly(snapshot.Drive.IdentityQuality.ToString()), monospace: true);
    }

    private void RenderClassification(StorageSnapshot snapshot)
    {
        ClearRows(ClassificationRows);
        var hasClassification = snapshot.Categories.Count > 0 || snapshot.Findings.Count > 0;
        ClassificationSection.Visibility = hasClassification ? Visibility.Visible : Visibility.Collapsed;
        if (!hasClassification) return;

        foreach (var category in snapshot.Categories.OrderByDescending(item => item.LogicalBytes))
        {
            AddTechnicalRow(ClassificationRows, Friendly(category.Category.ToString()),
                $"{OverviewPage.Format(category.LogicalBytes)} · {Number(category.FileCount)} files",
                $"{Friendly(category.Precision.ToString())} measurement · {Friendly(category.Status.ToString())} status");
        }
        foreach (var finding in snapshot.Findings.OrderByDescending(item => item.LogicalBytes))
        {
            AddTechnicalRow(ClassificationRows, FriendlyRule(finding.RuleId),
                $"{OverviewPage.Format(finding.LogicalBytes)} · {Number(finding.FileCount)} files",
                $"Rule {finding.RuleId} · {Friendly(finding.Confidence.ToString())} confidence · {Friendly(finding.Status.ToString())}");
        }
    }

    private void RenderWarnings(StorageSnapshot snapshot)
    {
        ClearRows(WarningRows);
        var warnings = snapshot.Warnings
            .Select(SafeDiagnosticText)
            .GroupBy(value => value, StringComparer.Ordinal)
            .Select(group => (Message: group.Key, Count: group.Count()))
            .ToArray();
        var missingWarningDetails = warnings.Length == 0 && snapshot.State == SnapshotState.Partial;
        WarningsSection.Visibility = warnings.Length > 0 || missingWarningDetails
            ? Visibility.Visible
            : Visibility.Collapsed;

        foreach (var warning in warnings)
            AddTechnicalRow(WarningRows, "⚠ Warning", warning.Message,
                warning.Count == 1 ? "Recorded once." : $"Recorded {warning.Count:N0} times.");
        if (missingWarningDetails)
            AddTechnicalRow(WarningRows, "⚠ Warning", "Not recorded",
                "This older or incomplete analysis indicates warnings, but their details were not retained.");
    }

    private async void DetectClick(object sender, RoutedEventArgs args)
    {
        if (ViewModel.SelectedSnapshot is null)
        {
            DeveloperStatus.Text = "No local analysis is available. Run an analysis first.";
            return;
        }

        DetectButton.IsEnabled = false;
        DeveloperStatus.Text = "Evaluating the selected analysis with the existing bounded, read-only tool checks…";
        try
        {
            await ViewModel.DetectAsync();
            DeveloperStatus.Text = ViewModel.StatusMessage ?? $"Detection complete. {ViewModel.Reports.Count:N0} tools evaluated.";
            RenderTools();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Clyr.Persistence.SnapshotStoreException)
        {
            DeveloperStatus.Text = "Developer tool diagnostics could not be completed. The selected analysis was not changed.";
        }
        finally
        {
            DetectButton.IsEnabled = true;
        }
    }

    private void RenderTools()
    {
        DeveloperToolList.Children.Clear();
        ToolEmpty.Visibility = ViewModel.Reports.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (ViewModel.Reports.Count == 0) return;

        foreach (var report in ViewModel.Reports)
        {
            var descriptor = DeveloperToolRegistry.Descriptor(report.ToolId);
            var status = StatusLabel(report.Status);
            var version = report.DetectedVersion is null ? string.Empty : $" · version {report.DetectedVersion}";
            var detailAction = new Button
            {
                Content = "View details",
                Style = (Style)Application.Current.Resources["QuietButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Left
            };
            AutomationProperties.SetName(detailAction, $"View {descriptor.DisplayName} diagnostic details");
            var toolId = report.ToolId;
            detailAction.Click += (_, _) =>
            {
                selectedToolId = toolId;
                RenderDetail();
            };
            AddTechnicalRow(DeveloperToolList, descriptor.DisplayName,
                $"{StatusIcon(report.Status)} {status}{version}",
                $"{OverviewPage.Format(report.TotalObservedLogicalBytes)} observed · {report.Candidates.Length:N0} findings · {report.Diagnostics.Length:N0} diagnostics",
                action: detailAction, valueBrush: StatusBrush(report.Status));
        }
        Reflow(PageHost.LayoutMode);
    }

    private void RenderDetail()
    {
        var report = selectedToolId is { } id ? ViewModel.Reports.FirstOrDefault(item => item.ToolId == id) : null;
        if (report is null)
        {
            DetailPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var descriptor = DeveloperToolRegistry.Descriptor(report.ToolId);
        DetailPanel.Visibility = Visibility.Visible;
        DetailTitle.Text = $"{descriptor.DisplayName} — {StatusLabel(report.Status)}";
        DetailExplanation.Text = descriptor.Explanation;

        ClearRows(DetailDiagnosticsStack);
        if (report.DetectedVersion is not null)
            AddTechnicalRow(DetailDiagnosticsStack, "Detected version", report.DetectedVersion, monospace: true, copy: true);
        if (report.ExecutableDiscoverySource is not null)
            AddTechnicalRow(DetailDiagnosticsStack, "Discovery source",
                SafeDiagnosticText(report.ExecutableDiscoverySource), monospace: true);
        AddTechnicalRow(DetailDiagnosticsStack, "Observed storage", OverviewPage.Format(report.TotalObservedLogicalBytes));
        AddTechnicalRow(DetailDiagnosticsStack, "Tool-reported storage", Bytes(report.ToolReportedBytes));

        foreach (var group in report.Diagnostics.GroupBy(item => (item.Code, Message: SafeDiagnosticText(item.Message))))
        {
            var severity = DiagnosticSeverity(group.Key.Code);
            AddTechnicalRow(DetailDiagnosticsStack, severity, group.Key.Message,
                $"Code {group.Key.Code} · {group.Count():N0} occurrence(s)",
                valueBrush: severity == "Warning" ? ResourceBrush("Warning") : null);
        }

        DetailStack.Children.Clear();
        foreach (var candidate in report.Candidates)
            DetailStack.Children.Add(CreateCandidateSurface(candidate));
        if (report.Candidates.Length == 0)
        {
            DetailStack.Children.Add(new TextBlock
            {
                Text = "No storage findings are available for this tool in the selected analysis.",
                Style = (Style)Application.Current.Resources["BodyMutedStyle"],
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private Border CreateCandidateSurface(CleanupCandidate candidate)
    {
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = candidate.Title,
            Style = (Style)Application.Current.Resources["CardTitleStyle"],
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = $"{OverviewPage.Format(candidate.Impact.ObservedLogicalBytes)} observed · {Friendly(candidate.Eligibility.ToString())} · {Friendly(candidate.Risk.ToString())} risk · {Friendly(candidate.Confidence.ToString())} confidence",
            Style = (Style)Application.Current.Resources["BodyMutedStyle"],
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = SafeDiagnosticText(candidate.EligibilityReason),
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = "Possible consequence: " + SafeDiagnosticText(candidate.Consequence.PossibleOutcome),
            Style = (Style)Application.Current.Resources["BodyMutedStyle"],
            TextWrapping = TextWrapping.Wrap
        });

        if (candidate.Eligibility == CleanupEligibility.DryRunEligible)
        {
            var reviewButton = new Button
            {
                Content = "Review in plan",
                Style = (Style)Application.Current.Resources["SecondaryActionStyle"],
                HorizontalAlignment = HorizontalAlignment.Left
            };
            AutomationProperties.SetName(reviewButton, candidate.Title + " review in plan");
            var findingId = candidate.FindingId;
            reviewButton.Click += async (_, _) =>
            {
                var plan = await ViewModel.CreatePlanAsync(findingId);
                if (plan is not null) ViewModel.Navigate("Review Plan");
                else DeveloperStatus.Text = "This finding could not be added to a plan. Try detecting developer tools again.";
            };
            content.Children.Add(reviewButton);
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                Text = "No automatic action is available for this finding. Review it manually if needed.",
                Style = (Style)Application.Current.Resources["BodyMutedStyle"],
                TextWrapping = TextWrapping.Wrap
            });
        }

        return new Border
        {
            Style = (Style)Application.Current.Resources["CompactCardStyle"],
            Child = content
        };
    }

    private void AddTechnicalRow(Panel parent, string label, string value, string? explanation = null,
        bool monospace = false, bool copy = false, Button? action = null, Brush? valueBrush = null)
    {
        var row = new Grid { ColumnSpacing = 18, RowSpacing = 6, Padding = new Thickness(0, 10, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var labelText = new TextBlock
        {
            Text = label,
            Style = (Style)Application.Current.Resources["BodyMutedStyle"],
            TextWrapping = TextWrapping.Wrap
        };
        var valueText = new TextBlock
        {
            Text = Recorded(value),
            Style = monospace ? (Style)Application.Current.Resources["MonospaceDetailStyle"] : null,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = monospace
        };
        if (valueBrush is not null) valueText.Foreground = valueBrush;
        ToolTipService.SetToolTip(valueText, Recorded(value));
        AutomationProperties.SetName(valueText, $"{label}: {Recorded(value)}");

        var valueArea = new Grid { ColumnSpacing = 8, RowSpacing = 4 };
        valueArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        valueArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        valueArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        valueArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        valueArea.Children.Add(valueText);
        var rowAction = action ?? (copy ? CreateCopyButton(label, Recorded(value)) : null);
        if (rowAction is not null)
        {
            Grid.SetColumn(rowAction, 1);
            valueArea.Children.Add(rowAction);
        }
        if (!string.IsNullOrWhiteSpace(explanation))
        {
            var explanationText = new TextBlock
            {
                Text = explanation,
                Style = (Style)Application.Current.Resources["BodyMutedStyle"],
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(explanationText, 1);
            Grid.SetColumnSpan(explanationText, 2);
            valueArea.Children.Add(explanationText);
        }

        Grid.SetColumn(valueArea, 1);
        row.Children.Add(labelText);
        row.Children.Add(valueArea);
        technicalRows.Add(row);
        parent.Children.Add(row);
        parent.Children.Add(new Border { Style = (Style)Application.Current.Resources["DividerStyle"] });
    }

    private Button CreateCopyButton(string label, string value)
    {
        var button = new Button
        {
            Content = "Copy",
            Style = (Style)Application.Current.Resources["QuietButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 0
        };
        ToolTipService.SetToolTip(button, $"Copy {label}");
        AutomationProperties.SetName(button, $"Copy {label}");
        button.Click += (_, _) => CopyValue(button, label, value);
        return button;
    }

    private void CopyValue(Button button, string label, string value)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(value);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            var original = button.Content;
            button.Content = "Copied";
            CopyStatus.Text = $"Copied {label}.";
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(1600);
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                button.Content = original;
                CopyStatus.Text = string.Empty;
                copyFeedbackTimers.Remove(timer);
            };
            copyFeedbackTimers.Add(timer);
            timer.Start();
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            CopyStatus.Text = $"{label} could not be copied. Try again.";
        }
    }

    private void CloseDetails(object sender, RoutedEventArgs args)
    {
        selectedToolId = null;
        DetailPanel.Visibility = Visibility.Collapsed;
    }

    private void Reflow(ResponsivePageWidth mode)
    {
        var narrow = mode == ResponsivePageWidth.Narrow;
        Grid.SetColumn(SnapshotPicker, 0);
        Grid.SetRow(SnapshotPicker, 0);
        Grid.SetColumnSpan(SnapshotPicker, narrow ? 2 : 1);
        Grid.SetColumn(DetectButton, narrow ? 0 : 1);
        Grid.SetRow(DetectButton, narrow ? 1 : 0);
        Grid.SetColumnSpan(DetectButton, narrow ? 2 : 1);
        DetectButton.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        SummaryActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
        Grid.SetColumn(SummaryHeading, 0);
        Grid.SetRow(SummaryHeading, 0);
        Grid.SetColumnSpan(SummaryHeading, narrow ? 2 : 1);
        Grid.SetColumn(SummaryActions, narrow ? 0 : 1);
        Grid.SetRow(SummaryActions, narrow ? 1 : 0);
        Grid.SetColumnSpan(SummaryActions, narrow ? 2 : 1);

        var summaryColumns = mode switch
        {
            ResponsivePageWidth.Wide => 4,
            ResponsivePageWidth.Medium => 2,
            _ => 1
        };
        ReflowSummary(summaryColumns);
        foreach (var row in technicalRows) ReflowTechnicalRow(row, narrow);
    }

    private void ReflowSummary(int columns)
    {
        SummaryGrid.ColumnDefinitions.Clear();
        SummaryGrid.RowDefinitions.Clear();
        for (var index = 0; index < columns; index++)
            SummaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rows = Math.Max(1, (SummaryGrid.Children.Count + columns - 1) / columns);
        for (var index = 0; index < rows; index++)
            SummaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < SummaryGrid.Children.Count; index++)
        {
            var child = (FrameworkElement)SummaryGrid.Children[index];
            Grid.SetColumn(child, index % columns);
            Grid.SetRow(child, index / columns);
        }
    }

    private static void ReflowTechnicalRow(Grid row, bool narrow)
    {
        row.ColumnDefinitions[0].Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(190);
        row.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        var label = (FrameworkElement)row.Children[0];
        var value = (FrameworkElement)row.Children[1];
        Grid.SetColumn(label, 0);
        Grid.SetRow(label, 0);
        Grid.SetColumnSpan(label, narrow ? 2 : 1);
        Grid.SetColumn(value, narrow ? 0 : 1);
        Grid.SetRow(value, narrow ? 1 : 0);
        Grid.SetColumnSpan(value, narrow ? 2 : 1);
    }

    private void ClearRows(Panel panel)
    {
        foreach (var row in panel.Children.OfType<Grid>()) technicalRows.Remove(row);
        panel.Children.Clear();
    }

    private static double? AccountedPercentage(StorageSnapshot snapshot)
    {
        if (snapshot.Drive.UsedBytes is not > 0 || snapshot.LogicalBytesObserved < 0 ||
            snapshot.LogicalBytesObserved > snapshot.Drive.UsedBytes.Value) return null;
        return snapshot.LogicalBytesObserved * 100d / snapshot.Drive.UsedBytes.Value;
    }

    private static double? ClassificationPercentage(StorageSnapshot snapshot)
    {
        var basis = snapshot.ClassifiedBytes + snapshot.UnknownBytes;
        return basis > 0 ? snapshot.ClassifiedBytes * 100d / basis : null;
    }

    private static string DiagnosticSummary(StorageSnapshot snapshot) => string.Join(Environment.NewLine,
    [
        $"Scan ID: {snapshot.ScanId:D}",
        $"Captured UTC: {snapshot.CapturedAtUtc.ToUniversalTime():O}",
        $"Analysis type: {ModeLabel(snapshot.Mode)}",
        $"Completion status: {StateLabel(snapshot.State)}",
        $"Drive: {snapshot.Drive.Root}",
        $"File system: {Recorded(snapshot.Drive.FileSystem)}",
        $"Observed storage: {OverviewPage.Format(snapshot.LogicalBytesObserved)}",
        $"Accounted storage: {Percentage(AccountedPercentage(snapshot))}",
        $"Files examined: {Number(snapshot.Coverage.FilesObserved)}",
        $"Directories examined: {Number(snapshot.Coverage.DirectoriesObserved)}",
        $"Warnings: {Number(snapshot.Warnings.Count)}",
        $"Snapshot schema: {snapshot.SchemaVersion:N0}",
        $"Rule pack: {Recorded(snapshot.RulePackId)} {Recorded(snapshot.RulePackVersion)}"
    ]);

    private static string SafeDiagnosticText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Not recorded";
        var firstLine = value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "Not recorded";
        return firstLine.Length <= 280 ? firstLine : firstLine[..277] + "…";
    }

    private static string StateLabel(SnapshotState state) => state switch
    {
        SnapshotState.Complete => "Completed",
        SnapshotState.Partial => "Completed with warnings",
        SnapshotState.Cancelled => "Cancelled",
        SnapshotState.Failed => "Failed",
        SnapshotState.Pending or SnapshotState.Writing => "Incomplete",
        SnapshotState.Incompatible => "Legacy format",
        SnapshotState.Corrupted => "Unavailable",
        _ => "Not recorded"
    };

    private static string ModeLabel(ScanMode mode) => mode == ScanMode.Deep ? "Deep" : "Quick";

    private static string StatusLabel(DeveloperToolStatus status) => status switch
    {
        DeveloperToolStatus.FullyDetected => "Detected",
        DeveloperToolStatus.PartiallyDetected => "Partially detected",
        DeveloperToolStatus.InstalledNoData => "Installed · no storage data",
        DeveloperToolStatus.NotInstalled => "Not found",
        DeveloperToolStatus.Unavailable => "No evidence yet",
        DeveloperToolStatus.PermissionLimited => "Permission limited",
        DeveloperToolStatus.UnsupportedVersion => "Unsupported version",
        DeveloperToolStatus.ProbeFailed => "Status check failed",
        _ => "Not recorded"
    };

    private static string StatusIcon(DeveloperToolStatus status) => status switch
    {
        DeveloperToolStatus.FullyDetected => "✓",
        DeveloperToolStatus.PartiallyDetected or DeveloperToolStatus.PermissionLimited => "⚠",
        DeveloperToolStatus.UnsupportedVersion or DeveloperToolStatus.ProbeFailed => "!",
        _ => "ℹ"
    };

    private static Brush StatusBrush(DeveloperToolStatus status) => ResourceBrush(status switch
    {
        DeveloperToolStatus.FullyDetected => "Success",
        DeveloperToolStatus.PartiallyDetected or DeveloperToolStatus.PermissionLimited => "Warning",
        DeveloperToolStatus.UnsupportedVersion or DeveloperToolStatus.ProbeFailed => "Error",
        _ => "TextMuted"
    });

    private static string DiagnosticSeverity(string code) =>
        code.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
        code.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
        code.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
        code.Contains("truncated", StringComparison.OrdinalIgnoreCase)
            ? "Warning"
            : "Information";

    private static string FriendlyRule(string ruleId)
    {
        var segment = ruleId.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? ruleId;
        return Friendly(segment);
    }

    private static string Friendly(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Not recorded";
        var result = new System.Text.StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(value[index - 1])) result.Append(' ');
            result.Append(character is '_' or '-' ? ' ' : character);
        }
        var text = result.ToString().Trim();
        return text.Length == 0 ? "Not recorded" : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
    }

    private static string Bytes(long? value) => value.HasValue ? OverviewPage.Format(value.Value) : "Not recorded";
    private static string Number(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
    private static string Percentage(double? value) => value.HasValue
        ? value.Value.ToString("F1", CultureInfo.CurrentCulture) + "%"
        : "Not recorded";
    private static string Recorded(string? value) => string.IsNullOrWhiteSpace(value) ? "Not recorded" : value;
    private static Brush ResourceBrush(string key) => (Brush)Application.Current.Resources[key];
}
