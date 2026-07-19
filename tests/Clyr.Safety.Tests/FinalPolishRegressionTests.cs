using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Clyr.Safety.Tests;

public sealed class FinalPolishRegressionTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string App = Path.Combine(Root, "src", "Clyr.App");
    private static readonly string Pages = Path.Combine(App, "Pages");
    private static readonly string[] PageNames =
    [
        "Overview", "Scan", "Results", "ReviewPlan", "History",
        "DeveloperMode", "Privacy", "Licenses", "About", "Settings"
    ];

    [Fact]
    public void EveryNormalPageUsesOneSharedFrameAndCanonicalThemeBackground()
    {
        foreach (var name in PageNames)
        {
            var path = Path.Combine(Pages, name + "Page.xaml");
            var xaml = File.ReadAllText(path);
            var document = XDocument.Load(path);
            Assert.Single(document.Descendants(), element => element.Name.LocalName == "ResponsivePageHost");
            Assert.Contains("Background=\"{ThemeResource AppBackground}\"", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("<ScrollViewer", xaml, StringComparison.Ordinal);
            Assert.DoesNotMatch(new Regex("#[0-9A-Fa-f]{3,8}", RegexOptions.None, TimeSpan.FromSeconds(1)), xaml);
        }
    }

    [Fact]
    public void RetiredPresentationAliasesCannotDriftBackIntoTheApplication()
    {
        var source = string.Join(Environment.NewLine, Directory.EnumerateFiles(App, "*.*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".xaml" or ".cs")
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText));
        foreach (var retired in new[]
        {
            "AppBackgroundBrush", "CardBackgroundBrush", "CardBorderBrush", "MutedTextBrush",
            "WarmAccentBrush", "SubtleAccentBrush", "SelectedCardBorderBrush",
            "TrustBadgeBackgroundBrush", "TrustBadgeBorderBrush", "PrivacyBannerBrush", "DisabledReadableBrush"
        })
            Assert.DoesNotContain(retired, source, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedHeadersExposeHierarchyWithoutGenericDuplicateNames()
    {
        var typography = Read("Styles", "Typography.xaml");
        var pageHeader = Read("Controls", "PageHeader.xaml");
        var sectionHeader = Read("Controls", "SectionHeader.xaml");
        Assert.Contains("AutomationProperties.HeadingLevel\" Value=\"Level2\"", typography, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment\" Value=\"Left\"", typography, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HeadingLevel=\"Level1\"", pageHeader, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.Name=\"Page subtitle\"", pageHeader, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.Name=\"{x:Bind Title", sectionHeader, StringComparison.Ordinal);
    }

    [Fact]
    public void ReusableStatesExposeMeaningAndHideDecorativeChildren()
    {
        var empty = Read("Controls", "EmptyState.xaml");
        var loading = Read("Controls", "LoadingState.xaml");
        var status = Read("Controls", "StatusBadge.xaml");
        var privacy = Read("Controls", "PrivacyBadge.xaml");
        var keyValue = Read("Controls", "KeyValueRow.xaml");
        var listItem = Read("Controls", "ListItemRow.xaml");
        Assert.Contains("AutomationProperties.AccessibilityView=\"Raw\"", empty, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", loading, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HelpText=\"{x:Bind Message", loading, StringComparison.Ordinal);
        Assert.True(Regex.Count(status, "AutomationProperties.AccessibilityView=\"Raw\"") >= 2);
        Assert.True(Regex.Count(privacy, "AutomationProperties.AccessibilityView=\"Raw\"") >= 2);
        Assert.Contains("AutomationProperties.HelpText=\"{x:Bind Value", keyValue, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AccessibilityView=\"Raw\"", listItem, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalTerminologyRemainsTruthfulAndNoPresentationCapabilityWasAdded()
    {
        var privacy = Read("Pages", "PrivacyPage.xaml");
        var review = Read("Pages", "ReviewPlanPage.xaml");
        var results = Read("Pages", "ResultsPage.xaml.cs");
        var developer = Read("Pages", "DeveloperModePage.xaml");
        var responsive = Read("Controls", "ResponsivePageHost.xaml.cs");
        var shell = Read("Styles", "Shell.xaml");
        var mainWindow = Read("MainWindow.xaml.cs");
        Assert.Contains("Read-only analysis", privacy, StringComparison.Ordinal);
        Assert.DoesNotContain("Read-only by default", privacy, StringComparison.Ordinal);
        Assert.Contains("Content=\"Run an analysis\"", review, StringComparison.Ordinal);
        Assert.Contains("AccountedPortion(accounted)", results, StringComparison.Ordinal);
        Assert.DoesNotContain("observed most of the drive", results, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SecondaryActionStyle}\"", developer, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueue.TryEnqueue", responsive, StringComparison.Ordinal);
        Assert.Contains("<ThemeResource x:Key=\"NavigationViewDefaultPaneBackground\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("<StaticResource x:Key=\"NavigationViewDefaultPaneBackground\"", shell, StringComparison.Ordinal);
        Assert.Contains("Configure CLYR appearance and local history settings.", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("appearance, privacy and local history settings", mainWindow, StringComparison.Ordinal);

        var presentation = string.Join(Environment.NewLine,
            PageNames.Select(name => File.ReadAllText(Path.Combine(Pages, name + "Page.xaml"))));
        foreach (var forbidden in new[]
        {
            "Process.Start", "powershell.exe", "cmd.exe", "runas", "HttpClient",
            "NamedPipe", "TelemetryClient", "PackageReference"
        })
            Assert.DoesNotContain(forbidden, presentation, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([App, .. parts]));
}
