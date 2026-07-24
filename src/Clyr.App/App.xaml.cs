using Clyr.Contracts;
using Clyr.Core;
using Clyr.Core.DeveloperMode;
using Clyr.Core.Execution;
using Clyr.Persistence;
using Clyr.Rules;
using Clyr.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Clyr.App;

public partial class App : Application
{
    private Window? window;
    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    public ServiceProvider Services { get; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // Crash-recovery correction: any durable "Started" execution record left by a previous crash is
            // resolved to Interrupted before anything (including Review Plan's receipt history) can render it —
            // never resumed, never guessed successful. Best-effort: a corrupted or inaccessible history store
            // must not block the app from starting at all.
            try
            {
                var receiptStore = Services.GetRequiredService<IExecutionReceiptStore>();
                var clock = Services.GetRequiredService<IClock>();
                await receiptStore.ReconcileInterruptedAsync(TimeSpan.Zero, clock.UtcNow);
            }
            catch (ExecutionReceiptStoreException) { /* best-effort; surfaced only when the user opens Review Plan's receipt history */ }

            window = Services.GetRequiredService<MainWindow>();
        }
        catch (Exception exception)
        {
            window = new StartupErrorWindow(exception);
        }
        window.Activate();
    }

    private static ServiceProvider ConfigureServices()
    {
        var configurationRoot = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();
        var demoDataOnly = bool.TryParse(configurationRoot["Application:DemoDataOnly"], out var configuredDemoDataOnly)
            ? configuredDemoDataOnly
            : false;
        var applicationConfiguration = new ApplicationConfiguration(
            configurationRoot["Application:Phase"] ?? "Phase 7",
            demoDataOnly);

        var services = new ServiceCollection();
        services.AddSingleton(applicationConfiguration);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEnvironmentInfo, WindowsEnvironmentInfo>();
        services.AddSingleton<IPrivacyRedactor, PrivacyRedactor>();
        services.AddSingleton<IDemoDataService, DemoDataService>();
        var uiFixture = string.Equals(Environment.GetEnvironmentVariable("CLYR_UI_FIXTURE"), "1", StringComparison.Ordinal);
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CLYR");
        if (uiFixture)
        {
            services.AddSingleton<IDriveDiscovery, UiFixtureDriveDiscovery>();
            services.AddSingleton<ISnapshotStore, UiFixtureSnapshotStore>();
            services.AddSingleton<IScanService, UiFixtureScanService>();
            services.AddSingleton<IElevatedScanRetryService, UiFixtureElevatedScanRetryService>();
            services.AddSingleton<IDriveIdentityProvider, UiFixtureDriveIdentityProvider>();
        }
        else
        {
            services.AddSingleton<IDriveDiscovery, WindowsDriveDiscovery>();
            services.AddSingleton<ISnapshotStore>(_ => new SqliteSnapshotStore(Path.Combine(dataDirectory, "history.db")));
            services.AddSingleton<IIdentityKeyProvider>(_ => new FileIdentityKeyProvider(Path.Combine(dataDirectory, "identity.key")));
            services.AddSingleton<IRawDriveIdentitySource, WindowsVolumeIdentitySource>();
            services.AddSingleton<IDriveIdentityProvider, HmacDriveIdentityProvider>();
            services.AddSingleton<IFileSystemEnumerator, WindowsFileSystemEnumerator>();
            // The one production composition of the elevated permission-limited-root retry service — see
            // ElevatedScanRetryServiceFactory for the full dependency graph it wires together. Reuses the
            // app's own already-registered IDriveDiscovery/IDriveIdentityProvider rather than standing up a
            // second, independent copy of either.
            services.AddSingleton(provider => ElevatedScanRetryServiceFactory.Create(
                provider.GetRequiredService<IDriveDiscovery>(), provider.GetRequiredService<IDriveIdentityProvider>()));
        }
        services.AddSingleton<IApplicationVersion>(_ => ApplicationVersion.Current);
        // Registered unconditionally (both fixture and real modes now register an IDriveIdentityProvider) so
        // ResultsViewModel can persist an Administrator-Retry-enriched result back over its original History
        // record — see ISnapshotStore.UpdateAsync and ResultsViewModel.PersistEnrichedResultAsync.
        services.AddSingleton<SnapshotFactory>();
        services.AddSingleton<ICleanupPlanStore, InMemoryCleanupPlanStore>();
        services.AddSingleton<ICleanupExecutor, PhaseFiveDisabledCleanupExecutor>();
        services.AddSingleton<IExecutionTokenService, ExecutionTokenService>();
        services.AddSingleton(_ => new ExecutionSessionContext(new ExecutionSessionId(Guid.NewGuid())));
        services.AddSingleton(uiFixture ? ExecutionFixtureRoot.CreateSeeded() : new ExecutionFixtureRoot(null));
        services.AddSingleton<IExecutionReceiptStore>(_ => uiFixture
            ? new UiFixtureExecutionReceiptStore()
            : new SqliteExecutionReceiptStore(Path.Combine(dataDirectory, "history.db")));
        services.AddSingleton(_ => BuiltInRulePackLoader.Load(Path.Combine(AppContext.BaseDirectory, "rules", "builtin")));
        if (!uiFixture) services.AddSingleton<IScanService>(provider =>
        {
            IScanService scanner = new ScanCoordinator(provider.GetRequiredService<IFileSystemEnumerator>(),
                provider.GetRequiredService<IDriveDiscovery>(), provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<RulePackLoadResult>().Pack);
            return new SnapshotSavingScanService(scanner,
                new SnapshotFactory(provider.GetRequiredService<IDriveIdentityProvider>(), provider.GetRequiredService<IApplicationVersion>()),
                provider.GetRequiredService<ISnapshotStore>());
        });
        services.AddSingleton<IScanReportExporter, ScanReportExporter>();
        services.AddSingleton<TrustedExecutableLocator>();
        services.AddSingleton<DeveloperToolProbeRunner>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }
}
