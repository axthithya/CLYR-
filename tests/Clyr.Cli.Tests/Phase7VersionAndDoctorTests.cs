using Clyr.Cli;
using Clyr.Core;
using Clyr.Rules;

namespace Clyr.Cli.Tests;

public sealed class Phase7VersionAndDoctorTests
{
    [Fact]
    public void VersionReportsThePhase7DevelopmentVersion()
    {
        var output = new StringWriter();
        Assert.Equal(0, CreateApplication().Run(["--version"], output, TextWriter.Null));
        Assert.Equal("CLYR 0.7.0-phase7", output.ToString().Trim());
    }

    [Fact]
    public void DoctorDescribesTheCurrentGuardedExecutionBoundaryTruthfully()
    {
        var output = new StringWriter();
        Assert.Equal(0, CreateApplication().Run(["doctor"], output, TextWriter.Null));
        var text = output.ToString();

        // Truthfully describes what is actually enabled — not a blanket "read-only" claim, and not broad cleanup.
        Assert.Contains("guarded low-risk execution is enabled only for approved CLYR-owned temporary artifacts", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("validated active-session plan", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("arbitrary paths and general cleanup are unavailable", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no developer-tool action is currently enabled for execution", text, StringComparison.OrdinalIgnoreCase);

        // Must not regress to the stale Phase 5 "read-only scanner only" framing.
        Assert.DoesNotContain("read-only scanner available", text, StringComparison.OrdinalIgnoreCase);

        // Must not overclaim: no broad/general cleanup support, and no developer-tool execution capability implied
        // as present — Phase 7 only added read-only detection, never execution.
        foreach (var overclaim in new[] { "general cleanup available", "cleanup any file", "Developer Mode available", "tool adapters" })
            Assert.DoesNotContain(overclaim, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VersionAndDoctorAgreeWithApplicationVersionMetadata()
    {
        var expected = "CLYR " + ApplicationVersion.Current.Value;
        var output = new StringWriter();
        Assert.Equal(0, CreateApplication().Run(["--version"], output, TextWriter.Null));
        Assert.Equal(expected, output.ToString().Trim());
    }

    private static CliApplication CreateApplication()
    {
        var schema = File.ReadAllText(Path.Combine(RepositoryRoot(), "rules", "schemas", "rule.schema.json"));
        var environment = new FakeEnvironment();
        var applicationVersion = ApplicationVersion.Current;
        return new(environment, new DemoDataService(), new RuleValidator(schema), new PrivacyRedactor(environment),
            "CLYR " + applicationVersion.Value);
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed class FakeEnvironment : IEnvironmentInfo
    {
        public string UserName => "TestUser";
        public string UserProfilePath => "X:\\TestUser";
        public string OperatingSystem => "Windows test";
        public string Architecture => "X64";
    }
}
