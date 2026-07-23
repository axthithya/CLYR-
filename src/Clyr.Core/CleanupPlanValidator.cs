using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Clyr.Contracts;

namespace Clyr.Core;

public sealed class CleanupPlanValidator
{
    public static PlanValidationResult Validate(CleanupPlan plan, PlanValidationContext context)
    {
        var diagnostics = ImmutableArray.CreateBuilder<PlanDiagnostic>();
        var violations = ImmutableArray.CreateBuilder<ProtectedPathViolation>();
        if (plan.SchemaVersion != CleanupPlanningConstants.PlanSchemaVersion)
            diagnostics.Add(Error("plan.schema", "The cleanup-plan schema is unsupported."));
        if (!FixedDigest(plan.Digest, CleanupPlanCanonicalizer.Digest(plan)))
            diagnostics.Add(Error("plan.digest", "The cleanup-plan integrity digest does not match."));
        if (plan.Expiry.IsExpired(context.NowUtc)) diagnostics.Add(Error("plan.expired", "The cleanup plan has expired."));
        Compare(plan.Binding.SourceScanId == context.SourceScanId, "plan.scan-stale", "The source scan changed.");
        Compare(plan.Binding.SourceSnapshotId == context.SourceSnapshotId, "plan.snapshot-stale", "The source snapshot changed.");
        Compare(string.Equals(plan.Binding.DriveIdentity, context.DriveIdentity, StringComparison.Ordinal), "plan.drive-stale", "The drive identity changed.");
        Compare(string.Equals(plan.Binding.SourceRulePackId, context.RulePackId, StringComparison.Ordinal)
            && string.Equals(plan.Binding.SourceRulePackVersion, context.RulePackVersion, StringComparison.Ordinal)
            && string.Equals(plan.Binding.SourceRulePackDigest, context.RulePackDigest, StringComparison.Ordinal),
            "plan.rules-stale", "The rule pack changed.");
        Compare(string.Equals(plan.Binding.CategoryRegistryVersion, context.CategoryRegistryVersion, StringComparison.Ordinal),
            "plan.categories-stale", "The category registry changed.");
        Compare(string.Equals(plan.Binding.ApplicationCompatibilityVersion, context.ApplicationCompatibilityVersion, StringComparison.Ordinal),
            "plan.application-stale", "The application compatibility version changed.");
        Compare(string.Equals(plan.Binding.PrivacyMode, context.PrivacyMode, StringComparison.Ordinal),
            "plan.privacy-stale", "The privacy mode changed.");
        // The Administrator Retry correction: ScanId alone is insufficient, because a successful retry
        // deliberately keeps the original ScanId while enriching root contributions, coverage, allocation, and
        // findings — exactly the evidence cleanup candidates are built from. EvidenceStateId is a content digest
        // over that evidence (see EvidenceState), so it changes whenever the evidence does, even though the scan,
        // snapshot, drive, rule pack, category registry, and privacy mode all still match.
        Compare(string.Equals(plan.Binding.EvidenceStateId, context.CurrentEvidenceStateId, StringComparison.Ordinal),
            "plan.evidence-stale", "The analysis evidence changed after this plan was created.");
        foreach (var item in plan.Items)
        {
            if (item.Eligibility != CleanupEligibility.DryRunEligible
                || item.Action.ExecutionAvailability is not (ExecutionAvailability.ExecutionNotAvailableInPhase5
                    or ExecutionAvailability.Phase6BuiltInExecutable))
                diagnostics.Add(Error("plan.action-unavailable", "A plan item crossed the supported action boundary.", item.ItemId));
            foreach (var target in item.Targets)
            {
                if (target.CanonicalPath is null) continue;
                var current = context.CurrentTargets.GetValueOrDefault(target.TargetId);
                if (current is null || !SameMetadata(target, current))
                    diagnostics.Add(Error("plan.target-changed", "Target metadata or identity changed after observation.", item.ItemId));
                var root = current?.CanonicalPath ?? target.CanonicalPath;
                var result = WindowsPathSafetyValidator.Validate(target.CanonicalPath, root, target.IsReparsePoint);
                if (!result.IsValid)
                {
                    diagnostics.Add(Error(result.Code, result.Message, item.ItemId));
                    if (result.IsProtected) violations.Add(new(item.ItemId, target.TargetId, result.Code, result.Message));
                }
            }
        }
        var status = diagnostics.Any(item => item.Severity == PlanDiagnosticSeverity.Error)
            ? diagnostics.Any(item => item.Code == "plan.expired") ? CleanupPlanStatus.Expired
            : diagnostics.Any(item => item.Code.EndsWith("-stale", StringComparison.Ordinal)
                || item.Code == "plan.target-changed") ? CleanupPlanStatus.Stale
            : CleanupPlanStatus.Invalid
            : CleanupPlanStatus.Valid;
        return new(status == CleanupPlanStatus.Valid, status, diagnostics.ToImmutable(), violations.ToImmutable());

        void Compare(bool condition, string code, string message) { if (!condition) diagnostics.Add(Error(code, message)); }
    }

    private static bool SameMetadata(CleanupTarget left, CleanupTarget right) =>
        string.Equals(left.VolumeIdentity, right.VolumeIdentity, StringComparison.Ordinal)
        && string.Equals(left.StableFileIdentity, right.StableFileIdentity, StringComparison.Ordinal)
        && left.LogicalBytes == right.LogicalBytes && left.LastWriteAtUtc == right.LastWriteAtUtc
        && left.IsReparsePoint == right.IsReparsePoint && left.IsCloudPlaceholder == right.IsCloudPlaceholder;
    private static bool FixedDigest(string left, string right)
    {
        if (left.Length != right.Length) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
    }
    private static PlanDiagnostic Error(string code, string message, string? itemId = null) =>
        new(code, PlanDiagnosticSeverity.Error, message, itemId);
}

