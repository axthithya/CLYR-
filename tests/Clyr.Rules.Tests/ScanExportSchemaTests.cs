using System.Text.Json;
using Clyr.Contracts;
using Clyr.Core;
using Json.Schema;

namespace Clyr.Rules.Tests;

public sealed class ScanExportSchemaTests
{
    [Fact]
    public void PhaseTwoSummaryConformsToVersionedSchema()
    {
        var now = DateTimeOffset.Parse("2026-07-13T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var result = new ScanResult(Guid.NewGuid(), ScanStatus.Completed, ScanMode.Quick, "C:\\", "NTFS", now, now,
            10, 20, 10, MeasurementPrecision.Estimated, "Logical metadata only.", new(1, 1, 0, 0, 0, 0, 0, false, false, false),
            [new("C:\\Users\\Fixture", 10, 1, MeasurementPrecision.Estimated)], [], [], [], [], null, null);
        var report = new ScanReportExporter().Serialize(result);
        var schemaText = File.ReadAllText(Path.Combine(RepositoryRoot(), "rules", "schemas", "scan-report.schema.json"));
        var schema = JsonSchema.FromText(schemaText);
        using var document = JsonDocument.Parse(report);
        var evaluation = schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(evaluation.IsValid, string.Join(Environment.NewLine, evaluation.Details?.Where(item => !item.IsValid).Select(item => item.EvaluationPath.ToString()) ?? []));
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
