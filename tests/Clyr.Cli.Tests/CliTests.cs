using Clyr.Cli;
using Clyr.Core;
using Clyr.Rules;

namespace Clyr.Cli.Tests;

public sealed class CliTests
{
    private static readonly string[] MissingRulePathArguments = { "rules", "validate" };
    private static readonly string[] DoctorArguments = { "doctor" };
    private readonly CliApplication application = CreateApplication();

    [Theory]
    [InlineData("--help", 0, "Commands:")]
    [InlineData("--version", 0, "CLYR 0.1.0-phase1")]
    [InlineData("doctor", 0, "no drives have been scanned")]
    [InlineData("demo", 0, "Demo data — no real drives have been scanned.")]
    [InlineData("unknown", 2, "Unknown command")]
    public void CommandsHaveStableExitCodes(string command, int expectedCode, string expectedText)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = application.Run(new[] { command }, output, error);
        Assert.Equal(expectedCode, code);
        Assert.Contains(expectedText, output + error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RulesValidateRequiresAPath()
    {
        var code = application.Run(MissingRulePathArguments, TextWriter.Null, TextWriter.Null);
        Assert.Equal(2, code);
    }

    [Fact]
    public void ValidRuleReturnsSuccess()
    {
        var path = Path.Combine(RepositoryRoot(), "rules", "examples", "npm-cache.valid.yaml");
        var code = application.Run(new[] { "rules", "validate", path }, TextWriter.Null, TextWriter.Null);
        Assert.Equal(0, code);
    }

    [Fact]
    public void DoctorDoesNotExposeUserOrHomePath()
    {
        var output = new StringWriter();
        var code = application.Run(DoctorArguments, output, TextWriter.Null);
        Assert.Equal(0, code);
        Assert.DoesNotContain("TestUser", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("X:\\\\TestUser", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static CliApplication CreateApplication()
    {
        var schema = File.ReadAllText(Path.Combine(RepositoryRoot(), "rules", "schemas", "rule.schema.json"));
        var environment = new FakeEnvironment();
        return new CliApplication(environment, new DemoDataService(), new RuleValidator(schema), new PrivacyRedactor(environment), "CLYR 0.1.0-phase1");
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
