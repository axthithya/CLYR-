using Clyr.Contracts;
using Clyr.Core;

namespace Clyr.Core.Tests;

/// <summary>Phase 7.2.6B: the random, single-use pipe name — pure string generation and validation, no I/O.</summary>
public sealed class ElevatedScanPipeNameTests
{
    [Fact]
    public void GeneratedNameHasTheExpectedPrefix() =>
        Assert.StartsWith(ElevatedScanPipeNameFormat.Prefix, ElevatedScanPipeName.New(), StringComparison.Ordinal);

    [Fact]
    public void GeneratedNamesAreDifferentEachTime() =>
        Assert.NotEqual(ElevatedScanPipeName.New(), ElevatedScanPipeName.New());

    [Fact]
    public void GeneratedNamePassesValidation() =>
        Assert.True(ElevatedScanPipeName.IsValid(ElevatedScanPipeName.New()));

    [Fact]
    public void EmptyNameIsRejected() => Assert.False(ElevatedScanPipeName.IsValid(string.Empty));

    [Fact]
    public void NullNameIsRejected() => Assert.False(ElevatedScanPipeName.IsValid(null));

    [Fact]
    public void PathSeparatorInsideTheRandomComponentIsRejected() =>
        Assert.False(ElevatedScanPipeName.IsValid(ElevatedScanPipeNameFormat.Prefix + new string('a', 15) + "\\" + new string('a', 16)));

    [Fact]
    public void WhitespaceInsideTheRandomComponentIsRejected() =>
        Assert.False(ElevatedScanPipeName.IsValid(ElevatedScanPipeNameFormat.Prefix + new string('a', 15) + " " + new string('a', 16)));

    [Fact]
    public void WrongPrefixIsRejected() =>
        Assert.False(ElevatedScanPipeName.IsValid("wrong-prefix-" + Convert.ToHexString(new byte[16]).ToLowerInvariant()));

    [Fact]
    public void OversizedNameIsRejected() =>
        Assert.False(ElevatedScanPipeName.IsValid(ElevatedScanPipeName.New() + "extra"));
}
