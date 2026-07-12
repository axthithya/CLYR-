using Clyr.Contracts;

namespace Clyr.Core;

public sealed record ClyrError(string Code, string Message);
public sealed record Outcome<T>(T? Value, ClyrError? Error)
{
    public bool IsSuccess => Error is null;
    public static Outcome<T> Success(T value) => new(value, null);
    public static Outcome<T> Failure(string code, string message) => new(default, new(code, message));
}

public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
public interface IApplicationVersion { string Value { get; } }
public sealed class ApplicationVersion(string value) : IApplicationVersion { public string Value { get; } = value; }

public interface IPathPolicy { bool IsProtected(string normalizedPath); }

public sealed class SafePathPolicy : IPathPolicy
{
    public bool IsProtected(string normalizedPath) => normalizedPath.Contains(Windows, StringComparison.OrdinalIgnoreCase);
}

public interface IPrivacyRedactor { string Redact(string value); }
public sealed class PrivacyRedactor : IPrivacyRedactor
{
    public string Redact(string value) => value.Replace(Environment.UserName, <user>, StringComparison.OrdinalIgnoreCase);
}

public interface IDemoDataService { IReadOnlyList<DemoFinding> GetFindings(); }
public sealed class DemoDataService : IDemoDataService
{
    public IReadOnlyList<DemoFinding> GetFindings() => Array.Empty<DemoFinding>();
}
