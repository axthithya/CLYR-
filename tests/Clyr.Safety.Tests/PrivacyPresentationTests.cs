using System.Xml.Linq;

namespace Clyr.Safety.Tests;

public sealed class PrivacyPresentationTests
{
    private const char Quote = '"';
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string PagePath = Path.Combine(Root, "src", "Clyr.App", "Pages", "PrivacyPage.xaml");
    private static readonly string CodePath = PagePath + ".cs";

    [Fact]
    public void HeaderSummaryAndThreeTruthsAreClearAndLocal()
    {
        var xaml = File.ReadAllText(PagePath);
        Assert.Contains("Title=" + Quote + "Privacy" + Quote, xaml, StringComparison.Ordinal);
        Assert.Contains("Subtitle=" + Quote + "Understand what CLYR reads, stores and never sends." + Quote, xaml, StringComparison.Ordinal);
        foreach (var required in new[]
        {
            "Your storage analysis stays on this device", "Analysis runs on this PC.", "Metadata only",
            "Read-only by default", "does not upload your scan data", "Analysis never deletes, moves or changes files."
        })
            Assert.Contains(required, xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadAndDoesNotReadClaimsMatchTheMetadataContract()
    {
        var document = XDocument.Load(PagePath);
        var boundaries = Named(document, "ReadGrid").ToString();
        var scanning = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Core", "Scanning.cs"));
        foreach (var required in new[]
        {
            "Names and paths", "Logical and allocated sizes", "reparse points and cloud placeholders",
            "Hard-link identity", "sparse or compressed", "access-denied or unavailable status",
            "does not open files to understand their contents", "Document text", "Photo pixels",
            "Passwords or account credentials", "Browser history, messages or email contents"
        })
            Assert.Contains(required, boundaries, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("timestamps", boundaries, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FileSystemEntry(string FullPath, long LogicalBytes, EntryTraits Traits", scanning, StringComparison.Ordinal);
        Assert.Contains("long? AllocatedBytes", scanning, StringComparison.Ordinal);
        Assert.Contains("ulong? FileIdentity", scanning, StringComparison.Ordinal);
        Assert.Contains("neither ever opens or reads file content", scanning, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalDataFlowHasFourAccessibleStagesAndNoUploadStage()
    {
        var document = XDocument.Load(PagePath);
        var flow = Named(document, "DataFlowGrid").ToString();
        foreach (var required in new[]
        {
            "read metadata locally", "analyze storage usage", "show local results", "export only when requested"
        })
            Assert.Contains(required, flow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Upload", flow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("There is no upload stage", File.ReadAllText(PagePath), StringComparison.Ordinal);
    }

    [Fact]
    public void AdministratorRetryExplanationMatchesTheExistingBoundaries()
    {
        var xaml = File.ReadAllText(PagePath);
        var service = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Core", "ElevatedScanRetryService.cs"));
        var reconciler = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Core", "ElevatedScanResultReconciler.cs"));
        foreach (var required in new[]
        {
            "Administrator retry is optional", "eligible Deep Analysis", "Windows shows UAC",
            "metadata-only and read-only", "original scan remains unchanged", "does not show an automatic second UAC prompt"
        })
            Assert.Contains(required, xaml, StringComparison.Ordinal);
        Assert.Contains("QuickAnalysisNotEligible", service, StringComparison.Ordinal);
        Assert.Contains("OriginalResult", reconciler, StringComparison.Ordinal);
        Assert.Contains("untouched object", reconciler, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportPrivacyIsTruthfulAndDoesNotPromiseAnonymity()
    {
        var xaml = File.ReadAllText(PagePath);
        var exporter = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Core", "ScanExport.cs"));
        foreach (var required in new[]
        {
            "Reports are created only when requested", "does not promise perfect anonymization",
            "Review every report before sharing it", "storage categories, sizes, timestamps and technical metadata"
        })
            Assert.Contains(required, xaml, StringComparison.Ordinal);
        Assert.Contains("fullPathsIncluded = false", exporter, StringComparison.Ordinal);
        Assert.Contains("userNamesIncluded = false", exporter, StringComparison.Ordinal);
        Assert.Contains("fileContentsIncluded = false", exporter, StringComparison.Ordinal);
        Assert.Contains("RedactContributions", exporter, StringComparison.Ordinal);
        Assert.DoesNotContain("perfectly anonymous", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CleanupIsSeparateAndPrivacyPageContainsNoAction()
    {
        var xaml = File.ReadAllText(PagePath);
        foreach (var required in new[]
        {
            "Analysis and cleanup are separate", "Analysis is read-only", "Review Plan is a separate step",
            "protected and unsupported locations remain excluded", "final confirmation",
            "Estimated reclaimable space is not guaranteed", "No cleanup can start from the Privacy page"
        })
            Assert.Contains(required, xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Button", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Click=", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalStorageAndAdvancedDetailsAvoidUnsupportedClaims()
    {
        var xaml = File.ReadAllText(PagePath);
        foreach (var required in new[]
        {
            "When history is enabled", "aggregate scan summaries locally", "does not store file contents in history",
            "Reports exist only when explicitly requested", "Advanced privacy details",
            "allocated size, file identity or protected entries are recorded as unavailable rather than invented",
            "Cloud placeholders are not opened to download their contents", "no scan-data upload or telemetry service"
        })
            Assert.Contains(required, xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("database path", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("always anonymous", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResponsivePrivacySurfaceUsesOneScrollOwnerAndNoCapabilityCode()
    {
        var xaml = File.ReadAllText(PagePath);
        var code = File.ReadAllText(CodePath);
        var combined = xaml + code;
        Assert.Contains("ResponsivePageHost", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ListView", xaml, StringComparison.Ordinal);
        Assert.Contains("ReflowFlow", code, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(items[index], narrow ? index : 0)", code, StringComparison.Ordinal);
        Assert.Contains("arrow.Visibility = narrow ? Visibility.Collapsed", code, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "Process.Start", "NamedPipe", "runas", "powershell.exe", "cmd.exe", "Directory.Enumerate",
            "File.Delete", "File.Move", "File.SetAccessControl", "RegistryKey", "HttpClient", "Socket",
            "TelemetryClient", "TrackEvent", "CleanupExecutor", "RequestAdministratorRetry"
        })
            Assert.DoesNotContain(forbidden, combined, StringComparison.OrdinalIgnoreCase);
    }

    private static XElement Named(XDocument document, string name) => document.Descendants()
        .Single(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name));
}
