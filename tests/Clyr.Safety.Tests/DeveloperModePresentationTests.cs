using System.Xml.Linq;

namespace Clyr.Safety.Tests;

public sealed class DeveloperModePresentationTests
{
    private const char Quote = '"';
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string PagePath = Path.Combine(Root, "src", "Clyr.App", "Pages", "DeveloperModePage.xaml");
    private static readonly string CodePath = PagePath + ".cs";
    private static readonly string ViewModelPath = Path.Combine(Root, "src", "Clyr.App", "ViewModels", "AppSessionViewModel.cs");

    [Fact]
    public void HeaderAndAdvancedBoundaryUseTheRequiredCalmLanguage()
    {
        var xaml = File.ReadAllText(PagePath);
        Assert.Contains("Title=" + Quote + "Developer Mode" + Quote, xaml, StringComparison.Ordinal);
        Assert.Contains("Subtitle=" + Quote + "Inspect advanced scan, accounting and diagnostic information." + Quote, xaml, StringComparison.Ordinal);
        Assert.Contains("Advanced", xaml, StringComparison.Ordinal);
        Assert.Contains("intended for troubleshooting and verification", xaml, StringComparison.Ordinal);
        Assert.Contains("does not enable unsafe cleanup or unrestricted system access", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void NoResultStateIsIntentionalAndHidesTechnicalViewers()
    {
        var document = XDocument.Load(PagePath);
        var empty = Named(document, "NoDataState");
        var content = Named(document, "DeveloperContent");
        Assert.Equal("Collapsed", Attribute(empty, "Visibility"));
        Assert.Equal("Collapsed", Attribute(content, "Visibility"));
        Assert.Contains("No diagnostic data available", empty.ToString(), StringComparison.Ordinal);
        Assert.Contains("Run an analysis to populate Developer Mode.", empty.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SummaryGrid", empty.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("TechnicalSections", empty.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SummaryAndTechnicalSectionsUseExistingSnapshotValues()
    {
        var combined = File.ReadAllText(PagePath) + File.ReadAllText(CodePath) + File.ReadAllText(ViewModelPath);
        foreach (var required in new[]
        {
            "Current diagnostic summary", "Scan availability", "Analysis type", "Completion status",
            "Accounted storage", "Files examined", "Directories examined", "Warning count", "Snapshot schema",
            "Scan execution", "Storage accounting", "Classification", "Warnings and limitations", "Report metadata",
            "SelectedSnapshot", "snapshot.Coverage.FilesObserved", "snapshot.Coverage.DirectoriesObserved"
        })
            Assert.Contains(required, combined, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=" + Quote + "True" + Quote, File.ReadAllText(PagePath), StringComparison.Ordinal);
        Assert.Contains("Visibility = hasClassification ? Visibility.Visible : Visibility.Collapsed", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeValuesUseThemeAwareMonospaceCopyWithTemporaryFeedback()
    {
        var combined = File.ReadAllText(PagePath) + File.ReadAllText(CodePath);
        foreach (var required in new[]
        {
            "MonospaceDetailStyle", "TextWrapping = TextWrapping.Wrap", "ToolTipService.SetToolTip",
            "AutomationProperties.SetName", "Clipboard.SetContent", "diagnostic summary",
            "button.Content = " + Quote + "Copied" + Quote, "TimeSpan.FromMilliseconds(1600)",
            "AutomationProperties.LiveSetting=" + Quote + "Polite" + Quote
        })
            Assert.Contains(required, combined, StringComparison.Ordinal);
        Assert.DoesNotContain("exception.Message", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("exception.ToString", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void WarningsAreAggregatedTextualAndLegacyFieldsDoNotBecomeFakeZeros()
    {
        var code = File.ReadAllText(CodePath);
        Assert.Contains("GroupBy(value => value, StringComparer.Ordinal)", code, StringComparison.Ordinal);
        Assert.Contains("⚠ Warning", code, StringComparison.Ordinal);
        Assert.Contains("DiagnosticSeverity", code, StringComparison.Ordinal);
        Assert.Contains("SafeDiagnosticText", code, StringComparison.Ordinal);
        Assert.Contains("Not recorded", code, StringComparison.Ordinal);
        Assert.Contains("Legacy format", code, StringComparison.Ordinal);
        Assert.Contains("missingWarningDetails", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingDetectionAndPlanReviewRemainReachableWithoutInventedExport()
    {
        var pageAndCode = File.ReadAllText(PagePath) + File.ReadAllText(CodePath);
        var combined = pageAndCode + File.ReadAllText(ViewModelPath);
        var scanExport = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Core", "ScanExport.cs"));
        Assert.Contains("Detect developer tools", combined, StringComparison.Ordinal);
        Assert.Contains("View details", combined, StringComparison.Ordinal);
        Assert.Contains("Review in plan", combined, StringComparison.Ordinal);
        Assert.Contains("DeveloperToolRegistry.DetectAllAsync", combined, StringComparison.Ordinal);
        Assert.Contains("CleanupPlanBuilder.Create", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", pageAndCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Export privacy-safe", pageAndCode, StringComparison.Ordinal);
        Assert.Contains("Redact", scanExport, StringComparison.Ordinal);
    }

    [Fact]
    public void DeveloperModeUsesOneScrollOwnerAndContainsNoPrivilegedOrMutationCapability()
    {
        var xaml = File.ReadAllText(PagePath);
        var code = File.ReadAllText(CodePath);
        var combined = xaml + code;
        Assert.Contains("ResponsivePageHost", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ListView", xaml, StringComparison.Ordinal);
        Assert.Contains("ReflowTechnicalRow", code, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(value, narrow ? 1 : 0)", code, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "Process.Start", "powershell.exe", "cmd.exe", "ShellExecute", "NamedPipe", "runas",
            "Directory.Enumerate", "File.Delete", "File.Move", "File.SetAccessControl", "RegistryKey",
            "HttpClient", "Socket", "Clean now", "Prune", "Execute tool", "Uninstall tool"
        })
            Assert.DoesNotContain(forbidden, combined, StringComparison.OrdinalIgnoreCase);
    }

    private static XElement Named(XDocument document, string name) => document.Descendants()
        .Single(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name));

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == localName)?.Value;
}
