using Clyr.Core;
using Clyr.Windows;

namespace Clyr.Windows.Tests;

public sealed class WindowsServiceTests
{
    [Fact]
    public void LocalLogRedactsPersonalValues()
    {
        var directory = Path.Combine(Path.GetTempPath(), "clyr-log-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var environment = new FakeEnvironment();
            var log = new PrivacySafeLocalLog(new FixedClock(), new PrivacyRedactor(environment), directory);
            log.Information("test.event", "C:\\Users\\Alice\\private belongs to Alice");
            var content = File.ReadAllText(Path.Combine(directory, "clyr.jsonl"));
            Assert.DoesNotContain("Alice", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("test.event", content, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void WindowsEnvironmentReportsNonEmptyMetadata()
    {
        var environment = new WindowsEnvironmentInfo();
        Assert.False(string.IsNullOrWhiteSpace(environment.OperatingSystem));
        Assert.False(string.IsNullOrWhiteSpace(environment.Architecture));
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 13, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeEnvironment : IEnvironmentInfo
    {
        public string UserName => "Alice";
        public string UserProfilePath => "C:\\Users\\Alice";
        public string OperatingSystem => "Windows test";
        public string Architecture => "X64";
    }
}
