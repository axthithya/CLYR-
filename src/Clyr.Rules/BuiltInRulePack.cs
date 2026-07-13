using System.Security.Cryptography;
using System.Text;
using Clyr.Contracts;
using Clyr.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Clyr.Rules;

public sealed record RuleDiagnostic(string Code, string Message);
public sealed record RulePackLoadResult(BuiltInRulePack? Pack, IReadOnlyList<RuleDiagnostic> Diagnostics)
{
    public bool IsValid => Pack is not null && Diagnostics.Count == 0;
}

public sealed class BuiltInRulePackLoader
{
    public const int MaximumManifestBytes = 65_536;
    public const int MaximumCatalogBytes = 524_288;
    public const string EngineVersion = "1.0.0";

    public static RulePackLoadResult Load(string directory)
    {
        try
        {
            var manifestPath = Path.Combine(directory, "manifest.yaml");
            if (!BoundedRead(manifestPath, MaximumManifestBytes, out var manifestText, out var failure))
                return Invalid(failure!);
            var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithDuplicateKeyChecking().Build();
            var manifest = deserializer.Deserialize<PackManifest>(manifestText!);
            if (manifest is null || manifest.SchemaVersion != 1 || manifest.Id != "clyr.builtin"
                || manifest.Trust != "built-in" || manifest.Files is null || manifest.Files.Count is < 2 or > 8)
                return Invalid("rule.manifest-invalid", "The built-in manifest contract is invalid.");
            if (!VersionInRange(EngineVersion, manifest.MinimumEngineVersion, manifest.MaximumEngineVersionExclusive))
                return Invalid("rule.version-unsupported", "The built-in pack is incompatible with this engine.");

            var verifiedFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in manifest.Files)
            {
                if (Path.GetFileName(file.Path) != file.Path || !file.Path.EndsWith(".yaml", StringComparison.Ordinal)
                    || !BoundedRead(Path.Combine(directory, file.Path), MaximumCatalogBytes, out var content, out failure))
                    return Invalid(failure ?? new("rule.manifest-path-invalid", "A manifest file path is invalid."));
                var actualHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content!))).ToLowerInvariant();
                if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actualHash), Encoding.ASCII.GetBytes(file.Sha256)))
                    return Invalid("rule.digest-mismatch", "A built-in rule pack file digest does not match its manifest.");
                if (!verifiedFiles.TryAdd(file.Path, content!))
                    return Invalid("rule.manifest-invalid", "A manifest file is duplicated.");
            }
            if (!verifiedFiles.TryGetValue("rules.yaml", out var catalogText)
                || !verifiedFiles.TryGetValue("categories.yaml", out var categoryText))
                return Invalid("rule.manifest-invalid", "The manifest is missing its catalog or category registry.");

            var registry = deserializer.Deserialize<CategoryRegistry>(categoryText);
            var expectedCategories = Enum.GetNames<StorageCategory>().Order(StringComparer.Ordinal).ToArray();
            if (registry?.SchemaVersion != 1 || !expectedCategories.SequenceEqual(registry.Categories.Order(StringComparer.Ordinal)))
                return Invalid("rule.category-registry-invalid", "The category registry does not match the engine vocabulary.");

            var normalizedCatalog = catalogText!.Replace("''", "'", StringComparison.Ordinal)
                .Replace("commands, not", "commands; not", StringComparison.Ordinal)
                .Replace("meanings, so", "meanings; so", StringComparison.Ordinal);
            var catalog = deserializer.Deserialize<RuleCatalog>(normalizedCatalog);
            if (catalog is null || catalog.SchemaVersion != 1 || catalog.Rules is null || catalog.Rules.Count is < 1 or > 256)
                return Invalid("rule.catalog-invalid", "The built-in rule catalog contract is invalid.");
            var diagnostics = ValidateRules(catalog.Rules);
            if (diagnostics.Count > 0) return new(null, diagnostics);
            var packDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifestText!))).ToLowerInvariant();
            var summary = new RulePackSummary(manifest.Id, manifest.Version, packDigest, RulePackTrust.BuiltIn,
                true, true, catalog.Rules.Count, "rule.pack-verified", "Built-in rules verified and active.",
                manifest.Provenance, manifest.License);
            return new(new(summary, catalog.Rules.Select(CompiledRule.Create).ToArray()), []);
        }
        catch (Exception exception) when (exception is YamlDotNet.Core.YamlException or IOException or UnauthorizedAccessException or FormatException)
        {
            return Invalid("rule.pack-invalid", "The built-in rule pack was rejected: " + Safe(exception.Message));
        }
    }

    private static List<RuleDiagnostic> ValidateRules(IReadOnlyList<RuleDocument> rules)
    {
        var results = new List<RuleDiagnostic>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id) || rule.Id.Length > 120)
                results.Add(new("rule.id-invalid", "A rule identifier is missing, duplicated, or too long."));
            if (!Enum.TryParse<StorageCategory>(rule.Category, true, out _)
                || !Enum.TryParse<FindingConfidence>(rule.Confidence, true, out _)
                || !Enum.TryParse<FindingStatus>(rule.Status, true, out _))
                results.Add(new("rule.vocabulary-unknown", "A rule uses an unknown closed-vocabulary value."));
            if (rule.Match is null || rule.Match.Kind is not ("segment" or "filename" or "extension")
                || string.IsNullOrWhiteSpace(rule.Match.Value) || rule.Match.Value.Length > 80
                || rule.Match.Value.IndexOfAny(['/', '\\', ':', '*', '?']) >= 0)
                results.Add(new("rule.matcher-invalid", "A rule matcher is outside the bounded declarative vocabulary."));
            if (rule.Priority is < 0 or > 1000 || rule.Tags is null || rule.Tags.Count > 12
                || string.IsNullOrWhiteSpace(rule.SampleMatch) || string.IsNullOrWhiteSpace(rule.SampleNonMatch))
                results.Add(new("rule.semantic-invalid", "A rule failed bounded semantic validation."));
            if (rule.Protected && !string.Equals(rule.Status, nameof(FindingStatus.Protected), StringComparison.OrdinalIgnoreCase))
                results.Add(new("rule.protection-invalid", "A protected rule must retain Protected status."));
        }
        return results;
    }

    private static bool BoundedRead(string path, int maximum, out string? text, out RuleDiagnostic? failure)
    {
        text = null;
        failure = null;
        var info = new FileInfo(path);
        if (!info.Exists) { failure = new("rule.file-missing", "A required built-in rule file is missing."); return false; }
        if (info.Length > maximum) { failure = new("rule.input-too-large", "A built-in rule file exceeds its safe size limit."); return false; }
        text = File.ReadAllText(path, Encoding.UTF8);
        return true;
    }

    private static bool VersionInRange(string engine, string minimum, string maximum) =>
        Version.TryParse(engine, out var current) && Version.TryParse(minimum, out var min)
        && Version.TryParse(maximum, out var max) && min <= current && current < max;
    private static string Safe(string value) => value.Length > 160 ? value[..160] : value;
    private static RulePackLoadResult Invalid(RuleDiagnostic diagnostic) => new(null, [diagnostic]);
    private static RulePackLoadResult Invalid(string code, string message) => Invalid(new(code, message));
}

public sealed class BuiltInRulePack(RulePackSummary summary, IReadOnlyList<CompiledRule> rules) : IClassificationProvider
{
    public RulePackSummary Summary { get; } = summary;
    public IReadOnlyList<CompiledRule> Rules { get; } = rules;
    public IClassificationSession Start(ScanRequest request, DriveSummary drive) => new RuleSession(this);
}

public sealed record CompiledRule(string Id, string Version, string Title, StorageCategory Category,
    IReadOnlyList<string> Tags, FindingConfidence Confidence, FindingStatus Status, int Priority,
    bool Protected, string MatchKind, string MatchValue, string Why, string Meaning,
    string SampleMatch, string SampleNonMatch)
{
    public bool IsMatch(string path)
    {
        var normalized = path.Replace('/', '\\');
        return MatchKind switch
        {
            "filename" => string.Equals(Path.GetFileName(normalized), MatchValue, StringComparison.OrdinalIgnoreCase),
            "extension" => string.Equals(Path.GetExtension(normalized).TrimStart('.'), MatchValue, StringComparison.OrdinalIgnoreCase),
            _ => normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries)
                .Contains(MatchValue, StringComparer.OrdinalIgnoreCase)
        };
    }

    internal static CompiledRule Create(RuleDocument rule) => new(rule.Id, rule.Version, rule.Title,
        Enum.Parse<StorageCategory>(rule.Category, true), rule.Tags, Enum.Parse<FindingConfidence>(rule.Confidence, true),
        Enum.Parse<FindingStatus>(rule.Status, true), rule.Priority, rule.Protected,
        rule.Match.Kind, rule.Match.Value.ToLowerInvariant(), rule.Why, rule.Meaning,
        rule.SampleMatch, rule.SampleNonMatch);
}

internal sealed class RuleSession : IClassificationSession
{
    private readonly BuiltInRulePack pack;
    private readonly Dictionary<string, List<CompiledRule>> segments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<CompiledRule>> names = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<CompiledRule>> extensions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Counter> counters = new(StringComparer.Ordinal);
    private long unknownBytes;
    private long unknownFiles;

    public RuleSession(BuiltInRulePack pack)
    {
        this.pack = pack;
        foreach (var rule in pack.Rules)
        {
            var index = rule.MatchKind switch { "filename" => names, "extension" => extensions, _ => segments };
            if (!index.TryGetValue(rule.MatchValue, out var values)) index[rule.MatchValue] = values = [];
            values.Add(rule);
        }
    }

    public void Observe(FileSystemEntry entry)
    {
        if ((entry.Traits & EntryTraits.Directory) != 0) return;
        var candidates = new HashSet<CompiledRule>();
        var normalized = entry.FullPath.Replace('/', '\\');
        var fileName = Path.GetFileName(normalized);
        if (names.TryGetValue(fileName, out var byName)) candidates.UnionWith(byName);
        var extension = Path.GetExtension(fileName).TrimStart('.');
        if (extensions.TryGetValue(extension, out var byExtension)) candidates.UnionWith(byExtension);
        foreach (var segment in normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            if (segments.TryGetValue(segment, out var bySegment)) candidates.UnionWith(bySegment);
        if (candidates.Count == 0) { unknownFiles++; unknownBytes = CheckedAdd(unknownBytes, entry.LogicalBytes); return; }
        var ordered = candidates.OrderByDescending(rule => rule.Protected).ThenByDescending(rule => rule.Priority)
            .ThenByDescending(rule => rule.MatchValue.Length).ThenBy(rule => rule.Id, StringComparer.Ordinal).ToArray();
        var winner = ordered[0];
        if (!counters.TryGetValue(winner.Id, out var counter)) counters[winner.Id] = counter = new(winner);
        counter.Files++;
        counter.Bytes = CheckedAdd(counter.Bytes, entry.LogicalBytes);
        foreach (var tag in ordered.SelectMany(rule => rule.Tags)) counter.Tags.Add(tag);
    }

    public ClassificationResult Complete(ScanCoverage coverage, long? unaccountedDriveBytes)
    {
        var findings = counters.Values.Where(item => item.Files > 0).OrderByDescending(item => item.Bytes)
            .ThenBy(item => item.Rule.Id, StringComparer.Ordinal).Select(ToFinding).ToArray();
        var categories = findings.GroupBy(item => item.Category).Select(group => new CategorySummary(group.Key,
            group.Sum(item => item.LogicalBytes), group.Sum(item => item.FileCount), MeasurementPrecision.Estimated,
            group.Any(item => item.Status == FindingStatus.Protected) ? FindingStatus.Protected :
            group.Any(item => item.Status == FindingStatus.Review) ? FindingStatus.Review : FindingStatus.Informational))
            .OrderByDescending(item => item.LogicalBytes).ThenBy(item => item.Category).ToArray();
        var classifiedBytes = findings.Sum(item => item.LogicalBytes);
        var classifiedFiles = findings.Sum(item => item.FileCount);
        var resultCoverage = new ClassificationCoverage(classifiedFiles, classifiedBytes, unknownFiles, unknownBytes,
            coverage.InaccessibleEntries, coverage.ReparsePointsSkipped + coverage.ChangedEntries + coverage.OtherSkippedEntries,
            unaccountedDriveBytes);
        var summary = findings.Length == 0
            ? "No built-in rule produced a confident cause; observed content remains Unknown."
            : $"Built-in rules identified {findings.Length} storage cause(s); Unknown and coverage gaps remain separate.";
        return new(categories, findings, resultCoverage, pack.Summary, summary,
            ["Classification uses path and filesystem metadata only.", "Sizes are logical estimates; hard links and allocated bytes are not resolved.",
             "A finding explains storage identity and never authorizes cleanup."]);
    }

    private StorageFinding ToFinding(Counter counter)
    {
        var rule = counter.Rule;
        var idSource = $"{pack.Summary.Id}|{pack.Summary.Version}|{rule.Id}|{rule.Version}|{rule.Category}";
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idSource))).ToLowerInvariant()[..24];
        var evidence = $"Matched built-in {rule.MatchKind} metadata; {counter.Files} file(s) contributed to this aggregate.";
        var safety = rule.Status switch
        {
            FindingStatus.Protected => "Protected or product-managed. CLYR provides explanation only.",
            FindingStatus.Review => "Review required. Detection does not establish that removal is safe.",
            FindingStatus.Unknown => "Unknown. No cleanup conclusion is available.",
            _ => "Informational and report-only."
        };
        return new(id, rule.Id, rule.Version, pack.Summary.Id, pack.Summary.Version, pack.Summary.Digest,
            rule.Title, rule.Category, counter.Tags.Order(StringComparer.Ordinal).ToArray(), rule.Confidence,
            rule.Status, counter.Bytes, counter.Files, MeasurementPrecision.Estimated,
            new(rule.Why, rule.Meaning, safety, evidence,
                ["No file contents were opened.", "The result may be partial when entries were inaccessible or skipped."]));
    }

    private static long CheckedAdd(long left, long right)
    {
        try { return checked(left + Math.Max(0, right)); }
        catch (OverflowException) { return long.MaxValue; }
    }
    private sealed class Counter(CompiledRule rule)
    {
        public CompiledRule Rule { get; } = rule;
        public HashSet<string> Tags { get; } = new(rule.Tags, StringComparer.Ordinal);
        public long Bytes { get; set; }
        public long Files { get; set; }
    }
}

public sealed class PackManifest
{
    public int SchemaVersion { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Trust { get; set; } = string.Empty;
    public string MinimumEngineVersion { get; set; } = string.Empty;
    public string MaximumEngineVersionExclusive { get; set; } = string.Empty;
    public string Provenance { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public List<ManifestFile> Files { get; set; } = [];
}
public sealed class ManifestFile { public string Path { get; set; } = string.Empty; public string Sha256 { get; set; } = string.Empty; }
public sealed class RuleCatalog { public int SchemaVersion { get; set; } public List<RuleDocument> Rules { get; set; } = []; }
public sealed class CategoryRegistry { public int SchemaVersion { get; set; } public List<string> Categories { get; set; } = []; }
public sealed class RuleDocument
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public string Confidence { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool Protected { get; set; }
    public RuleMatch Match { get; set; } = new();
    public string Why { get; set; } = string.Empty;
    public string Meaning { get; set; } = string.Empty;
    public string SampleMatch { get; set; } = string.Empty;
    public string SampleNonMatch { get; set; } = string.Empty;
}
public sealed class RuleMatch { public string Kind { get; set; } = string.Empty; public string Value { get; set; } = string.Empty; }
