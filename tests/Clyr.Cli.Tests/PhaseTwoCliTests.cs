using Clyr.Cli;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Rules;

namespace Clyr.Cli.Tests;

public sealed class PhaseTwoCliTests
{
    [Fact]
    public void DrivesListsCapabilityWithoutStartingScan()
    {
        var app = CreateApplication();
        var output = new StringWriter();
        Assert.Equal(0, app.Run(["drives"], output, TextWriter.Null));
        Assert.Contains("C:\\", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DriveJsonOmitsPotentiallyPersonalVolumeLabel()
    {
        var output = new StringWriter();
        Assert.Equal(0, CreateApplication().Run(["drives", "--json"], output, TextWriter.Null));
        Assert.DoesNotContain("Fixture", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("fixed", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("scan", 2)]
    [InlineData("scan|relative", 2)]
    [InlineData("scan|C:\\|--unknown", 2)]
    public void InvalidScanArgumentsHaveStableUsageExitCode(string encoded, int expected)
    {
        var args = encoded.Split('|');
        Assert.Equal(expected, CreateApplication().Run(args, TextWriter.Null, TextWriter.Null));
    }

    [Fact]
    public void ScanWritesHumanSummaryAndProgressSeparately()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = CreateApplication().Run(["scan", "C:\\", "--quick", "--top", "5"], output, error);
        Assert.Equal(0, code);
        Assert.Contains("Observed logical size", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Scanning", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ScanSummaryUsesHonestRemainderWordingAndNeverImpliesReclaimability()
    {
        // Phase 7.2.4: the CLI's human summary must use the documented honest-remainder vocabulary and must
        // never fall back to the old, misleading blanket terms for whatever a scan didn't observe.
        var output = new StringWriter();
        var code = CreateApplication().Run(["scan", "C:\\", "--quick"], output, TextWriter.Null);
        Assert.Equal(0, code);
        var text = output.ToString();

        Assert.Contains("Elapsed:", text, StringComparison.Ordinal);
        Assert.Contains("Inaccessible roots:", text, StringComparison.Ordinal);
        Assert.Contains("Accounted coverage:", text, StringComparison.Ordinal);
        Assert.Contains("Accounting consistency:", text, StringComparison.Ordinal);
        Assert.Contains("Unresolved remainder:", text, StringComparison.Ordinal);

        foreach (var forbidden in new[] { "Unknown files", "Missing files", "Hidden junk", "Recoverable storage" })
            Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
        Assert.DoesNotContain("safe to delete", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reclaim", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TechnicalJsonIsOnlyAddedWhenExplicitlyRequested()
    {
        var plain = new StringWriter();
        Assert.Equal(0, CreateApplication().Run(["scan", "C:\\", "--json"], plain, TextWriter.Null));
        Assert.DoesNotContain("volumeRemainder", plain.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("clyr-scan-technical", plain.ToString(), StringComparison.Ordinal);

        var technical = new StringWriter();
        Assert.Equal(0, CreateApplication().Run(["scan", "C:\\", "--json", "--technical"], technical, TextWriter.Null));
        var json = technical.ToString();
        Assert.Contains("clyr-scan-technical", json, StringComparison.Ordinal);
        Assert.Contains("volumeRemainder", json, StringComparison.Ordinal);
        Assert.Contains("accounting", json, StringComparison.Ordinal);
        System.Text.Json.JsonDocument.Parse(json).Dispose();
    }

    [Fact]
    public void JsonOutputIsPrivacySafeAndParseable()
    {
        var output = new StringWriter();
        var code = CreateApplication().Run(["scan", "C:\\", "--json"], output, TextWriter.Null);
        Assert.Equal(0, code);
        var json = output.ToString();
        Assert.Contains("clyr-scan-summary", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Alice", json, StringComparison.OrdinalIgnoreCase);
        System.Text.Json.JsonDocument.Parse(json).Dispose();
    }

    [Fact]
    public void ExplicitOutputWritesOnlyTheSelectedReportFile()
    {
        var destination = Path.Combine(Path.GetTempPath(), "clyr-scan-export-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.Equal(0, CreateApplication().Run(["scan", "C:\\", "--output", destination], TextWriter.Null, TextWriter.Null));
            Assert.Contains("support-safe", File.ReadAllText(destination), StringComparison.Ordinal);
        }
        finally { if (File.Exists(destination)) File.Delete(destination); }
    }

    [Fact]
    public void EachPersistenceFlagCombinationRoutesToItsOwnDedicatedScannerInstance()
    {
        // Four genuinely distinct scanner instances, one per (history, checkpoint) combination. A flag that
        // claims something will not be persisted must select an instance that structurally cannot write it —
        // proven here by checking each flag combination calls exactly its own dedicated fake and no other.
        var full = new FakeScanner();
        var noHistoryScanner = new FakeScanner();
        var noCheckpointScanner = new FakeScanner();
        var nothingScanner = new FakeScanner();
        var environment = new FakeEnvironment();
        var app = new CliApplication(environment, new DemoDataService(),
            new RuleValidator(File.ReadAllText(Path.Combine(RepositoryRoot(), "rules", "schemas", "rule.schema.json"))),
            new PrivacyRedactor(environment), "CLYR 0.2.0-phase2", new FakeDrives(), full, new ScanReportExporter())
        {
            NonPersistingScanner = noHistoryScanner,
            NoCheckpointScanner = noCheckpointScanner,
            NoCheckpointNoHistoryScanner = nothingScanner
        };
        Assert.Equal(0, app.Run(["scan", "C:\\", "--quick"], TextWriter.Null, TextWriter.Null));
        Assert.Equal((1, 0, 0, 0), (full.CallCount, noHistoryScanner.CallCount, noCheckpointScanner.CallCount, nothingScanner.CallCount));

        Assert.Equal(0, app.Run(["scan", "C:\\", "--quick", "--no-history"], TextWriter.Null, TextWriter.Null));
        Assert.Equal((1, 1, 0, 0), (full.CallCount, noHistoryScanner.CallCount, noCheckpointScanner.CallCount, nothingScanner.CallCount));

        Assert.Equal(0, app.Run(["scan", "C:\\", "--quick", "--no-checkpoint"], TextWriter.Null, TextWriter.Null));
        Assert.Equal((1, 1, 1, 0), (full.CallCount, noHistoryScanner.CallCount, noCheckpointScanner.CallCount, nothingScanner.CallCount));

        Assert.Equal(0, app.Run(["scan", "C:\\", "--quick", "--no-persist"], TextWriter.Null, TextWriter.Null));
        Assert.Equal((1, 1, 1, 1), (full.CallCount, noHistoryScanner.CallCount, noCheckpointScanner.CallCount, nothingScanner.CallCount));

        Assert.Equal(0, app.Run(["scan", "C:\\", "--quick", "--no-history", "--no-checkpoint"], TextWriter.Null, TextWriter.Null));
        Assert.Equal((1, 1, 1, 2), (full.CallCount, noHistoryScanner.CallCount, noCheckpointScanner.CallCount, nothingScanner.CallCount));
    }

    private static CliApplication CreateApplication()
    {
        var environment = new FakeEnvironment();
        var drives = new FakeDrives();
        var schema = File.ReadAllText(Path.Combine(RepositoryRoot(), "rules", "schemas", "rule.schema.json"));
        return new(environment, new DemoDataService(), new RuleValidator(schema), new PrivacyRedactor(environment),
            "CLYR 0.2.0-phase2", drives, new FakeScanner(), new ScanReportExporter());
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed class FakeDrives : IDriveDiscovery
    {
        public IReadOnlyList<DriveSummary> Discover() => [new("C:\\", "Fixture", "NTFS", DriveKind.Fixed, true, true, true, "Supported", 1000, 400, 600)];
    }

    private sealed class FakeScanner : IScanService
    {
        public int CallCount { get; private set; }

        public Task<ScanResult> ScanAsync(ScanRequest request, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
        {
            CallCount++;
            progress?.Report(new(ScanStatus.Scanning, TimeSpan.FromSeconds(1), 1, 1, 100, 0, "C:\\<redacted>", "Fixture"));
            var now = DateTimeOffset.Parse("2026-07-13T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
            return Task.FromResult(new ScanResult(Guid.Empty, ScanStatus.Completed, request.Mode, request.Root, "NTFS", now, now,
                100, 400, 300, MeasurementPrecision.Estimated, "Logical metadata only.", new(1, 1, 0, 0, 0, 0, 0, false, false, false),
                [new("C:\\Users\\Alice", 100, 1, MeasurementPrecision.Estimated)], [], [], [], [], null, null));
        }
    }

    private sealed class FakeEnvironment : IEnvironmentInfo
    {
        public string UserName => "Alice";
        public string UserProfilePath => "C:\\Users\\Alice";
        public string OperatingSystem => "Windows fixture";
        public string Architecture => "X64";
    }
}
