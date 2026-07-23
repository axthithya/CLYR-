using System.Security.Cryptography;
using System.Text.Json;
using Clyr.Contracts;

namespace Clyr.Core;

/// <summary>
/// A deterministic, content-derived identity for the exact cleanup-relevant evidence a completed analysis
/// currently carries: root contributions, scan coverage, allocation accounting, and per-category/per-finding
/// aggregate bytes and counts. Two <see cref="ScanResult"/> (or <see cref="StorageSnapshot"/>) instances that
/// expose identical evidence under this definition always produce the same identity, regardless of object
/// reference identity or how many times the same immutable result is reloaded or redisplayed. Two instances that
/// differ in any evidence a cleanup candidate could actually be built from — including an Administrator Retry
/// enrichment, which deliberately keeps the same <see cref="ScanResult.ScanId"/> — always produce different
/// identities. Display-only fields (titles, explanations, ranked path lists, wording) are deliberately excluded
/// so reopening a page, reformatting text, or any other cosmetic change never changes this identity. The
/// identity itself is an opaque SHA-256 digest — the same one-way-digest convention already used throughout this
/// codebase (see <see cref="CleanupPlanCanonicalizer"/>, <c>ExecutionReceiptCanonicalizer</c>) — so nothing raw
/// (paths, usernames) is ever exposed by displaying or exporting it.
/// </summary>
public static class EvidenceState
{
    private const int SchemaVersion = 1;

    /// <summary>The evidence-state identity for a cleanup plan built with no completed analysis result at all —
    /// for example, a plan built only from the always-live CLYR-owned-temp-artifact candidate, which carries no
    /// dependency on any <see cref="ScanResult"/>. Stable and deterministic so such a plan can still be genuinely
    /// re-validated later against "still no analysis result", rather than a fresh random value that could never
    /// match itself twice.</summary>
    public static readonly string NoResult = Digest(Utf8Bytes("evidence-state:no-analysis-result"));

    public static string ForResult(ScanResult result)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("scanId", result.ScanId);
            writer.WriteString("fileSystem", result.FileSystem);
            writer.WriteNumber("logicalBytesObserved", result.LogicalBytesObserved);
            WriteCoverage(writer, "coverage", result.Coverage);
            WriteAllocation(writer, result.Allocation);
            WriteClassification(writer, result.Classification);
            WriteRootContributions(writer, result.RootContributions);
            writer.WriteEndObject();
        }
        return Digest(stream.ToArray());
    }

    public static string ForSnapshot(StorageSnapshot snapshot)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("scanId", snapshot.ScanId);
            writer.WriteString("snapshotId", snapshot.Id);
            writer.WriteString("fileSystem", snapshot.Drive.FileSystem);
            writer.WriteNumber("logicalBytesObserved", snapshot.LogicalBytesObserved);
            WriteCoverage(writer, "coverage", snapshot.Coverage);
            writer.WriteStartArray("categories");
            foreach (var category in snapshot.Categories.OrderBy(item => item.Category).ThenBy(item => item.Status))
                WriteCategory(writer, category.Category, category.LogicalBytes, category.FileCount, category.Precision, category.Status);
            writer.WriteEndArray();
            writer.WriteStartArray("findings");
            foreach (var finding in snapshot.Findings.OrderBy(item => item.RuleId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("ruleId", finding.RuleId);
                writer.WriteString("ruleVersion", finding.RuleVersion);
                writer.WriteString("category", finding.Category.ToString());
                writer.WriteString("confidence", finding.Confidence.ToString());
                writer.WriteString("status", finding.Status.ToString());
                writer.WriteNumber("logicalBytes", finding.LogicalBytes);
                writer.WriteNumber("fileCount", finding.FileCount);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Digest(stream.ToArray());
    }

    private static void WriteCoverage(Utf8JsonWriter writer, string name, ScanCoverage coverage)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("filesObserved", coverage.FilesObserved);
        writer.WriteNumber("directoriesObserved", coverage.DirectoriesObserved);
        writer.WriteNumber("inaccessibleEntries", coverage.InaccessibleEntries);
        writer.WriteNumber("reparsePointsSkipped", coverage.ReparsePointsSkipped);
        writer.WriteNumber("cloudPlaceholdersObserved", coverage.CloudPlaceholdersObserved);
        writer.WriteNumber("changedEntries", coverage.ChangedEntries);
        writer.WriteNumber("otherSkippedEntries", coverage.OtherSkippedEntries);
        writer.WriteEndObject();
    }

    private static void WriteAllocation(Utf8JsonWriter writer, AllocationAccounting? allocation)
    {
        if (allocation is null) { writer.WriteNull("allocation"); return; }
        writer.WriteStartObject("allocation");
        writer.WriteNumber("allocatedBytesObserved", allocation.AllocatedBytesObserved);
        writer.WriteNumber("uniqueAllocatedBytesObserved", allocation.UniqueAllocatedBytesObserved);
        writer.WriteNumber("filesWithUnavailableAllocatedSize", allocation.FilesWithUnavailableAllocatedSize);
        writer.WriteNumber("sparseFileCount", allocation.SparseFileCount);
        writer.WriteNumber("compressedFileCount", allocation.CompressedFileCount);
        writer.WriteNumber("visibleHardLinkEntries", allocation.VisibleHardLinkEntries);
        writer.WriteNumber("uniqueFileIdentities", allocation.UniqueFileIdentities);
        writer.WriteString("consistency", allocation.Consistency.ToString());
        writer.WriteEndObject();
    }

    private static void WriteClassification(Utf8JsonWriter writer, ClassificationResult? classification)
    {
        if (classification is null) { writer.WriteNull("classification"); return; }
        writer.WriteStartObject("classification");
        var coverage = classification.Coverage;
        writer.WriteStartObject("coverage");
        writer.WriteNumber("classifiedFiles", coverage.ClassifiedFiles);
        writer.WriteNumber("classifiedBytes", coverage.ClassifiedBytes);
        writer.WriteNumber("unknownFiles", coverage.UnknownFiles);
        writer.WriteNumber("unknownBytes", coverage.UnknownBytes);
        writer.WriteNumber("inaccessibleEntries", coverage.InaccessibleEntries);
        writer.WriteNumber("skippedEntries", coverage.SkippedEntries);
        if (coverage.UnaccountedDriveBytes.HasValue) writer.WriteNumber("unaccountedDriveBytes", coverage.UnaccountedDriveBytes.Value);
        else writer.WriteNull("unaccountedDriveBytes");
        writer.WriteEndObject();
        writer.WriteStartArray("categories");
        foreach (var category in classification.Categories.OrderBy(item => item.Category).ThenBy(item => item.Status))
            WriteCategory(writer, category.Category, category.LogicalBytes, category.FileCount, category.Precision, category.Status);
        writer.WriteEndArray();
        writer.WriteStartArray("findings");
        foreach (var finding in classification.Findings.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", finding.Id);
            writer.WriteString("ruleId", finding.RuleId);
            writer.WriteString("ruleVersion", finding.RuleVersion);
            writer.WriteString("packVersion", finding.PackVersion);
            writer.WriteString("category", finding.Category.ToString());
            writer.WriteString("confidence", finding.Confidence.ToString());
            writer.WriteString("status", finding.Status.ToString());
            writer.WriteNumber("logicalBytes", finding.LogicalBytes);
            writer.WriteNumber("fileCount", finding.FileCount);
            writer.WriteString("precision", finding.Precision.ToString());
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteCategory(Utf8JsonWriter writer, StorageCategory category, long logicalBytes,
        long fileCount, MeasurementPrecision precision, FindingStatus status)
    {
        writer.WriteStartObject();
        writer.WriteString("category", category.ToString());
        writer.WriteNumber("logicalBytes", logicalBytes);
        writer.WriteNumber("fileCount", fileCount);
        writer.WriteString("precision", precision.ToString());
        writer.WriteString("status", status.ToString());
        writer.WriteEndObject();
    }

    private static void WriteRootContributions(Utf8JsonWriter writer, IReadOnlyList<ScanRootContribution> contributions)
    {
        writer.WriteStartArray("rootContributions");
        foreach (var root in contributions.OrderBy(item => item.CanonicalRootIdentity, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("canonicalRootIdentity", root.CanonicalRootIdentity);
            writer.WriteString("enumerationState", root.EnumerationState.ToString());
            writer.WriteNumber("filesExamined", root.FilesExamined);
            writer.WriteNumber("directoriesExamined", root.DirectoriesExamined);
            writer.WriteNumber("logicalBytesObserved", root.LogicalBytesObserved);
            writer.WriteNumber("allocatedBytesObserved", root.AllocatedBytesObserved);
            writer.WriteNumber("uniqueAllocatedBytesObservedWithinRoot", root.UniqueAllocatedBytesObservedWithinRoot);
            writer.WriteNumber("hardLinkEntriesDetected", root.HardLinkEntriesDetected);
            writer.WriteNumber("allocationUnavailableCount", root.AllocationUnavailableCount);
            writer.WriteNumber("sparseFileCount", root.SparseFileCount);
            writer.WriteNumber("compressedFileCount", root.CompressedFileCount);
            writer.WriteNumber("inaccessibleEntryCount", root.InaccessibleEntryCount);
            writer.WriteNumber("reparsePointsSkipped", root.ReparsePointsSkipped);
            writer.WriteNumber("diagnosticCount", root.DiagnosticCount);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static byte[] Utf8Bytes(string value) => System.Text.Encoding.UTF8.GetBytes(value);
}
