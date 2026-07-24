using System.Text.Json;
using System.Text.Json.Serialization;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Core.Execution;
using Clyr.Persistence;
using Clyr.Rules;

namespace Clyr.Cli;

public sealed partial class CliApplication
{
    private static readonly JsonSerializerOptions ExecutionJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    /// <summary>Section 9's exact required guidance for an execution whose true outcome CLYR could not durably
    /// confirm. Never claims success, never assumes zero files changed, never offers a resume or automatic
    /// retry — this is text output only.</summary>
    private const string InterruptedGuidanceText =
        "CLYR found an execution that started but did not record a final result. Some approved items may have changed. " +
        "CLYR will not repeat the operation automatically. Run a new Drive Analysis before creating another cleanup plan.";

    private readonly IExecutionReceiptStore? executionReceiptStore;
    private readonly ExecutionTokenService executionTokenService = new();
    private readonly ExecutionSessionId cliSessionId = new(Guid.NewGuid());
    private readonly HashSet<string> attemptedPlanIds = new(StringComparer.Ordinal);

    public CliApplication(IEnvironmentInfo environment, IDemoDataService demo, RuleValidator rules,
        IPrivacyRedactor redactor, string version, IDriveDiscovery driveDiscovery, IScanService scanner,
        IScanReportExporter exporter, RulePackLoadResult? rulePack, ISnapshotStore snapshotStore,
        ICleanupPlanStore? cleanupPlanStore, IExecutionReceiptStore? executionReceiptStore)
        : this(environment, demo, rules, redactor, version, driveDiscovery, scanner, exporter, rulePack, snapshotStore, cleanupPlanStore)
    {
        this.executionReceiptStore = executionReceiptStore;
    }

    /// <summary>Runs once at the start of every CLI invocation: any durable "Started" row a previous crashed
    /// process could not finalize is marked Interrupted here, before this invocation's own command runs — never
    /// resumed, never guessed successful. Best-effort: a corrupted or inaccessible history store must not block
    /// an unrelated command (see the same guard <see cref="Execution"/> already applies).</summary>
    private void ReconcileInterruptedExecutions()
    {
        if (executionReceiptStore is null) return;
        try { executionReceiptStore.ReconcileInterruptedAsync(TimeSpan.Zero, DateTimeOffset.UtcNow).GetAwaiter().GetResult(); }
        catch (ExecutionReceiptStoreException) { /* best-effort; surfaced only if the user explicitly asks for execution history */ }
    }

    private int PlanExecute(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!TryPlanId(args[2], out var id)) return Usage(error, "A valid plan ID is required.");
        if (!TryOption(args, "--confirm-digest", out var confirmDigest) || string.IsNullOrWhiteSpace(confirmDigest))
            return Usage(error, "Execution requires --confirm-digest <digest-prefix> matching the digest from 'plan show'.");
        var plan = cleanupPlanStore!.Find(id);
        if (plan is null) return Missing(error, "Plan not found or no longer held in this process. Imported and exported plans cannot execute.");
        if (!plan.Digest.StartsWith(confirmDigest, StringComparison.Ordinal))
            return Usage(error, "The --confirm-digest value does not match the plan's current digest.");
        // A durable execution-start record is required before any mutation can occur — with no receipt store
        // there is nowhere safe to write it, so execution must not proceed at all.
        if (executionReceiptStore is null)
        { error.WriteLine("execution.unavailable: Execution history is unavailable, so CLYR cannot safely start this cleanup."); return 3; }
        if (!attemptedPlanIds.Add(plan.Id.ToString()))
        { error.WriteLine("plan.consumed: This plan has already been used for an execution attempt."); return 1; }
        if (plan.Expiry.IsExpired(DateTimeOffset.UtcNow))
        { error.WriteLine("plan.expired: The plan has expired."); return 1; }

        // The Administrator Retry correction: a plan can carry the right digest and still be stale against the
        // analysis it was built from (evidence, scan, snapshot, rule pack, category registry, or privacy mode
        // may all have changed since). Full revalidation — not just the digest check above — must pass before
        // any item reaches the executor.
        var snapshot = plan.Binding.SourceSnapshotId.HasValue
            ? snapshotStore!.GetAsync(plan.Binding.SourceSnapshotId.Value).GetAwaiter().GetResult() : null;
        if (!Validate(plan, snapshot).IsValid)
        { error.WriteLine("plan.stale: The plan is no longer current and cannot be executed. Rebuild the plan and try again."); return 1; }

        var executableItemIds = plan.Items
            .Where(item => ExecutionEligibilityValidator.ValidateItemForExecution(item).IsSuccess)
            .Select(item => item.ItemId).ToArray();
        if (executableItemIds.Length == 0)
        { error.WriteLine("plan.no-executable-items: No plan item is eligible for Phase 6 execution."); return 1; }

        var userSid = ResolveUserSid();
        var actionIds = plan.Items.Where(item => executableItemIds.Contains(item.ItemId, StringComparer.Ordinal))
            .Select(item => item.Action.SourceRuleId).Distinct(StringComparer.Ordinal).ToArray();
        var token = executionTokenService.Issue(plan, cliSessionId, userSid, actionIds, DateTimeOffset.UtcNow);
        var executor = new NonElevatedCleanupExecutor(executionTokenService, new SystemClock(), executionReceiptStore);
        var outcome = executor.ExecuteAsync(plan, executableItemIds, token, cliSessionId, userSid, version, null, CancellationToken.None)
            .GetAwaiter().GetResult();

        if (args.Contains("--json", StringComparer.Ordinal))
            output.WriteLine(JsonSerializer.Serialize(outcome.Receipt, ExecutionJson));
        else
        {
            output.WriteLine($"Execution {outcome.Receipt.ExecutionId}: {outcome.State}");
            output.WriteLine($"Removed {outcome.Receipt.Summary.RemovedCount}; skipped {outcome.Receipt.Summary.SkippedCount}; " +
                $"failed {outcome.Receipt.Summary.FailedCount}; removed logical bytes {outcome.Receipt.Summary.RemovedLogicalBytes}.");
        }
        // Interrupted/UnknownOutcome are never produced by this call directly (only a future launch's startup
        // reconciliation can mark a row that way) — included here defensively so this exit-code rule stays
        // correct even if that ever changes, and so it reads as the deliberate, complete policy it is.
        return outcome.State is ExecutionState.Completed or ExecutionState.PartiallyCompleted ? 0 : 1;
    }

    private int Execution(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (executionReceiptStore is null) { error.WriteLine("execution.unavailable: Execution receipt history is unavailable."); return 3; }
        try
        {
            if (args.Count == 2 && args[1] == "list")
            {
                foreach (var item in executionReceiptStore.ListAsync().GetAwaiter().GetResult())
                {
                    output.WriteLine($"{item.ExecutionId} {item.StartedAtUtc:O} {item.FinalState} removed={item.RemovedCount} skipped={item.SkippedCount} failed={item.FailedCount} bytes={item.RemovedLogicalBytes}");
                    if (item.FinalState is ExecutionState.Interrupted or ExecutionState.UnknownOutcome)
                        output.WriteLine("  " + InterruptedGuidanceText);
                }
                return 0;
            }
            if (args.Count == 3 && args[1] == "status")
            {
                if (!TryExecutionId(args[2], out var id)) return Usage(error, "A valid execution ID is required.");
                var receipt = executionReceiptStore.GetAsync(id).GetAwaiter().GetResult();
                if (receipt is null) return Missing(error, "Execution receipt not found.");
                output.WriteLine(receipt.FinalState.ToString());
                if (receipt.FinalState is ExecutionState.Interrupted or ExecutionState.UnknownOutcome)
                {
                    output.WriteLine(InterruptedGuidanceText);
                    return 1;
                }
                return 0;
            }
            if (args.Count == 3 && args[1] == "receipt")
            {
                if (!TryExecutionId(args[2], out var id)) return Usage(error, "A valid execution ID is required.");
                var receipt = executionReceiptStore.GetAsync(id).GetAwaiter().GetResult();
                if (receipt is null) return Missing(error, "Execution receipt not found.");
                output.WriteLine(JsonSerializer.Serialize(receipt, ExecutionJson));
                return 0;
            }
            if (args.Count >= 4 && args[1] == "export")
            {
                if (!TryExecutionId(args[2], out var id) || !TryOption(args, "--output", out var path))
                    return Usage(error, "A valid execution ID and --output <path> are required.");
                var receipt = executionReceiptStore.GetAsync(id).GetAwaiter().GetResult();
                if (receipt is null) return Missing(error, "Execution receipt not found.");
                File.WriteAllText(path!, JsonSerializer.Serialize(receipt, ExecutionJson));
                output.WriteLine("Privacy-safe execution receipt exported.");
                return 0;
            }
            if (args.Count == 3 && args[1] == "discard-receipt")
            {
                if (!TryExecutionId(args[2], out var id)) return Usage(error, "A valid execution ID is required.");
                return executionReceiptStore.DiscardAsync(id).GetAwaiter().GetResult() ? 0 : Missing(error, "Execution receipt not found.");
            }
        }
        catch (ExecutionReceiptStoreException exception)
        { error.WriteLine(exception.Code + ": " + redactor.Redact(exception.Message)); return 3; }
        error.WriteLine("Usage: clyr execution status <id> | receipt <id> | list | export <id> --output <path> | discard-receipt <id>");
        return 2;
    }

    private static string ResolveUserSid() => OperatingSystem.IsWindows() ? WindowsUserIdentity.CurrentSid() : "unavailable";

    private static bool TryExecutionId(string value, out ExecutionId id)
    {
        if (Guid.TryParse(value, out var parsed) && parsed != Guid.Empty) { id = new(parsed); return true; }
        id = default; return false;
    }
}
