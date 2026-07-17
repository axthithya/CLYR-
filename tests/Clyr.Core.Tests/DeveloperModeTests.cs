using System.Collections.Immutable;
using Clyr.Contracts;
using Clyr.Core;
using Clyr.Core.DeveloperMode;

namespace Clyr.Core.Tests;

public sealed class DeveloperModeTaxonomyTests
{
    [Theory]
    [InlineData("developer.npm.cache", DeveloperToolId.NodeNpm)]
    [InlineData("developer.node.modules", DeveloperToolId.NodeNpm)]
    [InlineData("developer.yarn.cache", DeveloperToolId.Yarn)]
    [InlineData("developer.pnpm.store", DeveloperToolId.Pnpm)]
    [InlineData("developer.nuget.packages", DeveloperToolId.DotNetNuGet)]
    [InlineData("developer.dotnet.bin", DeveloperToolId.DotNetNuGet)]
    [InlineData("developer.gradle.cache", DeveloperToolId.Gradle)]
    [InlineData("developer.maven.cache", DeveloperToolId.Maven)]
    [InlineData("developer.pip.cache", DeveloperToolId.PythonPip)]
    [InlineData("developer.cargo.registry", DeveloperToolId.RustCargo)]
    [InlineData("developer.rust.target", DeveloperToolId.RustCargo)]
    [InlineData("developer.flutter.pubcache", DeveloperToolId.FlutterDart)]
    [InlineData("developer.playwright.cache", DeveloperToolId.Playwright)]
    [InlineData("android.emulator", DeveloperToolId.AndroidSdk)]
    [InlineData("containers.docker", DeveloperToolId.Docker)]
    [InlineData("virtualization.wsl", DeveloperToolId.Wsl)]
    [InlineData("virtualization.vhdx", DeveloperToolId.Wsl)]
    [InlineData("developer.buildoutput.generic", DeveloperToolId.BuildOutput)]
    public void EveryKnownRuleMapsToItsTool(string ruleId, DeveloperToolId expected) =>
        Assert.Equal(expected, DeveloperToolTaxonomy.ToolFor(ruleId));

    [Fact]
    public void UnknownRuleIdMapsToNoTool()
    {
        Assert.Null(DeveloperToolTaxonomy.ToolFor("windows.system32"));
        Assert.Null(DeveloperToolTaxonomy.ToolFor("not-a-real-rule"));
    }
}

public sealed class DeveloperToolReportBuilderTests
{
    [Fact]
    public void FromSnapshotGroupsFindingsByToolAndSumsObservedBytes()
    {
        var snapshot = Snapshot([
            new("developer.npm.cache", "1.1.0", StorageCategory.DeveloperCache, FindingConfidence.High, FindingStatus.Informational, 100, 2),
            new("developer.yarn.cache", "1.0.0", StorageCategory.DeveloperCache, FindingConfidence.High, FindingStatus.Informational, 50, 1),
            new("developer.node.modules", "1.0.0", StorageCategory.DeveloperDependencies, FindingConfidence.Confirmed, FindingStatus.Review, 900, 400),
            new("containers.docker", "1.0.0", StorageCategory.Containers, FindingConfidence.High, FindingStatus.Protected, 5000, 10),
            new("windows.system32", "1.0.0", StorageCategory.WindowsSystemManaged, FindingConfidence.Confirmed, FindingStatus.Protected, 999, 1),
        ]);

        var reports = DeveloperToolReportBuilder.FromSnapshot(snapshot);

        var node = Assert.Single(reports, r => r.ToolId == DeveloperToolId.NodeNpm);
        Assert.Equal(2, node.Candidates.Length); // npm cache + node_modules; yarn is a different tool
        Assert.Equal(100 + 900, node.TotalObservedLogicalBytes);

        var yarn = Assert.Single(reports, r => r.ToolId == DeveloperToolId.Yarn);
        Assert.Single(yarn.Candidates);
        Assert.Equal(50, yarn.TotalObservedLogicalBytes);

        var docker = Assert.Single(reports, r => r.ToolId == DeveloperToolId.Docker);
        Assert.Equal(CleanupEligibility.Protected, Assert.Single(docker.Candidates).Eligibility);

        Assert.DoesNotContain(reports, r => r.ToolId == DeveloperToolId.Wsl);
    }

    [Fact]
    public void FromSnapshotNeverProducesAToolReportForUnrelatedFindings()
    {
        var snapshot = Snapshot([new("windows.system32", "1.0.0", StorageCategory.WindowsSystemManaged, FindingConfidence.Confirmed, FindingStatus.Protected, 100, 1)]);
        Assert.Empty(DeveloperToolReportBuilder.FromSnapshot(snapshot));
    }

    private static StorageSnapshot Snapshot(IReadOnlyList<SnapshotFinding> findings) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, "test", DateTimeOffset.UtcNow, ScanMode.Quick,
            SnapshotState.Complete, new("drive", DriveIdentityQuality.Stable, "C:" + (char)92, "NTFS", 1000, 500, 500),
            100, 100, 0, 0, new(10, 2, 0, 0, 0, 0, 0, false, false, false),
            "clyr.builtin", "1.1.0", "digest", [], findings, []);
}

public sealed class TrustedExecutableLocatorTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), "clyr-tool-locator-" + Guid.NewGuid().ToString("N"));
    private readonly string? previousPath = Environment.GetEnvironmentVariable("PATH");

    public TrustedExecutableLocatorTests() => Directory.CreateDirectory(tempDirectory);
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", previousPath);
        if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true);
    }

    [Fact]
    public void LocatesATrustedExecutableOnPath()
    {
        var exePath = Path.Combine(tempDirectory, "fixture-tool.exe");
        File.WriteAllText(exePath, "not a real PE — only existence and extension matter for discovery");
        Environment.SetEnvironmentVariable("PATH", tempDirectory + Path.PathSeparator + previousPath);
        var descriptor = new DeveloperToolDescriptor(DeveloperToolId.Docker, "Fixture", ["windows"], ["fixture-tool.exe"], true, TimeSpan.FromSeconds(1), 1024, "test");

        var located = new TrustedExecutableLocator().Locate(descriptor);

        Assert.NotNull(located);
        Assert.Equal("PATH", located!.DiscoverySource);
        Assert.Equal(Path.GetFullPath(exePath), located.NormalizedFullPath);
    }

    [Fact]
    public void RejectsAScriptEvenWithATrustedName()
    {
        var scriptPath = Path.Combine(tempDirectory, "fixture-tool.cmd");
        File.WriteAllText(scriptPath, "@echo off");
        Environment.SetEnvironmentVariable("PATH", tempDirectory + Path.PathSeparator + previousPath);
        var descriptor = new DeveloperToolDescriptor(DeveloperToolId.Docker, "Fixture", ["windows"], ["fixture-tool.cmd"], true, TimeSpan.FromSeconds(1), 1024, "test");

        Assert.Null(new TrustedExecutableLocator().Locate(descriptor));
    }

    [Fact]
    public void ReturnsNullWhenNothingIsFound()
    {
        Environment.SetEnvironmentVariable("PATH", tempDirectory);
        var descriptor = new DeveloperToolDescriptor(DeveloperToolId.Docker, "Fixture", ["windows"], ["does-not-exist-anywhere.exe"], true, TimeSpan.FromSeconds(1), 1024, "test");
        Assert.Null(new TrustedExecutableLocator().Locate(descriptor));
    }

    [Fact]
    public void ReturnsNullWhenTheDescriptorDoesNotRequireAProbe()
    {
        var descriptor = new DeveloperToolDescriptor(DeveloperToolId.NodeNpm, "Fixture", ["windows"], [], false, TimeSpan.Zero, 0, "test");
        Assert.Null(new TrustedExecutableLocator().Locate(descriptor));
    }
}

public sealed class DeveloperToolProbeRunnerTests
{
    [Fact]
    public async Task RunsARealExecutableWithFixedArgumentsAndBoundsOutput()
    {
        var dotnet = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".tools", "dotnet", "dotnet.exe");
        dotnet = Path.GetFullPath(dotnet);
        if (!File.Exists(dotnet)) return; // environment without the repository-pinned SDK alongside; nothing to prove here.

        var request = new DeveloperToolProbeRequest(DeveloperToolId.Docker, dotnet, ["--version"], TimeSpan.FromSeconds(10), 1024);
        var result = await new DeveloperToolProbeRunner().RunAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.TimedOut);
        Assert.NotNull(result.StandardOutput);
        Assert.Matches(@"\d+\.\d+", result.StandardOutput!);
    }

    [Fact]
    public async Task RejectsAnExecutableThatDoesNotExist()
    {
        var request = new DeveloperToolProbeRequest(DeveloperToolId.Docker, @"C:\does\not\exist\tool.exe", ["--version"], TimeSpan.FromSeconds(1), 1024);
        var result = await new DeveloperToolProbeRunner().RunAsync(request, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task RejectsANonExeTarget()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), "clyr-probe-reject-" + Guid.NewGuid().ToString("N") + ".cmd");
        File.WriteAllText(scriptPath, "@echo off");
        try
        {
            var request = new DeveloperToolProbeRequest(DeveloperToolId.Docker, scriptPath, ["--version"], TimeSpan.FromSeconds(1), 1024);
            var result = await new DeveloperToolProbeRunner().RunAsync(request, CancellationToken.None);
            Assert.False(result.Succeeded);
        }
        finally { File.Delete(scriptPath); }
    }
}

public sealed class DeveloperToolRegistryTests
{
    [Fact]
    public async Task DockerNotInstalledWhenNoExecutableIsLocated()
    {
        var reports = await DeveloperToolRegistry.DetectAllAsync([], new FakeLocator(null), new FakeProbeRunner(null), CancellationToken.None);
        var docker = Assert.Single(reports, r => r.ToolId == DeveloperToolId.Docker);
        Assert.Equal(DeveloperToolStatus.NotInstalled, docker.Status);
    }

    [Fact]
    public async Task DockerInstalledNoDataWhenExecutableRespondsButNoFindingsExist()
    {
        var located = new DeveloperToolExecutableCandidate(@"C:\Program Files\Docker\docker.exe", null, null, "known-folder:test");
        var reports = await DeveloperToolRegistry.DetectAllAsync([], new FakeLocator(located),
            new FakeProbeRunner(new(true, "Docker version 24.0.7, build afdd53b", false, false, 0, null)), CancellationToken.None);
        var docker = Assert.Single(reports, r => r.ToolId == DeveloperToolId.Docker);
        Assert.Equal(DeveloperToolStatus.InstalledNoData, docker.Status);
        Assert.Equal("24.0.7", docker.DetectedVersion);
    }

    [Fact]
    public async Task DockerFullyDetectedWhenClassificationFindingsExistAlongsideTheProbe()
    {
        var located = new DeveloperToolExecutableCandidate(@"C:\Program Files\Docker\docker.exe", null, null, "known-folder:test");
        var candidate = new CleanupCandidate("docker-finding", "Docker data", StorageCategory.Containers, CleanupEligibility.Protected,
            "protected", null, new(1, 100, null, "test"), RiskLevel.Prohibited, FindingConfidence.High,
            new("d", "w", "p", false, "n", "a", "s", "r", "u"), []);
        var classification = ImmutableArray.Create(new DeveloperToolReport(DeveloperToolId.Docker, DeveloperToolStatus.FullyDetected, null, null, [candidate], [], 100, null));
        var reports = await DeveloperToolRegistry.DetectAllAsync(classification, new FakeLocator(located),
            new FakeProbeRunner(new(true, "Docker version 24.0.7, build afdd53b", false, false, 0, null)), CancellationToken.None);
        var docker = Assert.Single(reports, r => r.ToolId == DeveloperToolId.Docker);
        Assert.Equal(DeveloperToolStatus.FullyDetected, docker.Status);
        Assert.Single(docker.Candidates);
    }

    [Fact]
    public async Task ProbeTimeoutProducesProbeFailedNeverAFabricatedStatus()
    {
        var located = new DeveloperToolExecutableCandidate(@"C:\Program Files\Docker\docker.exe", null, null, "known-folder:test");
        var reports = await DeveloperToolRegistry.DetectAllAsync([], new FakeLocator(located),
            new FakeProbeRunner(new(false, null, true, false, null, "timed out")), CancellationToken.None);
        var docker = Assert.Single(reports, r => r.ToolId == DeveloperToolId.Docker);
        Assert.Equal(DeveloperToolStatus.ProbeFailed, docker.Status);
    }

    [Fact]
    public async Task NonProbeToolsWithoutEvidenceReportUnavailableNeverNotInstalled()
    {
        var reports = await DeveloperToolRegistry.DetectAllAsync([], new FakeLocator(null), new FakeProbeRunner(null), CancellationToken.None);
        var npm = Assert.Single(reports, r => r.ToolId == DeveloperToolId.NodeNpm);
        // Absence of one expected folder must never alone produce NotInstalled — only a failed trusted probe may.
        Assert.Equal(DeveloperToolStatus.Unavailable, npm.Status);
    }

    private sealed class FakeLocator(DeveloperToolExecutableCandidate? result) : IDeveloperToolExecutableLocator
    {
        public DeveloperToolExecutableCandidate? Locate(DeveloperToolDescriptor descriptor) => result;
    }

    private sealed class FakeProbeRunner(DeveloperToolProbeResult? result) : IDeveloperToolProbeRunner
    {
        public Task<DeveloperToolProbeResult> RunAsync(DeveloperToolProbeRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(result ?? new DeveloperToolProbeResult(false, null, false, false, null, "no result configured"));
    }
}
