using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Clyr.App.ViewModels;
using Clyr.Contracts;
using Clyr.Core.Execution;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Clyr.App.Pages;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The CancellationTokenSource is disposed within a single execution call; WinUI owns the Page lifecycle.")]
public sealed partial class ReviewPlanPage : Page
{
    private readonly Dictionary<string, Border> candidateRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> executionSelections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> selections = new(StringComparer.Ordinal);
    private readonly HashSet<string> selectedFindingIds = new(StringComparer.Ordinal);
    private readonly List<StackPanel> receiptRows = [];
    private IReadOnlyList<CleanupCandidate> candidates = [];
    private CancellationTokenSource? executionCancellation;
    private bool renderingCandidates;

    public ReviewPlanPage(ReviewPlanViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PageHost.LayoutModeChanged += (_, mode) => Reflow(mode);
        viewModel.Session.StateChanged += (_, _) => Refresh();
        Refresh();
    }

    public ReviewPlanViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();

    public void Refresh()
    {
        ViewModel.AdoptPending();
        candidates = ViewModel.Candidates;
        selections.Clear();
        selectedFindingIds.Clear();
        EmptyPanel.Visibility = candidates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReviewStage.Visibility = candidates.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PlanPanel.Visibility = Visibility.Collapsed;
        ExecutionPanel.Visibility = Visibility.Collapsed;
        RenderSummary();
        RenderCandidates();
        UpdateSelectionSummary();
        if (ViewModel.CurrentPlan is not null)
        {
            EmptyPanel.Visibility = Visibility.Collapsed;
            ShowPlan(ViewModel.CurrentPlan);
        }
        RefreshReceiptHistory();
    }

    private void RenderSummary()
    {
        var recommended = candidates.Count(IsEligible);
        var review = candidates.Count(candidate => candidate.Eligibility is CleanupEligibility.ManualReviewOnly or CleanupEligibility.InsufficientEvidence);
        var protectedCount = candidates.Count(candidate => candidate.Eligibility == CleanupEligibility.Protected);
        var unsupported = candidates.Count(candidate => candidate.Eligibility is CleanupEligibility.Unsupported or CleanupEligibility.NotEligible);
        TotalPotentialText.Text = OverviewPage.Format(SumBytes(candidates.Select(candidate => candidate.Impact.ObservedLogicalBytes)));
        CandidateCountText.Text = candidates.Count.ToString("N0", CultureInfo.CurrentCulture);
        SafetyCountsText.Text = $"Recommended {recommended:N0} · Review {review:N0} · Protected {protectedCount:N0} · Unsupported {unsupported:N0}";
        SelectAllButton.IsEnabled = recommended > 0;
    }

    private void RenderCandidates()
    {
        if (CandidateStack is null) return;
        renderingCandidates = true;
        CandidateStack.Children.Clear();
        candidateRows.Clear();
        selections.Clear();
        var visible = FilteredCandidates().ToArray();
        foreach (var candidate in visible)
        {
            var eligible = IsEligible(candidate);
            var choice = new CheckBox
            {
                Content = candidate.Title,
                IsChecked = eligible && selectedFindingIds.Contains(candidate.FindingId),
                IsEnabled = eligible,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(choice,
                $"{candidate.Title}, {SafetyLabel(candidate)}, {OverviewPage.Format(candidate.Impact.ObservedLogicalBytes)} estimated potential, selection");
            if (!eligible) AutomationProperties.SetHelpText(choice, candidate.EligibilityReason);
            if (eligible) selections[candidate.FindingId] = choice;

            var source = CandidateSource(candidate);
            var sourceText = new TextBlock
            {
                Text = source,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = ResourceBrush("TextMuted")
            };
            ToolTipService.SetToolTip(sourceText, source);
            AutomationProperties.SetName(sourceText, "Source: " + source);
            var safetyText = new TextBlock
            {
                Text = $"{SafetyIcon(candidate)}  {SafetyLabel(candidate)} · {Friendly(candidate.Category)} · {OverviewPage.Format(candidate.Impact.ObservedLogicalBytes)} estimated potential · {candidate.Confidence} evidence",
                TextWrapping = TextWrapping.Wrap,
                Foreground = SafetyBrush(candidate)
            };
            var reason = new TextBlock
            {
                Text = candidate.EligibilityReason,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ResourceBrush("TextMuted")
            };
            var details = new Button
            {
                Content = "View details",
                HorizontalAlignment = HorizontalAlignment.Left,
                Style = (Style)Application.Current.Resources["QuietButtonStyle"]
            };
            AutomationProperties.SetName(details, $"View details for {candidate.Title}");
            details.Click += (_, _) => ShowCandidateDetails(candidate);
            var content = new StackPanel { Spacing = 7 };
            content.Children.Add(choice);
            content.Children.Add(safetyText);
            content.Children.Add(sourceText);
            content.Children.Add(reason);
            content.Children.Add(details);
            var row = new Border { Style = (Style)Application.Current.Resources["CompactCardStyle"], Child = content };
            candidateRows[candidate.FindingId] = row;
            choice.Checked += (_, _) => CandidateSelectionChanged(candidate.FindingId);
            choice.Unchecked += (_, _) => CandidateSelectionChanged(candidate.FindingId);
            CandidateStack.Children.Add(row);
            UpdateCandidateSelectionVisual(candidate.FindingId);
        }
        FilterResultText.Text = visible.Length == candidates.Count
            ? $"Showing all {visible.Length:N0} candidates"
            : $"Showing {visible.Length:N0} of {candidates.Count:N0} candidates";
        renderingCandidates = false;
    }

    private IEnumerable<CleanupCandidate> FilteredCandidates()
    {
        IEnumerable<CleanupCandidate> result = candidates;
        result = StatusFilter?.SelectedIndex switch
        {
            1 => result.Where(IsEligible),
            2 => result.Where(candidate => candidate.Eligibility is CleanupEligibility.ManualReviewOnly or CleanupEligibility.InsufficientEvidence),
            3 => result.Where(candidate => candidate.Eligibility == CleanupEligibility.Protected),
            4 => result.Where(candidate => candidate.Eligibility is CleanupEligibility.Unsupported or CleanupEligibility.NotEligible),
            5 => result.Where(candidate => selectedFindingIds.Contains(candidate.FindingId)),
            _ => result
        };
        return SortOrder?.SelectedIndex switch
        {
            1 => result.OrderBy(candidate => SafetyOrder(candidate.Eligibility)).ThenBy(candidate => candidate.Title, StringComparer.CurrentCultureIgnoreCase),
            2 => result.OrderBy(candidate => candidate.Category).ThenByDescending(candidate => candidate.Impact.ObservedLogicalBytes),
            3 => result.OrderBy(candidate => candidate.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => result.OrderByDescending(candidate => candidate.Impact.ObservedLogicalBytes).ThenBy(candidate => candidate.Title, StringComparer.CurrentCultureIgnoreCase)
        };
    }

    private void FilterChanged(object sender, SelectionChangedEventArgs args)
    {
        if (CandidateStack is not null) RenderCandidates();
    }

    private void SelectAllEligible(object sender, RoutedEventArgs args)
    {
        selectedFindingIds.Clear();
        foreach (var candidate in candidates.Where(IsEligible)) selectedFindingIds.Add(candidate.FindingId);
        foreach (var choice in selections.Values) choice.IsChecked = true;
        UpdateSelectionSummary();
    }

    private void ClearSelection(object sender, RoutedEventArgs args)
    {
        selectedFindingIds.Clear();
        foreach (var choice in selections.Values) choice.IsChecked = false;
        UpdateSelectionSummary();
        if (StatusFilter.SelectedIndex == 5) RenderCandidates();
    }

    private void CandidateSelectionChanged(string findingId)
    {
        if (renderingCandidates) return;
        if (selections.TryGetValue(findingId, out var choice) && choice.IsChecked == true) selectedFindingIds.Add(findingId);
        else selectedFindingIds.Remove(findingId);
        UpdateCandidateSelectionVisual(findingId);
        UpdateSelectionSummary();
    }

    private void UpdateCandidateSelectionVisual(string findingId)
    {
        if (!candidateRows.TryGetValue(findingId, out var row)) return;
        var selected = selectedFindingIds.Contains(findingId);
        row.Background = ResourceBrush(selected ? "SurfaceSelected" : "SurfacePrimary");
        row.BorderBrush = ResourceBrush(selected ? "AccentPrimary" : "BorderSubtle");
        row.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
        AutomationProperties.SetHelpText(row, selected ? "Selected for dry-run review." : "Not selected.");
    }

    private void UpdateSelectionSummary()
    {
        var selected = SelectedCandidates().ToArray();
        var bytes = SumBytes(selected.Select(candidate => candidate.Impact.ObservedLogicalBytes));
        var protectedCount = candidates.Count(candidate => candidate.Eligibility == CleanupEligibility.Protected);
        var unsupported = candidates.Count(candidate => candidate.Eligibility is CleanupEligibility.Unsupported or CleanupEligibility.NotEligible);
        SelectedCountText.Text = selected.Length.ToString("N0", CultureInfo.CurrentCulture);
        SelectedPotentialText.Text = OverviewPage.Format(bytes);
        SelectionCountSummary.Text = selected.Length.ToString("N0", CultureInfo.CurrentCulture);
        SelectionPotentialSummary.Text = OverviewPage.Format(bytes);
        ExcludedCountSummary.Text = $"Protected {protectedCount:N0} · Unsupported {unsupported:N0}";
        ReviewSelectedButton.IsEnabled = selected.Length > 0;
        FilterClearButton.IsEnabled = selected.Length > 0;
        SelectionStatus.Text = selected.Length == 0
            ? "Select at least one recommended item to review. Protected and unsupported items cannot be selected."
            : $"Ready to review {selected.Length:N0} selected {(selected.Length == 1 ? "action" : "actions")}. No files have been changed.";
        AutomationProperties.SetName(SelectionStatus,
            $"{selected.Length:N0} selected items, {OverviewPage.Format(bytes)} estimated potential. {SelectionStatus.Text}");
    }

    private IEnumerable<CleanupCandidate> SelectedCandidates() =>
        candidates.Where(candidate => selectedFindingIds.Contains(candidate.FindingId) && IsEligible(candidate));

    private void ShowCandidateDetails(CleanupCandidate candidate)
    {
        DetailsPanel.Visibility = Visibility.Visible;
        DetailTitle.Text = candidate.Title;
        var source = CandidateSource(candidate);
        DetailSource.Text = "Source: " + source;
        ToolTipService.SetToolTip(DetailSource, source);
        AutomationProperties.SetName(DetailSource, "Full source: " + source);
        DetailCategory.Text = Friendly(candidate.Category);
        DetailEstimate.Text = OverviewPage.Format(candidate.Impact.ObservedLogicalBytes);
        DetailSafety.Text = SafetyLabel(candidate);
        DetailSafety.Foreground = SafetyBrush(candidate);
        DetailConfidence.Text = candidate.Confidence + " confidence";
        DetailReason.Text = candidate.EligibilityReason + " " + candidate.Consequence.WhyItExists;
        DetailOutcome.Text = candidate.Consequence.PossibleOutcome;
        DetailRecreation.Text = candidate.Consequence.CanRegenerate
            ? $"Recreation: This data can be recreated. {candidate.Consequence.NetworkImpact}"
            : $"Recreation: Not confirmed. {candidate.Consequence.NetworkImpact}";
        DetailRollback.Text = "Reversibility: " + candidate.Consequence.RollbackStatement;
        DetailAction.Text = candidate.Action is null
            ? "Supported cleanup mechanism: None. CLYR will not place this item into an executable plan."
            : $"Supported cleanup mechanism: {Friendly(candidate.Action.ActionType)}. Administrator permission: {(candidate.Action.RequiresElevation ? "may be required" : "not required")}. {candidate.Action.Explanation}";
        DetailWarnings.Text = candidate.Eligibility switch
        {
            CleanupEligibility.Protected => "Protected by CLYR. CLYR will not include this location in an executable plan.",
            CleanupEligibility.Unsupported or CleanupEligibility.NotEligible => "Unsupported. CLYR can identify this storage but does not have a supported cleanup method.",
            CleanupEligibility.ManualReviewOnly or CleanupEligibility.InsufficientEvidence => "Review required. This location may contain files you intentionally created or downloaded.",
            _ => "Recommended. CLYR recognizes this as temporary or recreatable data, but you should still review it before continuing."
        };
    }

    private void CloseDetails(object sender, RoutedEventArgs args) => DetailsPanel.Visibility = Visibility.Collapsed;

    private void PreviewPlan(object sender, RoutedEventArgs args)
    {
        var selected = SelectedCandidates().Select(candidate => candidate.FindingId).ToArray();
        if (selected.Length == 0)
        {
            SelectionStatus.Text = "Select at least one eligible finding.";
            return;
        }
        try
        {
            ShowPlan(ViewModel.Create(selected));
            SelectionStatus.Text = "A new immutable plan was created from this selection.";
        }
        catch (InvalidOperationException)
        {
            SelectionStatus.Text = "CLYR could not create this plan safely. Refresh the analysis and review the selection again.";
        }
    }

    private void ShowPlan(CleanupPlan plan)
    {
        ReviewStage.Visibility = Visibility.Collapsed;
        PlanPanel.Visibility = Visibility.Visible;
        PlanIdentity.Text = $"Integrity-checked cleanup plan {plan.Id}";
        PlanDigest.Text = $"SHA-256 digest: {plan.Digest}";
        PlanTiming.Text = $"Created {plan.Expiry.CreatedAtUtc.LocalDateTime:g} · expires {plan.Expiry.ExpiresAtUtc.LocalDateTime:g}";
        PlanImpact.Text = $"{plan.Items.Length:N0} selected actions · {plan.TotalImpact.ItemCount:N0} items · {OverviewPage.Format(plan.TotalImpact.ObservedLogicalBytes)} estimated potential";
        PlanValidation.Text = "Protected-path validation: passed · stale-plan status: current · physical bytes: unavailable";
        var protectedCount = candidates.Count(candidate => candidate.Eligibility == CleanupEligibility.Protected);
        var unsupportedCount = candidates.Count(candidate => candidate.Eligibility is CleanupEligibility.Unsupported or CleanupEligibility.NotEligible);
        var reviewCount = candidates.Count(candidate => candidate.Eligibility is CleanupEligibility.ManualReviewOnly or CleanupEligibility.InsufficientEvidence);
        PlanExcludedText.Text = $"Excluded from this plan: {protectedCount:N0} protected · {unsupportedCount:N0} unsupported · {reviewCount:N0} review required.";
        var permission = plan.Items.Any(item => item.Action.RequiresElevation)
            ? "Windows may ask for administrator permission for a selected action."
            : "No selected action requests administrator permission.";
        PlanWarnings.Text = (plan.Warnings.IsDefaultOrEmpty
            ? "No additional plan warnings. Applications may still need to be closed, and recreatable data may need to be downloaded again."
            : string.Join("\n", plan.Warnings)) + "\n" + permission;
        PlanItemStack.Children.Clear();
        foreach (var item in plan.Items)
        {
            var content = new StackPanel { Spacing = 6 };
            content.Children.Add(new TextBlock { Text = item.Title, Style = (Style)Application.Current.Resources["CardTitleStyle"], TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock
            {
                Text = $"{OverviewPage.Format(item.Impact.ObservedLogicalBytes)} estimated potential · {Friendly(item.Action.ActionType)} · {item.Confidence} evidence",
                TextWrapping = TextWrapping.Wrap,
                Foreground = ResourceBrush("Success")
            });
            content.Children.Add(new TextBlock { Text = "What CLYR plans to do: " + item.Action.Explanation, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock
            {
                Text = $"What may happen: {item.Consequence.PossibleOutcome} Rollback: {item.Consequence.RollbackStatement}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = ResourceBrush("TextMuted")
            });
            PlanItemStack.Children.Add(new Border { Style = (Style)Application.Current.Resources["CompactCardStyle"], Child = content });
        }
        ShowExecutableItems(plan);
        PageHost.ResetScroll();
    }

    private void ShowExecutableItems(CleanupPlan plan)
    {
        _ = plan;
        var executable = ViewModel.ExecutableItems();
        ExecutionPanel.Visibility = executable.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ExecutionProgressPanel.Visibility = Visibility.Collapsed;
        ExecutionResultPanel.Visibility = Visibility.Collapsed;
        ExecutableItemStack.Children.Clear();
        executionSelections.Clear();
        RunCleanupButton.IsEnabled = false;
        ExecutionSelectionStatus.Text = "Select an executable action to continue to confirmation.";
        foreach (var item in executable)
        {
            var choice = new CheckBox
            {
                IsChecked = false,
                Content = $"{item.Title} — {OverviewPage.Format(item.Impact.ObservedLogicalBytes)} estimated potential · {item.Targets.Length:N0} item(s)"
            };
            AutomationProperties.SetName(choice, $"{item.Title} executable item, not selected");
            choice.Checked += (_, _) => UpdateRunCleanupEnabled();
            choice.Unchecked += (_, _) => UpdateRunCleanupEnabled();
            executionSelections[item.ItemId] = choice;
            var details = new TextBlock
            {
                Text = $"{item.Risk} risk · no elevation required\n{item.Consequence.WhyItExists}\n{item.Consequence.PossibleOutcome}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = ResourceBrush("TextMuted")
            };
            var content = new StackPanel { Spacing = 7 };
            content.Children.Add(choice);
            content.Children.Add(details);
            ExecutableItemStack.Children.Add(new Border { Style = (Style)Application.Current.Resources["CompactCardStyle"], Child = content });
        }
    }

    private void UpdateRunCleanupEnabled()
    {
        var count = executionSelections.Count(item => item.Value.IsChecked == true);
        RunCleanupButton.IsEnabled = count > 0;
        ExecutionSelectionStatus.Text = count == 0
            ? "Select an executable action to continue to confirmation."
            : $"{count:N0} executable {(count == 1 ? "action" : "actions")} selected. Confirmation is still required.";
    }

    private async void RequestExecute(object sender, RoutedEventArgs args)
    {
        var selected = executionSelections.Where(item => item.Value.IsChecked == true).Select(item => item.Key).ToArray();
        if (selected.Length == 0)
        {
            ExecutionSelectionStatus.Text = "Select at least one executable item.";
            return;
        }
        var selectedItems = ViewModel.ExecutableItems().Where(item => selected.Contains(item.ItemId, StringComparer.Ordinal)).ToArray();
        var selectedBytes = SumBytes(selectedItems.Select(item => item.Impact.ObservedLogicalBytes));
        var acknowledgement = new CheckBox
        {
            Content = "I understand that selected cache or temporary data may be permanently removed.",
            IsChecked = false
        };
        AutomationProperties.SetName(acknowledgement, "Cleanup consent acknowledgement");
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            Text = $"{selected.Length:N0} selected {(selected.Length == 1 ? "action" : "actions")} · {OverviewPage.Format(selectedBytes)} estimated potential\n\n" +
                "CLYR will remove only the validated items shown in this plan.\nSome actions cannot be undone.\n" +
                "Actual drive free-space change may differ from the estimate.",
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(acknowledgement);
        var dialog = new ContentDialog
        {
            Title = "Confirm selected cleanup",
            Content = body,
            PrimaryButtonText = "Run selected cleanup",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false,
            XamlRoot = XamlRoot
        };
        AutomationProperties.SetName(dialog, "Final cleanup confirmation dialog");
        acknowledgement.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
        acknowledgement.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary) await RunExecutionAsync(selected);
    }

    private async Task RunExecutionAsync(string[] selectedItemIds)
    {
        ExecutionPanel.Visibility = Visibility.Visible;
        ExecutionProgressPanel.Visibility = Visibility.Visible;
        ExecutionResultPanel.Visibility = Visibility.Collapsed;
        RunCleanupButton.IsEnabled = false;
        var removed = 0;
        var skipped = 0;
        var failed = 0;
        ExecutionStateText.Text = "Preparing validated actions…";
        ExecutionCounters.Text = $"Processed 0 of {selectedItemIds.Length:N0} selected item(s).";
        ExecutionCurrentItem.Text = "CLYR is validating the current item.";
        CancelExecutionButton.IsEnabled = true;
        executionCancellation = new CancellationTokenSource();
        var progress = new Progress<ExecutionItemResult>(result =>
        {
            switch (result.Outcome)
            {
                case ExecutionItemOutcome.Removed: removed++; break;
                case ExecutionItemOutcome.Failed: failed++; break;
                default: skipped++; break;
            }
            ExecutionStateText.Text = "Executing validated actions…";
            ExecutionCounters.Text = $"Removed {removed:N0} · Skipped {skipped:N0} · Failed {failed:N0}";
            ExecutionCurrentItem.Text = $"Processed {removed + skipped + failed:N0} item(s). Safety checks remain active.";
        });
        try
        {
            var outcome = await Task.Run(() => ViewModel.Execute(selectedItemIds, progress, executionCancellation.Token));
            ShowExecutionResult(outcome);
        }
        catch (InvalidOperationException)
        {
            ExecutionResultPanel.Visibility = Visibility.Visible;
            ExecutionResultState.Text = "Action could not start";
            ExecutionResultCounts.Text = "No unvalidated action was performed.";
            ExecutionResultBytes.Text = "Observed free-space change: unavailable";
            ExecutionResultWarnings.Text = "The plan could not pass the current safety checks. Create a fresh plan and try again.";
        }
        finally
        {
            ExecutionProgressPanel.Visibility = Visibility.Collapsed;
            CancelExecutionButton.IsEnabled = false;
            executionCancellation?.Dispose();
            executionCancellation = null;
        }
    }

    private void CancelExecution(object sender, RoutedEventArgs args)
    {
        executionCancellation?.Cancel();
        ExecutionStateText.Text = "Cancelling…";
        ExecutionCurrentItem.Text = "The current safe boundary will complete before CLYR stops.";
        CancelExecutionButton.IsEnabled = false;
    }

    private void ShowExecutionResult(ExecutionOutcome outcome)
    {
        ExecutionResultPanel.Visibility = Visibility.Visible;
        ExecutionResultState.Text = outcome.State switch
        {
            ExecutionState.Completed => "Completed",
            ExecutionState.PartiallyCompleted => "Partially completed",
            ExecutionState.Cancelled => "Cancelled",
            ExecutionState.Failed => "Failed",
            ExecutionState.Interrupted => "Interrupted",
            ExecutionState.Rejected => "Rejected",
            ExecutionState.UnknownOutcome => "Unknown outcome",
            _ => outcome.State.ToString()
        };
        var summary = outcome.Receipt.Summary;
        ExecutionResultCounts.Text = $"Total {summary.TotalItems:N0} · Removed {summary.RemovedCount:N0} · Skipped {summary.SkippedCount:N0} · Failed {summary.FailedCount:N0}";
        ExecutionResultBytes.Text = $"Removed logical bytes: {OverviewPage.Format(summary.RemovedLogicalBytes)}" +
            (outcome.Receipt.ObservedFreeSpaceDeltaBytes.HasValue
                ? $" · Observed free-space change: {OverviewPage.Format(Math.Abs(outcome.Receipt.ObservedFreeSpaceDeltaBytes.Value))}"
                : " · Observed free-space change: unavailable");
        ExecutionResultWarnings.Text = outcome.Receipt.Warnings.IsDefaultOrEmpty
            ? "Review the counts above, then run a new analysis to refresh storage estimates."
            : string.Join("\n", outcome.Receipt.Warnings);
        ExecutionReceiptOutput.Visibility = Visibility.Collapsed;
        RefreshReceiptHistory();
    }

    private void ViewReceiptDetails(object sender, RoutedEventArgs args)
    {
        var id = ViewModel.LastOutcome?.Receipt.ExecutionId;
        if (id is null) return;
        ExecutionReceiptOutput.Text = ViewModel.ExportReceipt(id.Value) ?? string.Empty;
        ExecutionReceiptOutput.Visibility = Visibility.Visible;
    }

    private void ExportCurrentReceipt(object sender, RoutedEventArgs args) => ViewReceiptDetails(sender, args);
    private void RunNewAnalysis(object sender, RoutedEventArgs args) => ViewModel.Navigate("Scan");
    private void RunAnalysis(object sender, RoutedEventArgs args) => ViewModel.Navigate("Scan");

    private void RefreshReceiptHistory()
    {
        var history = ViewModel.ReceiptHistory();
        ReceiptHistoryPanel.Visibility = history.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ReceiptHistoryStack.Children.Clear();
        receiptRows.Clear();
        foreach (var entry in history)
        {
            var text = new TextBlock
            {
                Text = $"{entry.StartedAtUtc.LocalDateTime:g} · {entry.FinalState} · removed {entry.RemovedCount:N0} · skipped {entry.SkippedCount:N0} · failed {entry.FailedCount:N0} · {OverviewPage.Format(entry.RemovedLogicalBytes)}",
                TextWrapping = TextWrapping.Wrap
            };
            var view = new Button { Content = "View" };
            AutomationProperties.SetName(view, "View execution receipt " + entry.ExecutionId);
            view.Click += (_, _) =>
            {
                ExecutionReceiptOutput.Text = ViewModel.ExportReceipt(entry.ExecutionId) ?? string.Empty;
                ExecutionReceiptOutput.Visibility = Visibility.Visible;
                ExecutionResultPanel.Visibility = Visibility.Visible;
            };
            var discard = new Button { Content = "Delete receipt" };
            AutomationProperties.SetName(discard, "Delete execution receipt " + entry.ExecutionId);
            discard.Click += (_, _) => { ViewModel.DiscardReceipt(entry.ExecutionId); RefreshReceiptHistory(); };
            var row = new StackPanel
            {
                Orientation = PageHost.LayoutMode == Controls.ResponsivePageWidth.Narrow ? Orientation.Vertical : Orientation.Horizontal,
                Spacing = 12
            };
            row.Children.Add(text);
            row.Children.Add(view);
            row.Children.Add(discard);
            receiptRows.Add(row);
            ReceiptHistoryStack.Children.Add(new Border { Style = (Style)Application.Current.Resources["CompactCardStyle"], Child = row });
        }
    }

    private void ExportPlan(object sender, RoutedEventArgs args)
    {
        ExportOutput.Text = ViewModel.Export();
        ExportOutput.Visibility = Visibility.Visible;
    }

    private void DiscardPlan(object sender, RoutedEventArgs args)
    {
        ViewModel.Discard();
        PlanPanel.Visibility = Visibility.Collapsed;
        ExecutionPanel.Visibility = Visibility.Collapsed;
        ReviewStage.Visibility = candidates.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyPanel.Visibility = candidates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ExportOutput.Text = string.Empty;
        SelectionStatus.Text = "The in-memory plan record was discarded. No files were changed.";
        PageHost.ResetScroll();
    }

    private void Done(object sender, RoutedEventArgs args) => ViewModel.Navigate("Results");
    private void GoToResults(object sender, RoutedEventArgs args) => ViewModel.Navigate("Results");

    private void Reflow(Controls.ResponsivePageWidth mode)
    {
        var narrow = mode == Controls.ResponsivePageWidth.Narrow;
        var orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
        PreviewActions.Orientation = orientation;
        PlanActions.Orientation = orientation;
        ExecuteActions.Orientation = orientation;
        ExecutionResultActions.Orientation = orientation;
        foreach (var row in receiptRows) row.Orientation = orientation;
        ReflowGrid(SummaryMetrics, narrow, 2);
        ReflowGrid(DetailMetrics, narrow, 2);
        ReflowGrid(SelectionSummaryGrid, narrow, 1);
        for (var index = 0; index < FilterBar.Children.Count; index++)
        {
            var child = (FrameworkElement)FilterBar.Children[index];
            Grid.SetColumn(child, narrow ? 0 : index);
            Grid.SetRow(child, narrow ? index : 0);
            Grid.SetColumnSpan(child, narrow ? 4 : 1);
        }
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
            Grid.SetColumn(child, narrow ? index % narrowColumns : index);
            Grid.SetRow(child, narrow ? index / narrowColumns : 0);
            Grid.SetColumnSpan(child, narrow && narrowColumns == 1 ? grid.ColumnDefinitions.Count : 1);
        }
    }

    private static bool IsEligible(CleanupCandidate candidate) => candidate.Eligibility == CleanupEligibility.DryRunEligible;

    private static int SafetyOrder(CleanupEligibility eligibility) => eligibility switch
    {
        CleanupEligibility.DryRunEligible => 0,
        CleanupEligibility.ManualReviewOnly or CleanupEligibility.InsufficientEvidence => 1,
        CleanupEligibility.Protected => 2,
        _ => 3
    };

    private static string SafetyLabel(CleanupCandidate candidate) => candidate.Eligibility switch
    {
        CleanupEligibility.DryRunEligible => "Recommended",
        CleanupEligibility.ManualReviewOnly or CleanupEligibility.InsufficientEvidence => "Review required",
        CleanupEligibility.Protected => "Protected",
        _ => "Unsupported"
    };

    private static string SafetyIcon(CleanupCandidate candidate) => candidate.Eligibility switch
    {
        CleanupEligibility.DryRunEligible => "✓",
        CleanupEligibility.ManualReviewOnly or CleanupEligibility.InsufficientEvidence => "!",
        CleanupEligibility.Protected => "🔒",
        _ => "—"
    };

    private static Brush SafetyBrush(CleanupCandidate candidate) => ResourceBrush(candidate.Eligibility switch
    {
        CleanupEligibility.DryRunEligible => "Success",
        CleanupEligibility.ManualReviewOnly or CleanupEligibility.InsufficientEvidence => "Warning",
        CleanupEligibility.Protected => "Information",
        _ => "TextMuted"
    });

    private static string CandidateSource(CleanupCandidate candidate) => candidate.Targets.FirstOrDefault()?.DisplayLocation is { Length: > 0 } source
        ? source
        : "Source category: " + Friendly(candidate.Category);

    private static string Friendly<T>(T value) where T : struct, Enum
    {
        var source = value.ToString();
        var builder = new StringBuilder(source.Length + 8);
        for (var index = 0; index < source.Length; index++)
        {
            if (index > 0 && char.IsUpper(source[index]) && !char.IsUpper(source[index - 1])) builder.Append(' ');
            builder.Append(source[index]);
        }
        return builder.ToString();
    }

    private static long SumBytes(IEnumerable<long> values)
    {
        long total = 0;
        foreach (var value in values)
        {
            if (value <= 0) continue;
            total = value > long.MaxValue - total ? long.MaxValue : total + value;
        }
        return total;
    }

    private static Brush ResourceBrush(string key) => (Brush)Application.Current.Resources[key];
}
