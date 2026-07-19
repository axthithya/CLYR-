using Clyr.App.Controls;
using Clyr.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Pages;

public sealed partial class PrivacyPage : Page
{
    public PrivacyPage(PrivacyViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PageHost.LayoutModeChanged += (_, mode) => Reflow(mode);
        Reflow(ResponsivePageWidth.Wide);
    }

    public PrivacyViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();

    private void Reflow(ResponsivePageWidth mode)
    {
        var narrow = mode == ResponsivePageWidth.Narrow;
        ReflowGrid(TruthGrid, [LocalTruth, MetadataTruth, ReadOnlyTruth], narrow);
        ReflowGrid(ReadGrid, [ReadsSection, DoesNotReadSection], narrow);
        ReflowFlow(DataFlowGrid,
            [MetadataFlowStep, AnalysisFlowStep, ResultsFlowStep, ExportFlowStep],
            [FlowArrowOne, FlowArrowTwo, FlowArrowThree], narrow);
        ReflowGrid(LocalStorageGrid, [HistoryStorageColumn, WorkflowStorageColumn], narrow);
        ReflowFlow(RetryFlowGrid,
            [RetryStepOne, RetryStepTwo, RetryStepThree, RetryStepFour],
            [RetryArrowOne, RetryArrowTwo, RetryArrowThree], narrow);
        ReflowGrid(SafetyGrid, [ExportPrivacySection, CleanupSafetySection], narrow);
    }

    private static void ReflowGrid(Grid grid, IReadOnlyList<FrameworkElement> items, bool narrow)
    {
        var columns = grid.ColumnDefinitions.Count;
        for (var index = 0; index < items.Count; index++)
        {
            Grid.SetColumn(items[index], narrow ? 0 : index);
            Grid.SetRow(items[index], narrow ? index : 0);
            Grid.SetColumnSpan(items[index], narrow ? columns : 1);
        }
    }

    private static void ReflowFlow(Grid grid, IReadOnlyList<FrameworkElement> steps,
        IReadOnlyList<FrameworkElement> arrows, bool narrow)
    {
        for (var index = 0; index < grid.ColumnDefinitions.Count; index++)
            grid.ColumnDefinitions[index].Width = narrow
                ? index == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(0)
                : index % 2 == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;

        for (var index = 0; index < steps.Count; index++)
        {
            Grid.SetColumn(steps[index], narrow ? 0 : index * 2);
            Grid.SetRow(steps[index], narrow ? index : 0);
            Grid.SetColumnSpan(steps[index], narrow ? grid.ColumnDefinitions.Count : 1);
        }
        foreach (var arrow in arrows) arrow.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
    }
}
