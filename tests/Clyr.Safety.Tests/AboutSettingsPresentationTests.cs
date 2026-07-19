using System.Security.Cryptography;
using System.Xml.Linq;

namespace Clyr.Safety.Tests;

public sealed class AboutSettingsPresentationTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string AboutPath = Path.Combine(Root, "src", "Clyr.App", "Pages", "AboutPage.xaml");
    private static readonly string SettingsPath = Path.Combine(Root, "src", "Clyr.App", "Pages", "SettingsPage.xaml");

    [Fact]
    public void AboutUsesTheActualProportionalAccessibleApplicationIcon()
    {
        var document = XDocument.Load(AboutPath);
        var identity = Named(document, "IdentityGrid").ToString();
        Assert.Contains("CLYR-AppIcon-Master-1024.png", identity, StringComparison.Ordinal);
        Assert.Contains("Width=\"88\" Height=\"88\" Stretch=\"Uniform\"", identity, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"CLYR application icon\"", identity, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"C:\"", identity, StringComparison.Ordinal);
        Assert.Equal("FC7F7B57AFD235396232332A033E9BEA602B42C4BA51EDC41D67D899D3654AAE",
            Hash(Path.Combine("src", "Clyr.App", "Assets", "Branding", "CLYR-AppIcon-Master-1024.png")));
        Assert.Equal("09E147B9E8A437838939B8E3385C7DA56122D697FF5CA4E31A42CE9358BA7746",
            Hash(Path.Combine("src", "Clyr.App", "Assets", "Branding", "CLYR-AppIcon.ico")));
    }

    [Fact]
    public void AboutUsesRuntimeVersionAndDoesNotHardCodeTheExample()
    {
        var xaml = File.ReadAllText(AboutPath);
        var code = File.ReadAllText(AboutPath + ".cs");
        Assert.Contains("Recorded(viewModel.Version)", code, StringComparison.Ordinal);
        Assert.Contains("RuntimeInformation.ProcessArchitecture", code, StringComparison.Ordinal);
        Assert.Contains("RuntimeInformation.FrameworkDescription", code, StringComparison.Ordinal);
        Assert.DoesNotContain("0.7.0-phase7", xaml + code, StringComparison.Ordinal);
        Assert.Contains("VersionText.Text = \"Version \" + version", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AboutLicenseProjectPrivacyAndSafetyClaimsAreRepositoryBacked()
    {
        var xaml = File.ReadAllText(AboutPath);
        foreach (var required in new[]
        {
            "Apache-2.0", "Copyright 2026 CLYR contributors", "Repository URL", "Not recorded",
            "Quick and Deep Analysis", "filesystem metadata", "never performs silent cleanup",
            "Analysis runs on this computer", "rather than file contents", "Scanning does not delete, move, rename or change files"
        })
            Assert.Contains(required, xaml, StringComparison.Ordinal);
        Assert.Contains("Apache License", File.ReadAllText(Path.Combine(Root, "LICENSE")), StringComparison.Ordinal);
        Assert.DoesNotContain("support@", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("donate", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AboutUsesExistingInAppNavigationAndNoExternalLaunch()
    {
        var xaml = File.ReadAllText(AboutPath);
        var code = File.ReadAllText(AboutPath + ".cs");
        Assert.Contains("View Privacy details", xaml, StringComparison.Ordinal);
        Assert.Contains("Open Privacy page", xaml, StringComparison.Ordinal);
        Assert.Contains("Open Licenses page", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.Navigate(\"Privacy\")", code, StringComparison.Ordinal);
        Assert.Contains("ViewModel.Navigate(\"Licenses\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Launcher.Launch", code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AboutStacksItsMajorSurfacesAndHasOneScrollOwner()
    {
        var xaml = File.ReadAllText(AboutPath);
        var code = File.ReadAllText(AboutPath + ".cs");
        Assert.DoesNotContain("<ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.Contains("ReflowPair(AboutSummaryGrid", code, StringComparison.Ordinal);
        Assert.Contains("ReflowPair(NavigationGrid", code, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(ProductCopy, narrow ? 1 : 0)", code, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(values[index], narrow ? index * 2 + 1 : index)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsShowsOnlyTheTwoSupportedPreferenceGroups()
    {
        var xaml = File.ReadAllText(SettingsPath);
        Assert.Contains("Appearance settings", xaml, StringComparison.Ordinal);
        Assert.Contains("History and local data settings", xaml, StringComparison.Ordinal);
        Assert.Contains("System", xaml, StringComparison.Ordinal);
        Assert.Contains("Light", xaml, StringComparison.Ordinal);
        Assert.Contains("Dark", xaml, StringComparison.Ordinal);
        Assert.Contains("Save analysis history", xaml, StringComparison.Ordinal);
        Assert.Contains("Analyses retained per drive", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Privacy-safe export by default", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Reduce nonessential motion", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Developer options", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("default scan mode", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettingsLoadsAndSavesThroughTheExistingViewModelBoundary()
    {
        var page = File.ReadAllText(SettingsPath + ".cs");
        var viewModel = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "ViewModels", "HistoryViewModel.cs"));
        Assert.Contains("await ViewModel.LoadAsync()", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.History.IsEnabled", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.History.RetentionPerDrive", page, StringComparison.Ordinal);
        Assert.Contains("await ViewModel.SaveHistoryAsync(HistoryEnabled.IsOn, (int)Retention.Value)", page, StringComparison.Ordinal);
        Assert.Contains("await store.SetSettingsAsync(History)", viewModel, StringComparison.Ordinal);
        Assert.Contains("public Task<int> ClearHistoryAsync() => store.ClearAsync()", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("new SqliteSnapshotStore", page, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeAndRestartWordingMatchExistingImmediateNonPersistentBehavior()
    {
        var xaml = File.ReadAllText(SettingsPath);
        var code = File.ReadAllText(SettingsPath + ".cs");
        Assert.Contains("root.RequestedTheme", code, StringComparison.Ordinal);
        Assert.Contains("does not require restart", xaml, StringComparison.Ordinal);
        Assert.Contains("Theme selection is not saved by this preview", xaml, StringComparison.Ordinal);
        Assert.Contains("Theme applied to this window. No restart is required.", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Restart required", xaml + code, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearHistoryConfirmationIsDeliberateAndCancelIsSafeDefault()
    {
        var code = File.ReadAllText(SettingsPath + ".cs");
        Assert.Contains("Title = \"Clear local history?\"", code, StringComparison.Ordinal);
        Assert.Contains("CloseButtonText = \"Cancel\"", code, StringComparison.Ordinal);
        Assert.Contains("DefaultButton = ContentDialogButton.Close", code, StringComparison.Ordinal);
        Assert.Contains("does not delete files, settings, Review Plans or execution receipts", code, StringComparison.Ordinal);
        Assert.Contains("if (await dialog.ShowAsync() != ContentDialogResult.Primary) return", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Restore defaults", File.ReadAllText(SettingsPath), StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsUnavailableAndErrorStatesAreBoundedAndAccessible()
    {
        var xaml = File.ReadAllText(SettingsPath);
        var code = File.ReadAllText(SettingsPath + ".cs");
        Assert.Contains("AutomationProperties.HelpText", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SetHistoryControlsAvailable(false)", code, StringComparison.Ordinal);
        Assert.Contains("Local history settings could not be loaded, so this control is unavailable.", code, StringComparison.Ordinal);
        Assert.Contains("Your previous saved values remain in effect", code, StringComparison.Ordinal);
        Assert.DoesNotContain("exception.Message", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettingsCannotWeakenSafetyBoundariesOrInventResetBehavior()
    {
        var combined = File.ReadAllText(SettingsPath) + File.ReadAllText(SettingsPath + ".cs");
        Assert.Contains("Safety boundaries are not settings", combined, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "auto-cleanup", "automatically delete", "auto-elevate", "bypass protected", "skip confirmation",
            "unrestricted cleanup", "unrestricted scan", "retry all restricted", "ResetSettings", "RestoreDefaults"
        })
            Assert.DoesNotContain(forbidden, combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettingsRowsStackWithoutNestedPageScrolling()
    {
        var xaml = File.ReadAllText(SettingsPath);
        var code = File.ReadAllText(SettingsPath + ".cs");
        Assert.DoesNotContain("<ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.Contains("ReflowSettingRow(ThemeSettingRow", code, StringComparison.Ordinal);
        Assert.Contains("ReflowSettingRow(HistoryEnabledRow", code, StringComparison.Ordinal);
        Assert.Contains("ReflowSettingRow(RetentionSettingRow", code, StringComparison.Ordinal);
        Assert.Contains("SettingsActions.Orientation = narrow ? Orientation.Vertical", code, StringComparison.Ordinal);
        Assert.Contains("control.HorizontalAlignment = stack ? HorizontalAlignment.Stretch", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AboutAndSettingsIntroduceNoPrivilegedMutationNetworkOrTelemetryCapability()
    {
        var combined = string.Concat(
            File.ReadAllText(AboutPath), File.ReadAllText(AboutPath + ".cs"),
            File.ReadAllText(SettingsPath), File.ReadAllText(SettingsPath + ".cs"));
        foreach (var forbidden in new[]
        {
            "Process.Start", "ProcessStartInfo", "powershell.exe", "cmd.exe", "NamedPipe", "runas",
            "Directory.Enumerate", "Directory.GetFiles", "File.Delete", "File.Move", "HttpClient", "Socket",
            "TelemetryClient", "CleanupExecutor", "RegistryKey", "SetAccessControl", "TakeOwnership", "PackageReference"
        })
            Assert.DoesNotContain(forbidden, combined, StringComparison.OrdinalIgnoreCase);
    }

    private static string Hash(string relativePath) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Root, relativePath))));

    private static XElement Named(XDocument document, string name) => document.Descendants()
        .Single(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name));
}
