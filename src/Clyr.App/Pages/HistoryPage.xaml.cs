using System.Globalization;
using Clyr.App.ViewModels;
using Clyr.Contracts;
using Clyr.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Clyr.App.Pages;

public sealed partial class HistoryPage : Page
{
    private readonly Dictionary<Guid, CheckBox> comparisonSelections = [];
    private readonly List<Grid> recordHeadingGrids = [];
    private readonly List<Grid> recordMetricGrids = [];
    private readonly HashSet<Guid> selectedComparisonIds = [];
    private bool renderingRecords;

    public HistoryPage(HistoryViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PageHost.LayoutModeChanged += (_, mode) => Reflow(mode);
    }

    public HistoryViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();

    public async Task ActivateAsync()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        HistoryEmpty.Visibility = Visibility.Collapsed;
        HistoryError.Visibility = Visibility.Collapsed;
        HistoryContent.Visibility = Visibility.Collapsed;
        HistoryStatus.Text = string.Empty;
        try
        {
            await ViewModel.LoadAsync();
            Render();
        }
        catch (Exception exception) when (exception is IOException or Clyr.Persistence.SnapshotStoreException)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            HistoryError.Visibility = Visibility.Visible;
            HistoryStatus.Text = "History could not be loaded safely. Your saved analyses were not changed.";
        }
    }

    private void Render()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        HistoryError.Visibility = Visibility.Collapsed;
        var hasItems = ViewModel.Items.Count > 0;
        HistoryEmpty.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        HistoryContent.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        selectedComparisonIds.RemoveWhere(id => ViewModel.Items.All(item => item.Id != id));
        ComparisonPanel.Visibility = Visibility.Collapsed;
        if (!hasItems) return;
        RenderSummary();
        RenderRecords();
        UpdateComparisonSelection();
    }

    private void RenderSummary()
    {
        var quick = ViewModel.Items.Count(item => item.Mode == ScanMode.Quick);
        var deep = ViewModel.Items.Count(item => item.Mode == ScanMode.Deep);
        var completed = ViewModel.Items.Count(item => item.State is SnapshotState.Complete or SnapshotState.Partial);
        var warnings = ViewModel.Items.Count(HasWarnings);
        TotalAnalysisCount.Text = ViewModel.Items.Count.ToString("N0", CultureInfo.CurrentCulture);
        MostRecentDate.Text = "Most recent: " + ViewModel.Items.Max(item => item.CapturedAtUtc).LocalDateTime.ToString("f", CultureInfo.CurrentCulture);
        ModeCounts.Text = $"Quick {quick:N0} · Deep {deep:N0}";
        OutcomeCounts.Text = $"Completed {completed:N0}";
        WarningSummary.Text = warnings == 0 ? "No analyses with warnings" : $"{warnings:N0} with warnings";
    }

    private void RenderRecords()
    {
        if (HistoryStack is null) return;
        renderingRecords = true;
        HistoryStack.Children.Clear();
        comparisonSelections.Clear();
        recordHeadingGrids.Clear();
        recordMetricGrids.Clear();
        var visible = FilteredItems().ToArray();
        var useGroups = visible.Length > 3;
        string? previousGroup = null;
        foreach (var item in visible)
        {
            if (useGroups)
            {
                var group = DateGroup(item.CapturedAtUtc.LocalDateTime);
                if (!string.Equals(group, previousGroup, StringComparison.Ordinal))
                {
                    HistoryStack.Children.Add(new TextBlock
                    {
                        Text = group,
                        Style = (Style)Application.Current.Resources["SectionTitleStyle"],
                        Margin = new Thickness(0, previousGroup is null ? 0 : 8, 0, 0)
                    });
                    previousGroup = group;
                }
            }
            HistoryStack.Children.Add(CreateRecordRow(item));
        }
        FilterResultText.Text = visible.Length == ViewModel.Items.Count
            ? $"Showing all {visible.Length:N0} saved analyses"
            : $"Showing {visible.Length:N0} of {ViewModel.Items.Count:N0} saved analyses";
        renderingRecords = false;
        Reflow(PageHost.LayoutMode);
    }

    private IEnumerable<SnapshotSummary> FilteredItems()
    {
        IEnumerable<SnapshotSummary> result = ViewModel.Items;
        result = ModeFilter?.SelectedIndex switch
        {
            1 => result.Where(item => item.Mode == ScanMode.Quick),
            2 => result.Where(item => item.Mode == ScanMode.Deep),
            _ => result
        };
        result = StatusFilter?.SelectedIndex switch
        {
            1 => result.Where(item => item.State == SnapshotState.Complete),
            2 => result.Where(item => item.State == SnapshotState.Partial),
            3 => result.Where(item => item.State == SnapshotState.Cancelled),
            4 => result.Where(item => item.State is SnapshotState.Failed or SnapshotState.Incompatible or SnapshotState.Corrupted or SnapshotState.Pending or SnapshotState.Writing),
            _ => result
        };
        return SortOrder?.SelectedIndex switch
        {
            1 => result.OrderBy(item => item.CapturedAtUtc).ThenBy(item => item.Root, StringComparer.CurrentCultureIgnoreCase),
            2 => result.OrderByDescending(item => AccountedPercentage(item) ?? double.MinValue).ThenByDescending(item => item.CapturedAtUtc),
            _ => result.OrderByDescending(item => item.CapturedAtUtc).ThenBy(item => item.Root, StringComparer.CurrentCultureIgnoreCase)
        };
    }

    private void FilterChanged(object sender, SelectionChangedEventArgs args)
    {
        if (HistoryStack is not null && ViewModel.Items.Count > 0) RenderRecords();
    }

    private Border CreateRecordRow(SnapshotSummary item)
    {
        var detail = ViewModel.Detail(item.Id);
        var comparable = detail is not null && item.State is SnapshotState.Complete or SnapshotState.Partial or SnapshotState.Cancelled;
        var selected = selectedComparisonIds.Contains(item.Id);
        var compareChoice = new CheckBox
        {
            Content = "Select for comparison",
            IsChecked = selected,
            IsEnabled = comparable,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        AutomationProperties.SetName(compareChoice,
            $"Select {ModeLabel(item.Mode)} analysis from {FullDate(item.CapturedAtUtc)} for comparison");
        if (!comparable)
            AutomationProperties.SetHelpText(compareChoice, "This record does not contain enough compatible aggregate data for comparison.");
        comparisonSelections[item.Id] = compareChoice;
        compareChoice.Checked += (_, _) => ComparisonSelectionChanged(item.Id, true);
        compareChoice.Unchecked += (_, _) => ComparisonSelectionChanged(item.Id, false);

        var title = new TextBlock
        {
            Text = $"{ModeIcon(item.Mode)}  {ModeLabel(item.Mode)} analysis",
            Style = (Style)Application.Current.Resources["CardTitleStyle"],
            TextWrapping = TextWrapping.Wrap
        };
        var date = new TextBlock
        {
            Text = item.CapturedAtUtc.LocalDateTime.ToString("f", CultureInfo.CurrentCulture),
            Style = (Style)Application.Current.Resources["BodyMutedStyle"],
            TextWrapping = TextWrapping.Wrap
        };
        AutomationProperties.SetName(date, "Completed " + FullDate(item.CapturedAtUtc));
        var identity = new StackPanel { Spacing = 3 };
        identity.Children.Add(title);
        identity.Children.Add(date);
        identity.Children.Add(new TextBlock
        {
            Text = $"Drive {item.Root} · {item.FileSystem}",
            Style = (Style)Application.Current.Resources["BodyMutedStyle"],
            TextWrapping = TextWrapping.Wrap
        });

        var statusText = new TextBlock
        {
            Text = $"{StatusIcon(item.State)}  {StatusLabel(item.State)}",
            Foreground = StatusBrush(item.State),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        AutomationProperties.SetName(statusText, "Analysis status: " + StatusLabel(item.State));
        var status = new Border
        {
            Style = (Style)Application.Current.Resources["StatusBadgeStyle"],
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = statusText
        };
        var state = new StackPanel { Spacing = 7, HorizontalAlignment = HorizontalAlignment.Left };
        state.Children.Add(status);
        state.Children.Add(compareChoice);

        var heading = new Grid { ColumnSpacing = 16, RowSpacing = 8 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(state, 1);
        heading.Children.Add(identity);
        heading.Children.Add(state);
        recordHeadingGrids.Add(heading);

        var metrics = CreateMetricGrid(item, detail);
        recordMetricGrids.Add(metrics);
        var open = new Button
        {
            Content = "Open result",
            IsEnabled = ViewModel.CanOpenResult(item.Id),
            Style = (Style)Application.Current.Resources["PrimaryActionStyle"],
            HorizontalAlignment = HorizontalAlignment.Left
        };
        AutomationProperties.SetName(open, $"Open result for {ModeLabel(item.Mode)} analysis from {FullDate(item.CapturedAtUtc)}");
        if (!open.IsEnabled)
            AutomationProperties.SetHelpText(open,
                "This saved record contains privacy-safe aggregates only. Open result is available while the matching full result remains in the current session.");
        open.Click += (_, _) => OpenResult(item.Id);

        var details = CreateDetails(item, detail);
        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(heading);
        content.Children.Add(new Border { Style = (Style)Application.Current.Resources["DividerStyle"] });
        content.Children.Add(metrics);
        content.Children.Add(open);
        content.Children.Add(details);
        var row = new Border
        {
            BorderBrush = ResourceBrush("BorderSubtle"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(4, 4, 4, 18),
            Child = content
        };
        AutomationProperties.SetName(row,
            $"{ModeLabel(item.Mode)} analysis, {FullDate(item.CapturedAtUtc)}, {StatusLabel(item.State)}, {WarningCountText(detail)}");
        return row;
    }

    private static Grid CreateMetricGrid(SnapshotSummary item, StorageSnapshot? detail)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 12 };
        for (var index = 0; index < 4; index++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var metrics = new[]
        {
            ("Accounted", Percentage(AccountedPercentage(item))),
            ("Observed storage", OverviewPage.Format(item.LogicalBytesObserved)),
            ("Unobserved storage", BytesOrNotRecorded(detail?.UnaccountedBytes)),
            ("Warnings", detail is null ? "Not recorded" : detail.Warnings.Count.ToString("N0", CultureInfo.CurrentCulture)),
            ("Files examined", detail is null ? "Not recorded" : detail.Coverage.FilesObserved.ToString("N0", CultureInfo.CurrentCulture)),
            ("Directories examined", detail is null ? "Not recorded" : detail.Coverage.DirectoriesObserved.ToString("N0", CultureInfo.CurrentCulture)),
            ("Duration", "Not recorded")
        };
        for (var index = 0; index < metrics.Length; index++)
        {
            var metric = new StackPanel { Spacing = 2 };
            metric.Children.Add(new TextBlock
            {
                Text = metrics[index].Item1,
                Style = (Style)Application.Current.Resources["BodyMutedStyle"],
                TextWrapping = TextWrapping.Wrap
            });
            metric.Children.Add(new TextBlock { Text = metrics[index].Item2, TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(metric, index % 4);
            Grid.SetRow(metric, index / 4);
            grid.Children.Add(metric);
        }
        return grid;
    }

    private Expander CreateDetails(SnapshotSummary item, StorageSnapshot? detail)
    {
        var content = new StackPanel { Spacing = 7, Padding = new Thickness(0, 8, 0, 0) };
        if (detail is null)
        {
            content.Children.Add(new TextBlock
            {
                Text = "Some details were not recorded for this older analysis.",
                Style = (Style)Application.Current.Resources["BodyMutedStyle"],
                TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            var accounted = AccountedPercentage(item);
            var classifiedBasis = detail.ClassifiedBytes + detail.UnknownBytes;
            double? classified = classifiedBasis > 0 ? detail.ClassifiedBytes * 100d / classifiedBasis : null;
            AddDetail(content, "Scan quality", QualityLabel(ScanAccounting.QualityFor(accounted)));
            AddDetail(content, "Classified observed storage", Percentage(classified));
            AddDetail(content, "Inaccessible entries", detail.Coverage.InaccessibleEntries.ToString("N0", CultureInfo.CurrentCulture));
            AddDetail(content, "Warning count", detail.Warnings.Count.ToString("N0", CultureInfo.CurrentCulture));
            AddDetail(content, "Termination", TerminationLabel(item.State));
            AddDetail(content, "Continuation state", "Not recorded");
            AddDetail(content, "Result availability", ViewModel.CanOpenResult(item.Id)
                ? "Available in this session"
                : "Aggregate history only; the full result is not in this session");
        }
        return new Expander
        {
            Header = "Analysis details",
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private static void AddDetail(Panel panel, string label, string value)
    {
        panel.Children.Add(new TextBlock
        {
            Text = $"{label}: {value}",
            TextWrapping = TextWrapping.Wrap
        });
    }

    private void ComparisonSelectionChanged(Guid id, bool selected)
    {
        if (renderingRecords) return;
        if (selected)
        {
            if (selectedComparisonIds.Count >= 2 && !selectedComparisonIds.Contains(id))
            {
                renderingRecords = true;
                comparisonSelections[id].IsChecked = false;
                renderingRecords = false;
                HistoryStatus.Text = "Select only two analyses for comparison.";
                return;
            }
            selectedComparisonIds.Add(id);
        }
        else
        {
            selectedComparisonIds.Remove(id);
        }
        UpdateComparisonSelection();
    }

    private void UpdateComparisonSelection()
    {
        CompareButton.IsEnabled = selectedComparisonIds.Count == 2;
        ClearCompareButton.IsEnabled = selectedComparisonIds.Count > 0;
        ComparisonSelectionStatus.Text = selectedComparisonIds.Count switch
        {
            0 => "Select two analyses to compare their aggregate measurements.",
            1 => "One analysis selected. Select one more analysis.",
            _ => "Two analyses selected. Ready to compare aggregate measurements."
        };
        AutomationProperties.SetName(ComparisonSelectionStatus,
            $"{selectedComparisonIds.Count:N0} analyses selected for comparison. {ComparisonSelectionStatus.Text}");
    }

    private void ClearComparisonSelection(object sender, RoutedEventArgs args)
    {
        selectedComparisonIds.Clear();
        renderingRecords = true;
        foreach (var choice in comparisonSelections.Values) choice.IsChecked = false;
        renderingRecords = false;
        UpdateComparisonSelection();
        ComparisonPanel.Visibility = Visibility.Collapsed;
    }

    private async void CompareSelected(object sender, RoutedEventArgs args)
    {
        if (selectedComparisonIds.Count != 2) return;
        var selected = selectedComparisonIds.ToArray();
        var report = await ViewModel.CompareAsync(selected[0], selected[1]);
        if (report is null)
        {
            HistoryStatus.Text = "Comparison is unavailable because one of the selected records could not be loaded.";
            return;
        }
        ComparisonPanel.Visibility = Visibility.Visible;
        ComparisonPeriod.Text = $"{report.BeforeUtc.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)} to {report.AfterUtc.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)}";
        ComparisonConfidence.Text = $"{ComparisonLabel(report.Compatibility.Kind)} · {report.Compatibility.Confidence} confidence";
        ComparisonDeltaStack.Children.Clear();
        var changes = report.Metrics.Concat(report.Categories)
            .Where(delta => delta.Kind != DeltaKind.Unchanged)
            .OrderByDescending(delta => Absolute(delta.Change ?? 0))
            .Take(20)
            .ToArray();
        if (changes.Length == 0)
        {
            ComparisonDeltaStack.Children.Add(new TextBlock
            {
                Text = "No recorded aggregate difference is available for these analyses.",
                Style = (Style)Application.Current.Resources["BodyMutedStyle"],
                TextWrapping = TextWrapping.Wrap
            });
        }
        foreach (var delta in changes)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = MetricLabel(delta.Metric), TextWrapping = TextWrapping.Wrap });
            var change = new TextBlock
            {
                Text = $"{Signed(delta.Change)} · {DeltaLabel(delta.Kind)}",
                Foreground = DeltaBrush(delta.Kind),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(change, 1);
            row.Children.Add(change);
            ComparisonDeltaStack.Children.Add(row);
            ComparisonDeltaStack.Children.Add(new Border { Style = (Style)Application.Current.Resources["DividerStyle"] });
        }
        ComparisonWarnings.Text = report.Compatibility.Warnings.Count == 0
            ? "No additional comparison warnings."
            : string.Join(" ", report.Compatibility.Warnings);
        HistoryStatus.Text = "Comparison ready. Differences are observations, not proof of cause.";
    }

    private void CloseComparison(object sender, RoutedEventArgs args) => ComparisonPanel.Visibility = Visibility.Collapsed;

    private void OpenResult(Guid id)
    {
        if (ViewModel.OpenResult(id)) return;
        HistoryStatus.Text = "This saved record contains aggregate history only. Its full result is not available in the current session.";
    }

    private async void RefreshHistory(object sender, RoutedEventArgs args) => await ActivateAsync();
    private void RunAnalysis(object sender, RoutedEventArgs args) => ViewModel.Navigate("Scan");
    private void OpenSettings(object sender, RoutedEventArgs args) => ViewModel.Navigate("Settings");

    private void Reflow(Controls.ResponsivePageWidth mode)
    {
        var narrow = mode == Controls.ResponsivePageWidth.Narrow;
        CompareActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
        ReflowGrid(SummaryGrid, narrow, 1);
        ReflowGrid(ListHeadingGrid, narrow, 1);
        for (var index = 0; index < FilterBar.Children.Count; index++)
        {
            var child = (FrameworkElement)FilterBar.Children[index];
            Grid.SetColumn(child, narrow ? 0 : index);
            Grid.SetRow(child, narrow ? index : 0);
            Grid.SetColumnSpan(child, narrow ? 4 : 1);
        }
        SetRowCount(FilterBar, narrow ? FilterBar.Children.Count : 1);
        foreach (var heading in recordHeadingGrids) ReflowGrid(heading, narrow, 1);
        foreach (var metrics in recordMetricGrids) ReflowGrid(metrics, narrow, 2);
    }

    private static void ReflowGrid(Grid grid, bool narrow, int narrowColumns)
    {
        for (var index = 0; index < grid.ColumnDefinitions.Count; index++)
            grid.ColumnDefinitions[index].Width = !narrow || index < narrowColumns
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
        for (var index = 0; index < grid.Children.Count; index++)
        {
            var child = (FrameworkElement)grid.Children[index];
            var columnCount = narrow ? narrowColumns : grid.ColumnDefinitions.Count;
            Grid.SetColumn(child, index % columnCount);
            Grid.SetRow(child, index / columnCount);
            Grid.SetColumnSpan(child, narrow && narrowColumns == 1 ? grid.ColumnDefinitions.Count : 1);
        }
        var activeColumnCount = narrow ? narrowColumns : grid.ColumnDefinitions.Count;
        SetRowCount(grid, Math.Max(1, (grid.Children.Count + activeColumnCount - 1) / activeColumnCount));
    }

    private static void SetRowCount(Grid grid, int count)
    {
        grid.RowDefinitions.Clear();
        for (var index = 0; index < count; index++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    }

    private bool HasWarnings(SnapshotSummary item) => item.State == SnapshotState.Partial ||
        ViewModel.Detail(item.Id)?.Warnings.Count > 0;

    private static double? AccountedPercentage(SnapshotSummary item)
    {
        if (item.UsedBytes is not > 0 || item.LogicalBytesObserved < 0 || item.LogicalBytesObserved > item.UsedBytes.Value)
            return null;
        return item.LogicalBytesObserved * 100d / item.UsedBytes.Value;
    }

    private static string DateGroup(DateTime captured)
    {
        var date = captured.Date;
        var today = DateTime.Today;
        if (date == today) return "Today";
        if (date == today.AddDays(-1)) return "Yesterday";
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return date >= monday ? "Earlier this week" : "Older";
    }

    private static string FullDate(DateTimeOffset value) =>
        value.LocalDateTime.ToString("F", CultureInfo.CurrentCulture);

    private static string ModeLabel(ScanMode mode) => mode == ScanMode.Quick ? "Quick" : "Deep";
    private static string ModeIcon(ScanMode mode) => mode == ScanMode.Quick ? "⚡" : "⌕";

    private static string StatusLabel(SnapshotState state) => state switch
    {
        SnapshotState.Complete => "Completed",
        SnapshotState.Partial => "Completed with warnings",
        SnapshotState.Cancelled => "Cancelled",
        SnapshotState.Failed => "Failed",
        SnapshotState.Pending or SnapshotState.Writing => "Incomplete record",
        SnapshotState.Incompatible or SnapshotState.Corrupted => "Unavailable",
        _ => "Unavailable"
    };

    private static string StatusIcon(SnapshotState state) => state switch
    {
        SnapshotState.Complete => "✓",
        SnapshotState.Partial => "!",
        SnapshotState.Cancelled => "■",
        SnapshotState.Failed or SnapshotState.Corrupted => "×",
        _ => "—"
    };

    private static Brush StatusBrush(SnapshotState state) => ResourceBrush(state switch
    {
        SnapshotState.Complete => "Success",
        SnapshotState.Partial or SnapshotState.Cancelled => "Warning",
        SnapshotState.Failed or SnapshotState.Corrupted => "Error",
        _ => "MutedTextBrush"
    });

    private static string WarningCountText(StorageSnapshot? detail) => detail is null
        ? "warning count not recorded"
        : $"{detail.Warnings.Count:N0} warnings";

    private static string Percentage(double? value) => value.HasValue
        ? value.Value.ToString("F1", CultureInfo.CurrentCulture) + "%"
        : "Not recorded";

    private static string BytesOrNotRecorded(long? value) => value.HasValue
        ? OverviewPage.Format(value.Value)
        : "Not recorded";

    private static string QualityLabel(ScanQuality quality) => quality switch
    {
        ScanQuality.Excellent => "Excellent coverage",
        ScanQuality.Good => "Good coverage",
        ScanQuality.Partial => "Partial coverage",
        _ => "Insufficient coverage"
    };

    private static string TerminationLabel(SnapshotState state) => state switch
    {
        SnapshotState.Complete => "Analysis completed",
        SnapshotState.Partial => "Analysis completed with warnings",
        SnapshotState.Cancelled => "Analysis was cancelled",
        SnapshotState.Failed => "Analysis failed",
        _ => "Not recorded"
    };

    private static string ComparisonLabel(SnapshotCompatibility compatibility) => compatibility switch
    {
        SnapshotCompatibility.FullyComparable => "Comparable",
        SnapshotCompatibility.ComparableWithWarnings => "Comparable with warnings",
        SnapshotCompatibility.ClassificationAdjusted => "Classification changed",
        _ => "Not comparable"
    };

    private static string MetricLabel(string metric)
    {
        return metric switch
        {
            "drive.used" => "Drive used",
            "drive.free" => "Drive free",
            "observed" => "Observed storage",
            "classified" => "Classified storage",
            "unknown" => "Unclassified storage",
            "unaccounted" => "Unobserved storage",
            "coverage.files" => "Files examined",
            "coverage.skipped" => "Skipped entries",
            _ when metric.StartsWith("category.", StringComparison.Ordinal) => Friendly(metric["category.".Length..]),
            _ => Friendly(metric.Replace('.', ' '))
        };
    }

    private static string Friendly(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            if (index > 0 && char.IsUpper(value[index]) && !char.IsUpper(value[index - 1])) builder.Append(' ');
            builder.Append(value[index]);
        }
        return builder.ToString();
    }

    private static string Signed(long? value) => value switch
    {
        null => "Not recorded",
        > 0 => "+" + OverviewPage.Format(value.Value),
        _ => OverviewPage.FormatSigned(value)
    };

    private static string DeltaLabel(DeltaKind kind) => kind switch
    {
        DeltaKind.Increased => "Increased",
        DeltaKind.Decreased => "Decreased",
        DeltaKind.New => "Newly observed",
        DeltaKind.NoLongerPresent => "No longer observed",
        DeltaKind.Uncertain => "Uncertain",
        DeltaKind.Incomparable => "Not comparable",
        _ => "Unchanged"
    };

    private static Brush DeltaBrush(DeltaKind kind) => ResourceBrush(
        kind is DeltaKind.Uncertain or DeltaKind.Incomparable ? "Warning" : "Information");

    private static long Absolute(long value) => value == long.MinValue ? long.MaxValue : Math.Abs(value);
    private static Brush ResourceBrush(string key) => (Brush)Application.Current.Resources[key];
}
