using System.Xml.Linq;

namespace Clyr.Safety.Tests;

public sealed class DesignSystemResourceTests
{
    private const char Quote = '"';
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string App = Path.Combine(Root, "src", "Clyr.App");
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void AppLoadsDesignSystemDictionariesInDependencyOrder()
    {
        var app = Read("App.xaml");
        var expected = new[]
        {
            "Styles/DesignTokens.xaml",
            "Styles/Typography.xaml",
            "Styles/Controls.xaml",
            "Styles/SelectionControls.xaml"
        };
        var cursor = -1;
        foreach (var source in expected)
        {
            var next = app.IndexOf($"Source={Quote}{source}{Quote}", StringComparison.Ordinal);
            Assert.True(next > cursor, $"{source} is missing or out of dependency order.");
            cursor = next;
        }
    }

    [Fact]
    public void EveryThemeDefinesTheCompleteSemanticColorContract()
    {
        var document = XDocument.Load(Path.Combine(App, "Styles", "DesignTokens.xaml"));
        var required = new[]
        {
            "AppBackground", "NavigationBackground", "SurfacePrimary", "SurfaceSecondary", "SurfaceElevated",
            "SurfaceHover", "SurfaceSelected", "BorderSubtle", "BorderStrong", "TextPrimary", "TextSecondary",
            "TextMuted", "AccentPrimary", "AccentPrimaryHover", "AccentPrimaryPressed", "Success", "Warning",
            "Error", "Information", "PrivacySafe"
        };
        var themes = document.Descendants().Where(element =>
            element.Name.LocalName == "ResourceDictionary" && element.Attribute(Xaml + "Key") is not null).ToArray();
        foreach (var themeName in new[] { "Default", "Light", "HighContrast" })
        {
            var theme = Assert.Single(themes, element => (string?)element.Attribute(Xaml + "Key") == themeName);
            var keys = theme.Elements().Select(element => (string?)element.Attribute(Xaml + "Key")).ToHashSet(StringComparer.Ordinal);
            foreach (var key in required)
                Assert.Contains(key, keys);
        }
    }

    [Fact]
    public void TokensCoverSpacingRadiiDimensionsAndResponsiveLayout()
    {
        var tokens = Read(Path.Combine("Styles", "DesignTokens.xaml"));
        foreach (var spacing in new[] { 4, 8, 12, 16, 20, 24, 32, 40, 48 })
            Assert.Contains($"x:Key={Quote}Spacing{spacing}{Quote}", tokens, StringComparison.Ordinal);

        foreach (var key in new[]
        {
            "CornerRadiusSmall", "CornerRadiusControl", "CornerRadiusButton", "CornerRadiusCard", "CornerRadiusDialog",
            "CornerRadiusLargeSurface", "ControlHeightButton", "ControlHeightInput", "ControlHeightNavigationRow",
            "ControlSizeCompactButton", "IconSizeSmall", "IconSizeStandard", "IconSizeLarge", "CardPadding",
            "PageMarginNarrow", "PageMarginMedium", "PageMarginDesktop", "ContentMaxWidthDesktop",
            "ContentMaxWidthMedium", "ContentMaxWidthNarrow", "SidebarWidth", "PageSectionSpacing",
            "TwoColumnSpacing", "MetricCardGridSpacing"
        })
            Assert.Contains($"x:Key={Quote}{key}{Quote}", tokens, StringComparison.Ordinal);
    }

    [Fact]
    public void TypographyAndReusableStylesExposeTheCompleteFoundation()
    {
        var typography = Read(Path.Combine("Styles", "Typography.xaml"));
        foreach (var key in new[]
        {
            "PageTitleStyle", "PageSubtitleStyle", "SectionTitleStyle", "CardTitleStyle", "MetricValueStyle",
            "MetricLabelStyle", "BodyStyle", "BodySecondaryStyle", "CaptionStyle", "StatusTextStyle", "MonospaceDetailStyle"
        })
            Assert.Contains($"x:Key={Quote}{key}{Quote}", typography, StringComparison.Ordinal);

        var controls = Read(Path.Combine("Styles", "Controls.xaml"));
        foreach (var key in new[]
        {
            "StandardCardStyle", "CompactCardStyle", "MetricCardStyle", "StatusBadgeStyle", "PrivacyBadgeStyle",
            "PrimaryButtonStyle", "SecondaryButtonStyle", "QuietButtonStyle", "DestructiveButtonStyle", "IconButtonStyle",
            "InfoBannerStyle", "WarningBannerStyle", "SuccessBannerStyle", "DividerStyle"
        })
            Assert.Contains($"x:Key={Quote}{key}{Quote}", controls, StringComparison.Ordinal);
    }

    [Fact]
    public void ReusableControlsProvideAccessibleNonColorStateCommunication()
    {
        foreach (var name in new[] { "PageHeader", "SectionHeader", "StatusBadge", "PrivacyBadge", "EmptyState", "LoadingState", "KeyValueRow", "ListItemRow" })
        {
            Assert.True(File.Exists(Path.Combine(App, "Controls", name + ".xaml")), name + " XAML is missing.");
            Assert.True(File.Exists(Path.Combine(App, "Controls", name + ".xaml.cs")), name + " code-behind is missing.");
        }

        var selection = Read(Path.Combine("Styles", "SelectionControls.xaml"));
        foreach (var state in new[] { "Normal", "PointerOver", "Pressed", "Disabled", "Checked", "CheckedPointerOver", "CheckedPressed", "CheckedDisabled" })
            Assert.Contains($"x:Name={Quote}{state}{Quote}", selection, StringComparison.Ordinal);
        Assert.Contains("UseSystemFocusVisuals", selection, StringComparison.Ordinal);
        Assert.Contains("SelectionMark.Visibility", selection, StringComparison.Ordinal);
        Assert.Contains("Selected indicator", selection, StringComparison.Ordinal);

        var customControls = string.Join(Environment.NewLine,
            Directory.EnumerateFiles(Path.Combine(App, "Controls"), "*.xaml").Select(File.ReadAllText));
        Assert.Contains("AutomationProperties.Name", customControls, StringComparison.Ordinal);
        Assert.Contains($"AutomationProperties.LiveSetting={Quote}Polite{Quote}", customControls, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(App, relativePath));
}
