using System.Security.Cryptography;
using System.Text.Json;
using Clyr.Contracts;

namespace Clyr.Core.Execution;

public static class ExecutionReceiptCanonicalizer
{
    public static string Digest(ExecutionReceipt receipt) => Convert.ToHexString(SHA256.HashData(CanonicalBytes(receipt))).ToLowerInvariant();

    public static byte[] CanonicalBytes(ExecutionReceipt receipt)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", receipt.SchemaVersion);
            writer.WriteString("executionId", receipt.ExecutionId.ToString());
            writer.WriteString("sourcePlanId", receipt.SourcePlanId.ToString());
            writer.WriteString("sourcePlanDigest", receipt.SourcePlanDigest);
            writer.WriteString("applicationVersion", receipt.ApplicationVersion);
            writer.WriteString("rulePackVersion", receipt.RulePackVersion);
            writer.WriteString("driveIdentityFingerprint", receipt.DriveIdentityFingerprint);
            writer.WriteString("startedAtUtc", receipt.StartedAtUtc.ToUniversalTime());
            if (receipt.CompletedAtUtc.HasValue) writer.WriteString("completedAtUtc", receipt.CompletedAtUtc.Value.ToUniversalTime());
            else writer.WriteNull("completedAtUtc");
            writer.WriteString("finalState", receipt.FinalState.ToString());
            writer.WriteBoolean("cancelled", receipt.Cancelled);
            writer.WriteBoolean("elevationUsed", receipt.ElevationUsed);
            writer.WriteStartObject("summary");
            writer.WriteNumber("totalItems", receipt.Summary.TotalItems);
            writer.WriteNumber("removedCount", receipt.Summary.RemovedCount);
            writer.WriteNumber("skippedCount", receipt.Summary.SkippedCount);
            writer.WriteNumber("failedCount", receipt.Summary.FailedCount);
            writer.WriteNumber("plannedLogicalBytes", receipt.Summary.PlannedLogicalBytes);
            writer.WriteNumber("removedLogicalBytes", receipt.Summary.RemovedLogicalBytes);
            writer.WriteNumber("skippedLogicalBytes", receipt.Summary.SkippedLogicalBytes);
            writer.WriteNumber("failedLogicalBytes", receipt.Summary.FailedLogicalBytes);
            writer.WriteEndObject();
            writer.WriteString("privacyMode", receipt.PrivacyMode);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}
