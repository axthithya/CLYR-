namespace Clyr.Safety.Tests;

/// <summary>
/// Phase (progressive-analysis terminology and Administrator Retry validation correction): guards the specific
/// fixes this pass makes — the Results header still saying "Deep Analysis" internally, the false
/// AccountingBasisMismatch rejection, the collapsed generic retry failure message, second-analysis provisional
/// priority, and scan-ID correlation.
/// </summary>
public sealed class ProgressiveTerminologyAndRetryValidationTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void ResultsHeaderUsesDriveAnalysisTerminologyNeverTheInternalModeName()
    {
        var code = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "Pages", "ResultsPage.xaml.cs"));
        Assert.Contains("ScanIdentityText.Text = $\"{r.Root.TrimEnd('\\\\')} · Drive Analysis\";", code, StringComparison.Ordinal);
        Assert.DoesNotContain("{r.Mode} Analysis", code, StringComparison.Ordinal);
        // Never the malformed mojibake separator, never a spaced-out product name.
        Assert.DoesNotContain('Â', code);
        Assert.DoesNotContain('�', code);
        Assert.DoesNotContain("C L Y R", code, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewRecentActivityUsesDriveAnalysisForDeepModeAndKeepsQuickTruthful()
    {
        var code = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "Pages", "OverviewPage.xaml.cs"));
        Assert.Contains("item.Mode == ScanMode.Quick ? \"Quick Analysis\" : \"Drive Analysis\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalScanAndResultsPagesContainNoQuickOrDeepWordingForTheProgressiveFlow()
    {
        foreach (var file in new[] { "ScanPage.xaml", "ScanPage.xaml.cs", "ResultsPage.xaml", "OverviewPage.xaml" })
        {
            var text = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "Pages", file));
            foreach (var forbidden in new[] { "Quick Analysis", "Deep Analysis", "Quick estimate", "Quick mode", "Deep mode" })
                Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DeveloperModeMayStillExposeTheInternalStrategyTruthfully()
    {
        var code = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "Pages", "DeveloperModePage.xaml.cs"));
        Assert.Contains("private static string ModeLabel(ScanMode mode) => mode == ScanMode.Deep ? \"Deep\" : \"Quick\";", code, StringComparison.Ordinal);
    }

    [Fact]
    public void CliRetainsQuickAndDeepFlagsUnchanged()
    {
        var cli = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Cli", "ScanCliCommands.cs"));
        Assert.Contains("--quick", cli, StringComparison.Ordinal);
        Assert.Contains("--deep", cli, StringComparison.Ordinal);
    }

    [Fact]
    public void RetryFailureCategoriesAreBoundedAndDistinctNeverOneGenericBucket()
    {
        var text = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Core", "AdministratorRetryUx.cs"));
        // The three new bounded categories this correction adds, replacing the previous single generic Failed
        // bucket that every one of these outcomes used to collapse into.
        Assert.Contains("ResponseMismatch, AccountingMismatch, HelperValidationFailed", text, StringComparison.Ordinal);
        Assert.Contains("AdministratorRetryPhase.ResponseMismatch", text, StringComparison.Ordinal);
        Assert.Contains("AdministratorRetryPhase.AccountingMismatch", text, StringComparison.Ordinal);
        Assert.Contains("AdministratorRetryPhase.HelperValidationFailed", text, StringComparison.Ordinal);
        // Never a raw exception, pipe name, nonce, or executable path inside the new user-facing text constants
        // themselves (checked narrowly — this file's own doc comments legitimately discuss pipes/nonces as
        // internal protocol concepts elsewhere).
        var newConstants = text[text.IndexOf("public const string ResponseMismatchTitle", StringComparison.Ordinal)
            ..text.IndexOf("public const string FailedTitle", StringComparison.Ordinal)];
        foreach (var forbidden in new[] { "pipe", "nonce", ".exe", "Exception.Message", "StackTrace" })
            Assert.DoesNotContain(forbidden, newConstants, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProvisionalPriorityUsesExplicitRunStateNotSessionResultAlone()
    {
        var code = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "Pages", "ResultsPage.xaml.cs"));
        Assert.Contains("var showProvisional = snapshot is not null && (session.IsScanning || r is null);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgressiveSnapshotReporterCorrelatesByScanIdAndIgnoresForeignSnapshots()
    {
        var session = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "ViewModels", "AppSessionViewModel.cs"));
        Assert.Contains("currentAttemptScanId ??= value.ScanId;", session, StringComparison.Ordinal);
        Assert.Contains("if (value.ScanId != currentAttemptScanId) return;", session, StringComparison.Ordinal);
        Assert.Contains("currentAttemptScanId = null;", session, StringComparison.Ordinal);
    }

    [Fact]
    public void AdministratorRetryEnrichmentIsRejectedForAnyResultOtherThanTheExactActiveOne()
    {
        var session = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "ViewModels", "AppSessionViewModel.cs"));
        Assert.Contains("if (result is null || enriched.ScanId != result.ScanId) return;", session, StringComparison.Ordinal);
        Assert.Contains("if (!ReferenceEquals(combined.OriginalResult, Session.Result)) return;", session, StringComparison.Ordinal);
    }
}
