namespace Clyr.Safety.Tests;

/// <summary>
/// Phase (Administrator Retry result-integration correction): a successful retry previously computed a correct,
/// deduplicated combined byte total but never actually replaced what any page displayed — the original,
/// unenriched <see cref="Clyr.Core.ElevatedScanResultReconciler"/> result kept being shown. These checks guard
/// the specific wiring that closes that gap, plus the safety boundary around the new code that does it.
/// </summary>
public sealed class AdministratorRetryResultIntegrationTests
{
    private static readonly string Root = RepositoryRoot();

    [Fact]
    public void EnricherFileExistsAndContainsNoMutationOrExecutionCapability()
    {
        var file = Path.Combine(Root, "src", "Clyr.Core", "ElevatedScanResultEnricher.cs");
        Assert.True(File.Exists(file), $"Expected file not found: {file}");
        var text = File.ReadAllText(file);
        var forbidden = new[]
        {
            "Process.Start", "ProcessStartInfo", "System.Diagnostics.Process", "powershell.exe", "cmd.exe",
            "cmd /c", "runas", "requireAdministrator", "UseShellExecute",
            "File.Delete", "File.Move", "File.WriteAllText", "File.WriteAllBytes", "File.AppendAllText",
            "File.Create(", "File.OpenWrite", "File.Replace", "File.SetAttributes",
            "Directory.Delete", "Directory.Move", "Directory.CreateDirectory",
            "FileSecurity", "DirectorySecurity", "SetAccessControl", "FileSystemAclExtensions",
            "TakeOwnership", "Ownership.Set",
            "System.Net.Sockets", "TcpClient", "TcpListener", "UdpClient", "HttpClient", "WebRequest",
            "NamedPipe", "IFileSystemEnumerator", "Clyr.ElevatedHelper", "ElevatedHelperLauncher",
            "NonElevatedCleanupExecutor", "CleanupPlanBuilder", "ExecutionTokenService", "CleanupCandidateFactory",
            "BuiltInExecutionActions", "MoveKnownFolder", "MoveToAnotherDrive",
        };
        foreach (var token in forbidden) Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        Assert.Contains("public static class ElevatedScanResultEnricher", text, StringComparison.Ordinal);
        Assert.Contains("public static ScanResult Build(ElevatedReconciliationResult reconciliation)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ReconcilerTreatsLogicalExceedingDriveUsedAsAConsistencyFlagNeverARejection()
    {
        // Phase (Administrator Retry validation correction): the earlier version of this reconciler rejected the
        // whole retry outright whenever combined logical bytes exceeded the drive's physical used-bytes basis —
        // a false rejection, since logical (namespace) bytes legitimately exceed physical used bytes for hard
        // links, sparse files, and compression. This must now be recorded as AccountingConsistency.
        // LogicalExceedsDriveUsed (the same flag the original scan itself already uses for this exact condition),
        // never as a rejection reason.
        var text = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Core", "ElevatedScanResultReconciler.cs"));
        Assert.Contains("AccountingConsistency.LogicalExceedsDriveUsed", text, StringComparison.Ordinal);
        Assert.Contains("legitimately", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exceed this drive's own used-space basis", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ReconcilerStillRejectsAGenuinelyImpossibleNegativeDelta()
    {
        var text = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Core", "ElevatedScanResultReconciler.cs"));
        Assert.Contains("AccountingBasisMismatch", text, StringComparison.Ordinal);
        Assert.Contains("deltaLogical < 0 || deltaAllocated < 0", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionViewModelReplacesActiveResultOnlyWhenScanIdentityMatches()
    {
        var text = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "ViewModels", "AppSessionViewModel.cs"));
        Assert.Contains("public void ApplyEnrichedResult(ScanResult enriched)", text, StringComparison.Ordinal);
        Assert.Contains("enriched.ScanId != result.ScanId", text, StringComparison.Ordinal);
        Assert.Contains("public void ApplyAdministratorRetryResultIfPending()", text, StringComparison.Ordinal);
        // Idempotency guard: the same applied attempt must never be re-applied on a later, unrelated render pass.
        Assert.Contains("lastAppliedReconciliationId", text, StringComparison.Ordinal);
        // Review Plan's candidate list must keep reading Session.Result live (never a cached ScanResult reference)
        // so it automatically reflects the enriched result the moment it becomes active — no special-case wiring.
        Assert.Contains("Session.Result is null ? new List<CleanupCandidate>() : CleanupCandidateFactory.FromScan(Session.Result)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultsPageAppliesAnyPendingEnrichedResultBeforeRenderingTheRetryPanel()
    {
        var text = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "Pages", "ResultsPage.xaml.cs"));
        Assert.Contains("private void HandleAdministratorRetryStateChanged()", text, StringComparison.Ordinal);
        var handlerIndex = text.IndexOf("private void HandleAdministratorRetryStateChanged()", StringComparison.Ordinal);
        var applyIndex = text.IndexOf("ViewModel.ApplyAdministratorRetryResultIfPending();", StringComparison.Ordinal);
        var renderIndex = text.IndexOf("RenderAdministratorRetry();", handlerIndex, StringComparison.Ordinal);
        Assert.True(handlerIndex >= 0 && applyIndex > handlerIndex && renderIndex > applyIndex,
            "ApplyAdministratorRetryResultIfPending must run, in HandleAdministratorRetryStateChanged, before RenderAdministratorRetry.");
    }

    [Fact]
    public void RetryCompletionWordingDistinguishesInspectedAddedAndRemainingWithoutOverclaiming()
    {
        var text = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "Pages", "ResultsPage.xaml.cs"));
        // Section 9 correction: natural language distinguishing added/refreshed/overlap/remaining areas — never
        // describing a replacement's net change as "newly added" storage.
        Assert.Contains("if (rootsAdditive > 0) areaParts.Add($\"{rootsAdditive} added new storage information\");", text, StringComparison.Ordinal);
        Assert.Contains("if (rootsReplaced > 0) areaParts.Add($\"{rootsReplaced} refreshed existing results\");", text, StringComparison.Ordinal);
        Assert.Contains("if (rootsOverlapped > 0) areaParts.Add($\"{rootsOverlapped} contained no additional information\");", text, StringComparison.Ordinal);
        Assert.Contains("remain restricted", text, StringComparison.Ordinal);
        Assert.Contains("Files checked:", text, StringComparison.Ordinal);
        Assert.Contains("Folders checked:", text, StringComparison.Ordinal);
        Assert.Contains("Access issues:", text, StringComparison.Ordinal);
        // Never a bare "added {bytes}" without the reconciliation-mode context — the additive-vs-replacement
        // distinction stays anchored to per-root counts, never a raw byte figure alone.
        Assert.DoesNotContain("and added {OverviewPage.Format(state.AdditionalLogicalBytes", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultsPageShowsABeforeAndAfterCoverageComparisonOnlyWhenApplied()
    {
        var xaml = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "Pages", "ResultsPage.xaml"));
        Assert.Contains("x:Name=\"AdministratorRetryComparison\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", xaml.Substring(xaml.IndexOf("x:Name=\"AdministratorRetryComparison\"", StringComparison.Ordinal), 120));

        var codeBehind = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "Pages", "ResultsPage.xaml.cs"));
        Assert.Contains("AdministratorRetryComparison.Visibility = showSummary", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ScanAccounting.Summarize(combined.OriginalResult).AccountedPercentage", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ScanPageAndResultsPageDeriveWarningCountsFromTheIdenticalCategorizedSeveritySet()
    {
        // Section 9: for the same active result, Scan and Results must agree on genuine warning counts — proven
        // here by requiring both pages (and the enricher's own recomputation) to filter by the exact same four
        // severities, never a broader or narrower set that could silently disagree.
        const string severityFilter = "ScanIssueSeverity.AccessWarning or ScanIssueSeverity.PermissionLimited or ScanIssueSeverity.DataChanged or ScanIssueSeverity.Fatal";
        var scanPage = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "Pages", "ScanPage.xaml.cs")).Replace("\r\n", "\n").Replace("\n", " ");
        var resultsPage = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "Pages", "ResultsPage.xaml.cs")).Replace("\r\n", "\n").Replace("\n", " ");
        var overviewPage = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "Pages", "OverviewPage.xaml.cs")).Replace("\r\n", "\n").Replace("\n", " ");
        var enricher = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Core", "ElevatedScanResultEnricher.cs")).Replace("\r\n", "\n").Replace("\n", " ");
        foreach (var text in new[] { scanPage, resultsPage, overviewPage, enricher })
            Assert.Contains(severityFilter.Replace(" ", ""), text.Replace(" ", ""), StringComparison.Ordinal);
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
