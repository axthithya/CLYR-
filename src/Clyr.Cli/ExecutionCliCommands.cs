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

    private int PlanExecute(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (!TryPlanId(args[2], out var id)) return Usage(error, "A valid plan ID is required.");
        if (!TryOption(args, "--confirm-digest", out var confirmDigest) || string.IsNullOrWhiteSpace(confirmDigest))
            return Usage(error, "Execution requires --confirm-digest <digest-prefix> matching the digest from 'plan show'.");
        var plan = cleanupPlanStore!.Find(id);
        if (plan is null) return Missing(error, "Plan not found or no longer held in this process. Imported and exported plans cannot execute.");
        if (!plan.Digest.StartsWith(confirmDigest, StringComparison.Ordinal))
            return Usage(error, "The --confirm-digest value does not match the plan's current digest.");
        if (!attemptedPlanIds.Add(plan.Id.ToString()))
        { error.WriteLine("plan.consumed: This plan has already been used for an execution attempt."); return 1; }
        if (plan.Expiry.IsExpired(DateTimeOffset.UtcNow))
        { error.WriteLine("plan.expired: The plan has expired."); return 1; }

        var executableItemIds = plan.Items
            .Where(item => ExecutionEligibilityValidator.ValidateItemForExecution(item).IsSuccess)
            .Select(item => item.ItemId).ToArray();
        if (executableItemIds.Length == 0)
        { error.WriteLine("plan.no-executable-items: No plan item is eligible for Phase 6 execution."); return 1; }

        var userSid = ResolveUserSid();
        var actionIds = plan.Items.Where(item => executableItemIds.Contains(item.ItemId, StringComparer.Ordinal))
            .Select(item => item.Action.SourceRuleId).Distinct(StringComparer.Ordinal).ToArray();
        var token = executionTokenService.Issue(plan, cliSessionId, userSid, actionIds, DateTimeOffset.UtcNow);
        var executor = new NonElevatedCleanupExecutor(executionTokenService, new SystemClock());
        var outcome = executor.Execute(plan, executableItemIds, token, cliSessionId, userSid, version, null, CancellationToken.None);
        executionReceiptStore?.SaveAsync(outcome.Receipt).GetAwaiter().GetResult();

        if (args.Contains("--json", StringComparer.Ordinal))
            output.WriteLine(JsonSerializer.Serialize(outcome.Receipt, ExecutionJson));
        else
        {
            output.WriteLine($"Execution {outcome.Receipt.ExecutionId}: {outcome.State}");
            output.WriteLine($"Removed {outcome.Receipt.Summary.RemovedCount}; skipped {outcome.Receipt.Summary.SkippedCount}; " +
                $"failed {outcome.Receipt.Summary.FailedCount}; removed logical bytes {outcome.Receipt.Summary.RemovedLogicalBytes}.");
        }
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
                    output.WriteLine($"{item.ExecutionId} {item.StartedAtUtc:O} {item.FinalState} removed={item.RemovedCount} skipped={item.SkippedCount} failed={item.FailedCount} bytes={item.RemovedLogicalBytes}");
                return 0;
            }
            if (args.Count == 3 && args[1] == "status")
            {
                if (!TryExecutionId(args[2], out var id)) return Usage(error, "A valid execution ID is required.");
                var receipt = executionReceiptStore.GetAsync(id).GetAwaiter().GetResult();
                if (receipt is null) return Missing(error, "Execution receipt not found.");
                output.WriteLine(receipt.FinalState.ToString());
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
