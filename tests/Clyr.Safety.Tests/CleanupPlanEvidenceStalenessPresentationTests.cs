namespace Clyr.Safety.Tests;

/// <summary>
/// Phase 6 safety correction: guards the Review Plan / Administrator Retry wiring for the confirmed defect — a
/// plan built before Administrator Retry could remain valid after retry enriched the active result, because
/// retry deliberately preserves ScanId. These tests inspect actual source (the same pattern every other
/// App-layer safety test in this project uses, since <c>Clyr.App</c> is a WinUI project no plain xunit project
/// references) rather than asserting on rendered UI.
/// </summary>
public sealed class CleanupPlanEvidenceStalenessPresentationTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string Pages = Path.Combine(Root, "src", "Clyr.App", "Pages");
    private static readonly string ViewModels = Path.Combine(Root, "src", "Clyr.App", "ViewModels");
    private static readonly string Core = Path.Combine(Root, "src", "Clyr.Core");

    [Fact]
    public void EvidenceStateIsAContentDigestNeverJustScanId()
    {
        var code = File.ReadAllText(Path.Combine(Core, "EvidenceState.cs"));
        Assert.Contains("public static class EvidenceState", code, StringComparison.Ordinal);
        Assert.Contains("public static string ForResult(ScanResult result)", code, StringComparison.Ordinal);
        Assert.Contains("public static string ForSnapshot(StorageSnapshot snapshot)", code, StringComparison.Ordinal);
        // Root contributions, coverage, allocation, and classification are exactly what Administrator Retry
        // enrichment changes while deliberately keeping the same ScanId — this must feed the digest.
        Assert.Contains("WriteRootContributions(writer, result.RootContributions)", code, StringComparison.Ordinal);
        Assert.Contains("WriteAllocation(writer, result.Allocation)", code, StringComparison.Ordinal);
        Assert.Contains("WriteClassification(writer, result.Classification)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanBindingAndValidationContextCarryTheEvidenceStateIdentity()
    {
        var contracts = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Contracts", "CleanupPlanning.cs"));
        Assert.Contains("string EvidenceStateId, string ItemSelectionIdentity,", contracts, StringComparison.Ordinal);
        Assert.Contains("string PrivacyMode, string CurrentEvidenceStateId,", contracts, StringComparison.Ordinal);

        var canonicalizer = File.ReadAllText(Path.Combine(Core, "CleanupPlans.cs"));
        Assert.Contains("writer.WriteString(\"evidenceStateId\", value.EvidenceStateId);", canonicalizer, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatorComparesEvidenceStateAsItsOwnStaleReasonNotJustScanId()
    {
        var code = File.ReadAllText(Path.Combine(Core, "CleanupPlanValidator.cs"));
        Assert.Contains(
            "Compare(string.Equals(plan.Binding.EvidenceStateId, context.CurrentEvidenceStateId, StringComparison.Ordinal),\n            \"plan.evidence-stale\"",
            code.Replace("\r\n", "\n"), StringComparison.Ordinal);
        // The correction's whole point: ScanId still matches after a retry, so a distinct check is required.
        Assert.Contains("plan.scan-stale", code, StringComparison.Ordinal);
        Assert.Contains("plan.evidence-stale", code, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanSchemaVersionWasBumpedSoALegacyBindingCannotSilentlyPassAsCurrent()
    {
        var code = File.ReadAllText(Path.Combine(Core, "CleanupCandidates.cs"));
        Assert.Contains("public const int PlanSchemaVersion = 2;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewPlanViewModelValidatesAgainstLiveSessionEvidenceNeverThePlansOwnBindingEchoedBackAtItself()
    {
        var code = File.ReadAllText(Path.Combine(ViewModels, "AppSessionViewModel.cs"));
        Assert.Contains("private (Guid ScanId, string DriveIdentity, string RulePackId, string RulePackVersion, string RulePackDigest, string EvidenceStateId) CurrentEvidence()", code, StringComparison.Ordinal);
        Assert.Contains("var result = Session.Result;", code, StringComparison.Ordinal);
        Assert.Contains("result is null ? EvidenceState.NoResult : EvidenceState.ForResult(result));", code, StringComparison.Ordinal);
        Assert.Contains("public PlanValidationResult? ValidateCurrentPlan() => CurrentPlan is null ? null : ValidateAgainstCurrentEvidence(CurrentPlan);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteRefusesAPlanThatIsNoLongerCurrentBeforeItReachesTheExecutor()
    {
        var code = File.ReadAllText(Path.Combine(ViewModels, "AppSessionViewModel.cs"));
        var executeIndex = code.IndexOf("public ExecutionOutcome Execute(", StringComparison.Ordinal);
        var validateIndex = code.IndexOf("if (ValidateAgainstCurrentEvidence(plan) is { IsValid: false })", StringComparison.Ordinal);
        var executorIndex = code.IndexOf("var executor = new NonElevatedCleanupExecutor(tokenService, clock);", StringComparison.Ordinal);
        Assert.True(executeIndex >= 0 && validateIndex > executeIndex && executorIndex > validateIndex,
            "Execute must validate the plan against current evidence before constructing NonElevatedCleanupExecutor.");
        Assert.Contains("throw new InvalidOperationException(\"This plan is no longer current and cannot be executed. Rebuild the plan and try again.\");", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DeveloperModePlansAreAlsoBoundToSnapshotEvidence()
    {
        var code = File.ReadAllText(Path.Combine(ViewModels, "AppSessionViewModel.cs"));
        Assert.Contains("EvidenceState.ForSnapshot(snapshot), DateTimeOffset.UtcNow, candidates, [findingId]));", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewPlanRefreshDiscardsAStalePlanBeforeAnySelectionOrExecutionControlIsShown()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ReviewPlanPage.xaml.cs"));
        var refreshIndex = code.IndexOf("public void Refresh()", StringComparison.Ordinal);
        var clearIndex = code.IndexOf("selectedFindingIds.Clear();", StringComparison.Ordinal);
        var discardCallIndex = code.IndexOf("DiscardIfStale();", StringComparison.Ordinal);
        var planPanelCollapseIndex = code.IndexOf("PlanPanel.Visibility = Visibility.Collapsed;", StringComparison.Ordinal);
        Assert.True(refreshIndex >= 0 && clearIndex > refreshIndex && discardCallIndex > clearIndex && planPanelCollapseIndex > discardCallIndex,
            "Refresh must clear prior selections, then discard-if-stale, before deciding whether the plan/execution panels show at all.");
    }

    [Fact]
    public void DiscardIfStaleNeverAutomaticallyRebuildsOrExecutesAndAlwaysExplainsWhatHappened()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ReviewPlanPage.xaml.cs"));
        var methodStart = code.IndexOf("private void DiscardIfStale()", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        var methodEnd = code.IndexOf("\n    }", methodStart, StringComparison.Ordinal);
        var body = code[methodStart..methodEnd];
        Assert.Contains("ViewModel.ValidateCurrentPlan()", body, StringComparison.Ordinal);
        Assert.Contains("ViewModel.Discard();", body, StringComparison.Ordinal);
        Assert.Contains("\"The analysis changed after Administrator Retry. Rebuild this plan to review the updated results.\"", body, StringComparison.Ordinal);
        Assert.Contains("\"The analysis changed after this plan was created. Rebuild the Review Plan before continuing.\"", body, StringComparison.Ordinal);
        Assert.Contains("plan.evidence-stale", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.Create(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.Execute(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("RunExecutionAsync", body, StringComparison.Ordinal);
    }

    [Fact]
    public void RebuildNoticeBannerExistsAndIsCollapsedByDefault()
    {
        var xaml = File.ReadAllText(Path.Combine(Pages, "ReviewPlanPage.xaml"));
        Assert.Contains("x:Name=\"PlanRebuildNotice\" Visibility=\"Collapsed\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PlanRebuildNoticeText\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanValidationTextIsComputedLiveNeverAHardcodedCurrentClaim()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ReviewPlanPage.xaml.cs"));
        Assert.DoesNotContain("stale-plan status: current", code, StringComparison.Ordinal);
        Assert.Contains("var validation = ViewModel.ValidateCurrentPlan();", code, StringComparison.Ordinal);
        Assert.Contains("stale-plan status: {staleStatus}", code, StringComparison.Ordinal);
        // Section 4's constraint: no raw digest/hash in normal UI error text.
        Assert.DoesNotContain("PlanRebuildNoticeText.Text = plan.Digest", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PlanRebuildNoticeText.Text = validation", code, StringComparison.Ordinal);
    }

    [Fact]
    public void CliPlanExecuteRevalidatesFullPlanStalenessBeforeAnyItemReachesTheExecutor()
    {
        var code = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Cli", "ExecutionCliCommands.cs"));
        var expiredCheckIndex = code.IndexOf("plan.expired: The plan has expired.", StringComparison.Ordinal);
        var validateIndex = code.IndexOf("if (!Validate(plan, snapshot).IsValid)", StringComparison.Ordinal);
        var executableIndex = code.IndexOf("var executableItemIds = plan.Items", StringComparison.Ordinal);
        Assert.True(expiredCheckIndex >= 0 && validateIndex > expiredCheckIndex && executableIndex > validateIndex,
            "plan execute must run full CleanupPlanValidator.Validate (not just digest/expiry) before selecting executable items.");
        Assert.Contains("plan.stale: The plan is no longer current and cannot be executed.", code, StringComparison.Ordinal);
    }

    [Fact]
    public void NoPhase8MoveToAnotherDriveFunctionalityWasIntroduced()
    {
        foreach (var file in new[]
        {
            Path.Combine(Core, "EvidenceState.cs"),
            Path.Combine(Core, "CleanupPlanValidator.cs"),
            Path.Combine(Core, "CleanupPlans.cs"),
            Path.Combine(ViewModels, "AppSessionViewModel.cs"),
            Path.Combine(Pages, "ReviewPlanPage.xaml.cs"),
        })
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("MoveKnownFolder", text, StringComparison.Ordinal);
            Assert.DoesNotContain("MoveToAnotherDrive", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Process.Start", text, StringComparison.Ordinal);
            Assert.DoesNotContain("requireAdministrator", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NoNewCleanupActionWasAddedToTheExecutionAllowlist()
    {
        var code = File.ReadAllText(Path.Combine(Core, "Execution", "BuiltInExecutionActions.cs"));
        Assert.Contains("EnabledActions: [ClyrOwnedTempArtifacts]", code, StringComparison.Ordinal);
    }
}
