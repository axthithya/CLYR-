using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Clyr.Safety.Tests;

public sealed class RepositorySafetyTests
{
    private static readonly string Root = RepositoryRoot();
    private static readonly string[] CoreForbiddenReferences = { "Microsoft.Data.Sqlite", "Microsoft.WindowsAppSDK", "Clyr.Windows" };

    [Fact]
    public void PackageVersionsExistOnlyInCentralFile()
    {
        foreach (var project in Directory.EnumerateFiles(Root, "*.csproj", SearchOption.AllDirectories))
        {
            if (project.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            var document = XDocument.Load(project);
            var versioned = document.Descendants("PackageReference").Where(item => item.Attribute("Version") is not null || item.Element("Version") is not null);
            Assert.Empty(versioned);
        }
    }

    [Fact]
    public void CentralPackageManagementIsEnabledOnceWithoutOverridesOrAdvisorySuppression()
    {
        var central = XDocument.Load(Path.Combine(Root, "Directory.Packages.props"));
        Assert.Single(central.Descendants("ManagePackageVersionsCentrally"));
        Assert.Equal("true", central.Descendants("ManagePackageVersionsCentrally").Single().Value, ignoreCase: true);
        var packageVersions = central.Descendants("PackageVersion").ToArray();
        Assert.Equal(packageVersions.Length, packageVersions.Select(item => item.Attribute("Include")?.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var project in RepositoryFiles("*.csproj"))
        {
            var text = File.ReadAllText(project);
            Assert.DoesNotContain("VersionOverride", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("NU190", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("NU1008", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void CentralVersionsAreExactStableVersions()
    {
        var document = XDocument.Load(Path.Combine(Root, "Directory.Packages.props"));
        var versions = document.Descendants("PackageVersion").Select(item => item.Attribute("Version")?.Value ?? string.Empty);
        Assert.All(versions, version =>
        {
            Assert.DoesNotContain("*", version, StringComparison.Ordinal);
            Assert.DoesNotContain("-preview", version, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("-rc", version, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ArchitectureReferencesRespectPhaseOneBoundaries()
    {
        var contracts = Project("Clyr.Contracts");
        var core = Project("Clyr.Core");
        var app = Project("Clyr.App");
        Assert.Empty(contracts.Descendants("ProjectReference"));
        Assert.All(CoreForbiddenReferences, reference => Assert.DoesNotContain(reference, core.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(app.Descendants("PackageReference"), item => string.Equals(item.Attribute("Include")?.Value, "Microsoft.Data.Sqlite.Core", StringComparison.Ordinal));
    }

    [Fact]
    public void ProductionSourceContainsNoMutationOrProcessExecutor()
    {
        var forbidden = new[] { "Process.Start", "System.Diagnostics.Process", "File.Delete", "File.Move", "Directory.Delete", "RecycleOption", "requireAdministrator", "powershell.exe", "cmd.exe" };
        foreach (var source in Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (source.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            var text = File.ReadAllText(source);
            foreach (var token in forbidden) Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ManifestIsNonElevatedAndCapabilityFree()
    {
        var manifest = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "app.manifest"));
        Assert.Contains("asInvoker", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("requireAdministrator", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("Capability", manifest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PhaseThreeClassifierExistsWithoutCleanupOrMutationServices()
    {
        var names = Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories).Select(Path.GetFileName);
        Assert.Contains(names, name => name is not null && name.Contains("Scanning", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, name => name is not null && name.Contains("BuiltInRulePack", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name is not null && name.Contains("Cleanup", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplicationCompositionAndNavigationUseDistinctReadOnlyPages()
    {
        var appSource = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "App.xaml.cs"));
        var navigation = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "MainWindow.xaml"));
        Assert.Contains("ServiceCollection", appSource, StringComparison.Ordinal);
        Assert.Contains("StartupErrorWindow", appSource, StringComparison.Ordinal);
        foreach (var destination in new[] { "Overview", "Scan", "Results", "History", "Developer Mode", "Privacy", "Licenses", "About" })
            Assert.Contains($"Content=\"{destination}\"", navigation, StringComparison.Ordinal);
        Assert.Contains("IsSettingsVisible=\"True\"", navigation, StringComparison.Ordinal);
        Assert.Contains("ContentControl", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("DriveSelector", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("Start Analysis", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Data.Sqlite", appSource + navigation, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryContainsNoMachineSpecificPathOrCredentialMaterial()
    {
        var machinePaths = new[] { Root, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .SelectMany(path => new[] { path, path.Replace('\\', '/') })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var credentialPattern = new Regex("-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|AKIA[0-9A-Z]{16}|gh[pousr]_[A-Za-z0-9]{30,}", RegexOptions.CultureInvariant);
        foreach (var file in RepositoryFiles("*").Where(IsTextConfigurationFile))
        {
            var text = File.ReadAllText(file);
            foreach (var path in machinePaths) Assert.DoesNotContain(path, text, StringComparison.OrdinalIgnoreCase);
            Assert.False(credentialPattern.IsMatch(text), $"Credential-shaped material found in {file}.");
        }
    }

    private static XDocument Project(string name) => XDocument.Load(Path.Combine(Root, "src", name, name + ".csproj"));
    private static IEnumerable<string> RepositoryFiles(string pattern) => Directory.EnumerateFiles(Root, pattern, SearchOption.AllDirectories)
        .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        .Where(path => !path.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        .Where(path => !path.Contains(Path.DirectorySeparatorChar + ".tools" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    private static bool IsTextConfigurationFile(string path) => Path.GetExtension(path) is ".cs" or ".csproj" or ".props" or ".targets" or ".json" or ".yaml" or ".yml" or ".md" or ".ps1" or ".xaml";
    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
