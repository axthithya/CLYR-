namespace Clyr.Contracts;

public readonly record struct RuleId
{
    public RuleId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct FindingId
{
    public FindingId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public enum RiskLevel
{
    Informational,
    Low,
    Medium,
    High,
    Prohibited
}

public enum FindingConfidence
{
    Unknown,
    Low,
    Medium,
    High,
    Confirmed
}

public sealed record DemoFinding(
    FindingId Id,
    RuleId RuleId,
    string Title,
    string Summary,
    long EstimatedBytes,
    RiskLevel Risk,
    FindingConfidence Confidence);

public sealed record RuleValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static RuleValidationResult Valid { get; } = new(true, Array.Empty<string>());
    public static RuleValidationResult Invalid(params string[] errors) => new(false, errors);
}
