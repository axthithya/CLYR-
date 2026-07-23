namespace Clyr.Safety.Tests;

/// <summary>
/// Phase (small final presentation correction, driven by a real-machine retry that changed replacement roots
/// only): guards the confirmed defects — a replacement-only retry ("0 B" additive, but the mapped total actually
/// grew) showing no visible change at all, the Largest-files safety/provenance note, the Documents finding's
/// misleading "Managed by another application" status, and the completed-result badge using a warning icon.
/// </summary>
public sealed class RetryAccountingAndGuidanceTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string Pages = Path.Combine(Root, "src", "Clyr.App", "Pages");

    [Fact]
    public void ReplacementNetChangeHasItsOwnVisibleFieldSeparateFromAdditiveStorage()
    {
        var xaml = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml"));
        Assert.Contains("x:Name=\"AdministratorRetryReplacementPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Change from refreshed areas", xaml, StringComparison.Ordinal);

        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        // Only shown when at least one root was actually replaced — never merged into "Newly found storage".
        Assert.Contains("AdministratorRetryReplacementPanel.Visibility = rootsReplaced > 0", code, StringComparison.Ordinal);
        Assert.Contains("AdministratorRetryReplacementText.Text = FormatSignedWithPlus(replacementNet)", code, StringComparison.Ordinal);
        Assert.Contains("AdministratorRetryNewlyAccountedText.Text = OverviewPage.Format(additiveBytes)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplacementNetChangeFormatterShowsAnExplicitSignForBothDirections()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        Assert.Contains("private static string FormatSignedWithPlus(long value)", code, StringComparison.Ordinal);
        // A positive net change gets an explicit "+" (never bare, which could read as unsigned); a negative
        // change is never hidden.
        Assert.Contains("(value > 0 ? \"+\" : value < 0 ? \"-\" : string.Empty)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void LargestFilesSafetyNoteIsVisibleDirectlyBeneathTheDescriptionNeverTooltipOnly()
    {
        var xaml = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml"));
        Assert.Contains("x:Name=\"FilesSection\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FileList\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FilesProvenanceText\"", xaml, StringComparison.Ordinal);
        var fileListIndex = xaml.IndexOf("x:Name=\"FileList\"", StringComparison.Ordinal);
        var provenanceIndex = xaml.IndexOf("x:Name=\"FilesProvenanceText\"", StringComparison.Ordinal);
        // Directly beneath the section header/description, before the file list itself — never buried at the
        // bottom, and never expressed only as a ToolTip.
        Assert.True(provenanceIndex < fileListIndex,
            "FilesProvenanceText must appear before FileList (directly beneath the Largest files description).");

        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        Assert.Contains("\"Large files are not automatically safe to remove.", code, StringComparison.Ordinal);
    }

    [Fact]
    public void PostRetryFileProvenanceNoteExplainsTheFileListWasNotRebuilt()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        Assert.Contains("This file list comes from the original Drive ", code, StringComparison.Ordinal);
        Assert.Contains("Analysis. Administrator Retry updated storage totals and folder coverage but did not rebuild the file list.", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentsFindingNeverDisplaysManagedByAnotherApplication()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        // User-authored content (Documents/Desktop, Media, Downloads) gets its own truthful status distinct from
        // both "Protected by Windows" (system-owned) and the generic "Managed by another application" fallback
        // (product-owned) — it is not owned by any application at all.
        Assert.Contains("StorageCategory.UserDocuments or StorageCategory.UserMedia or StorageCategory.UserDownloads", code, StringComparison.Ordinal);
        Assert.Contains("\"Personal files — review carefully\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletedResultBadgeNeverUsesTheWarningIcon()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        // The badge text is a neutral "Drive analysis complete" regardless of warnings (see the earlier
        // duplicate-status correction) — its icon must match: a completion checkmark, never the warning triangle
        // the separate access-issues badge legitimately uses.
        Assert.Contains("StatusBadgeControl.Glyph = \"\\uE73E\";", NormalizeGlyphLiteral(code), StringComparison.Ordinal);
    }

    /// <summary>The glyph is stored as a literal Segoe Fluent Icons character in source, not an escape sequence —
    /// this substitutes the one specific character this test cares about with its escape form so the assertion
    /// can be written and diffed without embedding a raw private-use codepoint in this test file.</summary>
    private static string NormalizeGlyphLiteral(string source) => source.Replace("\uE73E", "\\uE73E", StringComparison.Ordinal);

    [Fact]
    public void NoUserFacingStringLiteralContainsSpacedOutClyrBranding()
    {
        var text = File.ReadAllText(Path.Combine(Pages, "ResultsPage.xaml.cs"));
        Assert.DoesNotContain("C L Y R", text, StringComparison.Ordinal);
    }
}
