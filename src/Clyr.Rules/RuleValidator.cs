using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Clyr.Contracts;
using Json.Schema;
using YamlDotNet.Serialization;

namespace Clyr.Rules;

public sealed class RuleValidator
{
    public const int MaximumRuleBytes = 262_144;
    private static readonly ConcurrentDictionary<string, JsonSchema> Schemas = new(StringComparer.Ordinal);
    private readonly JsonSchema schema;

    public RuleValidator(string schemaJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaJson);
        schema = Schemas.GetOrAdd(schemaJson, text => JsonSchema.FromText(text));
    }

    public RuleValidationResult ValidateFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        if (!info.Exists) return RuleValidationResult.Invalid("Rule file not found.");
        if (info.Length > MaximumRuleBytes) return RuleValidationResult.Invalid("Rule input exceeds the safe size limit.");
        return ValidateYaml(File.ReadAllText(path, Encoding.UTF8));
    }

    public RuleValidationResult ValidateYaml(string yaml)
    {
        if (Encoding.UTF8.GetByteCount(yaml) > MaximumRuleBytes) return RuleValidationResult.Invalid("Rule input exceeds the safe size limit.");
        try
        {
            var deserializer = new DeserializerBuilder().WithDuplicateKeyChecking().WithAttemptingUnquotedStringTypeDeserialization().Build();
            var model = deserializer.Deserialize<object>(yaml);
            var json = new SerializerBuilder().JsonCompatible().Build().Serialize(model);
            using var document = JsonDocument.Parse(json);
            if (json.Contains("..\\/", StringComparison.Ordinal) || json.Contains("..\\", StringComparison.Ordinal))
                return RuleValidationResult.Invalid("Parent-directory traversal is prohibited.");
            var result = schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (result.IsValid) return RuleValidationResult.Valid;
            IEnumerable<EvaluationResults> evaluations = result.Details ?? Enumerable.Empty<EvaluationResults>();
            var details = evaluations.Where(item => !item.IsValid).Select(item => item.InstanceLocation + " does not satisfy " + item.EvaluationPath).Take(8).ToArray();
            return details.Length == 0 ? RuleValidationResult.Invalid("Rule does not satisfy the detection-only JSON Schema.") : new RuleValidationResult(false, details);
        }
        catch (Exception exception) when (exception is YamlDotNet.Core.YamlException or JsonException or InvalidOperationException)
        {
            return RuleValidationResult.Invalid("Malformed YAML: " + exception.Message);
        }
    }
}
