using Clyr.Cli;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Rules;

namespace Clyr.Cli.Tests;

public sealed class PhaseThreeCliTests
{
    [Theory]
    [InlineData("list", "developer.node.modules")]
    [InlineData("verify", "verified")]
    public void BuiltInRuleCommandsAreOfflineAndReadOnly(string command, string expected)
    {
        var output = new StringWriter();
        Assert.Equal(0, CreateApplication().Run(["rules", command], output, TextWriter.Null));
        Assert.Contains(expected, output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescribeExplainsDetectionWithoutCleanupLanguage()
    {
        var output = new StringWriter();
        Assert.Equal(0, CreateApplication().Run(["rules", "describe", "windows.system32"], output, TextWriter.Null));
        Assert.Contains("Protected", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("report-only", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExternalValidationDoesNotActivateRule()
    {
        var output = new StringWriter();
        var rule = Path.Combine(RepositoryRoot(), "rules", "examples", "npm-cache.valid.yaml");
        Assert.Equal(0, CreateApplication().Run(["rules", "validate", rule], output, TextWriter.Null));
        Assert.Contains("inactive", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplainReadsOnlyAClassifiedReportAndPrintsSafetyMeaning()
    {
        var path = Path.Combine(Path.GetTempPath(), "clyr-explain-" + Guid.NewGuid().ToString("N") + ".json");
        const string report = """
            {
              "schemaVersion": 2,
              "scan": {
                "classification": {
                  "summary": "One cause identified.",
                  "findings": [{
                    "title": "Developer cache",
                    "logicalBytes": 42,
                    "status": "informational",
                    "explanation": { "whatItMeans": "Report-only metadata explanation." }
                  }]
                }
              }
            }
            """;
        try
        {
            File.WriteAllText(path, report);
            var output = new StringWriter();
            Assert.Equal(0, CreateApplication().Run(["explain", path], output, TextWriter.Null));
            Assert.Contains("Report-only metadata explanation.", output.ToString(), StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    private static CliApplication CreateApplication()
    {
        var environment = new FakeEnvironment();
        var schema = File.ReadAllText(Path.Combine(RepositoryRoot(), "rules", "schemas", "rule.schema.json"));
        var pack = BuiltInRulePackLoader.Load(Path.Combine(RepositoryRoot(), "rules", "builtin"));
        return new(environment, new DemoDataService(), new RuleValidator(schema), new PrivacyRedactor(environment),
            "CLYR 0.3.0-phase3", new FakeDrives(), new FakeScanner(), new ScanReportExporter(), pack);
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private sealed class FakeEnvironment : IEnvironmentInfo
    {
        public string UserName => "Fixture";
        public string UserProfilePath => Path.Combine(Path.GetTempPath(), "Fixture");
        public string OperatingSystem => "Windows fixture";
        public string Architecture => "X64";
    }
    private sealed class FakeDrives : IDriveDiscovery
    {
        public IReadOnlyList<DriveSummary> Discover() => [];
    }
    private sealed class FakeScanner : IScanService
    {
        public Task<ScanResult> ScanAsync(ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The command must not start a scan.");
    }
}
