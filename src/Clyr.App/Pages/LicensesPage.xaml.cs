using System.Runtime.InteropServices;
using Clyr.App.Controls;
using Clyr.App.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Clyr.App.Pages;

public sealed partial class LicensesPage : Page
{
    private static readonly LicenseEntry[] Inventory =
    [
        new("Microsoft.Data.Sqlite.Core", "10.0.9", "MIT", "Direct runtime", LicenseRecordStatus.LicenseAvailable),
        new("SQLitePCLRaw.bundle_e_sqlite3", "3.0.3", "Apache-2.0", "Direct runtime", LicenseRecordStatus.LicenseAvailable),
        new("Microsoft.Extensions.Configuration.Json", "10.0.9", "MIT", "Direct runtime", LicenseRecordStatus.LicenseAvailable),
        new("Microsoft.Extensions.DependencyInjection", "10.0.9", "MIT", "Direct runtime", LicenseRecordStatus.LicenseAvailable),
        new("Microsoft.Extensions.Logging", "10.0.9", "MIT", "Direct runtime", LicenseRecordStatus.LicenseAvailable),
        new("Microsoft.Extensions.Logging.Abstractions", "10.0.9", "MIT", "Direct runtime", LicenseRecordStatus.LicenseAvailable),
        new("Microsoft.Windows.SDK.BuildTools", "10.0.28000.2270", "Microsoft Windows SDK terms", "Direct build", LicenseRecordStatus.NoticeAvailable),
        new("Microsoft.WindowsAppSDK", "2.2.0", "Microsoft Windows App SDK terms", "Direct runtime", LicenseRecordStatus.NoticeAvailable),
        new("YamlDotNet", "18.1.0", "MIT", "Direct runtime", LicenseRecordStatus.LicenseAvailable),
        new("JsonSchema.Net", "7.4.0", "MIT", "Direct runtime", LicenseRecordStatus.LicenseAvailable),
        new("Microsoft.NET.Test.Sdk", "18.7.0", "MIT", "Direct test", LicenseRecordStatus.LicenseAvailable),
        new("xunit", "2.9.3", "Apache-2.0", "Direct test", LicenseRecordStatus.LicenseAvailable),
        new("xunit.runner.visualstudio", "3.1.5", "Apache-2.0", "Direct test", LicenseRecordStatus.LicenseAvailable),
        new("coverlet.collector", "10.0.1", "MIT", "Direct test", LicenseRecordStatus.LicenseAvailable),
        new("SourceGear.sqlite3", "3.50.4.5", "SQLite public domain and SourceGear notices", "Transitive runtime", LicenseRecordStatus.NoticeAvailable)
    ];

    private readonly List<DispatcherQueueTimer> copyFeedbackTimers = [];
    private LicenseEntry? selectedEntry;

    public LicensesPage(LicensesViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        LicenseSearch.TextChanged += FiltersChanged;
        LicenseFilter.SelectionChanged += FiltersChanged;
        LicenseSort.SelectionChanged += FiltersChanged;
        ApplicationVersionText.Text = "Version " + Recorded(viewModel.Session.ApplicationVersion);
        PageHost.LayoutModeChanged += (_, mode) => Reflow(mode);
        Reflow(ResponsivePageWidth.Wide);
        InitializeInventory();
    }

    public LicensesViewModel ViewModel { get; }
    public void ResetScroll() => PageHost.ResetScroll();

    private void InitializeInventory()
    {
        var available = Inventory.Length > 0;
        InventoryContent.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        NoInventoryState.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        if (available) ApplyFilters();
    }

    private void FiltersChanged(object sender, object args)
    {
        if (LicenseSearch is null || LicenseFilter is null || LicenseSort is null) return;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var query = LicenseSearch.Text.Trim();
        var filter = (LicenseFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
        var sort = (LicenseSort.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Package";
        IEnumerable<LicenseEntry> results = Inventory;

        if (!string.IsNullOrWhiteSpace(query))
            results = results.Where(entry => entry.Package.Contains(query, StringComparison.OrdinalIgnoreCase));

        results = filter switch
        {
            "MIT" => results.Where(entry => entry.License.Equals("MIT", StringComparison.Ordinal)),
            "Apache-2.0" => results.Where(entry => entry.License.Equals("Apache-2.0", StringComparison.Ordinal)),
            "Microsoft" => results.Where(entry => entry.License.StartsWith("Microsoft ", StringComparison.Ordinal)),
            "SQLite" => results.Where(entry => entry.License.StartsWith("SQLite ", StringComparison.Ordinal)),
            _ => results
        };

        var filtered = (sort switch
        {
            "License" => results.OrderBy(entry => entry.License, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Package, StringComparer.OrdinalIgnoreCase),
            "Type" => results.OrderBy(entry => entry.DependencyType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Package, StringComparer.OrdinalIgnoreCase),
            _ => results.OrderBy(entry => entry.Package, StringComparer.OrdinalIgnoreCase)
        }).ToArray();

        LicenseList.ItemsSource = filtered;
        var hasResults = filtered.Length > 0;
        LicenseWorkspace.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
        NoResultsState.Visibility = hasResults ? Visibility.Collapsed : Visibility.Visible;
        ResultStatus.Text = hasResults
            ? $"Showing {filtered.Length} of {Inventory.Length} recorded components. Select a row for exact metadata."
            : "No recorded package matches the current search and license filter.";

        if (!hasResults)
        {
            selectedEntry = null;
            return;
        }

        var next = selectedEntry is not null && filtered.Contains(selectedEntry) ? selectedEntry : filtered[0];
        LicenseList.SelectedItem = next;
        ShowDetails(next);
    }

    private void LicenseSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (LicenseList.SelectedItem is LicenseEntry entry) ShowDetails(entry);
    }

    private void ShowDetails(LicenseEntry entry)
    {
        selectedEntry = entry;
        DetailPackage.Text = entry.Package;
        DetailVersion.Text = Recorded(entry.Version);
        DetailLicense.Text = Recorded(entry.License);
        DetailType.Text = Recorded(entry.DependencyType);
        DetailCopyright.Text = Recorded(entry.Copyright);
        DetailProject.Text = Recorded(entry.ProjectUrl);
        DetailStatus.Text = entry.StatusText;
        DetailStatusIcon.Glyph = entry.StatusGlyph;
        DetailNotice.Text = entry.NoticeText;
        DetailAccessibleStatus.Text = $"Selected {entry.Package}, version {Recorded(entry.Version)}, {entry.StatusText.ToLowerInvariant()}.";
        AutomationProperties.SetName(LicenseDetailSurface, entry.AccessibilityLabel + ", selected");

        var hasText = !string.IsNullOrEmpty(entry.BundledLicenseText);
        FullLicenseText.Text = hasText ? entry.BundledLicenseText : string.Empty;
        FullLicenseText.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        UnavailableLicenseText.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        CopyFullTextButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CopyApplicationLicense(object sender, RoutedEventArgs args) =>
        CopyValue(CopyApplicationLicenseButton, "CLYR license identifier", "Apache-2.0");

    private void CopySelectedPackage(object sender, RoutedEventArgs args)
    {
        if (selectedEntry is not null) CopyValue(CopyPackageButton, "package name", selectedEntry.Package);
    }

    private void CopySelectedLicense(object sender, RoutedEventArgs args)
    {
        if (selectedEntry is not null) CopyValue(CopyLicenseButton, "license identifier", Recorded(selectedEntry.License));
    }

    private void CopySelectedFullText(object sender, RoutedEventArgs args)
    {
        if (selectedEntry?.BundledLicenseText is { Length: > 0 } text)
            CopyValue(CopyFullTextButton, "full license text", text);
    }

    private void CopyValue(Button button, string label, string value)
    {
        try
        {
            var data = new DataPackage();
            data.SetText(value);
            Clipboard.SetContent(data);
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

    private void Reflow(ResponsivePageWidth mode)
    {
        var narrow = mode == ResponsivePageWidth.Narrow;
        var stackWorkspace = mode != ResponsivePageWidth.Wide;
        ReflowPair(ApplicationSummaryGrid, ApplicationIdentity, ApplicationMetadata, narrow);

        Grid.SetColumn(LicenseSearch, 0);
        Grid.SetRow(LicenseSearch, 0);
        Grid.SetColumnSpan(LicenseSearch, narrow ? FilterGrid.ColumnDefinitions.Count : 1);
        Grid.SetColumn(LicenseFilter, narrow ? 0 : 1);
        Grid.SetRow(LicenseFilter, narrow ? 1 : 0);
        Grid.SetColumnSpan(LicenseFilter, narrow ? FilterGrid.ColumnDefinitions.Count : 1);
        Grid.SetColumn(LicenseSort, narrow ? 0 : 2);
        Grid.SetRow(LicenseSort, narrow ? 2 : 0);
        Grid.SetColumnSpan(LicenseSort, narrow ? FilterGrid.ColumnDefinitions.Count : 1);

        Grid.SetColumn(DependencyListSurface, 0);
        Grid.SetRow(DependencyListSurface, 0);
        Grid.SetColumnSpan(DependencyListSurface, stackWorkspace ? LicenseWorkspace.ColumnDefinitions.Count : 1);
        Grid.SetColumn(LicenseDetailSurface, stackWorkspace ? 0 : 1);
        Grid.SetRow(LicenseDetailSurface, stackWorkspace ? 1 : 0);
        Grid.SetColumnSpan(LicenseDetailSurface, stackWorkspace ? LicenseWorkspace.ColumnDefinitions.Count : 1);
        DetailActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
        LicenseList.ItemTemplate = (DataTemplate)Resources[narrow ? "NarrowLicenseTemplate" : "WideLicenseTemplate"];
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

public enum LicenseRecordStatus
{
    LicenseAvailable,
    NoticeAvailable,
    TextUnavailable,
    MetadataNotRecorded
}

public sealed record LicenseEntry(
    string Package,
    string Version,
    string License,
    string DependencyType,
    LicenseRecordStatus Status,
    string? Copyright = null,
    string? ProjectUrl = null,
    string? BundledLicenseText = null)
{
    public string StatusText => Status switch
    {
        LicenseRecordStatus.LicenseAvailable => "License available",
        LicenseRecordStatus.NoticeAvailable => "Notice available",
        LicenseRecordStatus.TextUnavailable => "License text unavailable",
        _ => "Metadata not recorded"
    };

    public string StatusGlyph => Status switch
    {
        LicenseRecordStatus.LicenseAvailable => "\uE8D7",
        LicenseRecordStatus.NoticeAvailable => "\uE946",
        LicenseRecordStatus.TextUnavailable => "\uE783",
        _ => "\uE897"
    };

    public string NoticeText => Status switch
    {
        LicenseRecordStatus.LicenseAvailable => "The exact license identifier is recorded. Full license text is not bundled for this component.",
        LicenseRecordStatus.NoticeAvailable => "Repository attribution records this notice or terms category. Full license text is not bundled for this component.",
        LicenseRecordStatus.TextUnavailable => "License metadata is recorded, but full license text is not bundled for this component.",
        _ => "Some license metadata is not recorded. This must not be interpreted as an absence of license requirements."
    };

    public string AccessibilityLabel => $"{Package}, version {RecordedValue(Version)}, {RecordedValue(License)}, {DependencyType}, {StatusText}";
    private static string RecordedValue(string? value) => string.IsNullOrWhiteSpace(value) ? "Not recorded" : value;
}
