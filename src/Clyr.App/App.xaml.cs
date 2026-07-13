using Clyr.Core;
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
            : ApplicationConfiguration.PhaseOneDefaults.DemoDataOnly;
        var applicationConfiguration = new ApplicationConfiguration(
            configurationRoot["Application:Phase"] ?? ApplicationConfiguration.PhaseOneDefaults.Phase,
            demoDataOnly);

        var services = new ServiceCollection();
        services.AddSingleton(applicationConfiguration);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEnvironmentInfo, WindowsEnvironmentInfo>();
        services.AddSingleton<IPrivacyRedactor, PrivacyRedactor>();
        services.AddSingleton<IDemoDataService, DemoDataService>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }
}
