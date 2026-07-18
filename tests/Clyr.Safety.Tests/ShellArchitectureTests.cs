namespace Clyr.Safety.Tests;

public sealed class ShellArchitectureTests
{
    private const char Quote = '"';
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string App = Path.Combine(Root, "src", "Clyr.App");

    [Fact]
    public void PrimaryNavigationRetainsExistingDestinations()
    {
        var xaml = Read("MainWindow.xaml");
        var code = Read("MainWindow.xaml.cs");
        foreach (var destination in new[] { "Overview", "Scan", "Results", "Review Plan", "History" })
        {
            Assert.Contains(Attribute("Tag", destination), xaml, StringComparison.Ordinal);
            Assert.Contains($"[{Quote}{destination}{Quote}]", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SettingsRemainsBottomPinnedAndUsesExistingRoute()
    {
        var xaml = Read("MainWindow.xaml");
        var code = Read("MainWindow.xaml.cs");
        Assert.Contains(Attribute("IsSettingsVisible", "True"), xaml, StringComparison.Ordinal);
        Assert.Contains("args.IsSettingsSelected ? \"Settings\"", code, StringComparison.Ordinal);
        Assert.Contains("Navigation.SettingsItem", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DeveloperModeIsSeparatedFromPrimaryWorkflow()
    {
        var xaml = Read("MainWindow.xaml");
        var history = xaml.IndexOf(Attribute("Tag", "History"), StringComparison.Ordinal);
        var advancedSeparator = xaml.IndexOf("Advanced navigation separator", StringComparison.Ordinal);
        var developer = xaml.IndexOf(Attribute("Tag", "Developer Mode"), StringComparison.Ordinal);
        var trustSeparator = xaml.IndexOf("Trust and information navigation separator", StringComparison.Ordinal);
        Assert.True(history < advancedSeparator && advancedSeparator < developer && developer < trustSeparator);
    }

    [Fact]
    public void TrustAndInformationDestinationsRemainReachable()
    {
        var xaml = Read("MainWindow.xaml");
        foreach (var destination in new[] { "Privacy", "Licenses", "About" })
            Assert.Contains(Attribute("Tag", destination), xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void BrandingAppearsOnceInTheTitleBarAndNeverInThePane()
    {
        var xaml = Read("MainWindow.xaml");
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(xaml, Attribute("Text", "CLYR"), System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)));
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(xaml, "CLYR-AppIcon-Master-1024.png", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)));
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(xaml, "<controls:PrivacyBadge", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)));
        Assert.DoesNotContain("NavigationView.PaneHeader", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PaneBranding", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedNavigationUsesSurfaceIndicatorAndTextEmphasis()
    {
        var shell = Read(Path.Combine("Styles", "Shell.xaml"));
        Assert.Contains("NavigationViewItemBackgroundSelected", shell, StringComparison.Ordinal);
        Assert.Contains("ResourceKey=\"SurfaceSelected\"", shell, StringComparison.Ordinal);
        Assert.Contains("NavigationViewSelectionIndicatorForeground", shell, StringComparison.Ordinal);
        Assert.Contains("x:Key=" + Quote + "NavigationViewSelectionIndicatorWidth" + Quote + ">3</x:Double>", shell, StringComparison.Ordinal);
        Assert.Contains("x:Key=" + Quote + "NavigationViewItemButtonMargin" + Quote + ">4,1</Thickness>", shell, StringComparison.Ordinal);
        Assert.Contains("CornerRadiusSmall", shell, StringComparison.Ordinal);
        Assert.Contains("BasedOn=" + Quote + "{StaticResource ShellNavigationItemStyle}" + Quote, shell, StringComparison.Ordinal);
        Assert.Contains("NavigationViewItemForegroundSelected", shell, StringComparison.Ordinal);
        Assert.Contains("UseSystemFocusVisuals", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactNavigationProvidesNamesDescriptionsAndTooltips()
    {
        var xaml = Read("MainWindow.xaml");
        foreach (var destination in new[] { "Overview", "Scan", "Results", "Review Plan", "History", "Developer Mode", "Privacy", "Licenses", "About" })
        {
            var itemStart = xaml.IndexOf(Attribute("Content", destination), StringComparison.Ordinal);
            Assert.True(itemStart >= 0, destination + " navigation item is missing.");
            var itemEnd = xaml.IndexOf("</NavigationViewItem>", itemStart, StringComparison.Ordinal);
            var item = xaml[itemStart..itemEnd];
            Assert.Contains("ToolTipService.ToolTip", item, StringComparison.Ordinal);
            Assert.Contains("AutomationProperties.Name", item, StringComparison.Ordinal);
            Assert.Contains("AutomationProperties.HelpText", item, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ShellUsesOnlySemanticDesignSystemColors()
    {
        var xaml = Read("MainWindow.xaml");
        foreach (var token in new[] { "NavigationBackground", "AppBackground", "BorderSubtle", "TextPrimary" })
            Assert.Contains($"{{ThemeResource {token}}}", xaml, StringComparison.Ordinal);
        Assert.DoesNotMatch("#[0-9A-Fa-f]{6,8}", xaml);

        var shell = Read(Path.Combine("Styles", "Shell.xaml"));
        foreach (var token in new[] { "SurfaceHover", "SurfaceSelected", "TextPrimary", "TextSecondary", "AccentPrimary" })
            Assert.Contains($"ResourceKey={Quote}{token}{Quote}", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellHasNoScrollerAndKeepsOneStablePageHost()
    {
        var xaml = Read("MainWindow.xaml");
        Assert.DoesNotContain("ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(xaml, "<ContentControl", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)));
        Assert.Contains(Attribute("x:Name", "ContentHost"), xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoPaneAndTokenizedBoundsSupportNarrowWindows()
    {
        var xaml = Read("MainWindow.xaml");
        var tokens = Read(Path.Combine("Styles", "DesignTokens.xaml"));
        foreach (var required in new[]
        {
            Attribute("PaneDisplayMode", "Auto"), Attribute("CompactModeThresholdWidth", "{StaticResource NavigationCompactModeThreshold}"),
            Attribute("ExpandedModeThresholdWidth", "{StaticResource NavigationExpandedModeThreshold}"), "NavigationPaneCompactWidth",
            "ShellMinimumWidth", "ShellMinimumHeight", Attribute("HorizontalContentAlignment", "Stretch")
        })
            Assert.Contains(required, xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HorizontalScroll", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=" + Quote + "SidebarWidth" + Quote + ">244</x:Double>", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=" + Quote + "NavigationPaneCompactWidth" + Quote + ">56</x:Double>", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=" + Quote + "NavigationCompactModeThreshold" + Quote + ">760</x:Double>", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=" + Quote + "NavigationExpandedModeThreshold" + Quote + ">1180</x:Double>", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=" + Quote + "ControlHeightNavigationRow" + Quote + ">44</x:Double>", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=" + Quote + "ShellNavigationIconSize" + Quote + ">20</x:Double>", tokens, StringComparison.Ordinal);
    }

    [Fact]
    public void PaneUsesNativeReducedMotionAwareTwoHundredMillisecondTransitions()
    {
        var shell = Read(Path.Combine("Styles", "Shell.xaml"));
        Assert.Contains("SplitViewPaneAnimationOpenDuration" + Quote + ">00:00:00.2</x:String>", shell, StringComparison.Ordinal);
        Assert.Contains("SplitViewPaneAnimationCloseDuration" + Quote + ">00:00:00.2</x:String>", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Storyboard.Begin", Read("MainWindow.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TitleBarUsesNativeCaptionButtonsAndThemeAwareInsets()
    {
        var xaml = Read("MainWindow.xaml");
        var code = Read("MainWindow.xaml.cs");
        Assert.Contains(Attribute("x:Name", "AppTitleBar"), xaml, StringComparison.Ordinal);
        Assert.Contains(Attribute("x:Name", "CaptionButtonSpacer"), xaml, StringComparison.Ordinal);
        Assert.Contains("ExtendsContentIntoTitleBar = true", code, StringComparison.Ordinal);
        Assert.Contains("SetTitleBar(AppTitleBar)", code, StringComparison.Ordinal);
        Assert.Contains("AppWindow.TitleBar.RightInset", code, StringComparison.Ordinal);
        Assert.Contains("ButtonHoverBackgroundColor", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptionButton Content", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedBrandAssetsConfigureWindowExecutableAndShellBranding()
    {
        var project = Read("Clyr.App.csproj");
        var xaml = Read("MainWindow.xaml");
        var code = Read("MainWindow.xaml.cs");
        var branding = Path.Combine(App, "Assets", "Branding");
        Assert.True(File.Exists(Path.Combine(branding, "CLYR-AppIcon.ico")));
        Assert.True(File.Exists(Path.Combine(branding, "CLYR-AppIcon-Master-1024.png")));
        var iconProjectPath = $"Assets{Path.DirectorySeparatorChar}Branding{Path.DirectorySeparatorChar}CLYR-AppIcon.ico";
        Assert.Contains($"<ApplicationIcon>{iconProjectPath}</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains("Assets/Branding/CLYR-AppIcon-Master-1024.png", xaml, StringComparison.Ordinal);
        Assert.Contains("AppWindow.SetIcon", code, StringComparison.Ordinal);
        Assert.Contains("CLYR-AppIcon.ico", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellContainsOnePrivacyBadgeAndNoPrivilegedOrMutationCapability()
    {
        var xaml = Read("MainWindow.xaml");
        var pageHeader = Read(Path.Combine("Controls", "PageHeader.xaml"));
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Count(xaml, "<controls:PrivacyBadge", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)));
        Assert.DoesNotContain("PrivacyBadge", pageHeader, StringComparison.Ordinal);

        var shell = xaml + Read("MainWindow.xaml.cs");
        foreach (var forbidden in new[]
        {
            "Process.Start", "ProcessStartInfo", "NamedPipe", "runas", "requireAdministrator",
            "File.Delete", "File.Move", "Directory.Delete", "Directory.Move", "IFileSystemEnumerator",
            "CleanupExecutor", "ElevatedHelperLauncher", "powershell.exe", "cmd.exe", "HttpClient"
        })
            Assert.DoesNotContain(forbidden, shell, StringComparison.OrdinalIgnoreCase);
    }

    private static string Attribute(string name, string value) => $"{name}={Quote}{value}{Quote}";
    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(App, relativePath));
}
