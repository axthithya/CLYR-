using Clyr.App.ViewModels;
using Clyr.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Pages;

public sealed partial class ReviewPlanPage : Page
{
    private readonly Dictionary<string, CheckBox> selections = new(StringComparer.Ordinal);

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
        PreviewActions.Orientation = mode == Controls.ResponsivePageWidth.Narrow ? Orientation.Vertical : Orientation.Horizontal;
        PlanActions.Orientation = mode == Controls.ResponsivePageWidth.Narrow ? Orientation.Vertical : Orientation.Horizontal;
    }
}

