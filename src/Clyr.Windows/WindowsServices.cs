using System.Runtime.InteropServices;
using System.Text.Json;
using Clyr.Core;

namespace Clyr.Windows;

public sealed class WindowsEnvironmentInfo : IEnvironmentInfo
{
    public string UserName => Environment.UserName;
    public string UserProfilePath => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string OperatingSystem => RuntimeInformation.OSDescription;
    public string Architecture => RuntimeInformation.OSArchitecture.ToString();
}

public sealed class PrivacySafeLocalLog : ILocalLog
{
    private readonly object sync = new();
    private readonly IClock clock;
    private readonly IPrivacyRedactor redactor;
    private readonly string logFile;

    public PrivacySafeLocalLog(IClock clock, IPrivacyRedactor redactor, string logDirectory)
    {
        this.clock = clock;
        this.redactor = redactor;
        Directory.CreateDirectory(logDirectory);
        logFile = Path.Combine(logDirectory, "clyr.jsonl");
    }

    public void Information(string eventName, string message) => Write(eventName, "Information", message);
    public void Failure(string eventName, Exception exception) => Write(eventName, "Error", redactor.Redact(exception));

    private void Write(string eventName, string level, string message)
    {
        var record = new
        {
            timestamp = clock.UtcNow,
            level,
            eventName,
            message = redactor.Redact(message)
        };
        var line = JsonSerializer.Serialize(record) + Environment.NewLine;
        lock (sync) File.AppendAllText(logFile, line);
    }
}
