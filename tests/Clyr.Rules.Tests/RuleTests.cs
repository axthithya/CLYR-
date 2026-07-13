using Clyr.Rules;

namespace Clyr.Rules.Tests;

public sealed class RuleTests
{
    private readonly RuleValidator validator = new(File.ReadAllText(Path.Combine(RepositoryRoot(), "rules", "schemas", "rule.schema.json")));

    [Fact]
    public void ValidDetectionOnlyRuleIsAccepted()
    {
        var path = Path.Combine(RepositoryRoot(), "rules", "examples", "npm-cache.valid.yaml");
        var result = validator.ValidateFile(path);
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    [Fact]
    public void TraversalRuleIsRejected()
    {
        var path = Path.Combine(RepositoryRoot(), "rules", "examples", "path-traversal.invalid.yaml");
        Assert.False(validator.ValidateFile(path).IsValid);
    }

    [Fact]
    public void CommandRuleIsRejected()
    {
        var path = Path.Combine(RepositoryRoot(), "rules", "examples", "shell-command.invalid.yaml");
        Assert.False(validator.ValidateFile(path).IsValid);
    }

    [Fact]
    public void MalformedYamlIsRejected()
    {
        Assert.False(validator.ValidateYaml("value: [unterminated").IsValid);
    }

    [Fact]
    public void UnsupportedActionIsRejected()
    {
        var yaml = ValidYaml().Replace("type: report-only", "type: delete", StringComparison.Ordinal);
        Assert.False(validator.ValidateYaml(yaml).IsValid);
    }

    [Fact]
    public void UnsupportedSchemaVersionIsRejected()
    {
        var yaml = ValidYaml().Replace("schemaVersion: 1", "schemaVersion: 2", StringComparison.Ordinal);
        Assert.False(validator.ValidateYaml(yaml).IsValid);
    }

    [Fact]
    public void OversizedInputIsRejected()
    {
        var yaml = new string('x', RuleValidator.MaximumRuleBytes + 1);
        Assert.False(validator.ValidateYaml(yaml).IsValid);
    }

    private static string ValidYaml() => File.ReadAllText(Path.Combine(RepositoryRoot(), "rules", "examples", "npm-cache.valid.yaml"));

    private static string RepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
