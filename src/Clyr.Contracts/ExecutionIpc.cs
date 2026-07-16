using System.Collections.Immutable;

namespace Clyr.Contracts;

/// <summary>
/// Typed, closed IPC surface between CLYR and the elevated helper. Every message type is a sealed record with
/// concrete, non-polymorphic properties — there is no type-name field, no command field, no script field, and
/// no unrestricted argument list, so there is nothing here a hostile peer could redirect into arbitrary execution.
/// </summary>
public static class HelperProtocol
{
    public const int Version = 1;
    public const int MaxMessageBytes = 262_144;
    public const int MaxManifestItems = 512;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
}

public sealed record HelperTargetManifestItem(
    string ItemId, string TargetId, string CanonicalPath, long LogicalBytes,
    DateTimeOffset? LastWriteAtUtc, bool IsReparsePoint);

public sealed record HelperRequest(
    int ProtocolVersion, Guid RequestId, string Nonce, Guid ExecutionSessionId, string WindowsUserSid,
    string DriveIdentity, string ActionId, string TrustedRootIdentity, string TrustedRootPath,
    string PlanId, string PlanDigest, DateTimeOffset TokenIssuedAtUtc, DateTimeOffset TokenExpiresAtUtc,
    ImmutableArray<HelperTargetManifestItem> Targets);

public enum HelperResponseStatus { Completed, PartiallyCompleted, Rejected, Cancelled, Failed }

public sealed record HelperResponse(
    int ProtocolVersion, Guid RequestId, HelperResponseStatus Status, string? RejectionCode,
    string? RejectionMessage, ImmutableArray<ExecutionItemResult> Items, string HelperVersion);
