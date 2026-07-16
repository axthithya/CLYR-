using Clyr.Core;
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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
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
            configurationRoot["Application:Phase"] ?? "Phase 5",
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
        }
        else
        {
            services.AddSingleton<IDriveDiscovery, WindowsDriveDiscovery>();
            services.AddSingleton<ISnapshotStore>(_ => new SqliteSnapshotStore(Path.Combine(dataDirectory, "history.db")));
            services.AddSingleton<IIdentityKeyProvider>(_ => new FileIdentityKeyProvider(Path.Combine(dataDirectory, "identity.key")));
            services.AddSingleton<IRawDriveIdentitySource, WindowsVolumeIdentitySource>();
            services.AddSingleton<IDriveIdentityProvider, HmacDriveIdentityProvider>();
            services.AddSingleton<IFileSystemEnumerator, WindowsFileSystemEnumerator>();
        }
        services.AddSingleton<IApplicationVersion>(_ => new ApplicationVersion("0.5.0-phase5"));
        services.AddSingleton<ICleanupPlanStore, InMemoryCleanupPlanStore>();
        services.AddSingleton<ICleanupExecutor, PhaseFiveDisabledCleanupExecutor>();
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
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }
}
