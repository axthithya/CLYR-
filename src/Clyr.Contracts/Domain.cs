namespace Clyr.Contracts;

public readonly record struct RuleId
{
    public RuleId(string value) => Value = value.Trim();
    public string Value { get; }
}

public enum RiskLevel { Informational, Low, Medium, High }
public enum FindingConfidence { Low, Medium, High }
public readonly record struct FindingId(string Value);
public sealed record DemoFinding(FindingId Id, RuleId RuleId, string Title, string Summary, long EstimatedBytes, RiskLevel Risk, FindingConfidence Confidence);
public sealed record RuleValidationResult(bool IsValid, IReadOnlyList<string> Errors);
