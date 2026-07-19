using System.Text.Json;
using System.Text.Json.Serialization;
using Clyr.Contracts;

namespace Clyr.Core;

public interface IScanReportExporter { string Serialize(ScanResult result); }

public sealed class ScanReportExporter : IScanReportExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    public string Serialize(ScanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Classification is not null) return SerializeClassified(result);
        var report = new
        {
            schemaVersion = 1,
            reportType = "clyr-scan-summary",
            generatedAt = result.EndedAt,
            privacy = new
            {
                classification = "support-safe",
                fullPathsIncluded = false,
                userNamesIncluded = false,
                fileNamesIncluded = false,
                fileContentsIncluded = false,
                uploadedAutomatically = false
            },
            scan = result with
            {
                TopLevelDirectories = Redact(result.Root, result.TopLevelDirectories, "folder"),
                LargestDirectories = Redact(result.Root, result.LargestDirectories, "directory"),
                LargestFiles = Redact(result.Root, result.LargestFiles, "file"),
                RootContributions = RedactContributions(result.Root, result.RootContributions),
                // Section 14: never export a negative unobserved-bytes figure as a valid storage measurement —
                // an existing, already-nullable field (schema-compatible, no new field needed). The
                // accounting-consistency status (Allocation.Consistency, including LogicalExceedsDriveUsed) is
                // already preserved unchanged elsewhere in this same exported result.
                UnaccountedBytes = PresentableUnaccountedBytes(result.UnaccountedBytes)
            }
        };
        return JsonSerializer.Serialize(report, Options);
    }

    private static string SerializeClassified(ScanResult result)
    {
        var report = new
        {
            schemaVersion = 2,
            reportType = "clyr-classified-summary",
            generatedAt = result.EndedAt,
            privacy = new
            {
                classification = "support-safe",
                fullPathsIncluded = false,
                userNamesIncluded = false,
                fileNamesIncluded = false,
                fileContentsIncluded = false,
                uploadedAutomatically = false
            },
            scan = result with
            {
                TopLevelDirectories = Redact(result.Root, result.TopLevelDirectories, "folder"),
                LargestDirectories = Redact(result.Root, result.LargestDirectories, "directory"),
                LargestFiles = Redact(result.Root, result.LargestFiles, "file"),
                RootContributions = RedactContributions(result.Root, result.RootContributions),
                // Section 14: never export a negative unobserved-bytes figure as a valid storage measurement —
                // an existing, already-nullable field (schema-compatible, no new field needed). The
                // accounting-consistency status (Allocation.Consistency, including LogicalExceedsDriveUsed) is
                // already preserved unchanged elsewhere in this same exported result.
                UnaccountedBytes = PresentableUnaccountedBytes(result.UnaccountedBytes)
            }
        };
        return JsonSerializer.Serialize(report, Options);
    }

    private static long? PresentableUnaccountedBytes(long? value) => value is < 0 ? null : value;

    private static RankedPath[] Redact(string root, IReadOnlyList<RankedPath> paths, string kind) =>
        paths.Select((item, index) => item with { DisplayPath = root + "<" + kind + "-" + (index + 1) + ">" }).ToArray();

    // Phase 7.2.6G2: root-contribution paths and their derived canonical identity are exactly as
    // privacy-sensitive as the ranked-path fields above (both can contain a username or other personal
    // directory name) and get the same redaction treatment before ever leaving this exporter.
    private static ScanRootContribution[] RedactContributions(string root, IReadOnlyList<ScanRootContribution> contributions) =>
        contributions.Select((item, index) => item with
        {
            RootPath = root + "<root-" + (index + 1) + ">",
            CanonicalRootIdentity = "<redacted>",
            StableRootIdentifier = null
        }).ToArray();
}
