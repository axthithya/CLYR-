using Clyr.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App;

public sealed partial class MainWindow : Window
{
    private readonly IDemoDataService demo;
    private readonly ApplicationConfiguration configuration;

    public MainWindow(IDemoDataService demo, ApplicationConfiguration configuration)
    {
        this.demo = demo;
        this.configuration = configuration;
        InitializeComponent();
        DemoFindings.ItemsSource = demo.GetFindings().Select(item => item.Title + " — synthetic");
        PageDescription.Text = $"Read-only {configuration.Phase} engineering foundation.";
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var label = args.IsSettingsSelected ? "Settings" : (args.SelectedItemContainer?.Tag?.ToString() ?? "Overview");
        PageTitle.Text = label;
        PageDescription.Text = label is "Overview" or "Settings"
            ? $"Read-only {configuration.Phase} engineering foundation."
            : "Placeholder only. This capability is not implemented in Phase 1.";
    }
}
