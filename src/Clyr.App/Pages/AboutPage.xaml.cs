using System.Runtime.InteropServices;
using Clyr.App.Controls;
using Clyr.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage(AboutViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        var version = Recorded(viewModel.Version);
        VersionText.Text = "Version " + version;
        VersionValue.Text = version;
        ArchitectureValue.Text = Recorded(RuntimeInformation.ProcessArchitecture.ToString());
        RuntimeValue.Text = Recorded(RuntimeInformation.FrameworkDescription);
        TechnicalValue.Text = Recorded(viewModel.TechnicalDetails);
        PageHost.LayoutModeChanged += (_, mode) => Reflow(mode);
        Reflow(ResponsivePageWidth.Wide);
    }

    public AboutViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();

    private void OpenPrivacy(object sender, RoutedEventArgs args) => ViewModel.Navigate("Privacy");
    private void OpenLicenses(object sender, RoutedEventArgs args) => ViewModel.Navigate("Licenses");

    private void Reflow(ResponsivePageWidth mode)
    {
        var narrow = mode == ResponsivePageWidth.Narrow;
        Grid.SetColumn(ProductIconSurface, 0);
        Grid.SetRow(ProductIconSurface, 0);
        Grid.SetColumn(ProductCopy, narrow ? 0 : 1);
        Grid.SetRow(ProductCopy, narrow ? 1 : 0);
        Grid.SetColumnSpan(ProductCopy, narrow ? IdentityGrid.ColumnDefinitions.Count : 1);

        ReflowPair(AboutSummaryGrid, PrivacySummary, ProjectSummary, narrow);
        ReflowPair(NavigationGrid, PrivacyNavigation, LicensesNavigation, narrow);
        ReflowDetails(narrow);
    }

    private void ReflowDetails(bool narrow)
    {
        FrameworkElement[] labels = [VersionLabel, ArchitectureLabel, RuntimeLabel, LicenseLabel, TechnicalLabel];
        FrameworkElement[] values = [VersionValue, ArchitectureValue, RuntimeValue, LicenseValue, TechnicalValue];
        for (var index = 0; index < labels.Length; index++)
        {
            Grid.SetColumn(labels[index], 0);
            Grid.SetRow(labels[index], narrow ? index * 2 : index);
            Grid.SetColumnSpan(labels[index], narrow ? DetailsGrid.ColumnDefinitions.Count : 1);
            Grid.SetColumn(values[index], narrow ? 0 : 1);
            Grid.SetRow(values[index], narrow ? index * 2 + 1 : index);
            Grid.SetColumnSpan(values[index], narrow ? DetailsGrid.ColumnDefinitions.Count : 1);
        }
    }

    private static void ReflowPair(Grid grid, FrameworkElement first, FrameworkElement second, bool stack)
    {
        Grid.SetColumn(first, 0);
        Grid.SetRow(first, 0);
        Grid.SetColumnSpan(first, stack ? grid.ColumnDefinitions.Count : 1);
        Grid.SetColumn(second, stack ? 0 : 1);
        Grid.SetRow(second, stack ? 1 : 0);
        Grid.SetColumnSpan(second, stack ? grid.ColumnDefinitions.Count : 1);
    }

    private static string Recorded(string? value) => string.IsNullOrWhiteSpace(value) ? "Not recorded" : value;
}
