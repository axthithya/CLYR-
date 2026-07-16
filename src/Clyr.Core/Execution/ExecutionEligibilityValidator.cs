using Clyr.Contracts;

namespace Clyr.Core.Execution;

/// <summary>
/// Pre-execution eligibility gate applied to every plan item immediately before it may execute.
/// Any uncertainty results in rejection; nothing here grants authority by itself — see <see cref="IExecutionTokenService"/>
/// and <see cref="NonElevatedCleanupExecutor"/> for the token and per-target TOCTOU revalidation that must also pass.
/// </summary>
public static class ExecutionEligibilityValidator
{
    public static Outcome<ExecutionCapability> ValidateItemForExecution(CleanupPlanItem item)
    {
        if (item.Eligibility != CleanupEligibility.DryRunEligible)
            return Outcomes.Failure<ExecutionCapability>("execution.not-eligible", "Only DryRunEligible items can execute.");
        if (item.Risk != RiskLevel.Low)
            return Outcomes.Failure<ExecutionCapability>("execution.risk", "Only Low risk items can execute in Phase 6.");
        if (item.Action.ActionType != CleanupActionType.TrustedBuiltInCleanup)
            return Outcomes.Failure<ExecutionCapability>("execution.action-type", "Only the built-in trusted action type can execute.");
        if (item.Action.ExecutionAvailability != ExecutionAvailability.Phase6BuiltInExecutable)
            return Outcomes.Failure<ExecutionCapability>("execution.unavailable", "The action does not declare Phase 6 execution availability.");
        if (item.Action.RequiresElevation)
            return Outcomes.Failure<ExecutionCapability>("execution.elevation-unsupported", "No enabled built-in action currently requires elevation.");

        var capability = BuiltInExecutionActions.Find(item.Action.SourceRuleId);
        if (capability is null)
            return Outcomes.Failure<ExecutionCapability>("execution.unknown-action", "The action is not an enabled built-in.");
        if (!string.Equals(item.Action.AllowedRootIdentity, capability.TrustedRootIdentity, StringComparison.Ordinal))
            return Outcomes.Failure<ExecutionCapability>("execution.root-mismatch", "The declared root does not match the enabled action's trusted root.");
        if (item.Targets.Length == 0)
            return Outcomes.Failure<ExecutionCapability>("execution.no-targets", "The item has no exact targets to execute.");
        if (item.Targets.Length > capability.MaxItems)
            return Outcomes.Failure<ExecutionCapability>("execution.too-many-targets", "The item exceeds the action's bounded manifest size.");
        return Outcomes.Success(capability);
    }
}
