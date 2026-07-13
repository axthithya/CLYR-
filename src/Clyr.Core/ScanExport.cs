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
                LargestFiles = Redact(result.Root, result.LargestFiles, "file")
            }
        };
        return JsonSerializer.Serialize(report, Options);
    }

    private static RankedPath[] Redact(string root, IReadOnlyList<RankedPath> paths, string kind) =>
        paths.Select((item, index) => item with { DisplayPath = root + "<" + kind + "-" + (index + 1) + ">" }).ToArray();
}
