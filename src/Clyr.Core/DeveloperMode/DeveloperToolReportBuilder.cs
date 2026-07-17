using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Clyr.Contracts;

namespace Clyr.Core.DeveloperMode;

/// <summary>
/// Groups the existing Phase 5 <see cref="CleanupCandidate"/> output (from the same classification/rule engine
/// every other page uses) by Phase 7's developer-tool taxonomy. This does not scan the filesystem itself and
/// does not add a second detection pipeline — it re-labels findings the classifier already produced. Docker and
/// WSL storage findings are included here too (they still come from the rule engine); their live install/version
/// status is added separately by <see cref="DeveloperToolRegistry"/> from the narrow read-only probe.
/// </summary>
public static class DeveloperToolReportBuilder
{
    public static ImmutableArray<DeveloperToolReport> FromScan(ScanResult result)
    {
        if (result.Classification is null) return [];
        var ruleIdByFindingId = result.Classification.Findings
            .ToDictionary(finding => finding.Id, finding => finding.RuleId, StringComparer.Ordinal);
        var candidates = CleanupCandidateFactory.FromScan(result);
        return Build(candidates, id => ruleIdByFindingId.GetValueOrDefault(id));
    }

    public static ImmutableArray<DeveloperToolReport> FromSnapshot(StorageSnapshot snapshot)
    {
        var ruleIdByFindingId = snapshot.Findings
            .ToDictionary(finding => SnapshotFindingId(snapshot.Id, finding.RuleId), finding => finding.RuleId, StringComparer.Ordinal);
        var candidates = CleanupCandidateFactory.FromSnapshot(snapshot);
        return Build(candidates, id => ruleIdByFindingId.GetValueOrDefault(id));
    }

    private static ImmutableArray<DeveloperToolReport> Build(IReadOnlyList<CleanupCandidate> candidates, Func<string, string?> ruleIdForFinding)
    {
        var byTool = new Dictionary<DeveloperToolId, List<CleanupCandidate>>();
        foreach (var candidate in candidates)
        {
            var ruleId = ruleIdForFinding(candidate.FindingId);
            if (ruleId is null) continue;
            var tool = DeveloperToolTaxonomy.ToolFor(ruleId);
            if (tool is null) continue;
            if (!byTool.TryGetValue(tool.Value, out var list)) byTool[tool.Value] = list = [];
            list.Add(candidate);
        }

        var reports = ImmutableArray.CreateBuilder<DeveloperToolReport>();
        foreach (var (tool, list) in byTool)
        {
            var status = list.All(item => item.Confidence is FindingConfidence.High or FindingConfidence.Confirmed)
                ? DeveloperToolStatus.FullyDetected : DeveloperToolStatus.PartiallyDetected;
            var totalBytes = list.Sum(item => item.Impact.ObservedLogicalBytes);
            reports.Add(new(tool, status, null, "classification", [.. list.OrderBy(item => item.Title, StringComparer.Ordinal)], [], totalBytes, null));
        }
        return reports.ToImmutable();
    }

    private static string SnapshotFindingId(Guid id, string ruleId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{id:D}|{ruleId}"))).ToLowerInvariant()[..24];
}
