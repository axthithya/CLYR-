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

    [Fact]
    public void ElevatedScanRetryValidationResultValidHasNoOutcomeDetail()
    {
        Assert.True(ElevatedScanRetryValidationResult.Valid.IsValid);
        Assert.Null(ElevatedScanRetryValidationResult.Valid.Detail);
    }

    [Fact]
    public void ElevatedScanRetryValidationResultInvalidCarriesOutcomeAndDetail()
    {
        var result = ElevatedScanRetryValidationResult.Invalid(ElevatedScanRetryValidationOutcome.Expired, "expired");
        Assert.False(result.IsValid);
        Assert.Equal(ElevatedScanRetryValidationOutcome.Expired, result.Outcome);
        Assert.Equal("expired", result.Detail);
    }

    [Fact]
    public void ElevatedScanOperationSupportsExactlyOneClosedOperation()
    {
        Assert.Single(Enum.GetValues<ElevatedScanOperation>());
        Assert.Equal(ElevatedScanOperation.RetryPermissionLimitedRoots, Enum.GetValues<ElevatedScanOperation>()[0]);
    }
}
