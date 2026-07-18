using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>Phase 7.2.6H2E: the one centrally coordinated timeout policy for the elevated permission-limited-root
/// retry workflow. Pure value checks only — no pipe, process, or real wait of any kind.</summary>
public sealed class ElevatedScanRetryTimeoutPolicyTests
{
    [Fact]
    public void ProductionPolicyHasABoundedMultiMinuteOperationBudget()
    {
        var policy = ElevatedScanRetryTimeoutPolicy.Default;

        Assert.True(policy.OperationBudget >= TimeSpan.FromMinutes(5));
        Assert.True(policy.OperationBudget <= TimeSpan.FromMinutes(30)); // bounded — never unlimited, never TimeSpan.MaxValue.
        Assert.Equal(TimeSpan.FromMinutes(10), policy.OperationBudget);
    }

    [Fact]
    public void ClientResponseDeadlineIsStrictlyLaterThanTheOperationDeadlinePlusResponseWriteMargin()
    {
        var policy = ElevatedScanRetryTimeoutPolicy.Default;

        // The critical invariant: the client must never stop waiting at (or before) the exact instant the helper
        // is still attempting to detect its own deadline and write its bounded timeout response.
        Assert.True(policy.ClientResponseDeadline > policy.OperationBudget + policy.ResponseWriteTimeout);
        Assert.Equal(policy.OperationBudget + policy.ClientResponseMargin, policy.ClientResponseDeadline);
    }

    [Fact]
    public void DerivedServerAndClientTimeoutsShareTheSameCoordinatedBudget()
    {
        var policy = ElevatedScanRetryTimeoutPolicy.Default;

        var server = policy.ToServerTimeouts();
        var client = policy.ToClientTimeouts();

        Assert.Equal(policy.OperationBudget, server.Operation);
        Assert.Equal(policy.ClientResponseDeadline, client.Read);
        Assert.True(client.Read > server.Operation + server.ResponseWrite);
    }

    [Fact]
    public void DefaultServerAndClientTimeoutsAreDerivedFromTheSamePolicy()
    {
        Assert.Equal(ElevatedScanRetryTimeoutPolicy.Default.ToServerTimeouts(), ElevatedScanIpcServerTimeouts.Default);
        Assert.Equal(ElevatedScanRetryTimeoutPolicy.Default.ToClientTimeouts(), ElevatedScanIpcClientTimeouts.Default);
    }
}
