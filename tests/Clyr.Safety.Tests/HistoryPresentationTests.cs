using System.Xml.Linq;

namespace Clyr.Safety.Tests;

public sealed class HistoryPresentationTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string PagePath = Path.Combine(Root, "src", "Clyr.App", "Pages", "HistoryPage.xaml");
    private static readonly string CodePath = PagePath + ".cs";
    private static readonly string ViewModelPath = Path.Combine(Root, "src", "Clyr.App", "ViewModels", "HistoryViewModel.cs");

    [Fact]
    public void EmptyStateIsIntentionalAndHidesHistoryControls()
    {
        var document = XDocument.Load(PagePath);
        var empty = Named(document, "HistoryEmpty");
        var content = Named(document, "HistoryContent");
        Assert.Equal("Collapsed", Attribute(empty, "Visibility"));
        Assert.Equal("Collapsed", Attribute(content, "Visibility"));
        Assert.Contains("No analysis history yet", empty.ToString(), StringComparison.Ordinal);
        Assert.Contains("Completed Quick and Deep analyses will appear here.", empty.ToString(), StringComparison.Ordinal);
        Assert.Contains("Run an analysis", empty.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("ModeFilter", empty.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("History summary", empty.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ModesStatusesFiltersAndSortingUsePlainExistingData()
    {
        var combined = File.ReadAllText(PagePath) + File.ReadAllText(CodePath);
        foreach (var required in new[]
        {
            "Quick", "Deep", "Completed", "Completed with warnings", "Cancelled", "Failed",
            "Newest first", "Oldest first", "Highest coverage", "ModeFilter", "StatusFilter",
            "OrderByDescending(item => item.CapturedAtUtc)", "item.Mode == ScanMode.Quick"
        })
            Assert.Contains(required, combined, StringComparison.Ordinal);
        Assert.DoesNotContain("CompletedWithWarnings", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordRowsSeparateCoverageWarningsAndLegacyFallbacks()
    {
        var code = File.ReadAllText(CodePath);
        foreach (var required in new[]
        {
            "Accounted", "Observed storage", "Unobserved storage", "Warnings", "Files examined",
            "Directories examined", "Duration", "Scan quality", "Classified observed storage",
            "Warning count", "Not recorded", "Some details were not recorded for this older analysis"
        })
            Assert.Contains(required, code, StringComparison.Ordinal);
        Assert.Contains("detail.Warnings.Count", code, StringComparison.Ordinal);
        // Phase (post-Administrator-Retry accounting correction): now passes a derived AccountingConsistency so
        // a logical-exceeds-drive-used history record shows "Coverage unavailable" rather than "Insufficient
        // coverage" — see ConsistencyFor.
        Assert.Contains("ScanAccounting.QualityFor(accounted, ConsistencyFor(item))", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultOpeningUsesOnlyTheMatchingInMemoryResult()
    {
        var page = File.ReadAllText(CodePath);
        var viewModel = File.ReadAllText(ViewModelPath);
        Assert.Contains("IsEnabled = ViewModel.CanOpenResult(item.Id)", page, StringComparison.Ordinal);
        Assert.Contains("Detail(id)?.ScanId", viewModel, StringComparison.Ordinal);
        Assert.Contains("Session.Result?.ScanId == scanId", viewModel, StringComparison.Ordinal);
        Assert.Contains("Navigate(", viewModel, StringComparison.Ordinal);
        Assert.Contains("Results", viewModel, StringComparison.Ordinal);
        Assert.Contains("aggregate history only", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Session.Result =", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void ComparisonUsesExistingAggregatesWithoutInventedExport()
    {
        var combined = File.ReadAllText(PagePath) + File.ReadAllText(CodePath) + File.ReadAllText(ViewModelPath);
        Assert.Contains("SnapshotComparer.Compare", combined, StringComparison.Ordinal);
        Assert.Contains("Select two analyses", combined, StringComparison.Ordinal);
        Assert.Contains("observations, not proof", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Not comparable", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Export history", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Scan ID", File.ReadAllText(PagePath), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Snapshot ID", File.ReadAllText(PagePath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HistoryUsesOneScrollOwnerAndContainsNoPrivilegedCapability()
    {
        var xaml = File.ReadAllText(PagePath);
        var code = File.ReadAllText(CodePath);
        Assert.DoesNotContain("<ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ListView", xaml, StringComparison.Ordinal);
        Assert.Contains("ResponsivePageHost", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow", code, StringComparison.Ordinal);
        Assert.Contains("Orientation.Vertical", code, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "Process.Start", "powershell.exe", "cmd.exe", "Directory.Enumerate", "File.Delete",
            "File.Move", "File.SetAccessControl", "runas", "NamedPipe", "HttpClient", "Socket",
            "DeleteSelected", "ClearHistory", "cleanup"
        })
            Assert.DoesNotContain(forbidden, xaml + code, StringComparison.OrdinalIgnoreCase);
    }

    private static XElement Named(XDocument document, string name) => document.Descendants()
        .Single(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name));

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == localName)?.Value;
}
