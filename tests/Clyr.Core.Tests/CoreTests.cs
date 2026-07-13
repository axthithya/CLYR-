using Clyr.Core;

namespace Clyr.Core.Tests;

public sealed class CoreTests
{
    [Fact]
    public void PrivacyRedactorRemovesUserAndHomePath()
    {
        var environment = new FakeEnvironment();
        var redactor = new PrivacyRedactor(environment);
        var result = redactor.Redact("C:\\Users\\Alice\\file.txt belongs to Alice");
        Assert.DoesNotContain("Alice", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<home>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void PrivacyRedactorSanitizesExceptions()
    {
        var redactor = new PrivacyRedactor(new FakeEnvironment());
        var result = redactor.Redact(new InvalidOperationException("C:\\Users\\Alice\\private"));
        Assert.DoesNotContain("Alice", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DemoDataIsDeterministicAndClearlySynthetic()
    {
        var first = new DemoDataService().GetFindings();
        var second = new DemoDataService().GetFindings();
        Assert.Equal(first, second);
        Assert.All(first, item => Assert.Contains("Synthetic", item.Summary, StringComparison.Ordinal));
    }

    [Fact]
    public void PhaseOneConfigurationDefaultsAreSafe()
    {
        var configuration = ApplicationConfiguration.PhaseOneDefaults;
        Assert.Equal("Phase 1", configuration.Phase);
        Assert.True(configuration.DemoDataOnly);
    }

    private sealed class FakeEnvironment : IEnvironmentInfo
    {
        public string UserName => "Alice";
        public string UserProfilePath => "C:\\Users\\Alice";
        public string OperatingSystem => "Windows test";
        public string Architecture => "X64";
    }
}
