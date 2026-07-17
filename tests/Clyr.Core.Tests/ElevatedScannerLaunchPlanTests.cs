using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>Phase 7.2.6F1: the pure, data-only launch-plan preparation for the elevated scanner. No test here
/// touches the real filesystem, launches a process, opens a pipe, or triggers UAC — every fixture is an
/// in-memory fake <see cref="ITrustedApplicationBaseDirectory"/>/<see cref="IElevatedScannerFileProbe"/>.</summary>
public sealed class ElevatedScannerLaunchPlanTests
{
    private const string TrustedBase = "C:\\CLYR";

    [Fact]
    public void ValidTrustedCoLocatedHelperProducesReady()
    {
        var result = ElevatedScannerLaunchPlanBuilder.Build(Trusted(TrustedBase), Probe());
        Assert.True(result.IsReady);
        Assert.NotNull(result.Plan);
    }

    [Fact]
    public void PlanContainsTheFixedHelperFilename()
    {
        var result = ElevatedScannerLaunchPlanBuilder.Build(Trusted(TrustedBase), Probe());
        Assert.Equal("C:\\CLYR\\Clyr.ElevatedScanner.exe", result.Plan!.ExecutablePath);
    }

    [Fact]
    public void PlanContainsExactlyOnePipeArgument()
    {
        var result = ElevatedScannerLaunchPlanBuilder.Build(Trusted(TrustedBase), Probe());
        Assert.Equal("--pipe=" + result.Plan!.PipeName, result.Plan.BootstrapArgument);
        Assert.True(ElevatedScannerBootstrapArguments.TryParse([result.Plan.BootstrapArgument]).IsValid);
    }

    [Fact]
    public void GeneratedPipeNameIsValid()
    {
        var result = ElevatedScannerLaunchPlanBuilder.Build(Trusted(TrustedBase), Probe());
        Assert.True(ElevatedScanPipeName.IsValid(result.Plan!.PipeName));
    }

    [Fact]
    public void CallerCannotProvideAnExecutablePath()
    {
        var method = typeof(ElevatedScannerLaunchPlanBuilder).GetMethod(nameof(ElevatedScannerLaunchPlanBuilder.Build));
        Assert.NotNull(method);
        // Only the two trusted abstractions are accepted — no parameter of type string (a path, filename, or
        // argument) exists anywhere on this public API.
        Assert.All(method!.GetParameters(), parameter => Assert.NotEqual(typeof(string), parameter.ParameterType));
    }

    [Fact]
    public void MissingTrustedBaseDirectoryIsRejected()
    {
        var result = ElevatedScannerLaunchPlanBuilder.Build(Trusted(""), Probe());
        Assert.Equal(ElevatedScannerLaunchPlanOutcome.TrustedBaseDirectoryUnavailable, result.Outcome);
    }

    [Fact]
    public void MissingHelperIsRejected()
    {
        var result = ElevatedScannerLaunchPlanBuilder.Build(Trusted(TrustedBase), Probe(fileExists: false));
        Assert.Equal(ElevatedScannerLaunchPlanOutcome.HelperMissing, result.Outcome);
    }

    [Fact]
    public void RelativeHelperPathIsRejected()
    {
        var result = ElevatedScannerLaunchPlanBuilder.Build(Trusted("CLYR\\bin"), Probe());
        Assert.Equal(ElevatedScannerLaunchPlanOutcome.TrustedBaseDirectoryUnavailable, result.Outcome);
    }

    [Fact]
    public void UncHelperPathIsRejected()
    {
        var result = ElevatedScannerLaunchPlanBuilder.Build(Trusted("\\\\server\\share\\CLYR"), Probe());
        Assert.Equal(ElevatedScannerLaunchPlanOutcome.TrustedBaseDirectoryUnavailable, result.Outcome);
    }

    [Fact]
    public void DirectoryMasqueradingAsHelperIsRejected()
    {
        var result = ElevatedScannerLaunchPlanBuilder.Build(Trusted(TrustedBase), Probe(directoryExists: true));
        Assert.Equal(ElevatedScannerLaunchPlanOutcome.HelperIsDirectory, result.Outcome);
    }

    [Fact]
    public void HelperOutsideTrustedDirectoryIsRejected() =>
        Assert.False(ElevatedScannerExecutableResolver.IsContainedWithin("C:\\Other\\Clyr.ElevatedScanner.exe", TrustedBase));

    [Fact]
    public void PrefixConfusionContainmentIsRejected() =>
        // "C:\CLYR2" shares a text prefix with "C:\CLYR" but is a different, sibling directory.
        Assert.False(ElevatedScannerExecutableResolver.IsContainedWithin("C:\\CLYR2\\Clyr.ElevatedScanner.exe", TrustedBase));

    [Fact]
    public void TraversalEscapeIsRejected()
    {
        var result = ElevatedScannerLaunchPlanBuilder.Build(Trusted("C:\\CLYR\\..\\Windows"), Probe());
        Assert.Equal(ElevatedScannerLaunchPlanOutcome.TrustedBaseDirectoryUnavailable, result.Outcome);
    }

    [Fact]
    public void AlternateFilenameIsRejected() =>
        Assert.False(ElevatedScannerExecutableResolver.HasExpectedFileName("C:\\CLYR\\notepad.exe"));

    [Fact]
    public void ReparsePointEscapeIsRejectedOrFailsClosed()
    {
        var result = ElevatedScannerLaunchPlanBuilder.Build(Trusted(TrustedBase), Probe(isReparsePoint: true));
        Assert.Equal(ElevatedScannerLaunchPlanOutcome.HelperReparsePointRejected, result.Outcome);
    }

    private static FakeTrustedDirectory Trusted(string baseDirectory) => new(baseDirectory);
    private static FakeFileProbe Probe(bool fileExists = true, bool directoryExists = false, bool isReparsePoint = false) =>
        new(fileExists, directoryExists, isReparsePoint);

    private sealed class FakeTrustedDirectory(string baseDirectory) : ITrustedApplicationBaseDirectory
    {
        public string BaseDirectory { get; } = baseDirectory;
    }

    private sealed class FakeFileProbe(bool fileExists, bool directoryExists, bool isReparsePoint) : IElevatedScannerFileProbe
    {
        public bool FileExists(string path) => fileExists;
        public bool DirectoryExists(string path) => directoryExists;
        public bool IsReparsePoint(string path) => isReparsePoint;
    }
}
