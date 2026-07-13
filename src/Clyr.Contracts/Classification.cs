namespace Clyr.Contracts;

public enum StorageCategory
{
    WindowsSystemManaged, WindowsUpdateServicing, TemporaryFiles, CrashDumpsDiagnostics,
    BrowserCache, ApplicationCache, UserDownloads, UserDocuments, UserMedia,
    ArchivesInstallers, GamesLaunchers, CloudSync, DeveloperDependencies, DeveloperCache,
    Containers, VirtualMachines, Wsl, AndroidSdkEmulators, BuildOutput, Logs,
    RecycleBin, RestoreRecovery, Unknown
}

public enum FindingStatus { Informational, Review, Protected, Unknown }
public enum RulePackTrust { BuiltIn, ExternalUntrusted }

public sealed record CategorySummary(StorageCategory Category, long LogicalBytes, long FileCount,
    MeasurementPrecision Precision, FindingStatus Status);

public sealed record ClassificationCoverage(long ClassifiedFiles, long ClassifiedBytes, long UnknownFiles,
    long UnknownBytes, long InaccessibleEntries, long SkippedEntries, long? UnaccountedDriveBytes)
{
    public long ObservedFiles => ClassifiedFiles + UnknownFiles;
    public long ObservedBytes => ClassifiedBytes + UnknownBytes;
}

public sealed record FindingExplanation(string WhyItExists, string WhatItMeans, string SafetyStatus,
    string Evidence, IReadOnlyList<string> Limitations);

public sealed record StorageFinding(string Id, string RuleId, string RuleVersion, string PackId,
    string PackVersion, string PackDigest, string Title, StorageCategory Category,
    IReadOnlyList<string> Tags, FindingConfidence Confidence, FindingStatus Status,
    long LogicalBytes, long FileCount, MeasurementPrecision Precision,
    FindingExplanation Explanation);

public sealed record RulePackSummary(string Id, string Version, string Digest, RulePackTrust Trust,
    bool IsActive, bool IsVerified, int RuleCount, string StatusCode, string StatusMessage,
    string Provenance, string License);

public sealed record ClassificationResult(IReadOnlyList<CategorySummary> Categories,
    IReadOnlyList<StorageFinding> Findings, ClassificationCoverage Coverage,
    RulePackSummary RulePack, string Summary, IReadOnlyList<string> Limitations);
