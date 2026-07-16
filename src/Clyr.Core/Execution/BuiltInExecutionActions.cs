using Clyr.Contracts;

namespace Clyr.Core.Execution;

/// <summary>
/// The closed, deliberately tiny Phase 6 execution allowlist. Nothing outside this registry can execute.
/// </summary>
public static class BuiltInExecutionActions
{
    public const string ClyrOwnedTempArtifactsId = "builtin.clyr-owned-temp-artifacts";

    public static readonly ExecutionCapability ClyrOwnedTempArtifacts = new(
        ClyrOwnedTempArtifactsId,
        "Remove stale CLYR-owned temporary artifacts",
        Enabled: true,
        Risk: RiskLevel.Low,
        TrustedRootIdentity: "known-folder:local-app-data/clyr/temp",
        MinimumAge: TimeSpan.FromDays(7),
        MaxItems: 512,
        MaxTotalBytes: 536_870_912,
        RequiresElevation: false,
        Explanation:
            "CLYR writes its own short-lived scratch files (export staging buffers, diagnostic snapshots) under its " +
            "private LocalAppData\\Clyr\\Temp folder. No other application or the user ever writes there. Files " +
            "older than 7 days are stale scratch data that CLYR regenerates automatically on demand.");

    public static readonly ExecutionPolicy Policy = new(SchemaVersion: 1, EnabledActions: [ClyrOwnedTempArtifacts]);

    public static ExecutionCapability? Find(string actionId) =>
        Policy.EnabledActions.FirstOrDefault(action =>
            action.Enabled && string.Equals(action.ActionId, actionId, StringComparison.Ordinal));
}
