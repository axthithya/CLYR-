using System.Text.Json;
using Clyr.Contracts;

namespace Clyr.Core.Execution;

/// <summary>
/// Strict, bounded JSON framing for the helper IPC protocol. Every type serialized here is a sealed record with
/// concrete properties (see <see cref="HelperRequest"/>/<see cref="HelperResponse"/>); System.Text.Json's default
/// reflection contract has no polymorphic type discriminator support unless one is explicitly opted into, and this
/// code never does, so there is no unsafe polymorphic deserialization surface.
/// </summary>
public static class HelperIpcSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        MaxDepth = 12,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict
    };

    public static byte[] SerializeRequest(HelperRequest request) => Bound(JsonSerializer.SerializeToUtf8Bytes(request, Options));
    public static byte[] SerializeResponse(HelperResponse response) => Bound(JsonSerializer.SerializeToUtf8Bytes(response, Options));

    public static HelperRequest DeserializeRequest(byte[] bytes)
    {
        Bound(bytes);
        return JsonSerializer.Deserialize<HelperRequest>(bytes, Options)
            ?? throw new InvalidDataException("The helper request could not be parsed.");
    }

    public static HelperResponse DeserializeResponse(byte[] bytes)
    {
        Bound(bytes);
        return JsonSerializer.Deserialize<HelperResponse>(bytes, Options)
            ?? throw new InvalidDataException("The helper response could not be parsed.");
    }

    private static byte[] Bound(byte[] data) =>
        data.Length <= HelperProtocol.MaxMessageBytes ? data : throw new InvalidDataException("The IPC message exceeds the bounded size limit.");
}
