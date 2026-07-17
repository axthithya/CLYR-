using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>Phase 7.2.6D: the elevated scanner's strict, pure bootstrap-argument parser. No test here executes
/// Clyr.ElevatedScanner — every argument list is a small in-memory array.</summary>
public sealed class ElevatedScannerBootstrapArgumentsTests
{
    [Fact]
    public void OneValidPipeArgumentSucceeds()
    {
        var pipeName = ElevatedScanPipeName.New();
        var result = ElevatedScannerBootstrapArguments.TryParse(["--pipe=" + pipeName]);
        Assert.True(result.IsValid);
        Assert.Equal(pipeName, result.PipeName);
    }

    [Fact]
    public void NoArgumentsReturnsMissingArgument() =>
        AssertOutcome([], ElevatedScannerBootstrapOutcome.MissingArgument);

    [Fact]
    public void TwoArgumentsReturnsTooManyArguments()
    {
        var pipeName = ElevatedScanPipeName.New();
        AssertOutcome(["--pipe=" + pipeName, "--pipe=" + pipeName], ElevatedScannerBootstrapOutcome.TooManyArguments);
    }

    [Fact]
    public void PositionalPipeNameIsRejected() =>
        AssertOutcome([ElevatedScanPipeName.New()], ElevatedScannerBootstrapOutcome.InvalidSwitch);

    [Fact]
    public void WrongSwitchIsRejected() =>
        AssertOutcome(["--pipename=" + ElevatedScanPipeName.New()], ElevatedScannerBootstrapOutcome.InvalidSwitch);

    [Fact]
    public void DifferentSwitchCasingIsRejected() =>
        AssertOutcome(["--Pipe=" + ElevatedScanPipeName.New()], ElevatedScannerBootstrapOutcome.InvalidSwitch);

    [Fact]
    public void EmptyPipeValueIsRejected() =>
        AssertOutcome(["--pipe="], ElevatedScannerBootstrapOutcome.EmptyPipeName);

    [Fact]
    public void WrongPipePrefixIsRejected() =>
        AssertOutcome(["--pipe=wrong-prefix-" + new string('a', 32)], ElevatedScannerBootstrapOutcome.InvalidPipeName);

    [Fact]
    public void WhitespaceInPipeNameIsRejected() =>
        AssertOutcome(["--pipe=" + ElevatedScanPipeNameFormat.Prefix + new string('a', 15) + " " + new string('a', 16)],
            ElevatedScannerBootstrapOutcome.InvalidPipeName);

    [Fact]
    public void PathSeparatorInPipeNameIsRejected() =>
        AssertOutcome(["--pipe=" + ElevatedScanPipeNameFormat.Prefix + new string('a', 15) + "\\" + new string('a', 16)],
            ElevatedScannerBootstrapOutcome.InvalidPipeName);

    [Fact]
    public void OversizedPipeNameIsRejected() =>
        AssertOutcome(["--pipe=" + ElevatedScanPipeName.New() + "extra"], ElevatedScannerBootstrapOutcome.InvalidPipeName);

    [Fact]
    public void ResponseFileSyntaxIsRejected() =>
        AssertOutcome(["@arguments.rsp"], ElevatedScannerBootstrapOutcome.InvalidSwitch);

    [Fact]
    public void DrivePathIsRejected() =>
        AssertOutcome(["C:\\Windows\\System32"], ElevatedScannerBootstrapOutcome.InvalidSwitch);

    [Fact]
    public void ArbitraryCommandTextIsRejected() =>
        AssertOutcome(["cmd.exe /c whoami"], ElevatedScannerBootstrapOutcome.InvalidSwitch);

    private static void AssertOutcome(string[] args, ElevatedScannerBootstrapOutcome expected)
    {
        var result = ElevatedScannerBootstrapArguments.TryParse(args);
        Assert.False(result.IsValid);
        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.PipeName);
    }
}
