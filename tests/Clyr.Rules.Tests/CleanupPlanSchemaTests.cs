using System.Text.Json;
using Json.Schema;

namespace Clyr.Rules.Tests;

public sealed class CleanupPlanSchemaTests
{
    [Fact]
    public void ValidExampleConformsAndUnsafeExampleFails()
    {
        var root = RepositoryRoot();
        var schema = JsonSchema.FromText(File.ReadAllText(
            Path.Combine(root, "rules", "schemas", "cleanup-plan-report.schema.json")));
        using var valid = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "rules", "examples", "cleanup-plan-report.valid.json")));
        using var invalid = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "rules", "examples", "cleanup-plan-report.invalid.json")));
        Assert.True(schema.Evaluate(valid.RootElement, new EvaluationOptions
        { OutputFormat = OutputFormat.List }).IsValid);
        Assert.False(schema.Evaluate(invalid.RootElement, new EvaluationOptions
        { OutputFormat = OutputFormat.List }).IsValid);
    }

    [Fact]
    public void SchemaForbidsRawPathsAndRequiresExecutionUnavailable()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "rules", "schemas",
            "cleanup-plan-report.schema.json"));
        var quote = (char)34;
        Assert.Contains(quote + "rawPathsIncluded" + quote + ": { " + quote + "const" + quote + ": false }",
            text, StringComparison.Ordinal);
        Assert.Contains(quote + "executionAvailability" + quote + ": { " + quote + "const" + quote + ": "
            + quote + "ExecutionNotAvailableInPhase5" + quote + " }", text, StringComparison.Ordinal);
        Assert.DoesNotContain(quote + "path" + quote + ":", text, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}

