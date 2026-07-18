using System.Diagnostics.CodeAnalysis;
using Clyr.App.Pages;
using Clyr.App.ViewModels;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Core.DeveloperMode;
using Clyr.Core.Execution;
using Clyr.Persistence;
using Clyr.Rules;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clyr.App;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "WinUI owns Window lifecycle; the session is disposed by the Closed event.")]
public sealed partial class MainWindow : Window
{
    private readonly AppSessionViewModel session;
    private readonly Dictionary<string, Page> pages;

    public MainWindow(IScanService scanService, IDriveDiscovery drives, RulePackLoadResult rules,
        ISnapshotStore history, IScanReportExporter exporter, IApplicationVersion version,
        ICleanupPlanStore cleanupPlans, IExecutionTokenService executionTokens, IExecutionReceiptStore? executionReceipts,
        IClock clock, ExecutionSessionContext executionSession, ExecutionFixtureRoot fixtureRoot,
        TrustedExecutableLocator developerLocator, DeveloperToolProbeRunner developerProbeRunner,
        IElevatedScanRetryService elevatedRetryService)
    {
        InitializeComponent();
        session = new(scanService, drives, rules, version);
        var overview = new OverviewPage(new(session));
        var scan = new ScanPage(new(session));
        var results = new ResultsPage(new(session, exporter, elevatedRetryService));
        var reviewPlan = new ReviewPlanPage(new(session, cleanupPlans, executionTokens, executionReceipts, clock,
            executionSession.Value, fixtureRoot.Path));
        var historyPage = new HistoryPage(new(session, history));
        var developer = new DeveloperModePage(new(session, history, cleanupPlans, developerLocator, developerProbeRunner));
        var privacy = new PrivacyPage(new(session));
        var licenses = new LicensesPage(new(session));
        var about = new AboutPage(new(session, version, rules));
        var settings = new SettingsPage(new(session, history));
        pages = new(StringComparer.Ordinal)
        {
            ["Overview"] = overview,
            ["Scan"] = scan,
            ["Results"] = results,
            ["Review Plan"] = reviewPlan,
            ["History"] = historyPage,
            ["Developer Mode"] = developer,
            ["Privacy"] = privacy,
            ["Licenses"] = licenses,
            ["About"] = about,
            ["Settings"] = settings
        };
        foreach (var viewModel in new PageViewModel[] { overview.ViewModel, scan.ViewModel, results.ViewModel, reviewPlan.ViewModel,
            historyPage.ViewModel, developer.ViewModel, privacy.ViewModel, licenses.ViewModel, about.ViewModel, settings.ViewModel })
            viewModel.NavigationRequested += (_, destination) => Select(destination);
        Navigation.SelectedItem = Navigation.MenuItems[0];
        ContentHost.Content = overview;
        Closed += (_, _) => { session.Dispose(); results.ViewModel.Dispose(); };
    }

    private async void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var destination = args.IsSettingsSelected ? "Settings" : args.SelectedItemContainer?.Tag?.ToString() ?? "Overview";
        await ShowAsync(destination);
    }

    private void Select(string destination)
    {
        if (destination == "Settings") { Navigation.SelectedItem = Navigation.SettingsItem; return; }
        Navigation.SelectedItem = Navigation.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), destination, StringComparison.Ordinal));
    }

    private async Task ShowAsync(string destination)
    {
        if (!pages.TryGetValue(destination, out var page)) return;
        ContentHost.Content = page;
        switch (page)
        {
            case OverviewPage value: value.ResetScroll(); break;
            case ScanPage value: value.ResetScroll(); break;
            case ResultsPage value: value.ResetScroll(); value.Refresh(); break;
            case ReviewPlanPage value: value.ResetScroll(); value.Refresh(); break;
            case HistoryPage value: value.ResetScroll(); await value.ActivateAsync(); break;
            case DeveloperModePage value: value.ResetScroll(); await value.ActivateAsync(); break;
            case PrivacyPage value: value.ResetScroll(); break;
            case LicensesPage value: value.ResetScroll(); break;
            case AboutPage value: value.ResetScroll(); break;
            case SettingsPage value: value.ResetScroll(); await value.ActivateAsync(); break;
        }
    }
}
