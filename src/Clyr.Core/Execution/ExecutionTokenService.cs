using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Clyr.Contracts;

namespace Clyr.Core.Execution;

public interface IExecutionTokenService
{
    ExecutionToken Issue(CleanupPlan plan, ExecutionSessionId sessionId, string windowsUserSid,
        IReadOnlyList<string> actionIds, DateTimeOffset nowUtc);

    Outcome<ExecutionToken> Validate(ExecutionToken token, CleanupPlan plan, ExecutionSessionId sessionId,
        string windowsUserSid, DateTimeOffset nowUtc);

    bool Consume(Guid tokenId);
}

/// <summary>
/// One-time, in-memory, single-process execution tokens. A token is authority to run one execution request
/// once; it is never persisted and never survives process restart, so it cannot be replayed across sessions.
/// </summary>
public sealed class ExecutionTokenService : IExecutionTokenService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<Guid, byte> issued = new();
    private readonly ConcurrentDictionary<Guid, byte> consumed = new();

    public ExecutionToken Issue(CleanupPlan plan, ExecutionSessionId sessionId, string windowsUserSid,
        IReadOnlyList<string> actionIds, DateTimeOffset nowUtc)
    {
        if (actionIds.Count == 0) throw new ArgumentException("At least one action ID is required.", nameof(actionIds));
        var tokenId = Guid.NewGuid();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var token = new ExecutionToken(tokenId, plan.Id, plan.Digest, sessionId, windowsUserSid,
            plan.Binding.DriveIdentity, [.. actionIds], nowUtc, nowUtc + TokenLifetime, nonce);
        issued[tokenId] = 1;
        return token;
    }

    public Outcome<ExecutionToken> Validate(ExecutionToken token, CleanupPlan plan, ExecutionSessionId sessionId,
        string windowsUserSid, DateTimeOffset nowUtc)
    {
        if (!issued.ContainsKey(token.TokenId))
            return Outcomes.Failure<ExecutionToken>("token.unknown", "The execution token was not issued by this application session.");
        if (consumed.ContainsKey(token.TokenId))
            return Outcomes.Failure<ExecutionToken>("token.consumed", "The execution token has already been used.");
        if (token.IsExpired(nowUtc))
            return Outcomes.Failure<ExecutionToken>("token.expired", "The execution token has expired.");
        if (!FixedEquals(token.PlanId.ToString(), plan.Id.ToString()))
            return Outcomes.Failure<ExecutionToken>("token.plan-mismatch", "The token does not match the active plan.");
        if (!FixedEquals(token.PlanDigest, plan.Digest))
            return Outcomes.Failure<ExecutionToken>("token.digest-mismatch", "The plan changed after the token was issued.");
        if (!FixedEquals(token.ApplicationSessionId.ToString(), sessionId.ToString()))
            return Outcomes.Failure<ExecutionToken>("token.session-mismatch", "The token does not match the current application session.");
        if (!FixedEquals(token.WindowsUserSid, windowsUserSid))
            return Outcomes.Failure<ExecutionToken>("token.user-mismatch", "The token does not match the current Windows user.");
        if (!FixedEquals(token.DriveIdentity, plan.Binding.DriveIdentity))
            return Outcomes.Failure<ExecutionToken>("token.drive-mismatch", "The drive identity changed.");
        return Outcomes.Success(token);
    }

    public bool Consume(Guid tokenId) => issued.ContainsKey(tokenId) && consumed.TryAdd(tokenId, 1);

    private static bool FixedEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
