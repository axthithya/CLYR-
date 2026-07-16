using System.Collections.Immutable;
using Clyr.Contracts;

namespace Clyr.Core.Execution;

/// <summary>
/// The helper's own validation and execution logic, independent of anything the requesting process claimed.
/// This runs inside the elevated helper process against the helper's own view of the filesystem — successful
/// IPC authentication never substitutes for this. Nothing here trusts the request beyond "well-formed input to
/// re-check"; every accept/reject decision is re-derived from the closed built-in allowlist and a live disk probe.
/// </summary>
public static class ElevatedHelperRequestHandler
{
    public static HelperResponse Handle(HelperRequest request, IClock clock, string helperVersion, CancellationToken cancellationToken)
    {
        if (request.ProtocolVersion != HelperProtocol.Version)
            return Rejected(request, helperVersion, "protocol.version-mismatch", "Unsupported protocol version.");
        if (request.Targets.IsDefaultOrEmpty || request.Targets.Length > HelperProtocol.MaxManifestItems)
            return Rejected(request, helperVersion, "request.manifest-bounds", "The target manifest is empty or exceeds the bounded limit.");
        if (string.IsNullOrWhiteSpace(request.Nonce) || request.Nonce.Length < 32)
            return Rejected(request, helperVersion, "request.nonce-invalid", "The request nonce is missing or too weak.");
        if (clock.UtcNow >= request.TokenExpiresAtUtc)
            return Rejected(request, helperVersion, "token.expired", "The execution token has expired.");
        if (string.IsNullOrWhiteSpace(request.PlanDigest) || request.PlanDigest.Length != 64)
            return Rejected(request, helperVersion, "request.plan-digest-invalid", "The plan digest is missing or malformed.");

        var capability = BuiltInExecutionActions.Find(request.ActionId);
        if (capability is null)
            return Rejected(request, helperVersion, "execution.unknown-action", "The action is not an enabled built-in.");
        if (!string.Equals(request.TrustedRootIdentity, capability.TrustedRootIdentity, StringComparison.Ordinal))
            return Rejected(request, helperVersion, "execution.root-mismatch", "The declared root does not match the enabled action's trusted root.");
        if (string.IsNullOrWhiteSpace(request.TrustedRootPath) || !Directory.Exists(request.TrustedRootPath))
            return Rejected(request, helperVersion, "execution.root-missing", "The trusted root could not be resolved on this system.");

        var results = ImmutableArray.CreateBuilder<ExecutionItemResult>();
        var cancelled = false;
        foreach (var target in request.Targets.OrderBy(item => item.ItemId, StringComparer.Ordinal).ThenBy(item => item.TargetId, StringComparer.Ordinal))
        {
            if (cancellationToken.IsCancellationRequested) { cancelled = true; break; }
            results.Add(ExecutionTargetProcessor.Process(clock, target.ItemId, target.TargetId, target.CanonicalPath,
                target.LogicalBytes, target.LastWriteAtUtc, target.IsReparsePoint, request.TrustedRootPath, capability.MinimumAge));
        }

        var status = DetermineStatus(cancelled, results);
        return new(HelperProtocol.Version, request.RequestId, status, null, null, results.ToImmutable(), helperVersion);
    }

    private static HelperResponseStatus DetermineStatus(bool cancelled, ImmutableArray<ExecutionItemResult>.Builder results)
    {
        if (cancelled) return results.Any(item => item.Outcome == ExecutionItemOutcome.Removed)
            ? HelperResponseStatus.PartiallyCompleted : HelperResponseStatus.Cancelled;
        var removed = results.Count(item => item.Outcome == ExecutionItemOutcome.Removed);
        var failed = results.Count(item => item.Outcome == ExecutionItemOutcome.Failed);
        var skipped = results.Count - removed - failed;
        if (failed > 0 && removed == 0 && skipped == 0) return HelperResponseStatus.Failed;
        if (failed > 0 || (skipped > 0 && removed > 0)) return HelperResponseStatus.PartiallyCompleted;
        return HelperResponseStatus.Completed;
    }

    private static HelperResponse Rejected(HelperRequest request, string helperVersion, string code, string message) =>
        new(HelperProtocol.Version, request.RequestId, HelperResponseStatus.Rejected, code, message,
            ImmutableArray<ExecutionItemResult>.Empty, helperVersion);
}
