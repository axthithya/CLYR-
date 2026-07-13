using Clyr.Contracts;

namespace Clyr.Core;

public sealed record ClyrError(string Code, string Message);

public readonly record struct Outcome<T>
{
    internal Outcome(T? value, ClyrError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }
    public ClyrError? Error { get; }
    public bool IsSuccess => Error is null;
}

public static class Outcomes
{
    public static Outcome<T> Success<T>(T value) => new(value, null);
    public static Outcome<T> Failure<T>(string code, string message) => new(default, new(code, message));
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface IApplicationVersion
{
    string Value { get; }
}

public sealed class ApplicationVersion(string value) : IApplicationVersion
{
    public string Value { get; } = value;
}

public sealed record ApplicationConfiguration(string Phase, bool DemoDataOnly)
{
    public static ApplicationConfiguration PhaseOneDefaults { get; } = new("Phase 1", true);
    public static ApplicationConfiguration PhaseTwoDefaults { get; } = new("Phase 2", false);
}

public interface IEnvironmentInfo
{
    string UserName { get; }
    string UserProfilePath { get; }
    string OperatingSystem { get; }
    string Architecture { get; }
}

public interface IPrivacyRedactor
{
    string Redact(string? value);
    string Redact(Exception exception);
}

public sealed class PrivacyRedactor(IEnvironmentInfo environment) : IPrivacyRedactor
{
    public string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var result = Replace(result: value, environment.UserProfilePath, "<home>");
        return Replace(result, environment.UserName, "<user>");
    }

    public string Redact(Exception exception) => Redact(exception.ToString());

    private static string Replace(string result, string sensitive, string replacement)
    {
        if (string.IsNullOrWhiteSpace(sensitive)) return result;
        return result.Replace(sensitive, replacement, StringComparison.OrdinalIgnoreCase);
    }
}

public interface ILocalLog
{
    void Information(string eventName, string message);
    void Failure(string eventName, Exception exception);
}

public interface IDemoDataService
{
    IReadOnlyList<DemoFinding> GetFindings();
}

public sealed class DemoDataService : IDemoDataService
{
    private static readonly IReadOnlyList<DemoFinding> Findings = new DemoFinding[]
    {
        new(new FindingId("demo-001"), new RuleId("demo.cache"), "Demo cache", "Synthetic example only.", 52_428_800, RiskLevel.Low, FindingConfidence.High),
        new(new FindingId("demo-002"), new RuleId("demo.logs"), "Demo logs", "Synthetic example only.", 8_388_608, RiskLevel.Informational, FindingConfidence.High)
    };

    public IReadOnlyList<DemoFinding> GetFindings() => Findings;
}

public enum StartupState
{
    Starting,
    Ready,
    Failed
}
