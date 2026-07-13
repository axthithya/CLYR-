using System.Text.Json;
using Clyr.Contracts;
using Clyr.Core;
using Json.Schema;

namespace Clyr.Rules.Tests;

public sealed class ClassifiedReportTests
{
    [Fact]
    public void ClassifiedSummaryConformsToVersionTwoSchemaAndContainsNoRawFindingPath()
    {
        var now = DateTimeOffset.Parse("2026-07-13T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var pack = new RulePackSummary("clyr.builtin", "1.0.0", new string('a', 64), RulePackTrust.BuiltIn,
            true, true, 1, "rule.pack-verified", "verified", "first-party", "MIT");
        var explanation = new FindingExplanation("A tool stores cache data.", "It may be downloaded again.",
            "Report-only.", "Matched metadata.", ["No content was read."]);
        var finding = new StorageFinding("stable-id", "developer.cache", "1.0.0", pack.Id, pack.Version,
            pack.Digest, "Developer cache", StorageCategory.DeveloperCache, ["developer", "cache"],
            FindingConfidence.High, FindingStatus.Informational, 10, 1, MeasurementPrecision.Estimated, explanation);
        var classification = new ClassificationResult(
            [new(StorageCategory.DeveloperCache, 10, 1, MeasurementPrecision.Estimated, FindingStatus.Informational)],
            [finding], new(1, 10, 0, 0, 0, 0, 10), pack, "One cause identified.", ["Metadata only."]);
        var root = Path.GetPathRoot(Environment.SystemDirectory)!;
        var result = new ScanResult(Guid.NewGuid(), ScanStatus.Completed, ScanMode.Quick, root, "NTFS", now, now,
            10, 20, 10, MeasurementPrecision.Estimated, "Logical metadata only.", new(1, 1, 0, 0, 0, 0, 0, false, false, false),
            [new(Path.Combine(root, "Users", "Alice"), 10, 1, MeasurementPrecision.Estimated)], [], [], [], [], null, null, classification);

        var report = new ScanReportExporter().Serialize(result);
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(RepositoryRoot(), "rules", "schemas", "classified-report.schema.json")));
        using var document = JsonDocument.Parse(report);
        var evaluation = schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(evaluation.IsValid);
        Assert.DoesNotContain("Alice", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clyr-classified-summary", report, StringComparison.Ordinal);
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
