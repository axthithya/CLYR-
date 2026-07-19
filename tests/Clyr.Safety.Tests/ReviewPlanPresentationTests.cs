using System.Xml.Linq;

namespace Clyr.Safety.Tests;

public sealed class ReviewPlanPresentationTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string PagePath = Path.Combine(Root, "src", "Clyr.App", "Pages", "ReviewPlanPage.xaml");
    private static readonly string CodePath = PagePath + ".cs";

    [Fact]
    public void EmptyStateIsIntentionalAndSeparateFromReviewControls()
    {
        var document = XDocument.Load(PagePath);
        var empty = Named(document, "EmptyPanel");
        var review = Named(document, "ReviewStage");
        Assert.Equal("Collapsed", Attribute(empty, "Visibility"));
        Assert.Contains("No actions to review", empty.ToString(), StringComparison.Ordinal);
        Assert.Contains("Run analysis", empty.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("StatusFilter", empty.ToString(), StringComparison.Ordinal);
        Assert.Equal("Collapsed", Attribute(review, "Visibility"));
    }

    [Fact]
    public void SelectionUsesExistingEligibilityAndStartsSafe()
    {
        var code = File.ReadAllText(CodePath);
        Assert.Contains("CleanupEligibility.DryRunEligible", code, StringComparison.Ordinal);
        Assert.Contains("IsEnabled = eligible", code, StringComparison.Ordinal);
        Assert.Contains("selectedFindingIds.Clear()", code, StringComparison.Ordinal);
        Assert.Contains("candidates.Where(IsEligible)", code, StringComparison.Ordinal);
        Assert.Contains("ReviewSelectedButton.IsEnabled = selected.Length > 0", code, StringComparison.Ordinal);
        Assert.Contains("Protected and unsupported items cannot be selected", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EstimatesAndSafetyStatesUseTruthfulText()
    {
        var combined = File.ReadAllText(PagePath) + File.ReadAllText(CodePath);
        foreach (var required in new[]
        {
            "Estimated potential", "not guaranteed reclaimed space", "Recommended", "Review required",
            "Protected", "Unsupported", "Protected by CLYR", "excluded"
        })
            Assert.Contains(required, combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Guaranteed space", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReviewAndConfirmationRemainDeliberate()
    {
        var combined = File.ReadAllText(PagePath) + File.ReadAllText(CodePath);
        foreach (var required in new[]
        {
            "Review selected actions", "Dry-run only", "selected actions",
            "Protected and unsupported locations remain excluded", "IsPrimaryButtonEnabled = false",
            "I understand that selected cache or temporary data may be permanently removed.",
            "DefaultButton = ContentDialogButton.Close", "CloseButtonText"
        })
            Assert.Contains(required, combined, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedStateDoesNotExposeRawExceptionOrTargetIdentity()
    {
        var code = File.ReadAllText(CodePath);
        Assert.Contains("catch (InvalidOperationException)", code, StringComparison.Ordinal);
        Assert.Contains("The plan could not pass the current safety checks", code, StringComparison.Ordinal);
        Assert.DoesNotContain("exception.Message", code, StringComparison.Ordinal);
        Assert.DoesNotContain("result.TargetId", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponsivePageHasOneScrollOwnerAndNoPrivilegedCapability()
    {
        var xaml = File.ReadAllText(PagePath);
        var code = File.ReadAllText(CodePath);
        Assert.DoesNotContain("<ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.Contains("ResponsivePageHost", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow", code, StringComparison.Ordinal);
        Assert.Contains("Orientation.Vertical", code, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "Process.Start", "powershell.exe", "cmd.exe", "Directory.Enumerate", "File.Delete",
            "File.Move", "File.SetAccessControl", "runas", "HttpClient", "Socket"
        })
            Assert.DoesNotContain(forbidden, code, StringComparison.OrdinalIgnoreCase);
    }

    private static XElement Named(XDocument document, string name) => document.Descendants()
        .Single(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name));

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == localName)?.Value;
}
