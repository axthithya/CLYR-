using System.Text.Json;
using System.Text.Json.Serialization;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Core.DeveloperMode;

namespace Clyr.Cli;

public sealed partial class CliApplication
{
    private static readonly JsonSerializerOptions DeveloperJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    private readonly TrustedExecutableLocator developerLocator = new();
    private readonly DeveloperToolProbeRunner developerProbeRunner = new();

    private int Developer(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Count >= 2 && args[1] == "tools") return DeveloperTools(args, output);
            if (args.Count >= 2 && args[1] == "scan") return DeveloperScan(args, output, error).GetAwaiter().GetResult();
            if (args.Count >= 3 && args[1] == "show") return DeveloperShow(args, output, error).GetAwaiter().GetResult();
            if (args.Count >= 2 && args[1] == "findings") return DeveloperFindings(args, output, error).GetAwaiter().GetResult();
            if (args.Count >= 3 && args[1] == "plan") return DeveloperPlan(args, output, error);
            if (args.Count >= 2 && args[1] == "capabilities") return DeveloperCapabilities(args, output);
            if (args.Count >= 2 && args[1] == "doctor") return DeveloperDoctor(args, output);
        }
        catch (InvalidOperationException exception) { error.WriteLine("developer.invalid: " + exception.Message); return 2; }
        error.WriteLine("Usage: clyr developer tools [--json] | scan --snapshot <id> [--json] | show <tool-id> --snapshot <id> [--json] | findings --snapshot <id> [--json] | plan <finding-id> --snapshot <id> [--json] | capabilities [--json] | doctor [--json]");
        return 2;
    }

    private static int DeveloperTools(IReadOnlyList<string> args, TextWriter output)
    {
        var json = args.Contains("--json", StringComparer.Ordinal);
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(DeveloperToolRegistry.Descriptors.Select(item => new
            {
                item.Id,
                item.DisplayName,
                item.RequiresProbe,
                item.Explanation
            }), DeveloperJson));
            return 0;
        }
        foreach (var descriptor in DeveloperToolRegistry.Descriptors)
            output.WriteLine($"{descriptor.Id} — {descriptor.DisplayName}{(descriptor.RequiresProbe ? " (status probe available)" : " (storage evidence only)")}");
        return 0;
    }

    private async Task<int> DeveloperScan(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (snapshotStore is null) { error.WriteLine("developer.unavailable: Snapshot history is unavailable."); return 3; }
        if (!TryOption(args, "--snapshot", out var value) || !Guid.TryParse(value, out var id))
            return Usage(error, "A valid --snapshot <id> is required.");
        var snapshot = await snapshotStore.GetAsync(id).ConfigureAwait(false);
        if (snapshot is null) return Missing(error, "Snapshot not found.");
        var reports = await DetectAsync(snapshot).ConfigureAwait(false);
        if (args.Contains("--json", StringComparer.Ordinal)) { output.WriteLine(JsonSerializer.Serialize(reports.Select(SafeReport), DeveloperJson)); return 0; }
        foreach (var report in reports)
            output.WriteLine($"{report.ToolId} {report.Status} version={report.DetectedVersion ?? "unknown"} candidates={report.Candidates.Length} observedBytes={report.TotalObservedLogicalBytes}");
        return 0;
    }

    private async Task<int> DeveloperShow(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (snapshotStore is null) { error.WriteLine("developer.unavailable: Snapshot history is unavailable."); return 3; }
        if (!Enum.TryParse<DeveloperToolId>(args[2], ignoreCase: true, out var toolId)) return Usage(error, "A valid tool ID is required.");
        if (!TryOption(args, "--snapshot", out var value) || !Guid.TryParse(value, out var id))
            return Usage(error, "A valid --snapshot <id> is required.");
        var snapshot = await snapshotStore.GetAsync(id).ConfigureAwait(false);
        if (snapshot is null) return Missing(error, "Snapshot not found.");
        var reports = await DetectAsync(snapshot).ConfigureAwait(false);
        var report = reports.FirstOrDefault(item => item.ToolId == toolId);
        if (report is null) return Missing(error, "Tool not found in the registry.");
        if (args.Contains("--json", StringComparer.Ordinal)) { output.WriteLine(JsonSerializer.Serialize(SafeReport(report), DeveloperJson)); return 0; }
        output.WriteLine($"{report.ToolId}: {report.Status}");
        output.WriteLine($"Version: {report.DetectedVersion ?? "unknown"}; discovery: {report.ExecutableDiscoverySource ?? "none"}");
        output.WriteLine($"Observed logical bytes: {report.TotalObservedLogicalBytes}; candidates: {report.Candidates.Length}");
        foreach (var diagnostic in report.Diagnostics) output.WriteLine($"  {diagnostic.Code}: {diagnostic.Message}");
        foreach (var candidate in report.Candidates)
            output.WriteLine($"  - {candidate.Title} [{candidate.Eligibility}/{candidate.Risk}] {candidate.Impact.ObservedLogicalBytes} bytes");
        return 0;
    }

    private async Task<int> DeveloperFindings(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (snapshotStore is null) { error.WriteLine("developer.unavailable: Snapshot history is unavailable."); return 3; }
        if (!TryOption(args, "--snapshot", out var value) || !Guid.TryParse(value, out var id))
            return Usage(error, "A valid --snapshot <id> is required.");
        var snapshot = await snapshotStore.GetAsync(id).ConfigureAwait(false);
        if (snapshot is null) return Missing(error, "Snapshot not found.");
        var reports = await DetectAsync(snapshot).ConfigureAwait(false);
        var findings = reports.SelectMany(report => report.Candidates.Select(candidate => (report.ToolId, candidate))).ToArray();
        if (args.Contains("--json", StringComparer.Ordinal))
        {
            output.WriteLine(JsonSerializer.Serialize(findings.Select(item => new
            {
                item.ToolId,
                item.candidate.FindingId,
                item.candidate.Title,
                item.candidate.Eligibility,
                item.candidate.Risk,
                item.candidate.Confidence,
                observedLogicalBytes = item.candidate.Impact.ObservedLogicalBytes
            }), DeveloperJson));
            return 0;
        }
        foreach (var (toolId, candidate) in findings)
            output.WriteLine($"{toolId} {candidate.FindingId} {candidate.Eligibility} {candidate.Risk} {candidate.Impact.ObservedLogicalBytes} {candidate.Title}");
        return 0;
    }

    private int DeveloperPlan(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (snapshotStore is null || cleanupPlanStore is null) { error.WriteLine("developer.unavailable: Planning is unavailable."); return 3; }
        if (!TryOption(args, "--snapshot", out var value) || !Guid.TryParse(value, out var id))
            return Usage(error, "A valid --snapshot <id> is required.");
        var snapshot = snapshotStore.GetAsync(id).GetAwaiter().GetResult();
        if (snapshot is null) return Missing(error, "Snapshot not found.");
        var findingId = args[2];
        var candidates = CleanupCandidateFactory.FromSnapshot(snapshot);
        if (candidates.All(candidate => candidate.FindingId != findingId)) return Missing(error, "Developer finding not found in this snapshot.");
        var plan = CleanupPlanBuilder.Create(new(snapshot.ScanId, snapshot.Id, snapshot.Drive.Fingerprint,
            snapshot.RulePackId, snapshot.RulePackVersion, snapshot.RulePackDigest, version, "support-safe",
            DateTimeOffset.UtcNow, candidates, [findingId]));
        cleanupPlanStore.Save(plan);
        if (args.Contains("--json", StringComparer.Ordinal)) { output.WriteLine(SafePlanJson(plan)); return 0; }
        output.WriteLine($"Integrity-checked cleanup plan {plan.Id} created from developer finding {findingId}.");
        output.WriteLine($"Digest: {plan.Digest}");
        return 0;
    }

    private static int DeveloperCapabilities(IReadOnlyList<string> args, TextWriter output)
    {
        // No developer-tool finding is currently allowlisted for Phase 6 execution — every one remains
        // dry-run/report-only or manual-review. This truthfully reflects that closed allowlist rather than
        // fabricating a capability that does not exist.
        if (args.Contains("--json", StringComparer.Ordinal))
        {
            output.WriteLine(JsonSerializer.Serialize(new { executableDeveloperActions = Array.Empty<string>(), note = "No developer-tool action is currently enabled for execution." }, DeveloperJson));
            return 0;
        }
        output.WriteLine("No developer-tool action is currently enabled for execution.");
        output.WriteLine("Every developer finding remains dry-run (report-only) or manual-review only.");
        return 0;
    }

    private int DeveloperDoctor(IReadOnlyList<string> args, TextWriter output)
    {
        var json = args.Contains("--json", StringComparer.Ordinal);
        var rows = DeveloperToolRegistry.Descriptors.Select(descriptor =>
        {
            if (!descriptor.RequiresProbe)
                return (descriptor.Id, Status: "relies on the most recent storage analysis — run clyr scan or provide --snapshot <id>");
            var located = developerLocator.Locate(descriptor);
            return (descriptor.Id, Status: located is null ? "no trusted executable found" : "found via " + located.DiscoverySource);
        }).ToArray();
        if (json) { output.WriteLine(JsonSerializer.Serialize(rows.Select(row => new { row.Id, row.Status }), DeveloperJson)); return 0; }
        foreach (var (id, status) in rows) output.WriteLine($"{id}: {status}");
        return 0;
    }

    private async Task<IReadOnlyList<DeveloperToolReport>> DetectAsync(StorageSnapshot snapshot)
    {
        var classification = DeveloperToolReportBuilder.FromSnapshot(snapshot);
        var reports = await DeveloperToolRegistry.DetectAllAsync(classification, developerLocator, developerProbeRunner, CancellationToken.None).ConfigureAwait(false);
        return reports;
    }

    private static object SafeReport(DeveloperToolReport report) => new
    {
        report.ToolId,
        report.Status,
        report.DetectedVersion,
        report.TotalObservedLogicalBytes,
        candidates = report.Candidates.Select(candidate => new
        {
            candidate.FindingId,
            candidate.Title,
            candidate.Eligibility,
            candidate.Risk,
            candidate.Confidence,
            observedLogicalBytes = candidate.Impact.ObservedLogicalBytes
        }),
        diagnostics = report.Diagnostics
    };
}
