using Clyr.App.Controls;
using Clyr.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App.Pages;

public sealed partial class LicensesPage : Page
{
    private static readonly LicenseEntry[] Inventory =
    [
        new("Microsoft.WindowsAppSDK", "2.2.0", "MIT"),
        new("Microsoft.Extensions.DependencyInjection", "10.0.9", "MIT"),
        new("Microsoft.Extensions.Configuration.Json", "10.0.9", "MIT"),
        new("Microsoft.Data.Sqlite.Core", "10.0.9", "MIT"),
        new("SQLitePCLRaw.bundle_e_sqlite3", "3.0.3", "Apache-2.0"),
        new("SourceGear.sqlite3", "3.50.4.5", "SQLite public domain"),
        new("JsonSchema.Net", "7.4.0", "MIT"),
        new("YamlDotNet", "18.1.0", "MIT"),
        new("xUnit", "2.9.3", "Apache-2.0"),
        new("coverlet.collector", "10.0.1", "MIT")
    ];

    public LicensesPage(LicensesViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        LicenseList.ItemsSource = Inventory;
        PageHost.LayoutModeChanged += (_, mode) => SetTemplate(mode);
        SetTemplate(ResponsivePageWidth.Wide);
    }

    public LicensesViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();

    private void SearchChanged(object sender, TextChangedEventArgs args)
    {
        var query = LicenseSearch.Text.Trim();
        LicenseList.ItemsSource = string.IsNullOrEmpty(query)
            ? Inventory
            : Inventory.Where(item => item.Summary.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void SetTemplate(ResponsivePageWidth mode) =>
        LicenseList.ItemTemplate = (DataTemplate)Resources[mode == ResponsivePageWidth.Narrow ? "NarrowLicenseTemplate" : "WideLicenseTemplate"];
}

public sealed record LicenseEntry(string Package, string Version, string License)
{
    public string Metadata => Version + " · " + License;
    public string Summary => Package + " " + Metadata;
}
