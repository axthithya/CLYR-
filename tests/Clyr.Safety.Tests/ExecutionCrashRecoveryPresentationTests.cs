namespace Clyr.Safety.Tests;

/// <summary>
/// Phase 6 crash-recovery correction: guards the durable execution-lifecycle wiring — a durable "Started" record
/// before any mutation, startup reconciliation before anything else runs, and receipt/history UI wording that
/// never claims success, never offers a Resume button, and never offers an automatic Retry for an execution
/// whose outcome could not be durably confirmed. Source-text based, matching every other App-layer safety test in
/// this project, since <c>Clyr.App</c> is a WinUI project no plain xunit project references.
/// </summary>
public sealed class ExecutionCrashRecoveryPresentationTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static readonly string Pages = Path.Combine(Root, "src", "Clyr.App", "Pages");
    private static readonly string ViewModels = Path.Combine(Root, "src", "Clyr.App", "ViewModels");
    private static readonly string Core = Path.Combine(Root, "src", "Clyr.Core", "Execution");
    private static readonly string Persistence = Path.Combine(Root, "src", "Clyr.Persistence");

    [Fact]
    public void ExecutionReceiptStoreInterfaceLivesInCoreSoTheExecutorCanDependOnItWithoutACircularReference()
    {
        var code = File.ReadAllText(Path.Combine(Core, "ExecutionReceiptStore.cs"));
        Assert.Contains("public interface IExecutionReceiptStore", code, StringComparison.Ordinal);
        Assert.Contains("Task BeginAsync(ExecutionReceipt startRecord, CancellationToken cancellationToken = default);", code, StringComparison.Ordinal);
        Assert.Contains("Task CompleteAsync(ExecutionId id, ExecutionReceipt finalReceipt, CancellationToken cancellationToken = default);", code, StringComparison.Ordinal);
        Assert.Contains("Task<bool> HasRecordForPlanAsync(CleanupPlanId planId, string planDigest, CancellationToken cancellationToken = default);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void NonElevatedExecutorPersistsStartedBeforeAnyMutationLoopAndCompletesAfterward()
    {
        var code = File.ReadAllText(Path.Combine(Core, "NonElevatedCleanupExecutor.cs"));
        var beginIndex = code.IndexOf("await receiptStore.BeginAsync(startReceipt, cancellationToken)", StringComparison.Ordinal);
        var mutationLoopIndex = code.IndexOf("foreach (var item in orderedItems)", StringComparison.Ordinal);
        var completeIndex = code.IndexOf("await receiptStore.CompleteAsync(executionId, receipt, cancellationToken)", StringComparison.Ordinal);
        Assert.True(beginIndex >= 0 && mutationLoopIndex > beginIndex && completeIndex > mutationLoopIndex,
            "BeginAsync must run before the mutation loop, and CompleteAsync only after it.");
        // A failed Begin must refuse to mutate anything; a failed Complete must never hide the true outcome.
        Assert.Contains("CLYR could not durably record that this execution was starting, so no files were changed.", code, StringComparison.Ordinal);
        Assert.Contains("could not be durably recorded. CLYR will show this as an unresolved execution the next time it starts.", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DurableReplayProtectionIsCheckedBeforeTokenConsumption()
    {
        var code = File.ReadAllText(Path.Combine(Core, "NonElevatedCleanupExecutor.cs"));
        var replayIndex = code.IndexOf("await receiptStore.HasRecordForPlanAsync(plan.Id, plan.Digest, cancellationToken)", StringComparison.Ordinal);
        var consumeIndex = code.IndexOf("if (!tokenService.Consume(token.TokenId))", StringComparison.Ordinal);
        Assert.True(replayIndex >= 0 && consumeIndex > replayIndex,
            "The durable plan-replay check must run before the one-time token is consumed, so a detected replay never burns a token needlessly.");
    }

    [Fact]
    public void SchemaMigrationAddsStartRecordColumnsWithSafeDefaultsAndPreservesExistingRows()
    {
        var code = File.ReadAllText(Path.Combine(Persistence, "SqlitePersistence.cs"));
        Assert.Contains("public const int CurrentSchemaVersion = 4;", code, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE ExecutionReceipt ADD COLUMN SourceScanId TEXT NOT NULL DEFAULT '';", code, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE ExecutionReceipt ADD COLUMN EvidenceStateId TEXT NOT NULL DEFAULT '';", code, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE ExecutionReceipt ADD COLUMN ActionIdsJson TEXT NOT NULL DEFAULT '[]';", code, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE ExecutionReceipt ADD COLUMN ExecutionSessionId TEXT NOT NULL DEFAULT '';", code, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE ExecutionReceipt ADD COLUMN WindowsUserSidFingerprint TEXT NOT NULL DEFAULT '';", code, StringComparison.Ordinal);
    }

    [Fact]
    public void BeginFailsOnDuplicateAndCompleteFailsClosedOnUnknownOrConflicting()
    {
        var code = File.ReadAllText(Path.Combine(Persistence, "ExecutionReceiptStore.cs"));
        Assert.Contains("\"receipt.duplicate-begin\"", code, StringComparison.Ordinal);
        Assert.Contains("\"receipt.unknown-execution\"", code, StringComparison.Ordinal);
        Assert.Contains("\"receipt.completion-mismatch\"", code, StringComparison.Ordinal);
        Assert.Contains("private static bool SameStartIdentity(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AppStartupReconcilesInterruptedExecutionsBeforeTheMainWindowIsShown()
    {
        var code = File.ReadAllText(Path.Combine(Root, "src", "Clyr.App", "App.xaml.cs"));
        var launchIndex = code.IndexOf("protected override async void OnLaunched(", StringComparison.Ordinal);
        var reconcileIndex = code.IndexOf("await receiptStore.ReconcileInterruptedAsync(TimeSpan.Zero, clock.UtcNow);", StringComparison.Ordinal);
        var windowIndex = code.IndexOf("window = Services.GetRequiredService<MainWindow>();", StringComparison.Ordinal);
        Assert.True(launchIndex >= 0 && reconcileIndex > launchIndex && windowIndex > reconcileIndex,
            "Reconciliation must run before the main window (and its Review Plan receipt history) can render anything.");
    }

    [Fact]
    public void CliReconcilesInterruptedExecutionsAtTheStartOfEveryInvocation()
    {
        var code = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Cli", "CliApplication.cs"));
        var runIndex = code.IndexOf("public int Run(IReadOnlyList<string> arguments,", StringComparison.Ordinal);
        var reconcileIndex = code.IndexOf("ReconcileInterruptedExecutions();", StringComparison.Ordinal);
        var commandIndex = code.IndexOf("var command = arguments.Count == 0", StringComparison.Ordinal);
        Assert.True(runIndex >= 0 && reconcileIndex > runIndex && commandIndex > reconcileIndex,
            "Reconciliation must run before any command (including plan execute) is dispatched.");
    }

    [Fact]
    public void CliPlanExecuteRefusesToRunWithoutADurableReceiptStore()
    {
        var code = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Cli", "ExecutionCliCommands.cs"));
        Assert.Contains("if (executionReceiptStore is null)", code, StringComparison.Ordinal);
        Assert.Contains("execution.unavailable: Execution history is unavailable, so CLYR cannot safely start this cleanup.", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewPlanViewModelRefusesToExecuteWithoutADurableReceiptStore()
    {
        var code = File.ReadAllText(Path.Combine(ViewModels, "AppSessionViewModel.cs"));
        Assert.Contains("if (receiptStore is null)", code, StringComparison.Ordinal);
        Assert.Contains("Execution history is unavailable, so CLYR cannot safely start this cleanup.", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ReceiptHistoryNeverOffersAResumeOrAutomaticRetryControl()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ReviewPlanPage.xaml.cs"));
        Assert.DoesNotContain("Content = \"Resume", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Content = \"Retry cleanup", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Content = \"Retry execution", code, StringComparison.Ordinal);
        // Every receipt-history row offers exactly "View" and "Delete receipt" — nothing that could resume or
        // silently retry an interrupted or unknown-outcome execution.
        Assert.Contains("var view = new Button { Content = \"View\" };", code, StringComparison.Ordinal);
        Assert.Contains("var discard = new Button { Content = \"Delete receipt\" };", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ReceiptHistoryUsesTheExactRequiredInterruptedGuidanceWording()
    {
        var code = File.ReadAllText(Path.Combine(Pages, "ReviewPlanPage.xaml.cs"));
        Assert.Contains(
            "\"CLYR found an execution that started but did not record a final result. Some approved items may have changed. \" +\n" +
            "        \"CLYR will not repeat the operation automatically. Run a new Drive Analysis before creating another cleanup plan.\";",
            code.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("ExecutionState.Interrupted => \"Interrupted — outcome could not be confirmed\",", code, StringComparison.Ordinal);
        Assert.Contains("ExecutionState.UnknownOutcome => \"Outcome unknown\",", code, StringComparison.Ordinal);
    }

    [Fact]
    public void CliUsesTheSameInterruptedGuidanceWordingAndReturnsAFailureExitCode()
    {
        var code = File.ReadAllText(Path.Combine(Root, "src", "Clyr.Cli", "ExecutionCliCommands.cs"));
        Assert.Contains("CLYR found an execution that started but did not record a final result.", code, StringComparison.Ordinal);
        var statusIndex = code.IndexOf("if (args.Count == 3 && args[1] == \"status\")", StringComparison.Ordinal);
        var interruptedCheckIndex = code.IndexOf("if (receipt.FinalState is ExecutionState.Interrupted or ExecutionState.UnknownOutcome)", StringComparison.Ordinal);
        var returnOneIndex = code.IndexOf("return 1;", interruptedCheckIndex, StringComparison.Ordinal);
        Assert.True(statusIndex >= 0 && interruptedCheckIndex > statusIndex && returnOneIndex > interruptedCheckIndex,
            "execution status must return a non-success exit code for an Interrupted or UnknownOutcome execution.");
    }

    [Fact]
    public void NoElevatedHelperLaunchIsWiredIntoAnyProductionExecutionPath()
    {
        // Confirms directly against the code (not prior reports) that the elevated helper remains dormant: no
        // production caller invokes ElevatedHelperLauncher.RunAsync anywhere in the execution orchestration this
        // correction touches.
        foreach (var file in new[]
        {
            Path.Combine(ViewModels, "AppSessionViewModel.cs"),
            Path.Combine(Pages, "ReviewPlanPage.xaml.cs"),
            Path.Combine(Root, "src", "Clyr.Cli", "ExecutionCliCommands.cs"),
            Path.Combine(Core, "NonElevatedCleanupExecutor.cs"),
        })
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("ElevatedHelperLauncher.RunAsync", text, StringComparison.Ordinal);
            Assert.DoesNotContain("requireAdministrator", text, StringComparison.Ordinal);
            Assert.DoesNotContain("runas", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NoNewCleanupActionOrPhase8FunctionalityWasIntroduced()
    {
        foreach (var file in new[]
        {
            Path.Combine(Core, "NonElevatedCleanupExecutor.cs"),
            Path.Combine(Core, "ExecutionReceiptStore.cs"),
            Path.Combine(Persistence, "ExecutionReceiptStore.cs"),
            Path.Combine(ViewModels, "AppSessionViewModel.cs"),
            Path.Combine(Pages, "ReviewPlanPage.xaml.cs"),
            Path.Combine(Root, "src", "Clyr.Cli", "ExecutionCliCommands.cs"),
        })
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("MoveKnownFolder", text, StringComparison.Ordinal);
            Assert.DoesNotContain("MoveToAnotherDrive", text, StringComparison.Ordinal);
            Assert.DoesNotContain("npm-cache", text, StringComparison.OrdinalIgnoreCase);
        }
        var actions = File.ReadAllText(Path.Combine(Core, "BuiltInExecutionActions.cs"));
        Assert.Contains("EnabledActions: [ClyrOwnedTempArtifacts]", actions, StringComparison.Ordinal);
    }
}
