using System.Security.Cryptography;
using System.Xml.Linq;

namespace Clyr.Safety.Tests;

public sealed class LicensesPresentationTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string PagePath = Path.Combine(Root, "src", "Clyr.App", "Pages", "LicensesPage.xaml");
    private static readonly string CodePath = PagePath + ".cs";

    [Fact]
    public void HeaderAndApplicationSummaryUseRecordedRepositoryValues()
    {
        var xaml = File.ReadAllText(PagePath);
        var code = File.ReadAllText(CodePath);
        var license = File.ReadAllText(Path.Combine(Root, "LICENSE"));
        Assert.Contains("Review CLYR’s license and third-party open-source notices.", xaml, StringComparison.Ordinal);
        Assert.Contains("Apache License 2.0", xaml, StringComparison.Ordinal);
        Assert.Contains("Apache-2.0", xaml, StringComparison.Ordinal);
        Assert.Contains("Copyright 2026 CLYR contributors", xaml, StringComparison.Ordinal);
        Assert.Contains("viewModel.Session.ApplicationVersion", code, StringComparison.Ordinal);
        Assert.Contains("Apache License", license, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyRecordsMatchTheRecordedInventory()
    {
        var code = File.ReadAllText(CodePath);
        var inventory = File.ReadAllText(Path.Combine(Root, "docs", "DEPENDENCY_INVENTORY.md"));
        foreach (var value in new[]
        {
            "Microsoft.Data.Sqlite.Core", "10.0.9", "SQLitePCLRaw.bundle_e_sqlite3", "3.0.3",
            "Microsoft.Windows.SDK.BuildTools", "10.0.28000.2270", "Microsoft.WindowsAppSDK", "2.2.0",
            "YamlDotNet", "18.1.0", "JsonSchema.Net", "7.4.0", "Microsoft.NET.Test.Sdk", "18.7.0",
            "xunit.runner.visualstudio", "3.1.5", "coverlet.collector", "10.0.1", "SourceGear.sqlite3", "3.50.4.5"
        })
        {
            Assert.Contains(value, code, StringComparison.Ordinal);
            Assert.Contains(value, inventory, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UnknownMetadataIsRecordedCalmlyAndNeverCalledUnlicensed()
    {
        var combined = File.ReadAllText(PagePath) + File.ReadAllText(CodePath);
        Assert.Contains("Not recorded", combined, StringComparison.Ordinal);
        Assert.Contains("Metadata not recorded", combined, StringComparison.Ordinal);
        Assert.Contains("must not be interpreted as an absence of license requirements", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("unlicensed", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("public domain when unknown", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SearchAndFiltersUseOnlyRealPackageAndLicenseMetadata()
    {
        var code = File.ReadAllText(CodePath);
        var xaml = File.ReadAllText(PagePath);
        Assert.Contains("entry.Package.Contains(query", code, StringComparison.Ordinal);
        Assert.DoesNotContain("entry.Summary.Contains(query", code, StringComparison.Ordinal);
        foreach (var category in new[] { "All licenses", "MIT", "Apache-2.0", "Microsoft terms", "SQLite notice" })
            Assert.Contains(category, xaml, StringComparison.Ordinal);
        Assert.Contains("entry.License.Equals(\"MIT\"", code, StringComparison.Ordinal);
        Assert.Contains("entry.License.Equals(\"Apache-2.0\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Unknown/Not recorded", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("bundled license text filter", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExactLicenseAndNoticeFilesRemainUnchangedAndAreNotRewrittenInPresentationCode()
    {
        Assert.Equal("D86D6ADA47E864C6C2A892E7355ED3234C42197BADB97FCEEAD05032C97D0039", Hash("LICENSE"));
        Assert.Equal("5C996923FC7310C04489E483865F690362204638CEEFE34A481B6F4E6619C6D1", Hash("THIRD_PARTY_NOTICES.md"));
        var combined = File.ReadAllText(PagePath) + File.ReadAllText(CodePath);
        Assert.DoesNotContain("TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION", combined, StringComparison.Ordinal);
        Assert.Contains("not rewritten on this page", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailSurfacePreservesSelectableExactValuesAndTruthfulTextAvailability()
    {
        var document = XDocument.Load(PagePath);
        var detail = Named(document, "LicenseDetailSurface").ToString();
        foreach (var name in new[] { "DetailPackage", "DetailVersion", "DetailLicense", "DetailType", "DetailCopyright", "DetailProject" })
            Assert.Contains("x:Name=\"" + name + "\"", detail, StringComparison.Ordinal);
        Assert.Contains("IsTextSelectionEnabled=\"True\"", detail, StringComparison.Ordinal);
        Assert.Contains("Selectable full license text", detail, StringComparison.Ordinal);
        Assert.Contains("License text is not bundled for this component", detail, StringComparison.Ordinal);
        Assert.Contains("Copy full bundled license text", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void CopyFeedbackIsDescriptiveTemporaryAndPolite()
    {
        var xaml = File.ReadAllText(PagePath);
        var code = File.ReadAllText(CodePath);
        Assert.Contains("Copy selected package name", xaml, StringComparison.Ordinal);
        Assert.Contains("Copy selected license identifier", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(1600)", code, StringComparison.Ordinal);
        Assert.Contains("button.Content = \"Copied\"", code, StringComparison.Ordinal);
        Assert.Contains("CopyStatus.Text = string.Empty", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyAndUnavailableStatesRemainVisibleAndHideIrrelevantWorkspace()
    {
        var xaml = File.ReadAllText(PagePath);
        var code = File.ReadAllText(CodePath);
        Assert.Contains("No third-party license records are available", xaml, StringComparison.Ordinal);
        Assert.Contains("No licenses match these filters", xaml, StringComparison.Ordinal);
        Assert.Contains("InventoryContent.Visibility = available ? Visibility.Visible : Visibility.Collapsed", code, StringComparison.Ordinal);
        Assert.Contains("LicenseWorkspace.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed", code, StringComparison.Ordinal);
        Assert.Contains("NoResultsState.Visibility = hasResults ? Visibility.Collapsed : Visibility.Visible", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponsiveLayoutStacksFiltersRowsAndDetailsWithoutNestedPageScrollViewer()
    {
        var xaml = File.ReadAllText(PagePath);
        var code = File.ReadAllText(CodePath);
        Assert.DoesNotContain("<ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxHeight=", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("NarrowLicenseTemplate", xaml, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", xaml, StringComparison.Ordinal);
        Assert.Contains("var stackWorkspace = mode != ResponsivePageWidth.Wide", code, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(LicenseDetailSurface, stackWorkspace ? 1 : 0)", code, StringComparison.Ordinal);
        Assert.Contains("DetailActions.Orientation = narrow ? Orientation.Vertical", code, StringComparison.Ordinal);
    }

    [Fact]
    public void LicensesPresentationIntroducesNoExternalPrivilegedOrPackageCapability()
    {
        var combined = File.ReadAllText(PagePath) + File.ReadAllText(CodePath);
        foreach (var forbidden in new[]
        {
            "Process.Start", "ProcessStartInfo", "powershell.exe", "cmd.exe", "NamedPipe", "runas",
            "Directory.Enumerate", "Directory.GetFiles", "File.Delete", "File.Move", "HttpClient", "Socket",
            "PackageReference", "dotnet add package", "TelemetryClient", "CleanupExecutor", "RegistryKey"
        })
            Assert.DoesNotContain(forbidden, combined, StringComparison.OrdinalIgnoreCase);
    }

    private static string Hash(string relativePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Root, relativePath));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static XElement Named(XDocument document, string name) => document.Descendants()
        .Single(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name));
}
