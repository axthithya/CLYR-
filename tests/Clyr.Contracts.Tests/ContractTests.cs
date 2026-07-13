using Clyr.Contracts;

namespace Clyr.Contracts.Tests;

public sealed class ContractTests
{
    [Fact]
    public void RuleIdTrimsAndPreservesValue()
    {
        var id = new RuleId("  developer.npm.cache  ");
        Assert.Equal("developer.npm.cache", id.Value);
        Assert.Equal(id.Value, id.ToString());
    }

    [Fact]
    public void RuleIdRejectsBlankValue()
    {
        Assert.Throws<ArgumentException>(() => new RuleId(" "));
    }

    [Fact]
    public void InvalidRuleResultCarriesErrors()
    {
        var result = RuleValidationResult.Invalid("invalid");
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }
}
