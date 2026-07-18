using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Clyr.Safety.Tests;

public sealed class ShellResourceTypeTests
{
    private const char Quote = '"';
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string App = Path.Combine(Root, "src", "Clyr.App");
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void RowDefinitionHeightsNeverConsumeDoubleResources()
    {
        AssertGridDefinitionsDoNotConsumeDouble("RowDefinition", "Height");
    }

    [Fact]
    public void ColumnDefinitionWidthsNeverConsumeDoubleResources()
    {
        AssertGridDefinitionsDoNotConsumeDouble("ColumnDefinition", "Width");
    }

    [Fact]
    public void TitleBarRowUsesA48PixelGridLength()
    {
        var resources = LoadResources();
        var rowHeight = Assert.Single(resources, resource => resource.Key == "TitleBarRowHeight");
        Assert.Equal("GridLength", rowHeight.Type);
        Assert.Equal("48", rowHeight.Value);
        var expected = "<RowDefinition Height=" + Quote + "{StaticResource TitleBarRowHeight}" + Quote + " />";
        Assert.Contains(expected, Read("MainWindow.xaml"), StringComparison.Ordinal);
    }

    [Fact]
    public void TitleBarElementHeightRetainsItsDoubleToken()
    {
        var resources = LoadResources();
        var elementHeight = Assert.Single(resources, resource => resource.Key == "TitleBarHeightValue");
        Assert.Equal("Double", elementHeight.Type);
        Assert.Equal("48", elementHeight.Value);
        var expected = "Height=" + Quote + "{StaticResource TitleBarHeightValue}" + Quote;
        Assert.Contains(expected, Read("MainWindow.xaml"), StringComparison.Ordinal);
    }

    [Fact]
    public void AppLoadsShellResourcesAfterTheirDependencies()
    {
        var app = Read("App.xaml");
        var cursor = -1;
        foreach (var source in new[]
        {
            "Styles/DesignTokens.xaml", "Styles/Typography.xaml", "Styles/Controls.xaml",
            "Styles/SelectionControls.xaml", "Styles/Shell.xaml"
        })
        {
            var next = app.IndexOf("Source=" + Quote + source + Quote, StringComparison.Ordinal);
            Assert.True(next > cursor, source + " is missing or out of dependency order.");
            cursor = next;
        }
    }

    [Fact]
    public void FixtureStartupResolvesAndInitializesMainWindow()
    {
        var app = Read("App.xaml.cs");
        var window = Read("MainWindow.xaml.cs");
        Assert.Contains("CLYR_UI_FIXTURE", app, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IDriveDiscovery, UiFixtureDriveDiscovery>()", app, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IScanService, UiFixtureScanService>()", app, StringComparison.Ordinal);
        Assert.Contains("Services.GetRequiredService<MainWindow>()", app, StringComparison.Ordinal);
        Assert.Contains("public MainWindow(", window, StringComparison.Ordinal);
        Assert.Contains("InitializeComponent();", window, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellRetainsBrandAssetsAndAllRoutes()
    {
        var xaml = Read("MainWindow.xaml");
        Assert.Contains("Assets/Branding/CLYR-AppIcon-Master-1024.png", xaml, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(App, "Assets", "Branding", "CLYR-AppIcon.ico")));
        foreach (var route in new[] { "Overview", "Scan", "Results", "Review Plan", "History", "Developer Mode", "Privacy", "Licenses", "About" })
            Assert.Contains("Tag=" + Quote + route + Quote, xaml, StringComparison.Ordinal);
        Assert.Contains("IsSettingsVisible=" + Quote + "True" + Quote, xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellHasNoPrivilegedScanningIpcOrMutationCapability()
    {
        var shell = string.Join(Environment.NewLine, Read("MainWindow.xaml"), Read("MainWindow.xaml.cs"),
            Read(Path.Combine("Styles", "Shell.xaml")));
        foreach (var forbidden in new[]
        {
            "Process.Start", "ProcessStartInfo", "NamedPipe", "runas", "requireAdministrator",
            "File.Delete", "File.Move", "Directory.Delete", "Directory.Move", "IFileSystemEnumerator",
            "CleanupExecutor", "ElevatedHelperLauncher", "powershell.exe", "cmd.exe", "HttpClient"
        })
            Assert.DoesNotContain(forbidden, shell, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertGridDefinitionsDoNotConsumeDouble(string element, string property)
    {
        var resourceTypes = LoadResources().GroupBy(resource => resource.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.Select(resource => resource.Type).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
        var pattern = $"<{element}\\b[^>]*\\b{property}=" + Quote
            + "\\{(?:Static|Theme)Resource\\s+([^}]+)\\}" + Quote;
        foreach (var path in EnumerateXamlFiles())
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(path), pattern, RegexOptions.None, TimeSpan.FromSeconds(1)))
            {
                var key = match.Groups[1].Value;
                if (resourceTypes.TryGetValue(key, out var types))
                    Assert.DoesNotContain("Double", types);
            }
        }
    }

    private static (string Key, string Type, string Value)[] LoadResources()
    {
        return Directory.EnumerateFiles(Path.Combine(App, "Styles"), "*.xaml")
            .SelectMany(path => XDocument.Load(path).Descendants())
            .Select(element => (Element: element, Key: (string?)element.Attribute(Xaml + "Key")))
            .Where(resource => resource.Key is not null)
            .Select(resource => (Key: resource.Key!, Type: resource.Element.Name.LocalName, Value: resource.Element.Value.Trim()))
            .ToArray();
    }

    private static IEnumerable<string> EnumerateXamlFiles() =>
        Directory.EnumerateFiles(App, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(App, relativePath));
}
