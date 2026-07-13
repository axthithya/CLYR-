using Clyr.Core;
using Clyr.Persistence;
using Clyr.Rules;

namespace Clyr.Integration.Tests;

public sealed class FoundationIntegrationTests
{
    [Fact]
    public void DemoPersistenceAndRulesComposeWithoutScanning()
    {
        var root = RepositoryRoot();
        var schema = File.ReadAllText(Path.Combine(root, "rules", "schemas", "rule.schema.json"));
        var rulePath = Path.Combine(root, "rules", "examples", "npm-cache.valid.yaml");
        var validator = new RuleValidator(schema);
        var database = new AppMetadataDatabase("Data Source=:memory:");
        Assert.True(validator.ValidateFile(rulePath).IsValid);
        Assert.NotEmpty(database.GetSqliteVersion());
        Assert.NotEmpty(new DemoDataService().GetFindings());
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
