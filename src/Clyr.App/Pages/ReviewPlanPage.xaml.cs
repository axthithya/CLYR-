using System.Diagnostics.CodeAnalysis;
using Clyr.App.ViewModels;
using Clyr.Contracts;
using Clyr.Core.Execution;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Pages;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The CancellationTokenSource is created and disposed within a single RunExecutionAsync call; WinUI owns the Page lifecycle.")]
public sealed partial class ReviewPlanPage : Page
{
    private readonly Dictionary<string, CheckBox> selections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> executionSelections = new(StringComparer.Ordinal);
    private CancellationTokenSource? executionCancellation;

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
        var candidates = ViewModel.Candidates;
        EmptyPanel.Visibility = candidates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CandidatePanel.Visibility = candidates.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        CandidateStack.Children.Clear();
        selections.Clear();
        foreach (var candidate in candidates)
        {
            var eligible = candidate.Eligibility == CleanupEligibility.DryRunEligible;
            var choice = new CheckBox
            {
                IsEnabled = eligible,
                IsChecked = false,
                Content = $"{candidate.Title} — {OverviewPage.Format(candidate.Impact.ObservedLogicalBytes)} observed"
            };
            AutomationProperties.SetName(choice, $"{candidate.Title} {candidate.Eligibility} candidate");
            if (eligible) selections[candidate.FindingId] = choice;
            var details = new TextBlock
            {
                Text = $"{candidate.Eligibility} · {candidate.Risk} risk · {candidate.Confidence} confidence\n" +
                    $"{candidate.EligibilityReason}\nPossible consequence: {candidate.Consequence.PossibleOutcome}\n" +
                    $"Rollback: {candidate.Consequence.RollbackStatement}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedTextBrush"]
            };
            var content = new StackPanel { Spacing = 7 };
            content.Children.Add(choice);
            content.Children.Add(details);
            CandidateStack.Children.Add(new Border
            {
                Style = (Style)Application.Current.Resources["CardStyle"],
                Child = content
            });
        }
        if (ViewModel.CurrentPlan is not null) ShowPlan(ViewModel.CurrentPlan);
        RefreshReceiptHistory();
    }

    private void PreviewPlan(object sender, RoutedEventArgs args)
    {
        var selected = selections.Where(item => item.Value.IsChecked == true).Select(item => item.Key).ToArray();
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
        catch (InvalidOperationException exception) { SelectionStatus.Text = exception.Message; }
    }

    private void ShowPlan(CleanupPlan plan)
    {
        PlanPanel.Visibility = Visibility.Visible;
        PlanIdentity.Text = $"Integrity-checked cleanup plan {plan.Id}";
        PlanDigest.Text = $"SHA-256 digest: {plan.Digest}";
        PlanTiming.Text = $"Created {plan.Expiry.CreatedAtUtc.LocalDateTime:g} · expires {plan.Expiry.ExpiresAtUtc.LocalDateTime:g}";
        PlanImpact.Text = $"{plan.TotalImpact.ItemCount:N0} items · {OverviewPage.Format(plan.TotalImpact.ObservedLogicalBytes)} potential logical bytes affected";
        PlanValidation.Text = "Protected-path validation: passed · stale-plan status: current · physical bytes: unavailable";
        PlanItemStack.Children.Clear();
        foreach (var item in plan.Items)
        {
            var text = new TextBlock
            {
                Text = $"{item.Title}\n{item.Risk} risk · {item.Confidence} confidence · {item.Action.Rollback}\n" +
                    $"{item.Consequence.WhyItExists}\n{item.Consequence.PossibleOutcome}\n{item.Consequence.Unknowns}",
                TextWrapping = TextWrapping.Wrap
            };
            PlanItemStack.Children.Add(new Border
            {
                Style = (Style)Application.Current.Resources["CardStyle"],
                Child = text
            });
        }
        ShowExecutableItems(plan);
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
        ExecutionSelectionStatus.Text = string.Empty;
        foreach (var item in executable)
        {
            var choice = new CheckBox
            {
                IsChecked = false,
                Content = $"{item.Title} — {OverviewPage.Format(item.Impact.ObservedLogicalBytes)} · {item.Targets.Length} item(s)"
            };
            AutomationProperties.SetName(choice, $"{item.Title} executable item");
            choice.Checked += (_, _) => UpdateRunCleanupEnabled();
            choice.Unchecked += (_, _) => UpdateRunCleanupEnabled();
            executionSelections[item.ItemId] = choice;
            var details = new TextBlock
            {
                Text = $"Low risk · no elevation required\n{item.Consequence.WhyItExists}\n{item.Consequence.PossibleOutcome}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MutedTextBrush"]
            };
            var content = new StackPanel { Spacing = 7 };
            content.Children.Add(choice);
            content.Children.Add(details);
            ExecutableItemStack.Children.Add(new Border { Style = (Style)Application.Current.Resources["CardStyle"], Child = content });
        }
    }

    private void UpdateRunCleanupEnabled() =>
        RunCleanupButton.IsEnabled = executionSelections.Any(item => item.Value.IsChecked == true);

    private async void RequestExecute(object sender, RoutedEventArgs args)
    {
        var selected = executionSelections.Where(item => item.Value.IsChecked == true).Select(item => item.Key).ToArray();
        if (selected.Length == 0)
        {
            ExecutionSelectionStatus.Text = "Select at least one executable item.";
            return;
        }

        var acknowledgement = new CheckBox
        {
            Content = "I understand that selected cache or temporary data may be permanently removed.",
            IsChecked = false
        };
        AutomationProperties.SetName(acknowledgement, "Cleanup consent acknowledgement");
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            Text = "CLYR will remove only the validated items shown in this plan.\nSome actions cannot be undone.\n" +
                "Actual drive free-space change may differ from the estimate.",
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(acknowledgement);

        var dialog = new ContentDialog
        {
            Title = "Confirm cleanup",
            Content = body,
            PrimaryButtonText = "Run selected cleanup",
            CloseButtonText = "Cancel",
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
        var removed = 0; var skipped = 0; var failed = 0;
        ExecutionStateText.Text = "Running…";
        ExecutionCounters.Text = $"Processed 0 of {selectedItemIds.Length} selected item(s).";
        ExecutionCurrentItem.Text = string.Empty;
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
            ExecutionCounters.Text = $"Removed {removed} · Skipped {skipped} · Failed {failed}";
            ExecutionCurrentItem.Text = $"Last processed target: {result.TargetId} ({result.Outcome})";
        });

        try
        {
            var outcome = await Task.Run(() => ViewModel.Execute(selectedItemIds, progress, executionCancellation.Token));
            ShowExecutionResult(outcome);
        }
        catch (InvalidOperationException exception)
        {
            ExecutionStateText.Text = "Rejected";
            ExecutionCurrentItem.Text = exception.Message;
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
        ExecutionResultCounts.Text = $"Total {summary.TotalItems} · Removed {summary.RemovedCount} · Skipped {summary.SkippedCount} · Failed {summary.FailedCount}";
        ExecutionResultBytes.Text = $"Removed logical bytes: {OverviewPage.Format(summary.RemovedLogicalBytes)}" +
            (outcome.Receipt.ObservedFreeSpaceDeltaBytes.HasValue
                ? $" · Observed free-space change: {OverviewPage.Format(Math.Abs(outcome.Receipt.ObservedFreeSpaceDeltaBytes.Value))}"
                : " · Observed free-space change: unavailable");
        ExecutionResultWarnings.Text = string.Join("\n", outcome.Receipt.Warnings);
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

    private void RefreshReceiptHistory()
    {
        var history = ViewModel.ReceiptHistory();
        ReceiptHistoryPanel.Visibility = history.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ReceiptHistoryStack.Children.Clear();
        foreach (var entry in history)
        {
            var text = new TextBlock
            {
                Text = $"{entry.StartedAtUtc.LocalDateTime:g} · {entry.FinalState} · removed {entry.RemovedCount} · " +
                    $"skipped {entry.SkippedCount} · failed {entry.FailedCount} · {OverviewPage.Format(entry.RemovedLogicalBytes)}",
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
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(text);
            row.Children.Add(view);
            row.Children.Add(discard);
            ReceiptHistoryStack.Children.Add(new Border { Style = (Style)Application.Current.Resources["CardStyle"], Child = row });
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
        ExportOutput.Text = string.Empty;
        SelectionStatus.Text = "The in-memory plan record was discarded. No files were changed.";
    }

    private void Done(object sender, RoutedEventArgs args) => ViewModel.Navigate("Results");
    private void GoToResults(object sender, RoutedEventArgs args) => ViewModel.Navigate("Results");
    private void Reflow(Controls.ResponsivePageWidth mode)
    {
        var orientation = mode == Controls.ResponsivePageWidth.Narrow ? Orientation.Vertical : Orientation.Horizontal;
        PreviewActions.Orientation = orientation;
        PlanActions.Orientation = orientation;
        ExecuteActions.Orientation = orientation;
        ExecutionResultActions.Orientation = orientation;
    }
}
